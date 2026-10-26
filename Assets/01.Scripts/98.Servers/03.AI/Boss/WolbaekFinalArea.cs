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
    // 월백 하드 네 번째 체력 기믹 "최후의 영역"의 서버 진행이다. 이지에는 만들지 않는다(이지는 0%에서 기존대로 보스가 처치된다).
    // 보스 두뇌의 기믹 훅(BossBrain.AddGimmick)에 연결되며 전환 판정보다 먼저 Tick이 불리고, 진입 순간에 두뇌가 진행 중인 공격과 지속물을
    // 정리한다(IBossGimmickCleanup). 하드 상시 소형 낙하 여우불은 이 기믹에서 발생하지 않는다.
    //
    // 사망 보류: 두뇌는 기믹 갱신보다 먼저 사망을 확정하므로, 체력이 0이 되는 순간부터 이 기믹이 IBossDeathHold로 사망을 막아야 한다.
    // 그래서 발동 전이라도 하드에서 체력이 0이면 보류하고, 진행 중과 실패와 정지에서도 보류하며 최종 성공에서만 푼다.
    // 사망 확정을 읽는 경로가 BossGimmickDeathPolicy를 거치지 않으면 이 보류가 닿지 않는다.
    //
    // 25% 봉인의 반격에서 체력이 0에 멈추면 봉인이 인계를 기다린다(IBossBoundaryHandoff). 이 기믹은 앞선 기믹이 인계 대기에 들어서면
    // TryTakeHandoff를 정확히 한 번 불러 받은 틱에 진입한다. 앞선 기믹이 아직 반격 중이면 체력이 0이어도 기다린다.
    // 앞선 기믹은 Tick 순서상 이 기믹보다 먼저 등록해야 같은 틱에 이어서 시작한다.
    //
    // 진행: 예고 4초 동안 중앙만 남는다고 알린 뒤 외곽 바닥을 제거한다(IWolbaekFinalFloor). 제거 순간 외곽에 서 있다는 이유만으로는
    // 아무도 죽이지 않는다. 바닥을 제거한 같은 순간 노란 여우불을 진입 시점 생존자 수로
    // 고정한 수만큼 중앙 바닥의 검증된 위치에 세우고(IWolbaekFinalSiteSource, IWolbaekYellowRegistry) 첫 빨간 묶음을 시작한다.
    //
    // 추락: 외곽 제거 뒤 진행 중인 틱마다 바닥 창구에 낙하가 확정된 참가자를 묻고(CollectFallen), 받은 사람 중 그 시각에 살아 있는
    // 참가자만 서버가 추락 사망으로 처리한다(IWolbaekFinalKiller). 누가 떨어졌는지는 창구가 서버 위치로 정하며 이 클래스는 위치를 보지
    // 않는다. 제거 순간에 서 있던 사람이든 뒤에 걸어 나가거나 밀려난 사람이든 같은 규칙이고, 경계 밖 지속 피해는 없다.
    // 추락으로 마지막 생존자가 쓰러지면 그 틱에 전멸 실패다.
    //
    // 노란 여우불이 남아 있는 동안 빨간 묶음을 반복한다. 묶음은 시작 순간의 생존자마다 한 개이고 4초 추적, 위치 고정, 2초 뒤 폭발이다.
    // 대상이 쓰러지거나 끊기면 마지막 서버 위치에 머물러 예정대로 터지며 도중 부활자는 다음 묶음부터 대상이 된다.
    //
    // 빨간 폭발: 한 개에만 실제로 닿으면 최대 체력 비율의 방어 전 피해(방어와 반격 불가), 같은 순간 둘 이상에 닿으면 즉사다. 표시만 겹친 것은
    // 즉사가 아니다. 최대 체력 비율 피해에는 시작 인원별 공격 피해 배율을 곱하지 않으므로 배율 이전의 판정기를 넘겨야 한다.
    // 이 기믹 중 부활한 참가자는 부활 시각부터 보호 시간 동안 빨간 폭발 피해와 즉사를 받지 않는다. 이 보호는 추락을 막지 않아, 보호 중에도
    // 창구가 낙하를 내주면 추락 사망이다. 부활한 사람은 새 생명이므로 창구가 다시 낙하를 내줄 수 있다.
    // 빨간 폭발은 플레이어만 겨냥하므로 노란 여우불을 파괴하지 못한다.
    //
    // 한 틱의 순서는 노란 여우불 파괴 확인(모두 부서졌으면 최종 성공이 추락과 전멸과 폭발보다 먼저다), 참가자 확인(전멸이면 실패),
    // 추락 처리(전멸이면 실패), 대상 관찰, 시각 순서의 사건 처리 순이다. 추락은 틱 시각의 사건이고 예약된 사건보다 먼저 처리하므로
    // 바닥을 제거한 틱에는 추락을 묻지 않고 다음 틱부터 묻는다. 최종 성공은 플레이어 보호(IWolbaekFinalGuard)를 먼저 확정하고 피해
    // 게이트를 닫아 남은 빨간 폭발을 무해하게 만든다. 거친 틱에서도 사건을 시각 순서대로 모두 처리하지만 과거 위치를 기록하지 않아 현재 위치로 판정한다.
    // 전체 20분 만료는 세션이 정하므로 이 클래스는 최종 성공을 IBossFinalSuccess로 먼저 읽히게 하기만 한다.
    //
    // 정지(Halted)는 기믹을 건너뛰는 것도 성공이나 실패도 아니다. 보스는 무적으로 남고 사망도 보류된 채 원인만 알린다
    /// <summary> 월백 하드 0% 최후의 영역 기믹, 바닥을 줄이고 노란 여우불을 모두 부수면 최종 성공한다 </summary>
    public sealed class WolbaekFinalArea : IBossGimmick, IBossGimmickCleanup, IBossDamageCap, IBossDeathHold, IBossFinalSuccess
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int    MAX_CANDIDATES     = 64;
        private const int    NO_INDEX           = -1;
        private const float  BOUNDARY_TOLERANCE = 0.00001f;  // 체력 비율, 0에 닿았다고 보는 부동소수 여유
        private const float  DISTANCE_EPSILON   = 0.001f;    // m, 거리 조건 경계의 부동소수 여유
        private const float  TIME_EPSILON       = 0.000001f; // 초, 시각 경계의 부동소수 여유
        private const string BLAST_ID           = "wolbaek_final_blast";

        /****************************************
        *                Fields
        ****************************************/

        // 같은 시각이면 아래 순서로 처리한다. 값이 작을수록 먼저다
        private enum EEventKind
        {
            Collapse   = 0,
            Lock       = 1,
            Blast      = 2,
            NextBundle = 3
        }

        private struct AreaEvent
        {
            public double     Time;
            public EEventKind Kind;
            public int        Index;
        }

        private struct Red
        {
            public int     TargetId;
            public Vector3 LastPosition;
            public Vector3 Position;
            public bool    Detached;
        }

        private readonly WolbaekFinalAreaSpec     _spec;
        private readonly ICombatTarget            _self;
        private readonly IBossVitals              _vitals;
        private readonly IBossParticipantRoster   _roster;
        private readonly IBossParticipantHealth   _health;
        private readonly IBossArenaSource         _arena;
        private readonly IWolbaekFinalSiteSource  _sites;
        private readonly IWolbaekFinalFloor       _floor;
        private readonly IWolbaekFinalKiller      _killer;
        private readonly IWolbaekFinalGuard       _guard;
        private readonly IWolbaekYellowRegistry   _registry;
        private readonly IBossReviveSource        _revive;
        private readonly IDamageResolver          _resolver;
        private readonly ICombatTargetQuery       _targets;
        private readonly IBossGimmick             _preceding;
        private readonly IBossBoundaryHandoff     _handoff;
        private readonly IWolbaekFinalAreaListener _listener;
        private readonly List<int>                _idBuffer   = new List<int>();
        private readonly List<int>                _ids        = new List<int>();
        private readonly List<BossParticipantInfo> _infos     = new List<BossParticipantInfo>();
        private readonly List<ICombatTarget>      _hitBuffer  = new List<ICombatTarget>();
        private readonly List<Vector3>            _rawSites   = new List<Vector3>();
        private readonly List<Vector3>            _yellowSites = new List<Vector3>();
        private readonly List<int>                _fallBuffer = new List<int>();
        private readonly List<int>                _fallen     = new List<int>();
        private readonly List<AreaEvent>          _events     = new List<AreaEvent>();

        private EWolbaekFinalState       _state = EWolbaekFinalState.Dormant;
        private EWolbaekFinalStep        _step;
        private EWolbaekFinalFail        _failReason;
        private EWolbaekFinalHaltReason  _haltReason;
        private BossDamageGate           _gate;
        private WolbaekFinalStart        _start;
        private WolbaekFinalBundle       _bundle;
        private BossFinalSuccess         _finalSuccess;
        private Red[]                    _reds = new Red[0];
        private int[]                    _reservedIds = new int[0];
        private WolbaekYellowFoxfire[]   _yellows = new WolbaekYellowFoxfire[0];
        private WolbaekFinalYellow[]     _yellowInputs = new WolbaekFinalYellow[0];
        private bool[]                   _yellowReported = new bool[0];
        private Vector3                  _anchor;
        private double                  _enteredAt;
        private int                      _castId;
        private int                      _bundleIndex;
        private int                      _eventCursor;
        private int                      _yellowCount;
        private bool                     _hasBundle;
        private bool                     _redsLocked;
        private bool                     _outerRemoved;
        private bool                     _successReady;
        private bool                     _successConsumed;
        private bool                     _cleanupRequested;
        private bool                     _healthMissingLogged;

        /****************************************
        *              Properties
        ****************************************/

        public EWolbaekFinalState State => _state;

        /// <summary> 지금 단계, 진행 중이 아니면 의미가 없다 </summary>
        public EWolbaekFinalStep Step => _step;

        /// <summary> 실패했다면 그 이유, 실패 전에는 의미가 없다 </summary>
        public EWolbaekFinalFail FailReason => _failReason;

        /// <summary> 정지했다면 그 이유, 정지 전에는 의미가 없다 </summary>
        public EWolbaekFinalHaltReason HaltReason => _haltReason;

        /// <summary> 이 기믹의 시전 번호, 진입 전에는 0이다 </summary>
        public int CastInstanceId => _castId;

        /// <summary> 진입 순간 고정된 노란 여우불 수, 진입 전에는 0이다 </summary>
        public int YellowCount => _yellowCount;

        /// <summary> 아직 부서지지 않은 노란 여우불 수 </summary>
        public int YellowRemaining
        {
            get
            {
                int remaining = 0;
                for (int i = 0; i < _yellows.Length; ++i)
                {
                    if (!_yellows[i].IsDestroyed)
                    {
                        ++remaining;
                    }
                }

                return remaining;
            }
        }

        /// <summary> 외곽 바닥 제거가 받아들여졌다 </summary>
        public bool IsOuterFloorRemoved => _outerRemoved;

        /// <summary> 지금까지 시작한 빨간 묶음 수 </summary>
        public int BundleCount => _bundleIndex + 1;

        /// <summary> 지금 묶음, 묶음이 없으면 기본값이다 </summary>
        public WolbaekFinalBundle Bundle => _bundle;

        /// <summary> 노란 여우불 입력 목록, 바닥 제거 전에는 비어 있다 </summary>
        public IReadOnlyList<WolbaekFinalYellow> Yellows => _yellowInputs;

        /// <summary> 노란 여우불 피격 몸체, 바닥 제거 전에는 비어 있다 </summary>
        public IReadOnlyList<WolbaekYellowFoxfire> YellowBodies => _yellows;

        /// <summary> 최종 성공이 확정됐다, 소비 여부와 상관없이 성공 뒤에는 계속 참이다 </summary>
        public bool IsFinalSucceeded => _state == EWolbaekFinalState.Succeeded;

        public bool HasFinalSuccess => _successReady && (!_successConsumed);

        public bool DefersPhaseTransition
        {
            get
            {
                switch (_state)
                {
                    case EWolbaekFinalState.Active:
                    case EWolbaekFinalState.Failed:
                    case EWolbaekFinalState.Halted:
                        return true;
                    case EWolbaekFinalState.Dormant:
                        return IsHealthAtZero();
                    default:
                        return false;
                }
            }
        }

        public bool BlocksSelection
        {
            get
            {
                return (_state == EWolbaekFinalState.Succeeded) || DefersPhaseTransition;
            }
        }

        public bool IsDamageBlocked
        {
            get
            {
                switch (_state)
                {
                    case EWolbaekFinalState.Dormant:
                        return IsHealthAtZero();
                    case EWolbaekFinalState.Active:
                    case EWolbaekFinalState.Succeeded:
                    case EWolbaekFinalState.Failed:
                    case EWolbaekFinalState.Halted:
                        return true;
                    default:
                        return false;
                }
            }
        }

        /// <summary> 하드의 체력 0을 사망으로 확정하지 않는다, 최종 성공에서만 풀린다 </summary>
        public bool IsDeathHeld
        {
            get
            {
                switch (_state)
                {
                    case EWolbaekFinalState.Dormant:
                        return IsHealthAtZero();
                    case EWolbaekFinalState.Active:
                    case EWolbaekFinalState.Failed:
                    case EWolbaekFinalState.Halted:
                        return true;
                    default:
                        return false;
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        // unscaledResolver는 시작 인원 피해 배율을 곱하기 전의 판정기여야 한다(비율 피해에 배율을 곱하지 않는 확정 계약).
        // health는 최대 체력 비율 피해를 방어 전 값으로 바꾸는 데 쓴다. precedingGimmick은 25% 봉인이며 IBossBoundaryHandoff도 구현하면
        // 인계를 받아 시작하고, 없으면 null이다
        public WolbaekFinalArea(WolbaekFinalAreaSpec spec, ICombatTarget self, IBossVitals vitals, IBossParticipantRoster roster,
            IBossParticipantHealth health, IBossArenaSource arena, IWolbaekFinalSiteSource sites, IWolbaekFinalFloor floor,
            IWolbaekFinalKiller killer, IWolbaekFinalGuard guard, IWolbaekYellowRegistry registry, IBossReviveSource revive,
            IDamageResolver unscaledResolver, ICombatTargetQuery targets, IBossGimmick precedingGimmick = null,
            IWolbaekFinalAreaListener listener = null)
        {
            _spec      = spec ?? throw new ArgumentNullException(nameof(spec));
            _self      = self ?? throw new ArgumentNullException(nameof(self));
            _vitals    = vitals ?? throw new ArgumentNullException(nameof(vitals));
            _roster    = roster ?? throw new ArgumentNullException(nameof(roster));
            _health    = health ?? throw new ArgumentNullException(nameof(health));
            _arena     = arena ?? throw new ArgumentNullException(nameof(arena));
            _sites     = sites ?? throw new ArgumentNullException(nameof(sites));
            _floor     = floor ?? throw new ArgumentNullException(nameof(floor));
            _killer    = killer ?? throw new ArgumentNullException(nameof(killer));
            _guard     = guard ?? throw new ArgumentNullException(nameof(guard));
            _registry  = registry ?? throw new ArgumentNullException(nameof(registry));
            _revive    = revive ?? throw new ArgumentNullException(nameof(revive));
            _resolver  = unscaledResolver ?? throw new ArgumentNullException(nameof(unscaledResolver));
            _targets   = targets ?? throw new ArgumentNullException(nameof(targets));
            _preceding = precedingGimmick;
            _handoff   = precedingGimmick as IBossBoundaryHandoff;
            _listener  = listener;
        }

        public void Tick(double serverTime, int phase)
        {
            switch (_state)
            {
                case EWolbaekFinalState.Dormant:
                    TickDormant(serverTime);
                    break;
                case EWolbaekFinalState.Active:
                    TickActive(serverTime);
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

        // 진행 중이던 표시와 공격을 정리하고 멈춘다. 최종 성공은 되돌리지 않으며 이미 멈춘 기믹에는 아무 일도 하지 않는다
        public void Stop(EBossInterruptReason reason)
        {
            if ((_state == EWolbaekFinalState.Succeeded) || (_state == EWolbaekFinalState.Stopped))
            {
                return;
            }

            bool hadDisplay = _state == EWolbaekFinalState.Active;

            if (_gate != null)
            {
                _gate.CloseForGood();
            }

            ReleaseYellows();

            _state            = EWolbaekFinalState.Stopped;
            _cleanupRequested = false;
            _events.Clear();

            if (hadDisplay)
            {
                _listener?.OnFinalCleared(_castId, reason);
            }
        }

        // 최후의 영역 전에는 일반 경계가 없어 체력을 그대로 두고, 진행 중과 끝난 뒤에는 보스가 어떤 피해도 받지 않는다.
        // 반환 0은 이 피해가 없던 것이라는 뜻이다
        public float CapDamage(float currentHealth, float maxHealth, float damage)
        {
            if (!(damage > 0f))
            {
                return 0f;
            }

            return IsDamageBlocked ? 0f : damage;
        }

        public bool TryConsumeFinalSuccess(out BossFinalSuccess success)
        {
            if (!HasFinalSuccess)
            {
                success = default;
                return false;
            }

            _successConsumed = true;
            success          = _finalSuccess;
            return true;
        }

        /// <summary> 남은 중앙 바닥의 검증된 부활 지점, 진행 중에만 얻는다. 공용 부활이 이 지점을 써야 한다 </summary>
        public bool TryGetSafeRevivePoint(int combatId, out Vector3 point)
        {
            if (_state != EWolbaekFinalState.Active)
            {
                point = default;
                return false;
            }

            return _floor.TryGetSafeRevivePoint(combatId, out point);
        }

        /// <summary> 늦게 접속하거나 재접속한 클라이언트에 줄 현재 값, 진행 중일 때만 참이다 </summary>
        public bool TryGetSnapshot(out WolbaekFinalSnapshot snapshot)
        {
            if (_state != EWolbaekFinalState.Active)
            {
                snapshot = default;
                return false;
            }

            float[] health = new float[_yellows.Length];
            for (int i = 0; i < _yellows.Length; ++i)
            {
                health[i] = _yellows[i].Health;
            }

            // 폭발한 묶음의 빨간 여우불은 이미 끝났으므로 묶음이 없을 때는 비운다
            int redCount = _hasBundle ? _reds.Length : 0;
            Vector3[] positions = new Vector3[redCount];
            bool[]    detached  = new bool[redCount];
            for (int i = 0; i < redCount; ++i)
            {
                positions[i] = _reds[i].Position;
                detached[i]  = _reds[i].Detached;
            }

            snapshot = new WolbaekFinalSnapshot(_step, in _start, _outerRemoved, _yellowInputs, health, _hasBundle, in _bundle,
                positions, detached, _redsLocked);
            return true;
        }

        // 하드의 0%는 비율이 아니라 체력이 0인 상태다
        private bool IsHealthAtZero()
        {
            return _vitals.IsHealthZero || (_vitals.HealthRate <= BOUNDARY_TOLERANCE);
        }

        /****************************************
        *                Entry
        ****************************************/

        // 체력이 0이어도 앞선 기믹이 반격 중이면 기다리고, 인계 대기에 들어서면 인계를 정확히 한 번 받아 시작한다
        private void TickDormant(double serverTime)
        {
            if (!IsHealthAtZero())
            {
                return;
            }

            if (_handoff != null)
            {
                if (_handoff.HasPendingHandoff)
                {
                    if (!_handoff.TryTakeHandoff())
                    {
                        return;
                    }

                    Begin(serverTime);
                    return;
                }
            }

            if ((_preceding != null) && _preceding.DefersPhaseTransition)
            {
                return;
            }

            Begin(serverTime);
        }

        // 진입한 순간 이전 공격은 정리를 요청해 이미 멈췄다. 전장을 모르거나 위치가 모자라면 바닥을 건드리기 전에 정지한다
        private void Begin(double serverTime)
        {
            _gate = new BossDamageGate(_resolver);
            _gate.Open();

            _castId           = _gate.IssueInstanceId();
            _cleanupRequested = true;
            _enteredAt        = serverTime;
            _bundleIndex      = NO_INDEX;

            if (!_arena.TryGetArena(out Vector3 center, out float _))
            {
                Debug.LogError("[Boss] 전장 정보를 얻지 못해 최후의 영역 기믹을 정지한다");
                Halt(EWolbaekFinalHaltReason.ArenaUnavailable, serverTime);
                return;
            }

            _anchor = center;

            ReadRoster();
            int living = CountLiving();
            if (living == 0)
            {
                Fail(EWolbaekFinalFail.Wipe, serverTime);
                return;
            }

            // 노란 여우불 수는 이 순간의 생존자 수로 고정되고 이후 사망과 접속 끊김으로 줄지 않는다
            _yellowCount = _spec.GetYellowCount(living);

            BuildYellowSites();
            if (_yellowSites.Count < _yellowCount)
            {
                Debug.LogError("[Boss] 검증된 노란 여우불 위치가 모자라 최후의 영역 기믹을 정지한다");
                Halt(EWolbaekFinalHaltReason.SitesInsufficient, serverTime);
                return;
            }

            if (!ReserveYellowIds())
            {
                Debug.LogError("[Boss] 노란 여우불 전투 번호를 받지 못해 최후의 영역 기믹을 정지한다");
                Halt(EWolbaekFinalHaltReason.RegistryUnavailable, serverTime);
                return;
            }

            double collapse = serverTime + _spec.CollapseSeconds;
            _start = new WolbaekFinalStart(_self.CombatId, _castId, serverTime, collapse, _spec.CentralRadius, _yellowCount,
                _spec.YellowMaxHealth, _spec.RedTrackSeconds, _spec.RedFuseSeconds, _spec.RedBlastRadius, _anchor);

            _state = EWolbaekFinalState.Active;
            _step  = EWolbaekFinalStep.Collapse;
            AddEvent(collapse, EEventKind.Collapse, 0);
            _listener?.OnFinalStarted(in _start);
        }

        // 중앙 바닥 안의 후보만 목록 순서 그대로 쓴다
        private void BuildYellowSites()
        {
            _rawSites.Clear();
            _yellowSites.Clear();
            _sites.CollectYellowSites(_rawSites);

            for (int i = 0; i < _rawSites.Count; ++i)
            {
                bool onCentralFloor = FlatDistance(_rawSites[i], _anchor) <= (_spec.CentralRadius + DISTANCE_EPSILON);
                if (onCentralFloor && (_yellowSites.Count < MAX_CANDIDATES))
                {
                    _yellowSites.Add(_rawSites[i]);
                }
            }

            _rawSites.Clear();
        }

        // 실제 생성은 바닥 제거 뒤이지만 번호는 미리 받아 두어 번호를 못 받는 문제를 바닥을 건드리기 전에 알게 한다
        private bool ReserveYellowIds()
        {
            _reservedIds = new int[_yellowCount];
            for (int i = 0; i < _yellowCount; ++i)
            {
                int issued = _registry.IssueCombatId();
                if (issued == CombatIds.NONE)
                {
                    return false;
                }

                _reservedIds[i] = issued;
            }

            return true;
        }

        /****************************************
        *                Events
        ****************************************/

        // 시각, 종류, 번호 순으로 정렬해 끼워 넣는다
        private void AddEvent(double time, EEventKind kind, int index)
        {
            AreaEvent added = new AreaEvent { Time = time, Kind = kind, Index = index };

            int position = _events.Count;
            while (position > _eventCursor)
            {
                AreaEvent previous = _events[position - 1];
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
            // 최종 성공이 같은 틱의 추락과 빨간 폭발과 전멸보다 앞선다. 그래서 어떤 판정보다 먼저 노란 여우불을 확인한다
            if (ObserveYellows())
            {
                Succeed(serverTime);
                return;
            }

            ReadRoster();
            if (CountLiving() == 0)
            {
                Fail(EWolbaekFinalFail.Wipe, serverTime);
                return;
            }

            if (_outerRemoved && ApplyFalls(serverTime))
            {
                ReadRoster();
                if (CountLiving() == 0)
                {
                    Fail(EWolbaekFinalFail.Wipe, serverTime);
                    return;
                }
            }

            ObserveTargets(serverTime);

            while ((_state == EWolbaekFinalState.Active) && (_eventCursor < _events.Count))
            {
                AreaEvent next = _events[_eventCursor];
                if (next.Time > (serverTime + TIME_EPSILON))
                {
                    break;
                }

                ++_eventCursor;
                Execute(next);

                // 폭발로 마지막 생존자가 쓰러졌다면 바로 실패이고 이어지는 사건은 일어나지 않는다
                if ((_state == EWolbaekFinalState.Active) && ((next.Kind == EEventKind.Collapse) || (next.Kind == EEventKind.Blast)))
                {
                    ReadRoster();
                    if (CountLiving() == 0)
                    {
                        Fail(EWolbaekFinalFail.Wipe, next.Time);
                    }
                }
            }

            if (_eventCursor > 0)
            {
                _events.RemoveRange(0, _eventCursor);
                _eventCursor = 0;
            }
        }

        private void Execute(AreaEvent areaEvent)
        {
            switch (areaEvent.Kind)
            {
                case EEventKind.Collapse:
                    Collapse(areaEvent.Time);
                    break;
                case EEventKind.Lock:
                    LockReds(areaEvent.Time);
                    break;
                case EEventKind.Blast:
                    BlastBundle(areaEvent.Time);
                    break;
                default:
                    StartBundle(areaEvent.Time);
                    break;
            }
        }

        /****************************************
        *                Collapse
        ****************************************/

        // 바닥 제거를 요청하고 받아들여지면 노란 여우불을 세우고 첫 묶음을 시작한다. 거절되면 정지한다.
        // 제거 순간에는 외곽에 서 있어도 아무도 죽이지 않는다. 추락은 그 뒤 틱에서 바닥 창구가 낙하를 내줄 때 처리한다
        private void Collapse(double eventTime)
        {
            if (!_floor.TryRemoveOuterFloor(_castId, eventTime))
            {
                Debug.LogError("[Boss] 외곽 바닥 제거가 거절돼 최후의 영역 기믹을 정지한다");
                Halt(EWolbaekFinalHaltReason.FloorUnavailable, eventTime);
                return;
            }

            _outerRemoved = true;

            _fallen.Clear();
            _listener?.OnOuterFloorRemoved(_castId, eventTime, _fallen);

            ReadRoster();
            if (CountLiving() == 0)
            {
                Fail(EWolbaekFinalFail.Wipe, eventTime);
                return;
            }

            if (!SpawnYellows(eventTime))
            {
                return;
            }

            _step = EWolbaekFinalStep.Hunt;
            StartBundle(eventTime);
        }

        // 바닥 창구가 낙하를 확정해 내준 참가자 중 이 시각에 살아 있는 사람만 추락 사망으로 처리하고, 한 명이라도 죽였으면 참이다.
        // 방금 읽은 명단을 쓰므로 부르기 전에 ReadRoster가 끝나 있어야 한다. 이미 쓰러졌거나 이 시각 뒤에 부활한 사람은 죽이지 않고
        // 알림에도 넣지 않는다. 창구는 한 생명에 한 번만 내주므로 걸러진 사람은 부활해 새 생명이 되기 전에는 다시 오지 않는다.
        // 부활 직후 보호는 추락을 막지 않아 보호 여부를 보지 않는다
        private bool ApplyFalls(double serverTime)
        {
            if (_floor.CollectFallen(serverTime, _fallBuffer) <= 0)
            {
                return false;
            }

            _fallen.Clear();
            for (int i = 0; i < _fallBuffer.Count; ++i)
            {
                int id        = _fallBuffer[i];
                int infoIndex = _ids.IndexOf(id);
                if ((infoIndex < 0) || (!IsAliveAt(infoIndex, serverTime)))
                {
                    continue;
                }

                if (_gate.IsOpen && _killer.TryKill(id, _castId, EWolbaekFinalDeath.Fall))
                {
                    _fallen.Add(id);
                }
            }

            _fallBuffer.Clear();

            if (_fallen.Count == 0)
            {
                return false;
            }

            _listener?.OnParticipantsFell(_castId, serverTime, _fallen);
            _fallen.Clear();
            return true;
        }

        // 고정된 수만큼 목록 앞에서부터 세운다. 하나라도 등록하지 못하면 세운 것을 거두고 정지한다
        private bool SpawnYellows(double eventTime)
        {
            _yellows        = new WolbaekYellowFoxfire[_yellowCount];
            _yellowInputs   = new WolbaekFinalYellow[_yellowCount];
            _yellowReported = new bool[_yellowCount];

            for (int i = 0; i < _yellowCount; ++i)
            {
                _yellows[i]      = new WolbaekYellowFoxfire(_reservedIds[i], _yellowSites[i], _spec.YellowMaxHealth,
                    _spec.YellowDefense, _spec.YellowBodyRadius);
                _yellowInputs[i] = new WolbaekFinalYellow(i, _reservedIds[i], _yellowSites[i], _spec.YellowMaxHealth);

                if (!_registry.Register(_yellows[i]))
                {
                    Debug.LogError("[Boss] 노란 여우불을 피격 대상으로 등록하지 못해 최후의 영역 기믹을 정지한다");
                    Halt(EWolbaekFinalHaltReason.RegistryUnavailable, eventTime);

                    // 아직 만들지 못한 칸이 비어 있어 이후 조회가 깨지지 않도록 비운다
                    _yellows        = new WolbaekYellowFoxfire[0];
                    _yellowInputs   = new WolbaekFinalYellow[0];
                    _yellowReported = new bool[0];
                    return false;
                }
            }

            _listener?.OnYellowsSpawned(_castId, eventTime, _yellowInputs);
            return true;
        }

        // 새로 부서진 노란 여우불을 알리고 모두 부서졌는지 돌려준다. 바닥 제거 전에는 노란 여우불이 없어 항상 거짓이다
        private bool ObserveYellows()
        {
            if (_yellows.Length == 0)
            {
                return false;
            }

            int reported = 0;
            for (int i = 0; i < _yellowReported.Length; ++i)
            {
                if (_yellowReported[i])
                {
                    ++reported;
                }
            }

            for (int i = 0; i < _yellows.Length; ++i)
            {
                if ((!_yellows[i].IsDestroyed) || _yellowReported[i])
                {
                    continue;
                }

                _yellowReported[i] = true;
                ++reported;
                _listener?.OnYellowDestroyed(_castId, i, _yellows[i].DestroyedServerTime, _yellows.Length - reported);
            }

            return reported == _yellows.Length;
        }

        /****************************************
        *                 Reds
        ****************************************/

        // 묶음 시작 순간의 생존자마다 빨간 여우불 한 개를 지정한다. 부활한 사람은 이번 묶음에 추가하지 않는다
        private void StartBundle(double eventTime)
        {
            ReadRoster();

            int count = 0;
            for (int i = 0; i < _infos.Count; ++i)
            {
                if (IsAliveAt(i, eventTime))
                {
                    ++count;
                }
            }

            if (count == 0)
            {
                Fail(EWolbaekFinalFail.Wipe, eventTime);
                return;
            }

            ++_bundleIndex;

            _reds = new Red[count];
            WolbaekFinalRed[] inputs = new WolbaekFinalRed[count];

            int next = 0;
            for (int i = 0; (i < _infos.Count) && (next < count); ++i)
            {
                if (!IsAliveAt(i, eventTime))
                {
                    continue;
                }

                _reds[next] = new Red
                {
                    TargetId     = _ids[i],
                    LastPosition = _infos[i].Position,
                    Position     = _infos[i].Position,
                    Detached     = !_infos[i].IsConnected
                };
                inputs[next] = new WolbaekFinalRed(next, _ids[i]);
                ++next;
            }

            double lockTime  = eventTime + _spec.RedTrackSeconds;
            double blastTime = lockTime + _spec.RedFuseSeconds;

            _bundle     = new WolbaekFinalBundle(_bundleIndex, eventTime, lockTime, blastTime, inputs);
            _hasBundle  = true;
            _redsLocked = false;

            AddEvent(lockTime, EEventKind.Lock, 0);
            AddEvent(blastTime, EEventKind.Blast, 0);
            AddEvent(blastTime + _spec.BundleGapSeconds, EEventKind.NextBundle, 0);

            _listener?.OnBundleStarted(_castId, in _bundle);

            // 접속이 끊긴 생존자도 빨간 여우불을 받는다. 처음부터 그 자리에 머물러 있으므로 클라이언트에 알려 둔다
            for (int i = 0; i < _reds.Length; ++i)
            {
                if (_reds[i].Detached)
                {
                    _listener?.OnRedDetached(_castId, _bundleIndex, i, eventTime, _reds[i].LastPosition);
                }
            }
        }

        // 추적 대상이 쓰러지거나 끊기면 마지막 서버 위치에 머문다. 그 뒤에 되돌아와도 다시 따라가지 않고 다른 사람에게 넘기지도 않는다.
        // 낙하 위치가 고정된 뒤의 변화는 폭발에 영향을 주지 않는다
        private void ObserveTargets(double serverTime)
        {
            if ((!_hasBundle) || _redsLocked)
            {
                return;
            }

            for (int i = 0; i < _reds.Length; ++i)
            {
                if (_reds[i].Detached)
                {
                    continue;
                }

                int infoIndex = _ids.IndexOf(_reds[i].TargetId);
                bool active   = (infoIndex >= 0) && (_infos[infoIndex].LifeState == EParticipantLifeState.Alive)
                    && _infos[infoIndex].IsConnected;

                if (!active)
                {
                    _reds[i].Detached = true;
                    _listener?.OnRedDetached(_castId, _bundleIndex, i, serverTime, _reds[i].LastPosition);
                    continue;
                }

                _reds[i].LastPosition = _infos[infoIndex].Position;
                _reds[i].Position     = _reds[i].LastPosition;
            }
        }

        private void LockReds(double eventTime)
        {
            _redsLocked = true;

            Vector3[] centers = new Vector3[_reds.Length];
            for (int i = 0; i < _reds.Length; ++i)
            {
                centers[i] = _reds[i].Position;
            }

            _listener?.OnRedsLocked(_castId, _bundleIndex, eventTime, centers);
        }

        // 한 묶음의 폭발은 같은 순간이다. 실제로 반경 안에 든 폭발 수가 하나면 비율 피해, 둘 이상이면 즉사이고 표시가 겹친 것은 세지 않는다.
        // 폭발 대상은 플레이어뿐이라 노란 여우불은 맞지 않는다. 대상의 생사와 상관없이 폭발은 고정 위치에서 일어난다
        private void BlastBundle(double eventTime)
        {
            _targets.GetLivingPlayers(_hitBuffer);

            int singleHits = 0;
            int doubleKills = 0;

            int[] actionIds = new int[_reds.Length];
            for (int i = 0; i < actionIds.Length; ++i)
            {
                actionIds[i] = _gate.IssueInstanceId();
            }

            for (int i = 0; i < _hitBuffer.Count; ++i)
            {
                ICombatTarget target = _hitBuffer[i];
                if (HasRevivedAfter(target.CombatId, eventTime))
                {
                    continue;
                }

                int reach = NO_INDEX;
                int reachCount = 0;
                for (int red = 0; red < _reds.Length; ++red)
                {
                    if (FlatDistance(target.Position, _reds[red].Position) <= (_spec.RedBlastRadius + DISTANCE_EPSILON))
                    {
                        if (reachCount == 0)
                        {
                            reach = red;
                        }

                        ++reachCount;
                    }
                }

                if ((reachCount == 0) || IsReviveProtected(target.CombatId, eventTime))
                {
                    continue;
                }

                if (reachCount >= 2)
                {
                    if (_gate.IsOpen && _killer.TryKill(target.CombatId, _castId, EWolbaekFinalDeath.DoubleBlast))
                    {
                        ++doubleKills;
                    }

                    continue;
                }

                if (!TryMakeRatioRequest(target.CombatId, _spec.RedSingleDamageRate, actionIds[reach], _reds[reach].Position,
                    EDamageFlags.Unblockable, out DamageRequest request))
                {
                    continue;
                }

                if (_gate.TryApply(in request, out DamageResult result) && result.IsApplied)
                {
                    ++singleHits;
                    BossTargetHitReport.Report(_listener, _castId, target, _reds[reach].Position);
                }
            }

            _hitBuffer.Clear();
            _hasBundle = false;
            _listener?.OnBundleResolved(_castId, _bundleIndex, eventTime, singleHits, doubleKills);
        }

        // 최대 체력에 비율을 곱한 방어 전 피해 요청을 만든다, 최대 체력을 모르면 만들지 못한다
        private bool TryMakeRatioRequest(int combatId, float rate, int actionInstanceId, Vector3 source, EDamageFlags flags,
            out DamageRequest request)
        {
            if (!_health.TryGetMaxHealth(combatId, out float maxHealth))
            {
                if (!_healthMissingLogged)
                {
                    _healthMissingLogged = true;
                    Debug.LogError($"[Boss] 최후의 영역 피해 대상의 최대 체력을 읽지 못했다 - {combatId}");
                }

                request = default;
                return false;
            }

            request = new DamageRequest(_self.CombatId, combatId, _castId, actionInstanceId, rate * maxHealth, flags, source,
                BLAST_ID);
            return true;
        }

        /****************************************
        *              Participants
        ****************************************/

        // 이 기믹 중 부활한 참가자가 이 사건 시각 뒤에 부활했다면 사건 시각에는 쓰러져 있었다
        private bool HasRevivedAfter(int combatId, double eventTime)
        {
            return _revive.TryGetLastReviveServerTime(combatId, out double stamp)
                && (stamp >= (_enteredAt - TIME_EPSILON)) && (stamp > (eventTime + TIME_EPSILON));
        }

        // 부활한 시각부터 보호 시간이 끝나기 전까지, 끝나는 순간은 보호하지 않는다
        private bool IsReviveProtected(int combatId, double eventTime)
        {
            if (!_revive.TryGetLastReviveServerTime(combatId, out double stamp))
            {
                return false;
            }

            return (stamp >= (_enteredAt - TIME_EPSILON)) && (eventTime >= (stamp - TIME_EPSILON))
                && (eventTime < (stamp + _spec.ReviveProtectionSeconds - TIME_EPSILON));
        }

        private bool IsAliveAt(int infoIndex, double eventTime)
        {
            return (_infos[infoIndex].LifeState == EParticipantLifeState.Alive) && (!HasRevivedAfter(_ids[infoIndex], eventTime));
        }

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

        // 최종 성공 신호와 플레이어 보호를 먼저 확정하고 피해 게이트를 닫아 남은 빨간 폭발을 무해하게 만든다. 예약된 사건은 버린다
        private void Succeed(double serverTime)
        {
            _finalSuccess = new BossFinalSuccess(_self.CombatId, _castId, serverTime);
            _successReady = true;
            _state        = EWolbaekFinalState.Succeeded;

            _guard.GrantVictoryProtection(_castId, serverTime);
            _gate.CloseForGood();

            _events.Clear();
            _eventCursor = 0;
            _hasBundle   = false;
            _redsLocked  = false;
            ReleaseYellows();

            _listener?.OnFinalSucceeded(_castId, serverTime);
        }

        private void Fail(EWolbaekFinalFail reason, double serverTime)
        {
            _gate.CloseForGood();
            ReleaseYellows();

            _failReason = reason;
            _state      = EWolbaekFinalState.Failed;
            _hasBundle  = false;
            _events.Clear();
            _eventCursor = 0;
            _listener?.OnFinalFailed(_castId, serverTime, reason);
        }

        // 성공도 실패도 아니다. 보스는 무적으로 남고 사망도 보류된 채 표시를 정리하고 원인만 알린다
        private void Halt(EWolbaekFinalHaltReason reason, double serverTime)
        {
            if (_gate != null)
            {
                _gate.CloseForGood();
            }

            ReleaseYellows();

            _haltReason = reason;
            _state      = EWolbaekFinalState.Halted;
            _hasBundle  = false;
            _events.Clear();
            _eventCursor = 0;
            _listener?.OnFinalHalted(_castId, serverTime, reason);
        }

        // 세운 노란 여우불을 더 이상 맞지 않게 봉인하고 피격 대상에서 뺀다
        private void ReleaseYellows()
        {
            for (int i = 0; i < _yellows.Length; ++i)
            {
                if (_yellows[i] == null)
                {
                    continue;
                }

                _yellows[i].Seal();
                _registry.Unregister(_yellows[i].CombatId);
            }
        }

        private static float FlatDistance(Vector3 from, Vector3 to)
        {
            float dx = from.x - to.x;
            float dz = from.z - to.z;
            return Mathf.Sqrt((dx * dx) + (dz * dz));
        }
    }
}
