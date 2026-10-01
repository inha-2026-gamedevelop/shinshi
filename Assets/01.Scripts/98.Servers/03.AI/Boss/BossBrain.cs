// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

using BossDefinition = Shinshi.Data.BossPatternCatalogSO.BossDefinition;

namespace Shinshi.AI
{
    // 서버 시각은 바깥이 Tick에 넘긴다. Mirror와 Time.time에 묶이지 않아 EditMode에서 그대로 돌릴 수 있다
    // 누가 Tick을 부르고 서버 객체로 만들어 ServerLinks에 등록하는지는 서버 부트스트랩의 몫이며 아직 연결하지 않았다
    // 이 클래스는 플레이어의 생사 상태를 바꾸지 않는다. 보스 사망과 전멸이 겹쳐도 플레이어 사망 상태는 그대로다
    /// <summary> 보스 상위 FSM, 등장 대기와 사망과 전투 종료를 판단해 Combat에 취소를 지시한다 </summary>
    public sealed class BossBrain
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string                     _bossId;
        private readonly BossBrainDependencies      _dependencies;
        private readonly BossDamageGate             _damageGate;
        private readonly List<IBossSelectionRule>   _pendingRules = new List<IBossSelectionRule>();

        private BossCombat      _combat;
        private BossDefinition  _boss;
        private double          _combatStartTime;
        private bool            _combatEndRequested;

        /****************************************
        *              Properties
        ****************************************/

        public EBossFlowState FlowState { get; private set; } = EBossFlowState.Inactive;

        /// <summary> 보스 사망이 확정됐다. 전투 종료와 같은 틱이어도 기록된다 </summary>
        public bool IsBossDeathRecorded { get; private set; }

        /// <summary> 전투 종료 요청(전멸 등)이 확인됐다. 보스 사망과 같은 틱이어도 기록된다 </summary>
        public bool IsCombatEndRecorded { get; private set; }

        /// <summary> 직접 제어가 보스에 적용됐다, 속박 등 취소형이 아닌 제어의 동작은 이 알림을 받는 쪽에서 붙인다 </summary>
        public event Action<BossDirectControlApplied> DirectControlApplied;

        public EBossPhaseState PhaseState => (_combat == null) ? EBossPhaseState.Active : _combat.PhaseState;

        public EBossAttackState AttackState => (_combat == null) ? EBossAttackState.Ready : _combat.AttackState;

        public int Phase => (_combat == null) ? 0 : _combat.Phase;

        public string ActivePatternId => (_combat == null) ? string.Empty : _combat.ActivePatternId;

        /****************************************
        *                Methods
        ****************************************/

        public BossBrain(string bossId, BossBrainDependencies dependencies)
        {
            if (string.IsNullOrEmpty(bossId))
            {
                throw new ArgumentException("보스 ID가 비어 있다", nameof(bossId));
            }

            if (dependencies == null)
            {
                throw new ArgumentNullException(nameof(dependencies));
            }

            dependencies.Validate();
            _bossId       = bossId;
            _dependencies = dependencies;
            _damageGate   = new BossDamageGate(dependencies.Damage);
        }

        /// <summary> 일반 가중 선택 앞에 평가할 규칙을 더한다, 시작 전에만 받는다 </summary>
        public bool AddSelectionRule(IBossSelectionRule rule)
        {
            if ((rule == null) || (FlowState != EBossFlowState.Inactive))
            {
                return false;
            }

            _pendingRules.Add(rule);
            return true;
        }

        /// <summary> 등장 대기 없이 바로 전투를 연다 </summary>
        public bool Begin(double serverTime)
        {
            return BeginAppearance(serverTime, 0f);
        }

