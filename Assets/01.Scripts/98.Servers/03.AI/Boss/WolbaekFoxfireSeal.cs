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
    // 월백 세 번째 체력 기믹 "여우불 유도와 봉인 핵"의 서버 진행이다. 보스 두뇌의 기믹 훅(BossBrain.AddGimmick)에 연결되며
    // 전환 판정보다 먼저 Tick이 불리고 진입 순간에 두뇌가 진행 중인 공격과 지속물을 정리한다(IBossGimmickCleanup).
    // 하드 상시 소형 낙하 여우불은 이 기믹과 별개이며 아직 구현되지 않았다. 이 클래스의 추적 여우불과 섞지 않는다.
    //
    // 경계에서 멈추기와 무적은 이 클래스가 계산만 하고(IBossDamageCap) 실제로 피해를 깎는 보스 몸체와 피해 판정기가 읽어야 닿는다.
    // 75% 부적진과 50% 색 결계도 같은 계약이므로 셋을 함께 읽게 하려면 BossDamageCapComposite를 거친다. 이 클래스는 보스 체력을 직접 바꾸지 않는다.
    //
    // 앞선 기믹(precedingGimmick, 50% 색 결계)이 전환을 보류하는 동안에는 체력이 경계에 닿았어도 시작하지 않고 기다린다.
    // 앞선 기믹은 Tick 순서상 이 기믹보다 먼저 등록해야 끝난 같은 틱에 이어서 시작하며, 반대 순서면 한 틱 늦게 시작한다.
    //
    // 한 차례는 준비, 추적, 낙하 위치 고정 뒤 탈출, 낙하와 핵 파괴 판정, 정리 순서이며 정리가 끝나는 시각에 다음 차례가 시작된다.
    // 차례의 핵 수와 추적 여우불 수는 시작 순간 출정 명단의 생존자 수(최대 4)이고 도중에 바뀌지 않는다. 사망 중인 참가자와 도중 부활자는
    // 그 차례에 세지 않으며 접속이 끊긴 생존자는 센다. 추적 대상이 쓰러지거나 끊기면 여우불은 마지막 서버 위치에 머물고 다시 따라가지 않으며
    // 예정 시각에 그 위치에서 폭발한다. 거친 틱에서도 예고와 판정을 시각 순서대로 모두 처리하지만 과거 위치를 기록하지 않아 현재 위치로 판정한다.
    //
    // 한 틱의 순서는 참가자 확인(전멸이면 실패), 대상 관찰, 시각 순서의 사건 처리, 사건 뒤 전멸 재확인, 성공과 제한 시간 확인이다.
    // 사건은 제한 시간 이하의 시각만 처리하므로 제한 시간과 마지막 성공이 같은 틱이면 성공이 이기고, 전멸은 성공보다 앞선다.
    //
    // 정지(Halted)는 기믹을 건너뛰는 것도 성공이나 실패도 아니다. 보스는 무적으로 남고 일반 선택도 열리지 않으며 원인만 알린다.
    // 피해는 이 기믹 전용 게이트로만 내고 성공, 실패, 정지에서 영구히 닫는다. 보스전 실패는 세션이 정하므로 이 클래스는 알리기만 한다.
    // 최대 체력 비율 피해는 방어 전 값이며 시작 인원별 공격 피해 배율을 곱하지 않는다. 배율을 곱하는 판정기를 거치면 계약 위반이므로
    // 배율 이전의 판정기를 넘겨야 한다.
    //
    // 반격에서 다음 경계(하드의 0%)에 닿으면 정확히 그 경계에 멈추고 사망을 보류한 채 다음 기믹의 이어받기를 기다린다(IBossBoundaryHandoff).
    // 다음 경계가 없는 설정(이지)은 반격 중에도 기존 처치 규칙을 그대로 따른다
    /// <summary> 월백 여우불 유도와 봉인 핵 기믹, 25% 경계에서 한 번 발동해 핵을 모두 부수는 차례를 필요한 횟수만큼 누적하면 성공한다 </summary>
    public sealed class WolbaekFoxfireSeal : IBossGimmick, IBossGimmickCleanup, IBossDamageCap, IBossDeathHold, IBossBoundaryHandoff
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int    FIRST_PHASE        = 1;
        private const int    MAX_FOXFIRES       = 4;
        private const int    MAX_CANDIDATES     = 64;
        private const int    PICK_ATTEMPTS      = 8;
        private const int    NO_INDEX           = -1;
        private const int    EVENT_BUFFER       = 16;
        private const float  BOUNDARY_TOLERANCE = 0.00001f;  // 체력 비율, 경계에 닿았다고 보는 부동소수 여유
        private const float  DISTANCE_EPSILON   = 0.001f;    // m, 거리 조건 경계의 부동소수 여유
        private const float  ANGLE_EPSILON      = 0.01f;     // 도, 각도 조건 경계의 부동소수 여유
        private const float  TIME_EPSILON       = 0.000001f; // 초, 제한 시간 경계의 부동소수 여유
        private const float  MIN_DIRECTION_SQR  = 0.000001f; // m^2, 이보다 가까우면 방향을 정할 수 없다
        private const float  FULL_TURN_DEGREES  = 360f;
        private const string BLAST_ID           = "wolbaek_seal_blast";
        private const string SWEEP_ID           = "wolbaek_seal_sweep";

        /****************************************
        *                Fields
        ****************************************/

        // 같은 시각이면 아래 순서로 처리한다. 값이 작을수록 먼저다
        private enum EEventKind
        {
            TrackStart    = 0,
            SweepTelegraph = 1,
            SweepStrike   = 2,
            Lock          = 3,
            Resolve       = 4,
            TurnEnd       = 5
        }

        private struct TurnEvent
        {
            public double     Time;
            public EEventKind Kind;
            public int        Index;
        }

        private struct Foxfire
        {
            public int     TargetId;
            public Vector3 LastPosition;
            public Vector3 Position;
            public bool    Detached;
        }

        private readonly WolbaekFoxfireSealSpec  _spec;
        private readonly ICombatTarget           _self;
        private readonly IBossVitals             _vitals;
        private readonly IBossParticipantRoster  _roster;
        private readonly IBossParticipantHealth  _health;
        private readonly IBossArenaSource        _arena;
        private readonly IWolbaekSealSiteSource  _sites;
        private readonly IDamageResolver         _resolver;
        private readonly ICombatTargetQuery      _targets;
        private readonly IBossRandom             _random;
        private readonly IBossHitDisplacer       _displacer;
        private readonly IBossGimmick            _preceding;
        private readonly IWolbaekFoxfireSealListener _listener;
        private readonly List<int>               _idBuffer      = new List<int>();
        private readonly List<int>               _ids           = new List<int>();
        private readonly List<BossParticipantInfo> _infos       = new List<BossParticipantInfo>();
        private readonly List<ICombatTarget>     _hitBuffer     = new List<ICombatTarget>();
        private readonly List<Vector3>           _rawCandidates = new List<Vector3>();
        private readonly List<Vector3>           _candidates    = new List<Vector3>();
        private readonly List<Vector3>           _picked        = new List<Vector3>(MAX_FOXFIRES);
        private readonly List<TurnEvent>         _events        = new List<TurnEvent>(EVENT_BUFFER);

        private EWolbaekSealState         _state = EWolbaekSealState.Dormant;
        private EWolbaekSealStep          _step;
        private EWolbaekSealFail          _failReason;
        private EWolbaekSealHaltReason    _haltReason;
        private BossDamageGate            _gate;
        private WolbaekSealStart          _start;
        private WolbaekSealTurn           _turn;
        private WolbaekSealCore[]         _cores = new WolbaekSealCore[0];
        private WolbaekSealFoxfire[]      _turnFoxfires = new WolbaekSealFoxfire[0];
        private Foxfire[]                 _foxfires = new Foxfire[0];
        private bool[]                    _destroyed = new bool[0];
        private WolbaekSealSweep[]        _sweeps = new WolbaekSealSweep[0];
        private bool[]                    _sweepTelegraphed = new bool[0];
        private bool[]                    _sweepStruck = new bool[0];
        private Vector3                   _anchor;
        private Vector3                   _sweepFirstDirection;
        private float                     _arenaRadius;
        private int                       _castId;
        private int                       _turnIndex;
        private int                       _successCount;
        private int                       _eventCursor;
        private int                       _lastPhase;
        private double                    _limitEnd;
        private double                    _counterEnd;
        private bool                      _foxfiresLocked;
        private bool                      _thirdSuccess;
        private bool                      _cleanupRequested;
        private bool                      _healthMissingLogged;

        /****************************************
        *              Properties
        ****************************************/

        public EWolbaekSealState State => _state;

        /// <summary> 지금 차례의 단계, 진행 중이 아니면 의미가 없다 </summary>
        public EWolbaekSealStep Step => _step;

        /// <summary> 실패했다면 그 이유, 실패 전에는 의미가 없다 </summary>
        public EWolbaekSealFail FailReason => _failReason;

        /// <summary> 정지했다면 그 이유, 정지 전에는 의미가 없다 </summary>
        public EWolbaekSealHaltReason HaltReason => _haltReason;

        /// <summary> 이 기믹의 시전 번호, 진입 전에는 0이다 </summary>
        public int CastInstanceId => _castId;

        /// <summary> 지금까지 누적한 성공 횟수 </summary>
        public int SuccessCount => _successCount;

        /// <summary> 지금 차례 번호, 시작한 차례가 없으면 0이다 </summary>
        public int TurnIndex => _turnIndex;

        /// <summary> 지금 차례, 시작한 차례가 없으면 기본값이다 </summary>
        public WolbaekSealTurn Turn => _turn;

        /// <summary> 지금 차례의 핵 </summary>
        public IReadOnlyList<WolbaekSealCore> Cores => _cores;

        /// <summary> 전체 제한 시간이 끝나는 서버 시각, 진입 전에는 0이다 </summary>
        public double LimitEndServerTime => _limitEnd;

        /// <summary> 반격이 끝나는 서버 시각, 성공 전에는 0이다 </summary>
        public double CounterEndServerTime => _counterEnd;

        public bool DefersPhaseTransition
        {
            get
            {
                switch (_state)
                {
                    case EWolbaekSealState.Active:
                    case EWolbaekSealState.Counter:
                    case EWolbaekSealState.NextBoundaryPending:
                    case EWolbaekSealState.Failed:
                    case EWolbaekSealState.Halted:
                        return true;
                    case EWolbaekSealState.Dormant:
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
                    case EWolbaekSealState.Dormant:
                        return _vitals.HealthRate <= (_spec.TriggerHealthRate + BOUNDARY_TOLERANCE);
                    case EWolbaekSealState.Active:
                    case EWolbaekSealState.NextBoundaryPending:
                    case EWolbaekSealState.Failed:
                    case EWolbaekSealState.Halted:
                        return true;
                    case EWolbaekSealState.Counter:
                        return _spec.HasNextBoundary && (_vitals.HealthRate <= (_spec.NextBoundaryHealthRate + BOUNDARY_TOLERANCE));
                    default:
                        return false;
                }
            }
        }

        /// <summary> 하드에서 다음 경계가 0%일 때 반격과 인계 대기 동안 체력 0을 사망으로 확정하지 않는다 </summary>
        public bool IsDeathHeld
        {
            get
            {
                bool holdsZero = _spec.HasNextBoundary && (_spec.NextBoundaryHealthRate <= BOUNDARY_TOLERANCE);
                return holdsZero && ((_state == EWolbaekSealState.Counter) || (_state == EWolbaekSealState.NextBoundaryPending));
            }
        }

        public bool HasPendingHandoff => _state == EWolbaekSealState.NextBoundaryPending;

        /****************************************
        *                Methods
        ****************************************/

        // unscaledResolver는 시작 인원 피해 배율을 곱하기 전의 판정기여야 한다(비율 피해에 배율을 곱하지 않는 확정 계약).
        // health는 최대 체력 비율 피해를 방어 전 값으로 바꾸는 데 쓰고 출정 명단을 읽는 쪽이 같이 구현한다.
        // sites는 맵에서 검증된 핵 후보만 준다. 하드 휩쓸기(spec.Sweep)가 있으면 맞은 대상을 미는 displacer가 필요하다.
        // precedingGimmick은 이 기믹보다 먼저 같은 보스에 얹은 체력 기믹(50% 색 결계)이며 없으면 null이다
        public WolbaekFoxfireSeal(WolbaekFoxfireSealSpec spec, ICombatTarget self, IBossVitals vitals,
            IBossParticipantRoster roster, IBossParticipantHealth health, IBossArenaSource arena, IWolbaekSealSiteSource sites,
            IDamageResolver unscaledResolver, ICombatTargetQuery targets, IBossRandom random, IBossHitDisplacer displacer = null,
            IBossGimmick precedingGimmick = null, IWolbaekFoxfireSealListener listener = null)
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
            _displacer = displacer;
            _preceding = precedingGimmick;
            _listener  = listener;

            if ((spec.Sweep != null) && (displacer == null))
            {
                throw new ArgumentNullException(nameof(displacer), "하드 휩쓸기는 맞은 대상을 미는 창구가 필요하다");
            }
        }

        public void Tick(double serverTime, int phase)
        {
            _lastPhase = phase;

            switch (_state)
            {
                case EWolbaekSealState.Dormant:
                    TickDormant(serverTime);
                    break;
                case EWolbaekSealState.Active:
                    TickActive(serverTime);
                    break;
                case EWolbaekSealState.Counter:
                    TickCounter(serverTime);
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
            if ((_state == EWolbaekSealState.Completed) || (_state == EWolbaekSealState.Stopped))
            {
                return;
            }

            bool hadDisplay = _state == EWolbaekSealState.Active;

            if (_gate != null)
            {
                _gate.CloseForGood();
            }

            _state            = EWolbaekSealState.Stopped;
            _cleanupRequested = false;

            if (hadDisplay)
            {
                _listener?.OnSealCleared(_castId, reason);
            }
        }

        // 체력이 경계에 닿은 상태에서 들어오는 다른 피해가 경계 아래로 내려가지 못하게 하고, 초과분은 버린다.
        // 기믹 중 무적 구간에서는 모두 버린다. 반격 중에는 다음 경계가 있을 때만 그 경계에서 멈춘다. 반환 0은 이 피해가 없던 것이라는 뜻이다
        public float CapDamage(float currentHealth, float maxHealth, float damage)
        {
            if (!(damage > 0f))
            {
                return 0f;
            }

            float boundaryRate;
            switch (_state)
            {
                case EWolbaekSealState.Active:
                case EWolbaekSealState.NextBoundaryPending:
                case EWolbaekSealState.Failed:
                case EWolbaekSealState.Halted:
                    return 0f;
                case EWolbaekSealState.Dormant:
                    boundaryRate = _spec.TriggerHealthRate;
                    break;
                case EWolbaekSealState.Counter:
                    if (!_spec.HasNextBoundary)
                    {
                        return damage;
                    }

                    boundaryRate = _spec.NextBoundaryHealthRate;
                    break;
                default:
                    return damage;
            }

            float allowed = currentHealth - (boundaryRate * maxHealth);
            if (!(allowed > Constants.Combat.HEALTH_EPSILON))
            {
                return 0f;
            }

            return Math.Min(damage, allowed);
        }

        public bool TryTakeHandoff()
        {
            if (_state != EWolbaekSealState.NextBoundaryPending)
            {
                return false;
            }

            _state = EWolbaekSealState.Completed;
            return true;
        }

        /// <summary> 지금 차례의 핵이 이미 파괴됐다 </summary>
        public bool IsCoreDestroyed(int index)
        {
            return (index >= 0) && (index < _destroyed.Length) && _destroyed[index];
        }

        /// <summary> 늦게 접속하거나 재접속한 클라이언트에 줄 현재 값, 진행 중이거나 반격 중일 때만 참이다 </summary>
        public bool TryGetSnapshot(out WolbaekSealSnapshot snapshot)
        {
            if ((_state != EWolbaekSealState.Active) && (_state != EWolbaekSealState.Counter))
            {
                snapshot = default;
                return false;
            }

            Vector3[] positions = new Vector3[_foxfires.Length];
            bool[]    detached  = new bool[_foxfires.Length];
            for (int i = 0; i < _foxfires.Length; ++i)
            {
                positions[i] = _foxfires[i].Position;
                detached[i]  = _foxfires[i].Detached;
            }

            bool hasPending = TryFindPendingSweep(out WolbaekSealSweep pending);
            snapshot = new WolbaekSealSnapshot(_state, _step, in _start, in _turn, _successCount, positions, detached,
                _foxfiresLocked, hasPending, in pending, (_state == EWolbaekSealState.Counter) ? _counterEnd : 0d);
            return true;
        }

        // 앞선 기믹이 전환을 보류하는 동안 체력이 경계에 닿았다면 시작하지 않고 기다린다
        private bool IsWaitingToStart()
        {
            return (_lastPhase == FIRST_PHASE) && (_vitals.HealthRate <= (_spec.TriggerHealthRate + BOUNDARY_TOLERANCE));
        }

        private void TickDormant(double serverTime)
        {
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

        // 진입한 순간 이전 공격은 정리를 요청해 이미 멈췄다. 전장을 모르거나 후보로 핵을 놓을 수 없으면 건너뛰지 않고 정지한다
        private void Begin(double serverTime)
        {
            _gate = new BossDamageGate(_resolver);
            _gate.Open();

            _castId           = _gate.IssueInstanceId();
            _cleanupRequested = true;

            if (!_arena.TryGetArena(out Vector3 center, out float radius))
            {
                Debug.LogError("[Boss] 전장 정보를 얻지 못해 봉인 핵 기믹을 정지한다");
                Halt(EWolbaekSealHaltReason.ArenaUnavailable, serverTime);
                return;
            }

            _anchor      = center;
            _arenaRadius = radius;

            ReadRoster();
            if (CountLiving() == 0)
            {
                Fail(EWolbaekSealFail.Wipe, serverTime);
                return;
            }

            // 가장 많이 필요한 경우(명단 전원 생존)의 핵을 놓을 수 있어야 시작한다. 중간에 막히는 일을 시작에서 알게 하려는 것이다
            BuildCandidates();
            int required = Math.Min(MAX_FOXFIRES, _ids.Count);
            if (!TryPickCores(required))
            {
                Debug.LogError("[Boss] 검증된 핵 후보로 한 차례의 핵을 놓을 수 없어 봉인 핵 기믹을 정지한다");
                Halt(EWolbaekSealHaltReason.SitesInsufficient, serverTime);
                return;
            }

            _limitEnd = serverTime + _spec.TimeLimitSeconds;
            _start    = new WolbaekSealStart(_self.CombatId, _castId, serverTime, _limitEnd, _spec.RequiredSuccesses,
                _spec.PrepareSeconds, _spec.TrackSeconds, _spec.EscapeSeconds, _spec.CleanupSeconds, _spec.BlastRadius,
                _spec.CoreHitRadius, _anchor);
            _state     = EWolbaekSealState.Active;
            _turnIndex = NO_INDEX;
            _listener?.OnSealStarted(in _start);

            BeginTurn(serverTime);
        }

        /****************************************
        *               Site Pick
        ****************************************/

        // 전장 안쪽 후보만 쓴다
        private void BuildCandidates()
        {
            _rawCandidates.Clear();
            _candidates.Clear();
            _sites.CollectCandidates(_rawCandidates);

            for (int i = 0; i < _rawCandidates.Count; ++i)
            {
                if ((FlatDistance(_rawCandidates[i], _anchor) <= (_arenaRadius + DISTANCE_EPSILON)) && (_candidates.Count < MAX_CANDIDATES))
                {
                    _candidates.Add(_rawCandidates[i]);
                }
            }

            _rawCandidates.Clear();
        }

        // 후보를 섞어 앞에서부터 최소 간격을 지키는 것만 고른다. 한 번에 못 채우면 몇 차례 다시 섞는다
        private bool TryPickCores(int count)
        {
            _picked.Clear();
            if (count <= 0)
            {
                return true;
            }

            if (_candidates.Count < count)
            {
                return false;
            }

            int[] order = new int[_candidates.Count];
            for (int attempt = 0; attempt < PICK_ATTEMPTS; ++attempt)
            {
                for (int i = 0; i < order.Length; ++i)
                {
                    order[i] = i;
                }

                Shuffle(order);

                _picked.Clear();
                for (int i = 0; (i < order.Length) && (_picked.Count < count); ++i)
                {
                    Vector3 candidate = _candidates[order[i]];
                    if (IsSeparatedFromPicked(candidate))
                    {
                        _picked.Add(candidate);
                    }
                }

                if (_picked.Count == count)
                {
                    return true;
                }
            }

            _picked.Clear();
            return false;
        }

        private bool IsSeparatedFromPicked(Vector3 candidate)
        {
            for (int i = 0; i < _picked.Count; ++i)
            {
                if (FlatDistance(candidate, _picked[i]) < (_spec.MinCoreSeparation - DISTANCE_EPSILON))
                {
                    return false;
                }
            }

            return true;
        }

        /****************************************
        *                 Turn
        ****************************************/

        // 이 시각의 명단을 새로 읽어 생존자 수만큼 핵과 추적 여우불을 만든다. 시각표는 차례 시작 시각을 기준으로 한 번에 정해 둔다
        private void BeginTurn(double startTime)
        {
            ReadRoster();

            int count = Math.Min(MAX_FOXFIRES, CountLiving());
            if (count == 0)
            {
                Fail(EWolbaekSealFail.Wipe, startTime);
                return;
            }

            if (!TryPickCores(count))
            {
                Debug.LogError("[Boss] 검증된 핵 후보로 이번 차례의 핵을 놓을 수 없어 봉인 핵 기믹을 정지한다");
                Halt(EWolbaekSealHaltReason.SitesInsufficient, startTime);
                return;
            }

            ++_turnIndex;

            _cores        = new WolbaekSealCore[count];
            _destroyed    = new bool[count];
            _foxfires     = new Foxfire[count];
            _turnFoxfires = new WolbaekSealFoxfire[count];

            for (int i = 0; i < count; ++i)
            {
                _cores[i] = new WolbaekSealCore(i, _picked[i]);
            }

            int next = 0;
            for (int i = 0; (i < _infos.Count) && (next < count); ++i)
            {
                if (_infos[i].LifeState != EParticipantLifeState.Alive)
                {
                    continue;
                }

                _foxfires[next] = new Foxfire
                {
                    TargetId     = _ids[i],
                    LastPosition = _infos[i].Position,
                    Position     = _infos[i].Position,
                    Detached     = !_infos[i].IsConnected
                };
                _turnFoxfires[next] = new WolbaekSealFoxfire(next, _ids[i]);
                ++next;
            }

            _picked.Clear();

            double trackStart = startTime + _spec.PrepareSeconds;
            double lockTime   = trackStart + _spec.TrackSeconds;
            double resolve    = lockTime + _spec.EscapeSeconds;
            double end        = resolve + _spec.CleanupSeconds;

            BuildEvents(startTime, trackStart, lockTime, resolve, end);

            _step           = EWolbaekSealStep.Prepare;
            _foxfiresLocked = false;
            _turn           = new WolbaekSealTurn(_turnIndex, startTime, trackStart, lockTime, resolve, end, _cores, _turnFoxfires);
            _listener?.OnTurnStarted(_castId, in _turn);

            // 접속이 끊긴 생존자도 여우불을 받는다. 처음부터 그 자리에 머물러 있으므로 클라이언트에 알려 둔다
            for (int i = 0; i < _foxfires.Length; ++i)
            {
                if (_foxfires[i].Detached)
                {
                    _listener?.OnFoxfireDetached(_castId, _turnIndex, i, startTime, _foxfires[i].LastPosition);
                }
            }
        }

        private void BuildEvents(double startTime, double trackStart, double lockTime, double resolve, double end)
        {
            _events.Clear();
            _eventCursor = 0;

            AddEvent(trackStart, EEventKind.TrackStart, 0);

            WolbaekSealSweepSpec sweep = _spec.Sweep;
            int sweepCount = (sweep == null) ? 0 : sweep.Count;
            _sweeps           = new WolbaekSealSweep[sweepCount];
            _sweepTelegraphed = new bool[sweepCount];
            _sweepStruck      = new bool[sweepCount];

            if (sweep != null)
            {
                float[] offsets = sweep.HitOffsetsSeconds;
                for (int i = 0; i < offsets.Length; ++i)
                {
                    double strike = trackStart + offsets[i];
                    AddEvent(strike - sweep.TelegraphSeconds, EEventKind.SweepTelegraph, i);
                    AddEvent(strike, EEventKind.SweepStrike, i);
                }
            }

            AddEvent(lockTime, EEventKind.Lock, 0);
            AddEvent(resolve, EEventKind.Resolve, 0);
            AddEvent(end, EEventKind.TurnEnd, 0);
        }

        // 시각, 종류, 번호 순으로 정렬해 끼워 넣는다
        private void AddEvent(double time, EEventKind kind, int index)
        {
            TurnEvent added = new TurnEvent { Time = time, Kind = kind, Index = index };

            int position = _events.Count;
            while (position > 0)
            {
                TurnEvent previous = _events[position - 1];
                bool later = (previous.Time > added.Time)
                    || ((previous.Time == added.Time) && (((int)previous.Kind > (int)added.Kind)
                        || (((int)previous.Kind == (int)added.Kind) && (previous.Index > added.Index))));
                if (!later)
                {
                    break;
                }

                --position;
            }

            _events.Insert(position, added);
        }

        private void TickActive(double serverTime)
        {
            ReadRoster();
            if (CountLiving() == 0)
            {
                Fail(EWolbaekSealFail.Wipe, serverTime);
                return;
            }

            ObserveTargets(serverTime);

            bool executed = false;
            while ((_state == EWolbaekSealState.Active) && (_eventCursor < _events.Count) && (!_thirdSuccess))
            {
                TurnEvent next = _events[_eventCursor];
                if ((next.Time > serverTime) || (next.Time > (_limitEnd + TIME_EPSILON)))
                {
                    break;
                }

                // 제한 시간이 끝나는 시각에 시작하는 차례는 의미가 없다
                if ((next.Kind == EEventKind.TurnEnd) && (next.Time >= (_limitEnd - TIME_EPSILON)))
                {
                    break;
                }

                ++_eventCursor;
                executed = true;
                Execute(next);
            }

            if (_state != EWolbaekSealState.Active)
            {
                return;
            }

            // 이번 틱의 폭발과 휩쓸기로 마지막 생존자가 쓰러졌다면 같은 틱의 성공보다 전멸이 앞선다
            if (executed)
            {
                ReadRoster();
                if (CountLiving() == 0)
                {
                    Fail(EWolbaekSealFail.Wipe, serverTime);
                    return;
                }
            }

            if (_thirdSuccess)
            {
                Succeed(serverTime);
                return;
            }

            if (serverTime >= _limitEnd)
            {
                Fail(EWolbaekSealFail.TimeExpired, serverTime);
            }
        }

        private void Execute(TurnEvent turnEvent)
        {
            switch (turnEvent.Kind)
            {
                case EEventKind.TrackStart:
                    BeginTrack(turnEvent.Time);
                    break;
                case EEventKind.SweepTelegraph:
                    TelegraphSweep(turnEvent.Index, turnEvent.Time);
                    break;
                case EEventKind.SweepStrike:
                    StrikeSweep(turnEvent.Index, turnEvent.Time);
                    break;
                case EEventKind.Lock:
                    LockFoxfires(turnEvent.Time);
                    break;
                case EEventKind.Resolve:
                    ResolveTurn(turnEvent.Time);
                    break;
                default:
                    BeginTurn(turnEvent.Time);
                    break;
            }
        }

        /****************************************
        *              Foxfires
        ****************************************/

        // 추적 대상이 쓰러지거나 끊기면 마지막 서버 위치에 머문다. 그 뒤에 되돌아와도 다시 따라가지 않고 다른 사람에게 넘기지도 않는다.
        // 낙하 위치가 고정된 뒤의 변화는 폭발에 영향을 주지 않는다
        private void ObserveTargets(double serverTime)
        {
            if (_foxfiresLocked)
            {
                return;
            }

            for (int i = 0; i < _foxfires.Length; ++i)
            {
                if (_foxfires[i].Detached)
                {
                    continue;
                }

                int infoIndex = _ids.IndexOf(_foxfires[i].TargetId);
                bool active   = (infoIndex >= 0) && (_infos[infoIndex].LifeState == EParticipantLifeState.Alive)
                    && _infos[infoIndex].IsConnected;

                if (!active)
                {
                    _foxfires[i].Detached = true;
                    _listener?.OnFoxfireDetached(_castId, _turnIndex, i, serverTime, _foxfires[i].LastPosition);
                    continue;
                }

                _foxfires[i].LastPosition = _infos[infoIndex].Position;
                if (_step == EWolbaekSealStep.Track)
                {
                    _foxfires[i].Position = _foxfires[i].LastPosition;
                }
            }
        }

        private void BeginTrack(double eventTime)
        {
            _step = EWolbaekSealStep.Track;

            for (int i = 0; i < _foxfires.Length; ++i)
            {
                _foxfires[i].Position = _foxfires[i].LastPosition;
            }

            _listener?.OnTrackingStarted(_castId, _turnIndex, eventTime);
        }

        private void LockFoxfires(double eventTime)
        {
            _step           = EWolbaekSealStep.Escape;
            _foxfiresLocked = true;

            Vector3[] centers = new Vector3[_foxfires.Length];
            for (int i = 0; i < _foxfires.Length; ++i)
            {
                centers[i] = _foxfires[i].Position;
            }

            _listener?.OnFoxfiresLocked(_castId, _turnIndex, eventTime, centers);
        }

        // 여우불 한 발은 핵 한 개만 부수고 핵 한 개는 한 발로만 부서진다. 핵 사이 간격이 적중 반경의 두 배보다 커서 한 발이 두 핵에 닿지 않으므로
        // 가장 가까운 핵 하나를 고르는 것으로 충분하다. 폭발은 대상의 생사와 상관없이 예정 시각에 고정 위치에서 일어난다
        private void ResolveTurn(double eventTime)
        {
            _step = EWolbaekSealStep.Cleanup;

            _targets.GetLivingPlayers(_hitBuffer);

            int playerHits = 0;
            for (int i = 0; i < _foxfires.Length; ++i)
            {
                Vector3 center = _foxfires[i].Position;
                playerHits += Blast(center);

                int   nearest  = NO_INDEX;
                float nearestD = float.MaxValue;
                for (int core = 0; core < _cores.Length; ++core)
                {
                    if (_destroyed[core])
                    {
                        continue;
                    }

                    float distance = FlatDistance(center, _cores[core].Center);
                    if ((distance <= (_spec.CoreHitRadius + DISTANCE_EPSILON)) && (distance < nearestD))
                    {
                        nearest  = core;
                        nearestD = distance;
                    }
                }

                if (nearest != NO_INDEX)
                {
                    _destroyed[nearest] = true;
                }
            }

            _hitBuffer.Clear();

            int destroyedCount = 0;
            for (int i = 0; i < _destroyed.Length; ++i)
            {
                if (_destroyed[i])
                {
                    ++destroyedCount;
                }
            }

            // 활성 핵을 모두 부수면 성공 한 번이 쌓인다. 실패한 차례는 이전 성공을 지우지 않는다
            bool succeeded = destroyedCount == _cores.Length;
            if (succeeded)
            {
                ++_successCount;
                _thirdSuccess = _successCount >= _spec.RequiredSuccesses;
            }

            _listener?.OnTurnResolved(_castId, _turnIndex, eventTime, destroyedCount, _cores.Length, playerHits, succeeded,
                _successCount);
        }

        // 폭발 반경 안의 살아 있는 플레이어에게 최대 체력 비율의 방어 전 피해를 준다. 방어는 되고 반격으로 취소되지 않으며 밀려남은 없다.
        // 한 발마다 동작 번호가 달라 같은 사람이 여러 폭발에 들어 있으면 각각 맞는다
        private int Blast(Vector3 center)
        {
            int actionInstanceId = _gate.IssueInstanceId();
            int hits             = 0;

            for (int i = 0; i < _hitBuffer.Count; ++i)
            {
                ICombatTarget target = _hitBuffer[i];
                if (FlatDistance(target.Position, center) > (_spec.BlastRadius + DISTANCE_EPSILON))
                {
                    continue;
                }

                if (!TryMakeRatioRequest(target.CombatId, _spec.BlastDamageRate, actionInstanceId, center, BLAST_ID,
                    EDamageFlags.None, out DamageRequest request))
                {
                    continue;
                }

                if ((_gate.TryApply(in request, out DamageResult result)) && (result.IsApplied))
                {
                    ++hits;
                    BossTargetHitReport.Report(_listener, _castId, target, center);
                }
            }

            return hits;
        }

        /****************************************
        *                Sweep
        ****************************************/

        // 첫 휩쓸기의 방향은 서버 난수로 고르는 임시 가정이고 이후는 번호마다 반대로 뒤집는다. 범위 값은 설정이 준다
        private void TelegraphSweep(int index, double eventTime)
        {
            WolbaekSealSweepSpec sweep = _spec.Sweep;
            float[] offsets = sweep.HitOffsetsSeconds;

            if (index == 0)
            {
                float radians = _random.NextFloat01() * FULL_TURN_DEGREES * Mathf.Deg2Rad;
                _sweepFirstDirection = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
            }

            Vector3 direction = ((index % 2) == 0) ? _sweepFirstDirection : -_sweepFirstDirection;
            double  strike    = _turn.TrackStartServerTime + offsets[index];

            _sweeps[index]           = new WolbaekSealSweep(_turnIndex, index, eventTime, strike, _self.Position, direction,
                sweep.Range, sweep.HalfAngleDegrees, sweep.PushDistance);
            _sweepTelegraphed[index] = true;
            _listener?.OnSweepTelegraphed(_castId, in _sweeps[index]);
        }

        // 맞아도 여우불 추적은 끊기지 않는다. 방어와 반격으로 막을 수 없고 중심에서 바깥쪽으로 민다
        private void StrikeSweep(int index, double eventTime)
        {
            WolbaekSealSweepSpec spec  = _spec.Sweep;
            WolbaekSealSweep     sweep = _sweeps[index];

            _sweepStruck[index] = true;
            _targets.GetLivingPlayers(_hitBuffer);

            int actionInstanceId = _gate.IssueInstanceId();
            int hitCount         = 0;

            for (int i = 0; i < _hitBuffer.Count; ++i)
            {
                ICombatTarget target = _hitBuffer[i];
                if (!IsInsideSweep(target.Position, in sweep))
                {
                    continue;
                }

                if (!TryMakeRatioRequest(target.CombatId, spec.DamageRate, actionInstanceId, sweep.Origin, SWEEP_ID,
                    EDamageFlags.Unblockable, out DamageRequest request))
                {
                    continue;
                }

                if ((!_gate.TryApply(in request, out DamageResult result)) || (!result.IsApplied))
                {
                    continue;
                }

                ++hitCount;
                BossTargetHitReport.Report(_listener, _castId, target, sweep.Origin);

                if (spec.PushDistance > 0f)
                {
                    _displacer.TryPush(target.CombatId, Flatten(target.Position - sweep.Origin, sweep.Direction), spec.PushDistance);
                }
            }

            _hitBuffer.Clear();
            _listener?.OnSweepResolved(_castId, _turnIndex, index, eventTime, hitCount);
        }

        // 경계는 안쪽에 포함한다
        private static bool IsInsideSweep(Vector3 position, in WolbaekSealSweep sweep)
        {
            Vector3 offset = position - sweep.Origin;
            offset.y = 0f;

            float distance = offset.magnitude;
            if (distance > (sweep.Range + DISTANCE_EPSILON))
            {
                return false;
            }

            if (offset.sqrMagnitude < MIN_DIRECTION_SQR)
            {
                return true;
            }

            float cosine = Mathf.Clamp(Vector3.Dot(offset / distance, sweep.Direction), -1f, 1f);
            float angle  = Mathf.Acos(cosine) * Mathf.Rad2Deg;
            return angle <= (sweep.HalfAngleDegrees + ANGLE_EPSILON);
        }

        private bool TryFindPendingSweep(out WolbaekSealSweep pending)
        {
            for (int i = 0; i < _sweeps.Length; ++i)
            {
                if (_sweepTelegraphed[i] && (!_sweepStruck[i]))
                {
                    pending = _sweeps[i];
                    return true;
                }
            }

            pending = default;
            return false;
        }

        // 최대 체력에 비율을 곱한 방어 전 피해 요청을 만든다, 최대 체력을 모르면 만들지 못한다
        private bool TryMakeRatioRequest(int combatId, float rate, int actionInstanceId, Vector3 source, string definitionId,
            EDamageFlags flags, out DamageRequest request)
        {
            if (!_health.TryGetMaxHealth(combatId, out float maxHealth))
            {
                if (!_healthMissingLogged)
                {
                    _healthMissingLogged = true;
                    Debug.LogError($"[Boss] 봉인 핵 피해 대상의 최대 체력을 읽지 못했다 - {combatId}");
                }

                request = default;
                return false;
            }

            request = new DamageRequest(_self.CombatId, combatId, _castId, actionInstanceId, rate * maxHealth, flags, source,
                definitionId);
            return true;
        }

        /****************************************
        *              Participants
        ****************************************/

        // 명단과 참가자 상태를 한 번 읽어 이번 판정이 모두 같은 값을 보게 한다
        private void ReadRoster()
        {
            _ids.Clear();
            _infos.Clear();
            _idBuffer.Clear();
            _roster.CollectParticipants(_idBuffer);

            for (int i = 0; i < _idBuffer.Count; ++i)
            {
                if (_roster.TryGetParticipant(_idBuffer[i], out BossParticipantInfo info))
                {
                    _ids.Add(_idBuffer[i]);
                    _infos.Add(info);
                }
            }

            _idBuffer.Clear();
        }

        private int CountLiving()
        {
            int living = 0;
            for (int i = 0; i < _infos.Count; ++i)
            {
                if (_infos[i].LifeState == EParticipantLifeState.Alive)
                {
                    ++living;
                }
            }

            return living;
        }

        /****************************************
        *               Outcome
        ****************************************/

        // 이 시각까지의 핵과 여우불과 휩쓸기는 모두 판정을 마쳤거나 정리 대상이다. 보스 무적을 풀고 반격 시간을 시작한다
        private void Succeed(double serverTime)
        {
            _gate.CloseForGood();

            _state      = EWolbaekSealState.Counter;
            _counterEnd = serverTime + _spec.CounterSeconds;
            _listener?.OnSealSucceeded(_castId, serverTime, _counterEnd);
        }

        private void TickCounter(double serverTime)
        {
            if (serverTime < _counterEnd)
            {
                return;
            }

            bool atBoundary = _spec.HasNextBoundary
                && (_vitals.HealthRate <= (_spec.NextBoundaryHealthRate + BOUNDARY_TOLERANCE));

            _state = atBoundary ? EWolbaekSealState.NextBoundaryPending : EWolbaekSealState.Completed;
            _listener?.OnCounterEnded(_castId, serverTime, atBoundary);
        }

        private void Fail(EWolbaekSealFail reason, double serverTime)
        {
            _gate.CloseForGood();

            _failReason = reason;
            _state      = EWolbaekSealState.Failed;
            _listener?.OnSealFailed(_castId, serverTime, reason);
        }

        // 성공도 실패도 아니다. 보스는 무적으로 남고 일반 선택은 열리지 않으며 표시를 정리하고 원인만 알린다
        private void Halt(EWolbaekSealHaltReason reason, double serverTime)
        {
            if (_gate != null)
            {
                _gate.CloseForGood();
            }

            _haltReason = reason;
            _state      = EWolbaekSealState.Halted;
            _listener?.OnSealHalted(_castId, serverTime, reason);
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
