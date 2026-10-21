// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    // 월백 두 번째 체력 기믹 "빨강 파랑 색 결계 생존"의 서버 진행이다. 보스 두뇌의 기믹 훅(BossBrain.AddGimmick)에 연결되며
    // 전환 판정보다 먼저 Tick이 불리고 진입 순간에 두뇌가 진행 중인 공격과 지속물을 정리한다(IBossGimmickCleanup).
    // 기존 BossBarrierDirector(50% 전환 뒤 전장 위험 구역)와는 다른 기믹이다. 이 클래스는 그것을 건드리지 않으며, 운영에서 둘이 함께
    // 발동하지 않게 하는 일은 BossServer를 만지는 쪽이 한다(이 기믹이 끝나면 전환이 일어나고 그 인계 대기가 결계 전개를 시작시키기 때문이다).
    //
    // 경계에서 멈추기와 무적은 이 클래스가 계산만 하고(IBossDamageCap) 실제로 피해를 깎는 보스 몸체와 피해 판정기가 읽어야 닿는다.
    // 75% 부적진도 같은 계약을 가지므로 둘을 함께 읽게 하려면 BossDamageCapComposite를 거친다. 이 클래스는 보스 체력을 직접 바꾸지 않는다.
    //
    // 앞선 기믹(precedingGimmick, 75% 부적진)이 전환을 보류하는 동안에는 체력이 경계에 닿았어도 시작하지 않고 기다린다. 그래서
    // 75% 반격 중에 50%에 닿아도 반격이 끝나는 틱에 이어서 한 번만 발동한다. 기다리는 동안에도 전환과 일반 선택은 막힌다.
    //
    // 필수 참가자는 진입 순간의 출정 명단이다(IBossParticipantRoster). 쓰러진 사람도 포함하고 이후 명단이 바뀌어도 색은 바뀌지 않는다.
    // 부활 불가이거나 명단에서 사라진 사람은 중앙 집결의 필수 조건에서 빠지며 기믹을 실패시키지 않는다. 전원이 쓰러졌을 때만 실패한다.
    //
    // 한 틱의 순서는 (집결) 참가자 확인과 집결 완료 확인, (지점) 광역 공격 판정, 참가자 확인(실패, 복귀 유예, 연결 거리 피해),
    // 성공 확인이다. 판정과 사망이 완료 확인보다 앞이라 완료가 되는 틱에 필수 참가자가 쓰러지면 완료되지 않는다.
    // 거친 틱에서도 예고와 광역 공격을 시각 순서대로 모두 처리하지만 과거 위치를 기록하지 않아 현재 위치로 판정한다.
    //
    // 정지(Halted)는 기믹을 건너뛰는 것도 성공이나 실패도 아니다. 보스는 무적으로 남고 일반 선택도 열리지 않으며 원인만 알린다.
    // 이 상태를 끝내는 방법은 정해지지 않았으므로 이 클래스가 정하지 않는다.
    // 피해는 이 기믹 전용 게이트로만 내고 성공, 실패, 정지에서 영구히 닫는다. 보스전 실패는 세션이 정하므로 이 클래스는 알리기만 한다.
    // 최대 체력 비율 피해는 방어 전 값으로 낸다. 확정 계약상 시작 인원별 공격 피해 배율은 이 비율 피해에 곱하지 않는다.
    // 보스 패턴 피해에 배율을 곱하는 판정기(BossDamageScaler)를 거치면 곱해지므로 배율 이전의 판정기를 넘겨야 한다.
    // 중앙 집결은 8초 연속 집결만 있고 별도의 도착 제한 시간이 없다. 집결 중 사망과 끊김은 진행만 초기화한다
    /// <summary> 월백 색 결계 기믹, 50% 경계에서 한 번 발동해 중앙 집결 뒤 세 지점의 광역 공격을 버티면 성공한다 </summary>
    public sealed class WolbaekColorWard : IBossGimmick, IBossGimmickCleanup, IBossDamageCap
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int    FIRST_PHASE        = 1;
        private const int    MAX_MEMBERS        = 4;
        private const int    TRIO_COUNT         = 3;
    private const int    COLOR_COUNT        = 2;
        private const int    PAIR_SIZE          = 2;
        private const int    NO_MEMBER          = -1;
        private const int    MAX_CANDIDATES     = 64;
        private const int    MAX_LINK_CATCHUP   = 4;
        private const float  BOUNDARY_TOLERANCE = 0.00001f;  // 체력 비율, 경계에 닿았다고 보는 부동소수 여유
        private const float  DISTANCE_EPSILON   = 0.001f;    // m, 거리 조건 경계의 부동소수 여유
        private const float  MIN_SITE_SHIFT     = 0.01f;     // m, 이보다 가까우면 이전과 같은 위치로 본다
        private const float  MIN_DIRECTION_SQR  = 0.000001f; // m^2, 이보다 가까우면 방향을 정할 수 없다
        private const string STRIKE_ID          = "wolbaek_ward_strike";
        private const string LINK_ID            = "wolbaek_ward_link";

        /****************************************
        *                Fields
        ****************************************/

        private struct Member
        {
            public int   CombatId;
            public EWolbaekWardColor Color;
            public bool  Away;
            public bool  HasGrace;
            public double GraceEnd;
        }

        private readonly WolbaekColorWardSpec    _spec;
        private readonly ICombatTarget           _self;
        private readonly IBossVitals             _vitals;
        private readonly IBossParticipantRoster  _roster;
        private readonly IBossParticipantHealth  _health;
        private readonly IBossArenaSource        _arena;
        private readonly IWolbaekWardSiteSource  _sites;
        private readonly IDamageResolver         _resolver;
        private readonly ICombatTargetQuery      _targets;
        private readonly IBossRandom             _random;
        private readonly IBossHitDisplacer       _displacer;
        private readonly IBossGimmick            _preceding;
        private readonly IWolbaekColorWardListener _listener;
        private readonly List<int>               _idBuffer        = new List<int>();
        private readonly List<ICombatTarget>     _hitBuffer       = new List<ICombatTarget>();
        private readonly List<Vector3>           _rawCandidates   = new List<Vector3>();
        private readonly List<Vector3>           _candidates      = new List<Vector3>();
        private readonly int[]                   _linkA           = new int[COLOR_COUNT];
        private readonly int[]                   _linkB           = new int[COLOR_COUNT];
        private readonly bool[]                  _exceeding       = new bool[COLOR_COUNT];
        private readonly double[]                _nextLinkDamage  = new double[COLOR_COUNT];
    private readonly bool[]                  _linkSuppressed  = new bool[COLOR_COUNT];

        private EWolbaekWardState         _state = EWolbaekWardState.Dormant;
        private EWolbaekWardFail          _failReason;
        private EWolbaekWardHaltReason    _haltReason;
        private BossDamageGate            _gate;
        private Member[]                  _members = new Member[0];
        private WolbaekWardMember[]       _startMembers = new WolbaekWardMember[0];
        private BossParticipantInfo[]     _infos = new BossParticipantInfo[0];
        private bool[]                    _present = new bool[0];
        private bool[,,]                  _deadEnds;
        private Vector3[]                 _planRed = new Vector3[0];
        private Vector3[]                 _planBlue = new Vector3[0];
        private WolbaekWardStart          _start;
        private WolbaekWardStage          _stage;
        private Vector3                   _anchor;
        private float                     _arenaRadius;
        private int                       _castId;
        private int                       _stageIndex;
        private int                       _strikeIndex;
        private int                       _lastPhase;
        private double                    _holdStart;
        private double                    _stageStart;
        private bool                      _hasHold;
        private bool                      _telegraphed;
        private bool                      _finalStruck;
        private bool                      _cleanupRequested;
        private bool                      _healthMissingLogged;

        /****************************************
        *              Properties
        ****************************************/

        public EWolbaekWardState State => _state;

        /// <summary> 실패했다면 그 이유, 실패 전에는 의미가 없다 </summary>
        public EWolbaekWardFail FailReason => _failReason;

        /// <summary> 정지했다면 그 이유, 정지 전에는 의미가 없다 </summary>
        public EWolbaekWardHaltReason HaltReason => _haltReason;

        /// <summary> 이 기믹의 시전 번호, 진입 전에는 0이다 </summary>
        public int CastInstanceId => _castId;

        /// <summary> 진입 순간 확정된 필수 참가자 수 </summary>
        public int RequiredCount => _members.Length;

        /// <summary> 시작 명단, 진입 전에는 비어 있다 </summary>
        public IReadOnlyList<WolbaekWardMember> Members => _startMembers;

        /// <summary> 필수 참가자 전원이 중앙 결계에서 연속으로 집결하는 중이다 </summary>
        public bool HasHold => _hasHold;

        /// <summary> 연속 집결이 시작된 서버 시각, 집결 중이 아니면 의미가 없다 </summary>
        public double HoldStartServerTime => _holdStart;

        /// <summary> 지금 지점의 번호, 지점이 시작되기 전에는 0이다 </summary>
        public int StageIndex => _stageIndex;

        /// <summary> 지금 지점, 지점이 시작되기 전에는 기본값이다 </summary>
        public WolbaekWardStage Stage => _stage;

        /// <summary> 지금 지점에서 다음에 판정될 광역 공격 번호 </summary>
        public int NextStrikeIndex => _strikeIndex;

        /// <summary> 보류했던 부활과 재접속 복귀를 처리해도 되는 때다, 광역 공격이 진행되는 동안만 거짓이다 </summary>
        public bool IsReviveWindowOpen => _state != EWolbaekWardState.Strike;

        public bool DefersPhaseTransition
        {
            get
            {
                switch (_state)
                {
                    case EWolbaekWardState.Gather:
                    case EWolbaekWardState.Search:
                    case EWolbaekWardState.Strike:
                    case EWolbaekWardState.Failed:
                    case EWolbaekWardState.Halted:
                        return true;
                    case EWolbaekWardState.Dormant:
                        return IsWaitingToStart();
                    default:
                        return false;
                }
            }
        }

        public bool BlocksSelection => DefersPhaseTransition;

        public bool IsDamageBlocked
        {
            get
            {
                switch (_state)
                {
                    case EWolbaekWardState.Dormant:
                        return _vitals.HealthRate <= (_spec.TriggerHealthRate + BOUNDARY_TOLERANCE);
                    case EWolbaekWardState.Gather:
                    case EWolbaekWardState.Search:
                    case EWolbaekWardState.Strike:
                    case EWolbaekWardState.Failed:
                    case EWolbaekWardState.Halted:
                        return true;
                    default:
                        return false;
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        // unscaledResolver는 시작 인원 피해 배율을 곱하기 전의 판정기여야 한다(비율 피해에 배율을 곱하지 않는 확정 계약). health는 최대 체력 비율 피해를 방어 전 값으로 바꾸는 데 쓰고
        // 출정 명단을 읽는 쪽이 같이 구현한다. sites는 맵에서 검증된 결계 후보만 준다. displacer는 광역 공격에 맞은 대상을 미는 창구다.
        // precedingGimmick은 이 기믹보다 먼저 같은 보스에 얹은 체력 기믹(75% 부적진)이며 없으면 null이다
        public WolbaekColorWard(WolbaekColorWardSpec spec, ICombatTarget self, IBossVitals vitals, IBossParticipantRoster roster,
            IBossParticipantHealth health, IBossArenaSource arena, IWolbaekWardSiteSource sites, IDamageResolver unscaledResolver,
            ICombatTargetQuery targets, IBossRandom random, IBossHitDisplacer displacer, IBossGimmick precedingGimmick = null,
            IWolbaekColorWardListener listener = null)
        {
            _spec      = spec ?? throw new ArgumentNullException(nameof(spec));
            _self      = self ?? throw new ArgumentNullException(nameof(self));
            _vitals    = vitals ?? throw new ArgumentNullException(nameof(vitals));
            _roster    = roster ?? throw new ArgumentNullException(nameof(roster));
            _health    = health ?? throw new ArgumentNullException(nameof(health));
            _arena     = arena ?? throw new ArgumentNullException(nameof(arena));
            _sites     = sites ?? throw new ArgumentNullException(nameof(sites));
            _resolver  = unscaledResolver ?? throw new ArgumentNullException(nameof(unscaledResolver));
            _targets   = targets ?? throw new ArgumentNullException(nameof(targets));
            _random    = random ?? throw new ArgumentNullException(nameof(random));
            _displacer = displacer ?? throw new ArgumentNullException(nameof(displacer));
            _preceding = precedingGimmick;
            _listener  = listener;

            ClearLinks();
        }

        public void Tick(double serverTime, int phase)
        {
            _lastPhase = phase;

            switch (_state)
            {
                case EWolbaekWardState.Dormant:
                    TickDormant(serverTime, phase);
                    break;
                case EWolbaekWardState.Gather:
                    TickGather(serverTime);
                    break;
                case EWolbaekWardState.Search:
                case EWolbaekWardState.Strike:
                    TickStage(serverTime);
                    break;
                default:
                    break;
            }
        }

        // 기믹은 직접 패턴을 고르지 않는다. 일반 선택은 BlocksSelection으로 막는다
        public bool TrySelect(BossBlackboard blackboard, out PatternDefinition pattern)
        {
            pattern = null;
            return false;
        }

        public void OnCastExecuted(int castInstanceId, double serverTime)
        {
        }

        public void OnCastFinished(int castInstanceId, double serverTime)
        {
        }

        public bool ConsumeCleanupRequest()
        {
            bool requested = _cleanupRequested;
            _cleanupRequested = false;
            return requested;
        }

        // 진행 중이던 표시와 공격을 정리하고 멈춘다. 이미 끝났거나 멈춘 기믹에는 아무 일도 하지 않는다
        public void Stop(EBossInterruptReason reason)
        {
            if ((_state == EWolbaekWardState.Completed) || (_state == EWolbaekWardState.Stopped))
            {
                return;
            }

            bool hadDisplay = (_state == EWolbaekWardState.Gather) || (_state == EWolbaekWardState.Search)
                || (_state == EWolbaekWardState.Strike);

            if (_gate != null)
            {
                _gate.CloseForGood();
            }

            _state            = EWolbaekWardState.Stopped;
            _hasHold          = false;
            _cleanupRequested = false;

            if (hadDisplay)
            {
                _listener?.OnWardCleared(_castId, reason);
            }
        }

        // 체력이 경계에 닿은 상태에서 들어오는 다른 피해가 경계 아래로 내려가지 못하게 하고, 초과분은 버린다.
        // 기믹 중 무적 구간에서는 모두 버린다. 반환 0은 이 피해가 없던 것이라는 뜻이다
        public float CapDamage(float currentHealth, float maxHealth, float damage)
        {
            if (!(damage > 0f))
            {
                return 0f;
            }

            switch (_state)
            {
                case EWolbaekWardState.Gather:
                case EWolbaekWardState.Search:
                case EWolbaekWardState.Strike:
                case EWolbaekWardState.Failed:
                case EWolbaekWardState.Halted:
                    return 0f;
                case EWolbaekWardState.Dormant:
                    break;
                default:
                    return damage;
            }

            float allowed = currentHealth - (_spec.TriggerHealthRate * maxHealth);
            if (!(allowed > Constants.Combat.HEALTH_EPSILON))
            {
                return 0f;
            }

            return Math.Min(damage, allowed);
        }

        /// <summary> 참가자의 배정 색, 명단에 없으면 거짓이다 </summary>
        public bool TryGetColor(int combatId, out EWolbaekWardColor color)
        {
            int index = FindMember(combatId);
            color = (index == NO_MEMBER) ? EWolbaekWardColor.Red : _members[index].Color;
            return index != NO_MEMBER;
        }

        /// <summary> 참가자가 지금 합류 유예 중이다 </summary>
        public bool IsInGrace(int combatId, double serverTime)
        {
            int index = FindMember(combatId);
            return (index != NO_MEMBER) && InGrace(index, serverTime);
        }

        /// <summary> 늦게 접속하거나 재접속한 클라이언트에 줄 현재 값, 집결과 지점 진행 중일 때만 참이다 </summary>
        public bool TryGetSnapshot(out WolbaekWardSnapshot snapshot)
        {
            if ((_state != EWolbaekWardState.Gather) && (_state != EWolbaekWardState.Search) && (_state != EWolbaekWardState.Strike))
            {
                snapshot = default;
                return false;
            }

            double[] graces = new double[_members.Length];
            for (int i = 0; i < _members.Length; ++i)
            {
                graces[i] = _members[i].HasGrace ? _members[i].GraceEnd : 0d;
            }

            snapshot = new WolbaekWardSnapshot(_state, in _start, _hasHold, _holdStart, in _stage, _strikeIndex, graces);
            return true;
        }

        // 앞선 기믹이 전환을 보류하는 동안 체력이 경계에 닿았다면 시작하지 않고 기다린다
        private bool IsWaitingToStart()
        {
            return (_lastPhase == FIRST_PHASE) && (_vitals.HealthRate <= (_spec.TriggerHealthRate + BOUNDARY_TOLERANCE));
        }

        private void TickDormant(double serverTime, int phase)
        {
            // 2페이즈로 넘어간 뒤에는 발동하지 않는다. 경계에서 멈추던 상태도 이때 풀린다
            if (phase != FIRST_PHASE)
            {
                _state = EWolbaekWardState.Completed;
                return;
            }

            if (_vitals.HealthRate > (_spec.TriggerHealthRate + BOUNDARY_TOLERANCE))
            {
                return;
            }

            if ((_preceding != null) && (_preceding.DefersPhaseTransition))
            {
                return;
            }

            Begin(serverTime);
        }

        // 진입한 순간 이전 공격은 정리를 요청해 이미 멈췄다. 전장을 모르거나 후보로 세 지점을 만들 수 없으면 건너뛰지 않고 정지한다
        private void Begin(double serverTime)
        {
            _gate = new BossDamageGate(_resolver);
            _gate.Open();

            _castId           = _gate.IssueInstanceId();
            _cleanupRequested = true;

            if (!_arena.TryGetArena(out Vector3 center, out float radius))
            {
                Debug.LogError("[Boss] 전장 정보를 얻지 못해 색 결계 기믹을 정지한다");
                Halt(EWolbaekWardHaltReason.ArenaUnavailable, serverTime);
                return;
            }

            _anchor      = center;
            _arenaRadius = radius;

            BuildMembers();
            if (_members.Length == 0)
            {
                Fail(EWolbaekWardFail.Wipe, serverTime);
                return;
            }

            if (!TryPlanSites())
            {
                Debug.LogError("[Boss] 검증된 결계 후보로 세 지점을 만들 수 없어 색 결계 기믹을 정지한다");
                Halt(EWolbaekWardHaltReason.SitesInsufficient, serverTime);
                return;
            }

            _start = new WolbaekWardStart(_self.CombatId, _castId, serverTime, _anchor, _spec.GatherRadius, _spec.GatherHoldSeconds,
                _spec.WardRadius, _spec.LinkWarnDistance, _spec.LinkDamageDistance, _startMembers);
            _state = EWolbaekWardState.Gather;
            _listener?.OnWardStarted(in _start);
        }

        // 명단 순서를 섞어 시도마다 다른 사람이 같은 색이 되게 한다. 4명 2대2, 3명 2대1, 2명은 둘 다 같은 색, 1명은 어느 한 색이다.
        // 파티 상한을 넘는 인원은 필수 참가자로 세지 않는다
        private void BuildMembers()
        {
            _idBuffer.Clear();
            _roster.CollectParticipants(_idBuffer);

            int count = _idBuffer.Count;
            if (count > MAX_MEMBERS)
            {
                Debug.LogError($"[Boss] 색 결계 필수 참가자가 상한을 넘었다 - {count}명, 앞의 {MAX_MEMBERS}명만 쓴다");
                count = MAX_MEMBERS;
            }

            int[] order = new int[count];
            for (int i = 0; i < count; ++i)
            {
                order[i] = _idBuffer[i];
            }

            Shuffle(order);

            int redCount = PickRedCount(count);
            _members      = new Member[count];
            _startMembers = new WolbaekWardMember[count];
            _infos        = new BossParticipantInfo[count];
            _present      = new bool[count];

            for (int i = 0; i < count; ++i)
            {
                EWolbaekWardColor color = (i < redCount) ? EWolbaekWardColor.Red : EWolbaekWardColor.Blue;
                _members[i]      = new Member { CombatId = order[i], Color = color };
                _startMembers[i] = new WolbaekWardMember(order[i], color, (int)color);
            }

            _idBuffer.Clear();
            BuildLinks();
        }

        private int PickRedCount(int count)
        {
            if (count == 1)
            {
                return _random.NextInt(COLOR_COUNT);
            }

            if (count == TRIO_COUNT)
            {
                return 1 + _random.NextInt(COLOR_COUNT);
            }

            // 2명은 둘 다 같은 색이다. 서로 연결되어 한 결계에 함께 서면 되고, 어느 색일지는 무작위다
            if (count == PAIR_SIZE)
            {
                return _random.NextInt(COLOR_COUNT) * PAIR_SIZE;
            }

            return count / COLOR_COUNT;
        }

        // 같은 색이 정확히 두 명일 때만 연결한다
        private void BuildLinks()
        {
            ClearLinks();

            for (int color = 0; color < COLOR_COUNT; ++color)
            {
                int first  = NO_MEMBER;
                int second = NO_MEMBER;
                int total  = 0;
                for (int i = 0; i < _members.Length; ++i)
                {
                    if ((int)_members[i].Color != color)
                    {
                        continue;
                    }

                    ++total;
                    if (first == NO_MEMBER)
                    {
                        first = i;
                    }
                    else if (second == NO_MEMBER)
                    {
                        second = i;
                    }
                }

                if (total == PAIR_SIZE)
                {
                    _linkA[color] = first;
                    _linkB[color] = second;
                }
            }
        }

        private void ClearLinks()
        {
            for (int color = 0; color < COLOR_COUNT; ++color)
            {
                _linkA[color]          = NO_MEMBER;
                _linkB[color]          = NO_MEMBER;
                _exceeding[color]      = false;
                _nextLinkDamage[color] = 0d;
                _linkSuppressed[color] = false;
            }
        }

        /****************************************
        *               Site Plan
        ****************************************/

        // 세 지점을 시작할 때 한 번에 정해 둔다. 중간에 후보가 막히는 일을 전투 도중이 아니라 시작에서 알게 하려는 것이다.
        // 후보는 전장 안쪽(결계 반경만큼 안)만 쓴다. 같은 색의 이전 위치(첫 지점은 중앙 결계)에서 새 위치까지의 거리와 두 색 중심 사이
        // 거리 조건을 만족하는 짝을 무작위 순서로 시도하고, 막다른 길은 기억해 다시 파지 않는다
        private bool TryPlanSites()
        {
            _rawCandidates.Clear();
            _candidates.Clear();
            _sites.CollectCandidates(_rawCandidates);

            float limit = Mathf.Max(0f, _arenaRadius - _spec.WardRadius);
            for (int i = 0; i < _rawCandidates.Count; ++i)
            {
                if ((FlatDistance(_rawCandidates[i], _anchor) <= (limit + DISTANCE_EPSILON)) && (_candidates.Count < MAX_CANDIDATES))
                {
                    _candidates.Add(_rawCandidates[i]);
                }
            }

            _rawCandidates.Clear();

            int count = _candidates.Count;
            if (count < COLOR_COUNT)
            {
                return false;
            }

            _planRed   = new Vector3[_spec.StageCount];
            _planBlue  = new Vector3[_spec.StageCount];
            _deadEnds  = new bool[_spec.StageCount, count + 1, count + 1];
            return PlanStage(0, NO_MEMBER, NO_MEMBER);
        }

        private bool PlanStage(int stage, int previousRed, int previousBlue)
        {
            if (stage >= _spec.StageCount)
            {
                return true;
            }

            // 색인 -1은 중앙 결계에서 갈라진 첫 지점이다
            if (_deadEnds[stage, previousRed + 1, previousBlue + 1])
            {
                return false;
            }

            int     count       = _candidates.Count;
            Vector3 redOrigin   = (previousRed == NO_MEMBER) ? _anchor : _candidates[previousRed];
            Vector3 blueOrigin  = (previousBlue == NO_MEMBER) ? _anchor : _candidates[previousBlue];
            List<int> pairs     = new List<int>();

            for (int red = 0; red < count; ++red)
            {
                if (!IsReachable(redOrigin, _candidates[red]))
                {
                    continue;
                }

                for (int blue = 0; blue < count; ++blue)
                {
                    if ((blue == red) || (!IsReachable(blueOrigin, _candidates[blue])))
                    {
                        continue;
                    }

                    if (FlatDistance(_candidates[red], _candidates[blue]) < (_spec.MinWardSeparation - DISTANCE_EPSILON))
                    {
                        continue;
                    }

                    pairs.Add((red * count) + blue);
                }
            }

            int[] order = pairs.ToArray();
            Shuffle(order);

            for (int i = 0; i < order.Length; ++i)
            {
                int red  = order[i] / count;
                int blue = order[i] % count;
                _planRed[stage]  = _candidates[red];
                _planBlue[stage] = _candidates[blue];

                if (PlanStage(stage + 1, red, blue))
                {
                    return true;
                }
            }

            _deadEnds[stage, previousRed + 1, previousBlue + 1] = true;
            return false;
        }

        private bool IsReachable(Vector3 from, Vector3 to)
        {
            float distance = FlatDistance(from, to);
            return (distance <= (_spec.MaxSiteTravel + DISTANCE_EPSILON)) && (distance > MIN_SITE_SHIFT);
        }

        /****************************************
        *                Gather
        ****************************************/

        private void TickGather(double serverTime)
        {
            ReadMembers();

            int living = CountLiving();
            if (living == 0)
            {
                Fail(EWolbaekWardFail.Wipe, serverTime);
                return;
            }

            bool allInPlace = true;
            int  offender   = CombatIds.NONE;
            EWolbaekHoldReset offenderReason = EWolbaekHoldReset.Left;

            for (int i = 0; i < _members.Length; ++i)
            {
                // 부활할 수 없는 사람과 명단에서 사라진 사람은 집결을 기다리지 않는다
                if (!IsRequiredToGather(i))
                {
                    continue;
                }

                bool alive   = _infos[i].LifeState == EParticipantLifeState.Alive;
                bool inPlace = alive && _infos[i].IsConnected && (FlatDistance(_infos[i].Position, _anchor) <= _spec.GatherRadius);
                if (inPlace)
                {
                    continue;
                }

                allInPlace = false;
                if (offender == CombatIds.NONE)
                {
                    offender       = _members[i].CombatId;
                    offenderReason = !alive ? EWolbaekHoldReset.Died : (!_infos[i].IsConnected ? EWolbaekHoldReset.Disconnected : EWolbaekHoldReset.Left);
                }
            }

            UpdateHold(serverTime, allInPlace, offender, offenderReason);

            if ((_hasHold) && (serverTime >= (_holdStart + _spec.GatherHoldSeconds)))
            {
                CompleteGather(serverTime);
            }
        }

        private bool IsRequiredToGather(int index)
        {
            if (!_present[index])
            {
                return false;
            }

            return (_infos[index].LifeState == EParticipantLifeState.Alive) || (_infos[index].IsRevivable);
        }

        private void UpdateHold(double serverTime, bool allInPlace, int offender, EWolbaekHoldReset reason)
        {
            if (allInPlace)
            {
                if (!_hasHold)
                {
                    _hasHold   = true;
                    _holdStart = serverTime;
                    _listener?.OnGatherHoldStarted(_castId, serverTime);
                }

                return;
            }

            if (!_hasHold)
            {
                return;
            }

            _hasHold = false;
            _listener?.OnGatherHoldReset(_castId, serverTime, reason, offender);
        }

        private void CompleteGather(double serverTime)
        {
            _hasHold = false;

            // 집결을 채운 순간부터 쓰러져 있거나 끊긴 사람은 복귀자로 다룬다
            for (int i = 0; i < _members.Length; ++i)
            {
                _members[i].Away = !IsActive(i);
            }

            _listener?.OnGatherCompleted(_castId, serverTime);
            BeginStage(0, serverTime);
        }

        /****************************************
        *                Stage
        ****************************************/

        private void BeginStage(int index, double startTime)
        {
            _stageIndex  = index;
            _stageStart  = startTime;
            _strikeIndex = 0;
            _telegraphed = false;
            _state       = EWolbaekWardState.Search;
            _stage       = new WolbaekWardStage(index, startTime, startTime + _spec.SearchSeconds, _spec.StrikeIntervalSeconds,
                _spec.StrikeCount, _planRed[index], _planBlue[index], _spec.WardRadius);

            _listener?.OnStageStarted(_castId, in _stage);
            if (index > 0)
            {
                _listener?.OnReviveWindowOpened(_castId, startTime);
            }
        }

        private void TickStage(double serverTime)
        {
            AdvanceStage(serverTime);
            EvaluateStage(serverTime);

            if ((_state != EWolbaekWardState.Search) && (_state != EWolbaekWardState.Strike))
            {
                return;
            }

            if (_finalStruck)
            {
                Succeed(serverTime);
            }
        }

        private double StrikeTime(int strikeIndex)
        {
            return _stageStart + _spec.SearchSeconds + (strikeIndex * (double)_spec.StrikeIntervalSeconds);
        }

        // 예고와 판정을 시각 순서대로 모두 처리한다. 지점의 마지막 판정이 끝나면 같은 시각에 다음 지점이 시작된다
        private void AdvanceStage(double serverTime)
        {
            while (((_state == EWolbaekWardState.Search) || (_state == EWolbaekWardState.Strike)) && (!_finalStruck))
            {
                double strikeTime    = StrikeTime(_strikeIndex);
                double telegraphTime = strikeTime - _spec.TelegraphSeconds;

                if ((!_telegraphed) && (serverTime >= telegraphTime))
                {
                    _telegraphed = true;
                    WolbaekWardStrike strike = new WolbaekWardStrike(_stageIndex, _strikeIndex, telegraphTime, strikeTime,
                        _self.Position, _spec.StrikePushDistance);
                    _listener?.OnStrikeTelegraphed(_castId, in strike);
                }

                if (serverTime < strikeTime)
                {
                    return;
                }

                Strike(strikeTime);
                ++_strikeIndex;
                _telegraphed = false;

                if (_strikeIndex < _spec.StrikeCount)
                {
                    continue;
                }

                if ((_stageIndex + 1) < _spec.StageCount)
                {
                    BeginStage(_stageIndex + 1, strikeTime);
                }
                else
                {
                    _finalStruck = true;
                }
            }
        }

        // 전장의 살아 있는 플레이어 모두가 대상이다. 올바른 색 결계 안에 있는 명단 참가자만 피해와 밀려남을 모두 피한다.
        // 한 동작 번호를 모든 대상이 나눠 쓰므로 대상당 한 번만 맞는다. 방어와 반격으로 막을 수 없고 월백 바깥쪽으로 민다
        private void Strike(double strikeTime)
        {
            _state = EWolbaekWardState.Strike;

            _targets.GetLivingPlayers(_hitBuffer);

            int     actionInstanceId = _gate.IssueInstanceId();
            Vector3 origin           = _self.Position;
            int     hitCount         = 0;
            int     protectedCount   = 0;

            for (int i = 0; i < _hitBuffer.Count; ++i)
            {
                ICombatTarget target = _hitBuffer[i];
                int memberIndex = FindMember(target.CombatId);
                if ((memberIndex != NO_MEMBER) && (IsInsideWard(target.Position, _members[memberIndex].Color)))
                {
                    ++protectedCount;
                    continue;
                }

                if (!TryMakeRatioRequest(target.CombatId, _spec.StrikeDamageRate, actionInstanceId, origin, STRIKE_ID,
                    out DamageRequest request))
                {
                    continue;
                }

                if ((!_gate.TryApply(in request, out DamageResult result)) || (!result.IsApplied))
                {
                    continue;
                }

                ++hitCount;

                if (_spec.StrikePushDistance > 0f)
                {
                    _displacer.TryPush(target.CombatId, Flatten(target.Position - origin, Vector3.forward), _spec.StrikePushDistance);
                }
            }

            _hitBuffer.Clear();
            _listener?.OnStrikeResolved(_castId, _stageIndex, _strikeIndex, strikeTime, hitCount, protectedCount);
        }

        private bool IsInsideWard(Vector3 position, EWolbaekWardColor color)
        {
            return FlatDistance(position, _stage.GetCenter(color)) <= _stage.Radius;
        }

        // 최대 체력에 비율을 곱한 방어 전 피해 요청을 만든다, 최대 체력을 모르면 만들지 못한다
        private bool TryMakeRatioRequest(int combatId, float rate, int actionInstanceId, Vector3 source, string definitionId,
            out DamageRequest request)
        {
            if (!_health.TryGetMaxHealth(combatId, out float maxHealth))
            {
                if (!_healthMissingLogged)
                {
                    _healthMissingLogged = true;
                    Debug.LogError($"[Boss] 색 결계 피해 대상의 최대 체력을 읽지 못했다 - {combatId}");
                }

                request = default;
                return false;
            }

            request = new DamageRequest(_self.CombatId, combatId, _castId, actionInstanceId, rate * maxHealth,
                EDamageFlags.Unblockable, source, definitionId);
            return true;
        }

        /****************************************
        *            Participants
        ****************************************/

        // 지점 진행 중 참가자 확인. 전원 사망은 실패, 그 밖의 사망은 실패가 아니다
        private void EvaluateStage(double serverTime)
        {
            if ((_state != EWolbaekWardState.Search) && (_state != EWolbaekWardState.Strike))
            {
                return;
            }

            ReadMembers();

            if (CountLiving() == 0)
            {
                Fail(EWolbaekWardFail.Wipe, serverTime);
                return;
            }

            UpdateReturns(serverTime);
            UpdateLinks(serverTime);
        }

        // 쓰러지거나 끊긴 사람이 다시 돌아오면 합류 유예를 준다. 같은 색 동료가 없으면 유예가 의미 없어 주지 않는다
        private void UpdateReturns(double serverTime)
        {
            for (int i = 0; i < _members.Length; ++i)
            {
                if (!IsActive(i))
                {
                    if (_members[i].HasGrace)
                    {
                        EndGrace(i, serverTime, EWolbaekGraceEnd.Interrupted);
                    }

                    _members[i].Away = true;
                    continue;
                }

                if ((_members[i].HasGrace) && (serverTime >= _members[i].GraceEnd))
                {
                    EndGrace(i, serverTime, EWolbaekGraceEnd.Expired);
                }

                if (!_members[i].Away)
                {
                    continue;
                }

                _members[i].Away = false;
                if ((_linkA[(int)_members[i].Color] != NO_MEMBER) && (_spec.RejoinGraceSeconds > 0f))
                {
                    StartGrace(i, serverTime);
                }
            }
        }

        private void StartGrace(int index, double serverTime)
        {
            _members[index].HasGrace = true;
            _members[index].GraceEnd = serverTime + _spec.RejoinGraceSeconds;
            _listener?.OnGraceStarted(_castId, _members[index].CombatId, _members[index].GraceEnd);
        }

        private void EndGrace(int index, double serverTime, EWolbaekGraceEnd reason)
        {
            _members[index].HasGrace = false;
            _members[index].GraceEnd = 0d;
            _listener?.OnGraceEnded(_castId, serverTime, _members[index].CombatId, reason);
        }

        private bool InGrace(int index, double serverTime)
        {
            return _members[index].HasGrace && (serverTime < _members[index].GraceEnd);
        }

        // 같은 색 두 명의 거리 피해. 동료가 쓰러지거나 끊기면 중단하고 처음부터 센다. 4m를 넘은 채 온전한 1초가 지나야 첫 피해가 나고
        // 이후 매초 그때 거리의 구간 비율로 둘 다 맞는다. 4m 이내로 모이면 즉시 멈추고 유예도 끝난다.
        // 유예 중에는 거리를 넘은 시간은 재지만 피해만 미룬다. 유예가 끝난 뒤에도 멀면 그때부터 둘 다 맞는다
        private void UpdateLinks(double serverTime)
        {
            for (int color = 0; color < COLOR_COUNT; ++color)
            {
                int a = _linkA[color];
                int b = _linkB[color];
                if (a == NO_MEMBER)
                {
                    continue;
                }

                if ((!IsActive(a)) || (!IsActive(b)))
                {
                    _exceeding[color]      = false;
                    _linkSuppressed[color] = false;
                    continue;
                }

                float distance = FlatDistance(_infos[a].Position, _infos[b].Position);
                if (distance <= _spec.LinkDamageDistance)
                {
                    _exceeding[color]      = false;
                    _linkSuppressed[color] = false;
                    EndGraceIfAny(a, serverTime);
                    EndGraceIfAny(b, serverTime);
                    continue;
                }

                if (!_exceeding[color])
                {
                    _exceeding[color]      = true;
                    _nextLinkDamage[color] = serverTime + _spec.LinkTickSeconds;
                }

                if ((InGrace(a, serverTime)) || (InGrace(b, serverTime)))
                {
                    _linkSuppressed[color] = true;
                    continue;
                }

                // 유예가 끝난 틱에는 미뤄 둔 피해를 한 번만 낸다, 유예 동안 밀린 횟수를 몰아서 내지 않는다
                if (_linkSuppressed[color])
                {
                    _linkSuppressed[color] = false;
                    _nextLinkDamage[color] = Math.Max(_nextLinkDamage[color], serverTime);
                }

                int applied = 0;
                while ((serverTime >= _nextLinkDamage[color]) && (applied < MAX_LINK_CATCHUP))
                {
                    ApplyLinkDamage(a, b, distance, serverTime);
                    _nextLinkDamage[color] += _spec.LinkTickSeconds;
                    ++applied;
                }

                if (serverTime >= _nextLinkDamage[color])
                {
                    _nextLinkDamage[color] = serverTime + _spec.LinkTickSeconds;
                }
            }
        }

        private void EndGraceIfAny(int index, double serverTime)
        {
            if (_members[index].HasGrace)
            {
                EndGrace(index, serverTime, EWolbaekGraceEnd.Joined);
            }
        }

        private void ApplyLinkDamage(int a, int b, float distance, double serverTime)
        {
            float rate = _spec.GetLinkDamageRate(distance);
            if (!(rate > 0f))
            {
                return;
            }

            int actionInstanceId = _gate.IssueInstanceId();
            ApplyLinkDamageTo(a, b, rate, actionInstanceId);
            ApplyLinkDamageTo(b, a, rate, actionInstanceId);
            _listener?.OnLinkDamaged(_castId, serverTime, _members[a].CombatId, _members[b].CombatId, distance, rate);
        }

        private void ApplyLinkDamageTo(int target, int partner, float rate, int actionInstanceId)
        {
            if (TryMakeRatioRequest(_members[target].CombatId, rate, actionInstanceId, _infos[partner].Position, LINK_ID,
                out DamageRequest request))
            {
                _gate.TryApply(in request, out DamageResult _);
            }
        }

        // 참가자 상태를 한 번 읽어 이번 틱의 판정이 모두 같은 값을 보게 한다
        private void ReadMembers()
        {
            for (int i = 0; i < _members.Length; ++i)
            {
                _present[i] = _roster.TryGetParticipant(_members[i].CombatId, out _infos[i]);
            }
        }

        private int CountLiving()
        {
            int living = 0;
            for (int i = 0; i < _members.Length; ++i)
            {
                if (_present[i] && (_infos[i].LifeState == EParticipantLifeState.Alive))
                {
                    ++living;
                }
            }

            return living;
        }

        // 살아 있고 접속 중이다
        private bool IsActive(int index)
        {
            return _present[index] && (_infos[index].LifeState == EParticipantLifeState.Alive) && _infos[index].IsConnected;
        }

        private int FindMember(int combatId)
        {
            for (int i = 0; i < _members.Length; ++i)
            {
                if (_members[i].CombatId == combatId)
                {
                    return i;
                }
            }

            return NO_MEMBER;
        }

        /****************************************
        *               Outcome
        ****************************************/

        private void Succeed(double serverTime)
        {
            _gate.CloseForGood();

            _hasHold = false;
            _state   = EWolbaekWardState.Completed;
            _listener?.OnWardSucceeded(_castId, serverTime);
        }

        private void Fail(EWolbaekWardFail reason, double serverTime)
        {
            _gate.CloseForGood();

            _hasHold    = false;
            _failReason = reason;
            _state      = EWolbaekWardState.Failed;
            _listener?.OnWardFailed(_castId, serverTime, reason);
        }

        // 성공도 실패도 아니다. 보스는 무적으로 남고 일반 선택은 열리지 않으며 표시를 정리하고 원인만 알린다
        private void Halt(EWolbaekWardHaltReason reason, double serverTime)
        {
            if (_gate != null)
            {
                _gate.CloseForGood();
            }

            _hasHold    = false;
            _haltReason = reason;
            _state      = EWolbaekWardState.Halted;
            _listener?.OnWardHalted(_castId, serverTime, reason);
        }

        private void Shuffle<T>(T[] values)
        {
            for (int i = values.Length - 1; i > 0; --i)
            {
                int swap = _random.NextInt(i + 1);
                T temp   = values[i];
                values[i]    = values[swap];
                values[swap] = temp;
            }
        }

        private static float FlatDistance(Vector3 from, Vector3 to)
        {
            float dx = from.x - to.x;
            float dz = from.z - to.z;
            return Mathf.Sqrt((dx * dx) + (dz * dz));
        }

        private static Vector3 Flatten(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            return (direction.sqrMagnitude < MIN_DIRECTION_SQR) ? fallback : direction.normalized;
        }
    }
}