        // 보스는 컷씬 전에 미리 스폰되어 있어야 한다. 전투 시작 시점은 이 서버 시각으로만 정하고
        // 클라이언트의 컷씬 종료 신호는 받지 않는다. 컷씬 길이를 어디서 가져올지는 미정이라 호출자가 넘긴다
        // TODO(기웅): 컷씬 길이를 담을 데이터 필드가 없다. 위치가 정해지면 호출자가 그 값을 넘기도록 연결한다
        /// <summary> 전투를 연다, 등장 대기 시간 동안은 타깃 선택과 공격과 피해 처리를 하지 않는다 </summary>
        public bool BeginAppearance(double serverTime, float appearanceSeconds)
        {
            if (FlowState != EBossFlowState.Inactive)
            {
                return false;
            }

            if (!_dependencies.Catalog.TryGetBoss(_bossId, out BossDefinition boss))
            {
                Debug.LogError($"[Boss] 보스 정의가 없다 - {_bossId}");
                return false;
            }

            if (!_dependencies.Catalog.IsTuned(_bossId))
            {
                Debug.LogWarning($"[Boss] 수치가 지정되지 않아 전투를 열지 않는다 - {_bossId}");
                return false;
            }

            _boss            = boss;
            _combatStartTime = serverTime + Math.Max(0f, appearanceSeconds);

            if (appearanceSeconds <= 0f)
            {
                StartCombat(serverTime);
                return true;
            }

            FlowState = EBossFlowState.Appearing;
            return true;
        }

        /// <summary> 세션이 전투 종료를 알린다, 등장 대기 중에도 받으며 다음 Tick에서 가장 먼저 처리한다 </summary>
        public void RequestCombatEnd()
        {
            if ((FlowState != EBossFlowState.Combat) && (FlowState != EBossFlowState.Appearing))
            {
                return;
            }

            _combatEndRequested = true;
        }

        /// <summary> 서버 시각으로 한 틱 진행한다 </summary>
        public void Tick(double serverTime)
        {
            if ((FlowState != EBossFlowState.Combat) && (FlowState != EBossFlowState.Appearing))
            {
                return;
            }

            EBossInterruptReason stop = DetectStop(serverTime);
            if (stop != EBossInterruptReason.None)
            {
                Stop(stop);
                return;
            }

            if (FlowState == EBossFlowState.Appearing)
            {
                if (serverTime < _combatStartTime)
                {
                    return;
                }

                // 전투 시작 시각은 틱 시각이 아니라 등장 대기가 끝나는 경계로 잡는다
                StartCombat(_combatStartTime);
            }

            _combat.Tick(serverTime);
        }

        private void StartCombat(double combatStartTime)
        {
            _combat = new BossCombat(_dependencies, _damageGate, _boss, combatStartTime);
            _combat.DirectControlApplied += OnCombatDirectControlApplied;
            for (int i = 0; i < _pendingRules.Count; ++i)
            {
                _combat.AddSelectionRule(_pendingRules[i]);
            }

            _damageGate.Open();
            FlowState = EBossFlowState.Combat;
        }

        private void OnCombatDirectControlApplied(BossDirectControlApplied applied)
        {
            DirectControlApplied?.Invoke(applied);
        }

        // 사망과 전투 종료는 같은 우선순위이며 둘 다 모든 것에 앞선다. 같은 틱에 겹치면 둘 다 기록한다
        // 정리 사유는 보스 사망을 우선하지만 지속물 정리는 두 경우 모두 같다. 승패 판정은 세션이 한다
        private EBossInterruptReason DetectStop(double serverTime)
        {
            bool isDeathConfirmed = _dependencies.DeathPolicy.IsDeathConfirmed(_dependencies.Vitals, serverTime);

            if (isDeathConfirmed)
            {
                IsBossDeathRecorded = true;
            }

            if (_combatEndRequested)
            {
                IsCombatEndRecorded = true;
            }

            if (isDeathConfirmed)
            {
                return EBossInterruptReason.Death;
            }

            return _combatEndRequested ? EBossInterruptReason.CombatEnd : EBossInterruptReason.None;
        }

        // 게이트를 먼저 닫아 정리 도중에도 예약 타격과 지속물의 추가 피해가 나가지 않게 한다. 이미 적용된 피해는 되돌리지 않는다
        private void Stop(EBossInterruptReason reason)
        {
            _damageGate.CloseForGood();

            if (_combat != null)
            {
                _combat.Interrupt(reason);
            }

            // 기획의 초기 기준대로 보스 사망과 전원 사망이 같은 시각이면 전멸(전투 종료)을 우선한다. 실제 전투 피드백을 본 뒤 조정될 수 있어서
            // 두 사실은 모두 기록해 두고 상태만 정한다. 난이도별 결과 규칙은 이 클래스가 정하지 않는다
            FlowState = IsCombatEndRecorded ? EBossFlowState.Ended : EBossFlowState.Defeated;
        }
    }
}
