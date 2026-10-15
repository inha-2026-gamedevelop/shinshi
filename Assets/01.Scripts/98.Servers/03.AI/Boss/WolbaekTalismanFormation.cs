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
    // 월백 첫 번째 체력 기믹 "사방 부적진"의 서버 진행이다. 보스 두뇌의 기믹 훅(BossBrain.AddGimmick)에 연결되며
    // 전환 판정보다 먼저 Tick이 불리고 진입 순간에 두뇌가 진행 중인 공격과 지속물을 정리한다(IBossGimmickCleanup).
    //
    // 경계에서 멈추기와 무적은 이 클래스가 계산만 하고(IBossDamageCap) 실제로 피해를 깎는 보스 몸체와 피해 판정기가 읽어야 닿는다.
    // 그 연결이 없으면 한 번의 큰 피해가 경계를 넘어가고 무적도 걸리지 않는다. 이 클래스는 보스 체력을 직접 바꾸지 않는다.
    //
    // 필수 참가자는 진입 순간의 출정 명단이다(IBossParticipantRoster). 쓰러진 사람도 포함하고 이후 명단이 바뀌어도 필수 참가자와
    // 구조물 배정, 색은 바뀌지 않는다. 사망, 재접속, 진행 초기화를 거쳐도 같은 전투 번호가 같은 구조물을 쓴다.
    //
    // 한 틱의 순서는 보스 공격 판정, 참가자 상태 확인(실패와 진행 초기화), 성공 확인이다. 판정과 사망이 성공 확인보다 앞이라
    // 5초가 차는 틱에 필수 참가자가 쓰러지면 진행이 먼저 초기화되고 성공하지 못한다.
    //
    // 기준점은 전장 중심이고 월백은 3초 전환 동안 그 중심으로 실제 이동한다(IBossMovement.Move). 구조물과 고리, 직선 공격의 중심은
    // 도착한 월백의 위치와 같다. 이동은 시작 위치에서 중심까지를 전환 시간에 걸쳐 일정한 속도로 가는 시각 기준 목표 위치를 매 틱 새로 계산해
    // 현재 위치에서 그 목표로 한 번 부탁하는 방식이다. 같은 시각에 여러 번 불려도 이미 따라잡은 뒤라 다시 움직이지 않고, 큰 틱도 목표를
    // 넘지 않는다. 전환이 끝나는 틱에 중심에 닿지 못했으면(벽이나 몸체의 이동 거절) 공격을 시작하지 않고 Halted로 멈춘다.
    // 중심에 이미 서 있어도 전환 시간은 그대로이고 공격과 자리 유지 시각은 앞당기지 않는다.
    //
    // 정지(Halted)는 기믹을 건너뛰는 것도 성공이나 실패도 아니다. 보스는 무적으로 남고 일반 선택도 열리지 않으며 원인만 알린다.
    // 이 상태를 끝내는 방법(전투 취소, 재시도, 시간 처리)은 정해지지 않았으므로 이 클래스가 정하지 않는다.
    // 피해는 이 기믹 전용 게이트로만 내고 성공, 실패, 정지에서 영구히 닫는다. 보스전 실패는 세션이 정하므로 이 클래스는 실패를 알리기만 한다
    /// <summary> 월백 사방 부적진 기믹, 75% 경계에서 한 번 발동해 필수 참가자 전원이 자기 자리에 연속으로 머물면 성공하고 반격 시간을 준다 </summary>
    public sealed class WolbaekTalismanFormation : IBossGimmick, IBossGimmickCleanup, IBossDamageCap
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int    FIRST_PHASE       = 1;
        private const int    DIRECTION_COUNT   = 4;
        private const int    MAX_MEMBERS       = 4;
        private const int    OPPOSITE_OFFSET   = 2;
        private const int    RING_PARITY       = 0;
        private const int    PARITY_DIVISOR    = 2;
        private const int    INNER_RING_CHOICE = 0;
        private const int    RING_CHOICE_COUNT = 2;
        private const float  ARRIVE_EPSILON     = 0.05f;    // m, 중앙에 도착했다고 보는 거리
        private const float  MOVE_EPSILON       = 0.001f;   // m, 목표를 따라잡았다고 보고 다시 이동을 부탁하지 않는 거리
        private const float  BOUNDARY_TOLERANCE = 0.00001f; // 체력 비율, 경계에 닿았다고 보는 부동소수 여유
        private const float  MIN_DIRECTION_SQR  = 0.000001f; // m^2, 이보다 가까우면 방향을 정할 수 없다
        private const float  HALF               = 0.5f;
        private const string RING_ID            = "wolbaek_talisman_ring";
        private const string LINE_ID            = "wolbaek_talisman_line";

        // 방위 번호 0 동, 1 북, 2 서, 3 남, 반대편은 번호가 2 차이 난다
        private static readonly Vector3[] G_Directions =
        {
            Vector3.right, Vector3.forward, Vector3.left, Vector3.back
        };

        /****************************************
        *                Fields
        ****************************************/

        private struct Member
        {
            public int CombatId;
            public int StructureSlot;
        }

        private readonly WolbaekTalismanSpec     _spec;
        private readonly ICombatTarget           _self;
        private readonly float                   _referenceHealth;
        private readonly IBossVitals             _vitals;
        private readonly IBossParticipantRoster  _roster;
        private readonly IBossArenaSource        _arena;
        private readonly IBossMovement           _movement;
        private readonly IDamageResolver         _resolver;
        private readonly ICombatTargetQuery      _targets;
        private readonly IBossRandom             _random;
        private readonly IBossHitDisplacer       _displacer;
        private readonly IBossAmbientHazard      _hazard;
        private readonly IWolbaekTalismanListener _listener;
        private readonly List<int>               _idBuffer     = new List<int>();
        private readonly List<int>               _livingBuffer = new List<int>();
        private readonly List<ICombatTarget>     _hitBuffer    = new List<ICombatTarget>();

        private EWolbaekTalismanState     _state = EWolbaekTalismanState.Dormant;
        private EWolbaekFormationFail     _failReason;
        private EWolbaekHaltReason        _haltReason;
        private BossDamageGate            _gate;
        private Member[]                  _members = new Member[0];
        private WolbaekTalismanStructure[] _structures = new WolbaekTalismanStructure[0];
        private WolbaekTalismanStart      _start;
        private WolbaekTalismanAttack     _pending;
        private Vector3                   _anchor;
        private Vector3                   _moveStart;
        private Vector3                   _moveTarget;
        private float                     _moveDistance;
        private float                     _arenaRadius;
        private int                       _castId;
        private int                       _attackIndex;
        private int                       _nextAttackId = 1;
        private double                    _entryTime;
        private double                    _transitionEnd;
        private double                    _holdStart;
        private double                    _counterEnd;
        private bool                      _hasHold;
        private bool                      _hasPending;
        private bool                      _cleanupRequested;

        /****************************************
        *              Properties
        ****************************************/

        public EWolbaekTalismanState State => _state;

        /// <summary> 실패했다면 그 이유, 실패 전에는 의미가 없다 </summary>
        public EWolbaekFormationFail FailReason => _failReason;

        /// <summary> 정지했다면 그 이유, 정지 전에는 의미가 없다 </summary>
        public EWolbaekHaltReason HaltReason => _haltReason;

        /// <summary> 이 기믹의 시전 번호, 진입 전에는 0이다 </summary>
        public int CastInstanceId => _castId;

        /// <summary> 진입 순간 확정된 필수 참가자 수 </summary>
        public int RequiredCount => _members.Length;

        public IReadOnlyList<WolbaekTalismanStructure> Structures => _structures;

        /// <summary> 필수 참가자 전원이 연속으로 자리를 지키는 중이다 </summary>
        public bool HasHold => _hasHold;

        /// <summary> 연속 유지가 시작된 서버 시각, 유지 중이 아니면 의미가 없다 </summary>
        public double HoldStartServerTime => _holdStart;

        /// <summary> 전환 시간이 끝나는 서버 시각, 진입 전에는 0이다 </summary>
        public double TransitionEndServerTime => _transitionEnd;

        /// <summary> 반격 시간이 끝나는 서버 시각, 성공 전에는 0이다 </summary>
        public double CounterEndServerTime => _counterEnd;

        public bool DefersPhaseTransition
        {
            get
            {
                return (_state == EWolbaekTalismanState.Transition) || (_state == EWolbaekTalismanState.Active)
                    || (_state == EWolbaekTalismanState.Counter) || (_state == EWolbaekTalismanState.Failed)
                    || (_state == EWolbaekTalismanState.Halted);
            }
        }

        public bool BlocksSelection => DefersPhaseTransition;

        public bool IsDamageBlocked
        {
            get
            {
                switch (_state)
                {
                    case EWolbaekTalismanState.Dormant:
                        return _vitals.HealthRate <= (_spec.TriggerHealthRate + BOUNDARY_TOLERANCE);
                    case EWolbaekTalismanState.Transition:
                    case EWolbaekTalismanState.Active:
                    case EWolbaekTalismanState.Failed:
                    case EWolbaekTalismanState.Halted:
                        return true;
                    case EWolbaekTalismanState.Counter:
                        return _vitals.HealthRate <= (_spec.NextBoundaryHealthRate + BOUNDARY_TOLERANCE);
                    default:
                        return false;
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        // resolver는 보스 두뇌에 넘기는 피해 경로와 같은 것이어야 시작 인원별 공격 피해 배율이 똑같이 적용된다.
        // movement는 월백을 중앙으로 옮기는 서버 몸체의 이동 창구이고 self의 위치도 그 몸체가 갱신해야 도착을 읽을 수 있다.
        // 하드 공격(spec.Attack)이 있으면 맞은 대상을 미는 displacer가 필요하다. hazard는 상시 환경 위험물이 있을 때만 넘긴다
        public WolbaekTalismanFormation(WolbaekTalismanSpec spec, ICombatTarget self, float referenceHealth, IBossVitals vitals,
            IBossParticipantRoster roster, IBossArenaSource arena, IBossMovement movement, IDamageResolver resolver,
            ICombatTargetQuery targets, IBossRandom random, IBossHitDisplacer displacer = null,
            IBossAmbientHazard hazard = null, IWolbaekTalismanListener listener = null)
        {
            _spec            = spec ?? throw new ArgumentNullException(nameof(spec));
            _self            = self ?? throw new ArgumentNullException(nameof(self));
            _vitals          = vitals ?? throw new ArgumentNullException(nameof(vitals));
            _roster          = roster ?? throw new ArgumentNullException(nameof(roster));
            _arena           = arena ?? throw new ArgumentNullException(nameof(arena));
            _movement        = movement ?? throw new ArgumentNullException(nameof(movement));
            _resolver        = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _targets         = targets ?? throw new ArgumentNullException(nameof(targets));
            _random          = random ?? throw new ArgumentNullException(nameof(random));
            _displacer       = displacer;
            _hazard          = hazard;
            _listener        = listener;
            _referenceHealth = referenceHealth;

            if ((spec.Attack != null) && (displacer == null))
            {
                throw new ArgumentNullException(nameof(displacer), "하드 공격은 맞은 대상을 미는 창구가 필요하다");
            }

            if ((float.IsNaN(referenceHealth)) || (float.IsInfinity(referenceHealth)) || (referenceHealth <= 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(referenceHealth), "지역 기준 체력은 0보다 큰 유한값이다");
            }
        }

        public void Tick(double serverTime, int phase)
        {
            switch (_state)
            {
                case EWolbaekTalismanState.Dormant:
                    TickDormant(serverTime, phase);
                    break;
                case EWolbaekTalismanState.Transition:
                case EWolbaekTalismanState.Active:
                    TickRunning(serverTime);
                    break;
                case EWolbaekTalismanState.Counter:
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
            if ((_state == EWolbaekTalismanState.Completed) || (_state == EWolbaekTalismanState.Stopped))
            {
                return;
            }

            bool hadDisplay = (_state == EWolbaekTalismanState.Transition) || (_state == EWolbaekTalismanState.Active);
            bool hasStarted = _state != EWolbaekTalismanState.Dormant;

            CancelPendingAttack();
            if (_gate != null)
            {
                _gate.CloseForGood();
            }

            if (hasStarted)
            {
                _hazard?.Suspend();
            }

            _state            = EWolbaekTalismanState.Stopped;
            _hasHold          = false;
            _cleanupRequested = false;

            if (hadDisplay)
            {
                _listener?.OnFormationCleared(_castId, reason);
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

            float boundaryRate;
            switch (_state)
            {
                case EWolbaekTalismanState.Transition:
                case EWolbaekTalismanState.Active:
                case EWolbaekTalismanState.Failed:
                case EWolbaekTalismanState.Halted:
                    return 0f;
                case EWolbaekTalismanState.Dormant:
                    boundaryRate = _spec.TriggerHealthRate;
                    break;
                case EWolbaekTalismanState.Counter:
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

        /// <summary> 늦게 접속하거나 재접속한 클라이언트에 줄 현재 값, 진행 중이거나 반격 중일 때만 참이다 </summary>
        public bool TryGetSnapshot(out WolbaekTalismanSnapshot snapshot)
        {
            if ((_state != EWolbaekTalismanState.Transition) && (_state != EWolbaekTalismanState.Active)
                && (_state != EWolbaekTalismanState.Counter))
            {
                snapshot = default;
                return false;
            }

            snapshot = new WolbaekTalismanSnapshot(_state, in _start, _hasHold, _holdStart, _hasPending, in _pending, _counterEnd);
            return true;
        }

        private void TickDormant(double serverTime, int phase)
        {
            // 2페이즈로 넘어간 뒤에는 발동하지 않는다. 경계에서 멈추던 상태도 이때 풀린다
            if (phase != FIRST_PHASE)
            {
                _state = EWolbaekTalismanState.Completed;
                return;
            }

            if (_vitals.HealthRate > (_spec.TriggerHealthRate + BOUNDARY_TOLERANCE))
            {
                return;
            }

            Begin(serverTime);
        }

        // 진입한 순간 이전 공격은 정리를 요청해 이미 멈췄다. 전장을 모르면 구조물도 이동 목표도 정할 수 없어 건너뛰지 않고 정지한다
        private void Begin(double serverTime)
        {
            _gate = new BossDamageGate(_resolver);
            _gate.Open();

            _castId           = _gate.IssueInstanceId();
            _entryTime        = serverTime;
            _transitionEnd    = serverTime + _spec.TransitionSeconds;
            _cleanupRequested = true;

            _hazard?.Suspend();

            if (!_arena.TryGetArena(out Vector3 center, out float radius))
            {
                Debug.LogError("[Boss] 전장 정보를 얻지 못해 부적진 기믹을 정지한다");
                Halt(EWolbaekHaltReason.ArenaUnavailable, serverTime);
                return;
            }

            _anchor      = center;
            _arenaRadius = radius;

            _moveStart    = _self.Position;
            _moveTarget   = new Vector3(center.x, _moveStart.y, center.z);
            _moveDistance = FlatDistance(_moveStart, _moveTarget);

            BuildMembers();
            BuildStructures();

            _start = new WolbaekTalismanStart(_self.CombatId, _castId, _entryTime, _transitionEnd, _spec.HoldSeconds, _anchor,
                _structures);
            _state = EWolbaekTalismanState.Transition;

            if (_members.Length == 0)
            {
                Fail(EWolbaekFormationFail.Wipe, serverTime);
                return;
            }

            _listener?.OnFormationStarted(in _start);
        }

        // 명단 순서를 섞어 시도마다 다른 구조물을 받게 한다. 파티 상한을 넘는 인원은 필수 참가자로 세지 않는다
        private void BuildMembers()
        {
            _idBuffer.Clear();
            _roster.CollectParticipants(_idBuffer);

            int count = _idBuffer.Count;
            if (count > MAX_MEMBERS)
            {
                Debug.LogError($"[Boss] 부적진 필수 참가자가 상한을 넘었다 - {count}명, 앞의 {MAX_MEMBERS}명만 쓴다");
                count = MAX_MEMBERS;
            }

            int[] order = new int[count];
            for (int i = 0; i < count; ++i)
            {
                order[i] = _idBuffer[i];
            }

            Shuffle(order);

            _members = new Member[count];
            for (int i = 0; i < count; ++i)
            {
                _members[i] = new Member { CombatId = order[i], StructureSlot = i };
            }

            _idBuffer.Clear();
        }

        // 참가 인원에 따라 쓰는 방위가 정해진다. 한 명은 한 곳, 두 명은 반대편 두 곳, 세 명은 한 곳을 뺀 세 곳, 네 명은 네 곳이다.
        // 색은 쓰는 구조물마다 서로 다르게 섞어 나눈다
        private void BuildStructures()
        {
            int[] directions = PickDirections(_members.Length);

            EWolbaekTalismanColor[] colors =
            {
                EWolbaekTalismanColor.Red, EWolbaekTalismanColor.Blue, EWolbaekTalismanColor.Yellow, EWolbaekTalismanColor.Green
            };
            Shuffle(colors);

            _structures = new WolbaekTalismanStructure[directions.Length];
            for (int i = 0; i < directions.Length; ++i)
            {
                Vector3 center = _anchor + (G_Directions[directions[i]] * _spec.StructureDistance);
                int assigned   = (i < _members.Length) ? _members[i].CombatId : CombatIds.NONE;
                _structures[i] = new WolbaekTalismanStructure(directions[i], colors[i], (int)colors[i], center,
                    _spec.StructureRadius, assigned);
            }
        }

        private int[] PickDirections(int count)
        {
            if (count <= 0)
            {
                return new int[0];
            }

            if (count == 1)
            {
                return new[] { _random.NextInt(DIRECTION_COUNT) };
            }

            if (count == 2)
            {
                int first = _random.NextInt(OPPOSITE_OFFSET);
                return new[] { first, first + OPPOSITE_OFFSET };
            }

            if (count == 3)
            {
                int omitted = _random.NextInt(DIRECTION_COUNT);
                int[] three = new int[DIRECTION_COUNT - 1];
                int next    = 0;
                for (int i = 0; i < DIRECTION_COUNT; ++i)
                {
                    if (i == omitted)
                    {
                        continue;
                    }

                    three[next] = i;
                    ++next;
                }

                return three;
            }

            return new[] { 0, 1, 2, 3 };
        }

        private void TickRunning(double serverTime)
        {
            if (_state == EWolbaekTalismanState.Transition)
            {
                AdvanceMovement(serverTime);

                if (serverTime >= _transitionEnd)
                {
                    // 중앙에 닿지 못한 채 공격과 자리 유지를 시작하면 구조물과 공격 중심이 월백과 어긋난다
                    if (!HasArrived())
                    {
                        Halt(EWolbaekHaltReason.MovementBlocked, serverTime);
                        return;
                    }

                    EnterActive();
                }
            }

            if (_state == EWolbaekTalismanState.Active)
            {
                AdvanceAttacks(serverTime);
            }

            EvaluateParticipants(serverTime);

            if ((_state == EWolbaekTalismanState.Active) && (_hasHold) && (serverTime >= (_holdStart + _spec.HoldSeconds)))
            {
                Succeed(serverTime);
            }
        }

        // 시작 위치에서 중앙까지 전환 시간에 걸쳐 일정한 속도로 가는 목표 위치를 시각으로 계산해 현재 위치에서 그곳으로 한 번 부탁한다.
        // 목표가 시각의 함수라 같은 시각에 여러 번 불려도 이동량이 쌓이지 않고, 큰 틱도 중앙을 넘지 않는다.
        // 벽에 막혀 뒤처지면 이후 틱의 목표가 막힌 지점에서 이어 가며 전환 끝 틱의 도착 검사가 막힘을 알아낸다
        private void AdvanceMovement(double serverTime)
        {
            if (_moveDistance <= ARRIVE_EPSILON)
            {
                return;
            }

            float   progress = Mathf.Clamp01((float)((serverTime - _entryTime) / _spec.TransitionSeconds));
            Vector3 desired  = Vector3.Lerp(_moveStart, _moveTarget, progress);
            Vector3 current  = _self.Position;

            if (FlatDistance(current, desired) <= MOVE_EPSILON)
            {
                return;
            }

            _movement.Move(current, desired, _self.BodyRadius);
        }

        private bool HasArrived()
        {
            return FlatDistance(_self.Position, _moveTarget) <= ARRIVE_EPSILON;
        }

        private void EnterActive()
        {
            _state       = EWolbaekTalismanState.Active;
            _attackIndex = 0;

            if ((_spec.Attack != null) && (_spec.Attack.ResumesAmbientHazard))
            {
                _hazard?.Resume(_transitionEnd);
            }
        }

        private void TickCounter(double serverTime)
        {
            if (serverTime < _counterEnd)
            {
                return;
            }

            _state = EWolbaekTalismanState.Completed;
            _listener?.OnCounterEnded(_castId, serverTime);
        }

        // 필수 참가자 전원의 상태를 한 번에 본다. 실패는 진행 초기화보다 앞선다
        private void EvaluateParticipants(double serverTime)
        {
            int  livingCount   = 0;
            bool unrecoverable = false;
            bool allInPlace    = true;
            int  offender      = CombatIds.NONE;
            EWolbaekHoldReset offenderReason = EWolbaekHoldReset.Left;

            for (int i = 0; i < _members.Length; ++i)
            {
                Member member  = _members[i];
                bool   present = _roster.TryGetParticipant(member.CombatId, out BossParticipantInfo info);
                bool   alive   = present && (info.LifeState == EParticipantLifeState.Alive);

                if (alive)
                {
                    ++livingCount;
                }

                if ((!present) || ((!alive) && (!info.IsRevivable)))
                {
                    unrecoverable = true;
                }

                bool inPlace = alive && info.IsConnected && IsInsideStructure(info.Position, _structures[member.StructureSlot]);
                if (inPlace)
                {
                    continue;
                }

                allInPlace = false;
                if (offender == CombatIds.NONE)
                {
                    offender       = member.CombatId;
                    offenderReason = !alive ? EWolbaekHoldReset.Died : (!info.IsConnected ? EWolbaekHoldReset.Disconnected : EWolbaekHoldReset.Left);
                }
            }

            if (livingCount == 0)
            {
                Fail(EWolbaekFormationFail.Wipe, serverTime);
                return;
            }

            if (unrecoverable)
            {
                Fail(EWolbaekFormationFail.Unrecoverable, serverTime);
                return;
            }

            // 전환 시간 동안에는 자리 유지를 재지 않는다
            if (_state != EWolbaekTalismanState.Active)
            {
                return;
            }

            UpdateHold(serverTime, allInPlace, offender, offenderReason);
        }

        private void UpdateHold(double serverTime, bool allInPlace, int offender, EWolbaekHoldReset reason)
        {
            if (allInPlace)
            {
                if (!_hasHold)
                {
                    _hasHold   = true;
                    _holdStart = serverTime;
                    _listener?.OnHoldStarted(_castId, serverTime);
                }

                return;
            }

            if (!_hasHold)
            {
                return;
            }

            _hasHold = false;
            _listener?.OnHoldReset(_castId, serverTime, reason, offender);
        }

        private static bool IsInsideStructure(Vector3 position, in WolbaekTalismanStructure structure)
        {
            return FlatDistance(position, structure.Center) <= structure.Radius;
        }

        // 공격은 참가자 도착 여부와 상관없이 전환 시간이 끝난 시각부터 정해진 간격으로 이어진다.
        // 거친 틱에서도 예고와 판정을 시각 순서대로 모두 처리한다
        private void AdvanceAttacks(double serverTime)
        {
            WolbaekTalismanAttackSpec attack = _spec.Attack;
            if (attack == null)
            {
                return;
            }

            while (_state == EWolbaekTalismanState.Active)
            {
                if (_hasPending)
                {
                    if (serverTime < _pending.StrikeServerTime)
                    {
                        return;
                    }

                    Strike();
                    ++_attackIndex;
                    continue;
                }

                double telegraphTime = _transitionEnd + (_attackIndex * (double)attack.IntervalSeconds);
                if (serverTime < telegraphTime)
                {
                    return;
                }

                if (!BeginAttack(telegraphTime, telegraphTime + attack.TelegraphSeconds))
                {
                    ++_attackIndex;
                }
            }
        }

        // 고리와 직선을 번갈아 쓴다. 직선은 살아 있는 필수 참가자 한 명의 구조물을 겨누는데, 아무도 살아 있지 않으면 이번 공격은 건너뛴다
        private bool BeginAttack(double telegraphTime, double strikeTime)
        {
            WolbaekTalismanAttackSpec spec = _spec.Attack;
            int attackId = _nextAttackId;

            if ((_attackIndex % PARITY_DIVISOR) == RING_PARITY)
            {
                bool isInner = _random.NextInt(RING_CHOICE_COUNT) == INNER_RING_CHOICE;
                float inner  = isInner ? spec.InnerRingMinRadius : spec.RingSplitRadius;
                float outer  = isInner ? spec.RingSplitRadius : spec.OuterRingMaxRadius;
                _pending = WolbaekTalismanAttack.Ring(attackId, telegraphTime, strikeTime, _anchor, inner, outer);
            }
            else
            {
                if (!TryPickLineTarget(out Member target))
                {
                    return false;
                }

                Vector3 direction = Flatten(_structures[target.StructureSlot].Center - _anchor, Vector3.forward);
                _pending = WolbaekTalismanAttack.Line(attackId, telegraphTime, strikeTime, _anchor, direction, _arenaRadius,
                    spec.LineWidth, target.CombatId);
            }

            ++_nextAttackId;
            _hasPending = true;
            _listener?.OnAttackTelegraphed(_castId, in _pending);
            return true;
        }

        private bool TryPickLineTarget(out Member target)
        {
            _livingBuffer.Clear();
            for (int i = 0; i < _members.Length; ++i)
            {
                if (_roster.TryGetParticipant(_members[i].CombatId, out BossParticipantInfo info)
                    && (info.LifeState == EParticipantLifeState.Alive))
                {
                    _livingBuffer.Add(i);
                }
            }

            if (_livingBuffer.Count == 0)
            {
                target = default;
                return false;
            }

            target = _members[_livingBuffer[_random.NextInt(_livingBuffer.Count)]];
            return true;
        }

        // 한 동작 번호를 모든 대상이 나눠 쓰므로 대상당 한 번만 맞는다. 방어와 반격으로 막을 수 없고 맞은 대상은 보스 바깥쪽으로 민다
        private void Strike()
        {
            WolbaekTalismanAttackSpec spec = _spec.Attack;
            bool   isRing = _pending.Kind == EWolbaekTalismanAttackKind.Ring;
            float  radius = isRing ? _pending.OuterRadius : (_pending.Length + _pending.Width);

            _targets.GetLivingInRadius(_anchor, radius, ECombatFaction.Player, true, _hitBuffer);

            int    actionInstanceId = _gate.IssueInstanceId();
            float  rawDamage        = spec.DamageRef * _referenceHealth;
            string definitionId     = isRing ? RING_ID : LINE_ID;
            int    hitCount         = 0;

            for (int i = 0; i < _hitBuffer.Count; ++i)
            {
                ICombatTarget target   = _hitBuffer[i];
                Vector3       position = target.Position;
                if (!IsInsideAttack(position))
                {
                    continue;
                }

                DamageRequest request = new DamageRequest(_self.CombatId, target.CombatId, _castId, actionInstanceId, rawDamage,
                    EDamageFlags.Unblockable, _anchor, definitionId);

                if ((!_gate.TryApply(in request, out DamageResult result)) || (!result.IsApplied))
                {
                    continue;
                }

                ++hitCount;

                if (spec.PushDistance > 0f)
                {
                    Vector3 fallback = isRing ? Vector3.forward : _pending.Direction;
                    _displacer.TryPush(target.CombatId, Flatten(position - _anchor, fallback), spec.PushDistance);
                }
            }

            _hitBuffer.Clear();
            _hasPending = false;
            _listener?.OnAttackStruck(_castId, _pending.AttackId, _pending.StrikeServerTime, hitCount);
        }

        // 경계는 안쪽에 포함한다. 두 고리가 맞닿는 반경 위의 대상은 두 고리 모두에 속한다
        private bool IsInsideAttack(Vector3 position)
        {
            if (_pending.Kind == EWolbaekTalismanAttackKind.Ring)
            {
                float distance = FlatDistance(position, _pending.Origin);
                return (distance >= _pending.InnerRadius) && (distance <= _pending.OuterRadius);
            }

            Vector3 offset = position - _pending.Origin;
            offset.y = 0f;

            float along = Vector3.Dot(offset, _pending.Direction);
            if ((along < 0f) || (along > _pending.Length))
            {
                return false;
            }

            float perpendicularSqr = Mathf.Max(0f, offset.sqrMagnitude - (along * along));
            float halfWidth        = _pending.Width * HALF;
            return perpendicularSqr <= (halfWidth * halfWidth);
        }

        private void Succeed(double serverTime)
        {
            CancelPendingAttack();
            _gate.CloseForGood();

            _hasHold    = false;
            _state      = EWolbaekTalismanState.Counter;
            _counterEnd = serverTime + _spec.CounterSeconds;
            _listener?.OnFormationSucceeded(_castId, serverTime, _counterEnd);
        }

        private void Fail(EWolbaekFormationFail reason, double serverTime)
        {
            CancelPendingAttack();
            _gate.CloseForGood();

            _hasHold    = false;
            _failReason = reason;
            _state      = EWolbaekTalismanState.Failed;
            _listener?.OnFormationFailed(_castId, serverTime, reason);
        }

        // 성공도 실패도 아니다. 보스는 무적으로 남고 일반 선택은 열리지 않으며 표시를 정리하고 원인만 알린다
        private void Halt(EWolbaekHaltReason reason, double serverTime)
        {
            CancelPendingAttack();
            if (_gate != null)
            {
                _gate.CloseForGood();
            }

            _hasHold    = false;
            _haltReason = reason;
            _state      = EWolbaekTalismanState.Halted;
            _listener?.OnFormationHalted(_castId, serverTime, reason);
        }

        private void CancelPendingAttack()
        {
            if (!_hasPending)
            {
                return;
            }

            _hasPending = false;
            _listener?.OnAttackCancelled(_castId, _pending.AttackId);
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
