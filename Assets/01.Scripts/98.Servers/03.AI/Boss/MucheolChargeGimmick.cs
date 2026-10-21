// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Common;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    // 무철 기믹 1 "돌진 유도"의 상태 관리와 P06 강제 선택 규칙이다. 발동 비율, 필요 구조물 수, 그로기 시간은 설정(MucheolChargeGimmickSpec)이 정한다.
    //
    // 진행도는 서버가 확인한 구조물 파괴(ConfirmStructureDestroyed)로만 오른다. P06의 충돌 알림(IBossChargeOutcomeListener)은
    // 이 클래스가 받지 않는다. 충돌이 곧 파괴가 아니고, 벽 충돌이나 플레이어 피격은 진행도에 영향을 주지 않는다.
    //
    // 시전 추적: 선택 규칙이 P06을 고르는 것(TrySelect)과 시전이 실제로 시작되는 것은 다르다. TrySelect는 선택 대기만 표시하고,
    // 패턴이 시작되며 알리는 OnChargeCastStarted가 와야 그 시전 ID를 추적한다. 선택 뒤 패턴 생성이 실패해 시작 알림이 오지 않으면
    // 다음 Tick에서 선택 대기를 시작 실패로 거두고 Active를 유지한다(공용 두뇌는 선택한 같은 틱 안에서 시작까지 처리한다).
    // 종료 알림(OnCastFinished)은 추적 중인 시전 ID와 같을 때만 받고 다른 시전의 종료는 무시한다. 추적 중인 시전이 정상 종료 없이
    // 취소되면 OnChargeCastCancelled로 추적을 거둔다. 이렇게 마지막 파괴를 기다리는 AwaitingFinalRecovery가 영구히 남지 않는다.
    // 시작과 취소는 팩토리가 이 기믹을 수신자로 연결해야(SetChargeCastTracker) 닿는다. 연결이 없으면 시전을 추적하지 못해 마지막 파괴가
    // 확인되는 순간 회복이 끝나기 전에 그로기가 시작되므로, 연결이 검증되지 않으면 발동하지 않고 진행 중에 풀리면 멈춘다.
    //
    // 그로기는 마지막 구조물을 부순 시전의 회복이 끝난 FSM 경계 시각부터 시작한다. 경계 시각은 틱 시각이 아니라 시전 종료 알림이
    // 싣고 오는 값이라 틱이 거칠어도 정확하다. 마지막 파괴 확인이 시전 중에 오면 그 시전의 종료 경계를 기다리고, 시전 사이에 오면 확인 시각부터,
    // 확인이 늦게 왔지만 시전 ID가 방금 정상 종료한 시전이고 그 뒤 새 시전이 시작되기 전이면 그 시전의 종료 경계를 보존해 거기서 시작한다.
    // 이미 다음 P06이 시작된 뒤에 늦게 오면 과거로 돌려 적용할 수 없어 그 다음 시전의 종료 경계를 기다린다(한계, 인수인계에 명시).
    //
    // 기믹 중 체력이 2페이즈 기준 이하가 되어도 구조물 파괴와 그로기를 먼저 마치도록 전환을 보류하고(DefersPhaseTransition),
    // 그로기 중에는 새 선택을 막는다(BlocksSelection). 사망과 전투 종료는 보스 두뇌가 이 틱 앞에서 처리하므로 기믹보다 앞선다.
    // 체력 락(IBossDamageCap): 발동 비율에 닿는 피해는 경계까지만 깎고 초과분은 버린다. 기믹이 진행되는 동안(Active, AwaitingFinalRecovery)에는
    // 모든 피해를 버리고, 그로기 중에는 락이 풀려 피해를 받는다. 발동할 수 없는 기믹(구조물 부족 등)은 락을 걸지 않는다.
    // 이 클래스는 체력을 직접 바꾸지 않고 계산만 하므로, 보스 몸체가 이 상한을 읽어야 락이 닿는다.
    // 표현 알림(IMucheolChargeGimmickListener)은 마지막 파괴 확인, 그로기 시작, 사망과 전투 종료가 아닌 이유로 접은 그로기만 알린다.
    // 그로기 끝은 시작 알림의 끝 시각으로 정해져 따로 알리지 않는다
    /// <summary> 무철 돌진 유도 기믹, 체력 구간에서 한 번 발동해 P06을 반복하고 지정 구조물 파괴가 확인되면 그로기에 들어간다 </summary>
    public sealed class MucheolChargeGimmick : IBossGimmick, IBossChargeCastTracker, IBossDamageCap
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int FIRST_PHASE = 1;
        private const int NO_CAST_ID  = 0; // 공용 시전 번호의 없음 값(CombatIds.NONE)과 같다

        private const float BOUNDARY_TOLERANCE = 0.00001f; // 체력 비율, 경계에 닿았다고 보는 부동소수 여유

        /****************************************
        *                Fields
        ****************************************/

        private readonly IBossVitals                    _vitals;
        private readonly IBossPatternCatalog            _catalog;
        private readonly IBossPatternFactory            _factory;
        private readonly string                         _bossId;
        private readonly MucheolChargeGimmickSpec       _spec;
        private readonly IMucheolChargeGimmickListener  _listener;
        private readonly HashSet<int>                   _designated = new HashSet<int>();
        private readonly HashSet<int>                   _destroyed  = new HashSet<int>();
        private readonly List<PatternDefinition>        _buffer     = new List<PatternDefinition>();

        private EMucheolChargeGimmickState _state = EMucheolChargeGimmickState.Dormant;
        private int                        _lastPhase = FIRST_PHASE;
        private bool                       _selectionPending;
        private int                        _trackedCastId       = NO_CAST_ID;
        private int                        _lastFinishedCastId  = NO_CAST_ID;
        private double                     _lastFinishedBoundary;
        private bool                       _beginGroggyOnNextTick;
        private int                        _failedStartCount;
        private double                     _groggyStart;
        private double                     _groggyEnd;
        private int                        _groggyCastId        = NO_CAST_ID; // 마지막 구조물을 부순 시전, 모르면 0이다
        private int                        _groggyStructureId;

        /****************************************
        *              Properties
        ****************************************/

        public EMucheolChargeGimmickState State => _state;

        /// <summary> 지금까지 확인된 서로 다른 지정 구조물 파괴 수 </summary>
        public int DestroyedCount => _destroyed.Count;

        /// <summary> 기믹이 고른 P06 시전이 실제로 시작되어 진행 중이다 </summary>
        public bool IsCastInFlight => _trackedCastId != NO_CAST_ID;

        /// <summary> 추적 중인 시전 ID, 없으면 0이다 </summary>
        public int TrackedCastId => _trackedCastId;

        /// <summary> P06을 골랐지만 시전이 시작됐다는 알림을 아직 받지 못했다 </summary>
        public bool IsSelectionPending => _selectionPending;

        /// <summary> 선택했지만 시전이 시작되지 않아 거둔 횟수 </summary>
        public int FailedStartCount => _failedStartCount;

        /// <summary> 그로기가 시작된 서버 시각, 그로기에 들어가기 전에는 0이다 </summary>
        public double GroggyStartTime => _groggyStart;

        /// <summary> 그로기가 끝나는 서버 시각, 이 시각부터는 그로기가 아니다(끝 경계 미포함) </summary>
        public double GroggyEndTime => _groggyEnd;

        public bool DefersPhaseTransition
        {
            get
            {
                return (_state == EMucheolChargeGimmickState.Active)
                    || (_state == EMucheolChargeGimmickState.AwaitingFinalRecovery)
                    || (_state == EMucheolChargeGimmickState.Groggy);
            }
        }

        /// <summary> 기믹이 진행되는 동안 체력이 경계에 잠겨 어떤 피해도 받지 않는다, 그로기에서 풀린다 </summary>
        public bool IsDamageBlocked
        {
            get
            {
                return (_state == EMucheolChargeGimmickState.Active)
                    || (_state == EMucheolChargeGimmickState.AwaitingFinalRecovery);
            }
        }

        public bool BlocksSelection
        {
            get
            {
                return (_state == EMucheolChargeGimmickState.AwaitingFinalRecovery)
                    || (_state == EMucheolChargeGimmickState.Groggy);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        // catalog와 factory는 P06을 지금 고를 수 있는지 확인하는 데 쓴다. 운영 카탈로그에 P06 정의가 없으면 기믹은 발동하지 않는다.
        // listener는 그로기 표현 알림을 받는 서버 연결이고 비우면 알리지 않는다
        public MucheolChargeGimmick(IBossVitals vitals, IBossPatternCatalog catalog, IBossPatternFactory factory, string bossId,
            MucheolChargeGimmickSpec spec, IMucheolChargeGimmickListener listener = null)
        {
            if (vitals == null)
            {
                throw new ArgumentNullException(nameof(vitals));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            if (string.IsNullOrEmpty(bossId))
            {
                throw new ArgumentException("보스 ID가 비어 있다", nameof(bossId));
            }

            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            _vitals  = vitals;
            _catalog = catalog;
            _factory = factory;
            _bossId   = bossId;
            _spec     = spec;
            _listener = listener;
        }

        /// <summary> 파괴해야 하는 지정 구조물의 식별자를 정한다, 발동 전에만 받는다. 식별자는 0보다 크고 서로 달라야 하며 어긋나면 아무것도 바꾸지 않고 거짓이다 </summary>
        public bool SetDesignatedStructures(IReadOnlyList<int> structureIds)
        {
            if ((_state != EMucheolChargeGimmickState.Dormant) || (structureIds == null))
            {
                return false;
            }

            HashSet<int> candidate = new HashSet<int>();
            for (int i = 0; i < structureIds.Count; ++i)
            {
                if ((structureIds[i] <= 0) || (!candidate.Add(structureIds[i])))
                {
                    return false;
                }
            }

            _designated.Clear();
            _designated.UnionWith(candidate);
            return true;
        }

        // 체력 조건은 이 틱의 값이다. 한 번의 피해로 발동 기준과 2페이즈 기준을 함께 넘어도 이 틱에서 먼저 발동하므로
        // 같은 틱의 전환 판정이 보류를 읽을 수 있다. 매 전투 틱 맨 앞에서 불려야 하며, 선택 평가가 없는 틱에도 불려야 한다
        public void Tick(double serverTime, int phase)
        {
            _lastPhase = phase;

            // 시전 추적 연결이 풀리면 어느 시전의 회복을 기다릴지 알 수 없어 회복 전에 그로기가 시작될 수 있다. 진행 중이던 기믹은 멈추고
            // 일반 흐름으로 돌아간다. 이미 시작한 그로기는 정당하게 시작한 것이라 끝까지 간다
            if (((_state == EMucheolChargeGimmickState.Active) || (_state == EMucheolChargeGimmickState.AwaitingFinalRecovery))
                && (!IsCastTrackerWired()))
            {
                bool wasAwaiting = _state == EMucheolChargeGimmickState.AwaitingFinalRecovery;
                Stop();
                if (wasAwaiting)
                {
                    _listener?.OnGroggyCancelled(_groggyCastId);
                }

                return;
            }

            // 지난 틱에 고른 P06이 같은 틱 안에 시작되지 않았다, 시작 알림이 왔다면 이미 대기가 풀려 있다
            if (_selectionPending)
            {
                _selectionPending = false;
                ++_failedStartCount;
            }

            // 마지막 파괴를 기다리던 시전이 정상 종료 없이 취소됐다, 종료 경계가 없으므로 이 틱 시각부터 그로기다
            if ((_beginGroggyOnNextTick) && (_state == EMucheolChargeGimmickState.AwaitingFinalRecovery))
            {
                BeginGroggy(serverTime);
            }

            if (_state == EMucheolChargeGimmickState.Dormant)
            {
                if ((phase == FIRST_PHASE) && (_vitals.HealthRate <= (_spec.TriggerHealthRate + BOUNDARY_TOLERANCE)) && (CanRun()))
                {
                    _state = EMucheolChargeGimmickState.Active;
                }

                return;
            }

            // 그로기는 끝 경계 시각부터 끝난다. 틱이 거칠어 한 번에 넘어도 같은 틱에서 끝나 전환이 일반 선택보다 먼저 판정된다
            if ((_state == EMucheolChargeGimmickState.Groggy) && (serverTime >= _groggyEnd))
            {
                _state = EMucheolChargeGimmickState.Completed;
            }
        }

        // 발동 전에는 피해가 발동 비율을 넘지 못하게 경계까지만 허용하고 초과분을 버린다. 기믹이 진행되는 동안에는 모두 버린다.
        // 2페이즈로 넘어갔거나 기믹이 끝났거나 멈췄으면 막지 않는다. 발동할 수 없는 상태라면 경계를 넘어도 락을 걸지 않아 보스가 영구히 잠기지 않는다
        public float CapDamage(float currentHealth, float maxHealth, float damage)
        {
            if (!(damage > 0f))
            {
                return 0f;
            }

            switch (_state)
            {
                case EMucheolChargeGimmickState.Active:
                case EMucheolChargeGimmickState.AwaitingFinalRecovery:
                    return 0f;
                case EMucheolChargeGimmickState.Dormant:
                    break;
                default:
                    return damage;
            }

            if (_lastPhase != FIRST_PHASE)
            {
                return damage;
            }

            float allowed = currentHealth - (_spec.TriggerHealthRate * maxHealth);
            if (damage <= allowed)
            {
                return damage;
            }

            if (!CanRun())
            {
                return damage;
            }

            return (allowed > Constants.Combat.HEALTH_EPSILON) ? allowed : 0f;
        }

        // 서버가 파괴를 확인한 시전 ID를 함께 넘기면 늦게 와도 그 시전의 회복 종료 경계를 보존할 수 있다. 모르면 비워 둔다.
        /// <summary> 서버가 확인한 구조물 파괴를 받는다, 진행도가 올랐으면 참이다. 기믹 중이 아니거나 미등록이거나 이미 센 식별자면 거짓이다 </summary>
        public bool ConfirmStructureDestroyed(int structureId, double serverTime, int castInstanceId = NO_CAST_ID)
        {
            if (_state != EMucheolChargeGimmickState.Active)
            {
                return false;
            }

            if ((!_designated.Contains(structureId)) || (!_destroyed.Add(structureId)))
            {
                return false;
            }

            if (_destroyed.Count >= _spec.RequiredStructureCount)
            {
                ArriveAtFinalDestruction(serverTime, castInstanceId, structureId);
            }

            return true;
        }

        // 기믹 중에만 P06을 정한다. 가중치는 읽지 않는다. 고른 것은 선택 대기일 뿐이고 시전이 시작됐는지는 OnChargeCastStarted가 정한다.
        // P06을 고를 수 없게 되면 가중 선택이 이어지도록 기믹을 멈춘다
        public bool TrySelect(BossBlackboard blackboard, out PatternDefinition pattern)
        {
            pattern = null;

            if (_state != EMucheolChargeGimmickState.Active)
            {
                return false;
            }

            if ((!IsCastTrackerWired()) || (!TryFindChargePattern(out PatternDefinition found)))
            {
                Stop();
                return false;
            }

            pattern           = found;
            _selectionPending = true;
            return true;
        }

        // 기믹이 고른 선택이 실제로 시작된 시전만 추적한다. 선택하지 않은 시전의 시작은 무시한다.
        // 이미 추적 중인 시전이 있어도(종료 알림을 받지 못한 경우) 새 시전이 시작됐다면 이전 시전은 끝난 것이라 새 시전으로 바꾼다
        public void OnChargeCastStarted(int castInstanceId)
        {
            if ((!_selectionPending) || (_state != EMucheolChargeGimmickState.Active) || (castInstanceId == NO_CAST_ID))
            {
                return;
            }

            _selectionPending = false;
            _trackedCastId    = castInstanceId;

            // 새 시전이 실제로 시작됐으니 이전 시전의 종료 경계는 더 이상 보존하지 않는다. 그 뒤에 보스가 다시 돌진했으므로
            // 이전 시전의 종료 시각으로 그로기를 소급하면 새 시전이 그로기 안에서 일어난 것이 된다. 새 시전이 취소돼도 마찬가지다
            _lastFinishedCastId = NO_CAST_ID;
        }

        // 추적 중인 시전이 정상 종료 없이 취소됐다. 사망이나 전투 종료면 기믹을 멈추고, 마지막 파괴를 기다리던 중이면 다음 틱에 그로기를 시작한다.
        // 구조물 파괴 전의 취소는 추적만 거두고 Active를 유지해 다음 선택이 P06을 다시 고른다
        public void OnChargeCastCancelled(int castInstanceId, EBossInterruptReason reason)
        {
            if ((_trackedCastId == NO_CAST_ID) || (castInstanceId != _trackedCastId))
            {
                return;
            }

            _trackedCastId = NO_CAST_ID;

            if ((reason == EBossInterruptReason.Death) || (reason == EBossInterruptReason.CombatEnd))
            {
                Stop();
                return;
            }

            if (_state == EMucheolChargeGimmickState.AwaitingFinalRecovery)
            {
                _beginGroggyOnNextTick = true;
            }
        }

        public void OnCastExecuted(int castInstanceId, double serverTime)
        {
        }

        // 추적 중인 시전의 종료만 받는다. 시전 ID가 다르면 기믹과 상관없는 시전의 종료라 무시한다.
        // 마지막 구조물 파괴를 기다리던 시전이면 회복 종료 경계에서 그로기를 시작한다
        public void OnCastFinished(int castInstanceId, double serverTime)
        {
            if ((_trackedCastId == NO_CAST_ID) || (castInstanceId != _trackedCastId))
            {
                return;
            }

            _trackedCastId         = NO_CAST_ID;
            _lastFinishedCastId    = castInstanceId;
            _lastFinishedBoundary  = serverTime;

            if (_state == EMucheolChargeGimmickState.AwaitingFinalRecovery)
            {
                BeginGroggy(serverTime);
            }
        }

        /// <summary> 사망이나 전투 종료 때 기믹을 멈춘다, 이후 입력은 모두 무시한다 </summary>
        public void Stop()
        {
            _state                 = EMucheolChargeGimmickState.Stopped;
            _selectionPending      = false;
            _trackedCastId         = NO_CAST_ID;
            _beginGroggyOnNextTick = false;
        }

        // 마지막 구조물 파괴가 확인됐을 때 그로기 시작 방식을 정한다. 순서는 파괴를 낸 시전이 진행 중인 경우, 방금 정상 종료한 경우,
        // 다른 시전이 진행 중인 경우(한계), 시전 사이인 경우다
        private void ArriveAtFinalDestruction(double serverTime, int castInstanceId, int structureId)
        {
            bool hasCast = castInstanceId != NO_CAST_ID;

            _groggyCastId      = castInstanceId;
            _groggyStructureId = structureId;
            _listener?.OnGroggyPending(castInstanceId, structureId, serverTime);

            if ((hasCast) && (castInstanceId == _trackedCastId))
            {
                _state = EMucheolChargeGimmickState.AwaitingFinalRecovery;
                return;
            }

            if ((hasCast) && (castInstanceId == _lastFinishedCastId) && (_trackedCastId == NO_CAST_ID))
            {
                BeginGroggy(_lastFinishedBoundary);
                return;
            }

            if (_trackedCastId != NO_CAST_ID)
            {
                _state = EMucheolChargeGimmickState.AwaitingFinalRecovery;
                return;
            }

            BeginGroggy(serverTime);
        }

        private void BeginGroggy(double startTime)
        {
            _groggyStart           = startTime;
            _groggyEnd             = startTime + _spec.GroggySeconds;
            _state                 = EMucheolChargeGimmickState.Groggy;
            _selectionPending      = false;
            _beginGroggyOnNextTick = false;
            _listener?.OnGroggyStarted(_groggyCastId, _groggyStructureId, _groggyStart, _groggyEnd);
        }

        // 지정 구조물이 필요한 수만큼 있어야 끝낼 수 있는 기믹이다. P06도 지금 고를 수 있어야 하고, 시전 추적 연결도 검증돼야 한다
        private bool CanRun()
        {
            return (_designated.Count >= _spec.RequiredStructureCount) && (IsCastTrackerWired())
                && (TryFindChargePattern(out PatternDefinition _));
        }

        // 팩토리가 시전의 시작과 취소를 알릴 수신자로 이 기믹을 가리키는지 확인한다. 팩토리가 연결 정보를 밝히지 않거나
        // 다른 수신자를 가리키면 거짓이다. 이 확인이 없으면 시작 알림이 오지 않아 시전을 추적하지 못하고, 마지막 구조물 파괴가
        // 확인되는 순간 회복이 끝나기 전에 그로기가 시작된다
        private bool IsCastTrackerWired()
        {
            return (_factory is IBossChargeCastTrackerSource source) && (ReferenceEquals(source.ChargeCastTracker, this));
        }

        // 1페이즈 카탈로그에 P06 정의가 있고 데이터가 지정됐으며 팩토리가 만들 수 있을 때만 고를 수 있다
        private bool TryFindChargePattern(out PatternDefinition pattern)
        {
            pattern = null;

            _catalog.CollectPatterns(_bossId, FIRST_PHASE, _buffer);

            PatternDefinition found = null;
            for (int i = 0; i < _buffer.Count; ++i)
            {
                if (_buffer[i].DefinitionId == MucheolPatternFactory.P06_ID)
                {
                    found = _buffer[i];
                    break;
                }
            }

            _buffer.Clear();

            if (found == null)
            {
                return false;
            }

            if ((_catalog is IBossPatternReadiness data) && (!data.IsPatternReady(_bossId, found)))
            {
                return false;
            }

            if ((_factory is IBossPatternAvailability availability) && (!availability.CanCreate(found)))
            {
                return false;
            }

            pattern = found;
            return true;
        }
    }
}
