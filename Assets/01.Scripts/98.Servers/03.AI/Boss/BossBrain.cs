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
    /// <summary> 보스 상위 FSM, 사망과 전투 종료를 판단해 Combat에 취소를 지시한다 </summary>
    public sealed class BossBrain
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string                     _bossId;
        private readonly BossBrainDependencies      _dependencies;
        private readonly List<IBossSelectionRule>   _pendingRules = new List<IBossSelectionRule>();

        private BossCombat _combat;
        private bool       _combatEndRequested;

        /****************************************
        *              Properties
        ****************************************/

        public EBossFlowState FlowState { get; private set; } = EBossFlowState.Inactive;

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
        }

        /// <summary> 일반 가중 선택 앞에 평가할 규칙을 더한다, Begin 전에만 받는다 </summary>
        public bool AddSelectionRule(IBossSelectionRule rule)
        {
            if ((rule == null) || (FlowState != EBossFlowState.Inactive))
            {
                return false;
            }

            _pendingRules.Add(rule);
            return true;
        }

        /// <summary> 전투를 연다, 보스 정의가 없거나 수치가 지정되지 않았으면 열지 않고 false를 돌려준다 </summary>
        public bool Begin(double serverTime)
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

            _combat = new BossCombat(_dependencies, boss, serverTime);
            for (int i = 0; i < _pendingRules.Count; ++i)
            {
                _combat.AddSelectionRule(_pendingRules[i]);
            }

            FlowState = EBossFlowState.Combat;
            return true;
        }

        /// <summary> 세션이 전투 종료를 알린다, 다음 Tick에서 가장 먼저 처리한다 </summary>
        public void RequestCombatEnd()
        {
            if (FlowState != EBossFlowState.Combat)
            {
                return;
            }

            _combatEndRequested = true;
        }

        /// <summary> 서버 시각으로 한 틱 진행한다 </summary>
        public void Tick(double serverTime)
        {
            if (FlowState != EBossFlowState.Combat)
            {
                return;
            }

            EBossInterruptReason stop = DetectStop(serverTime);
            if (stop != EBossInterruptReason.None)
            {
                _combat.Interrupt(stop);
                FlowState = (stop == EBossInterruptReason.Death) ? EBossFlowState.Defeated : EBossFlowState.Ended;
                return;
            }

            _combat.Tick(serverTime);
        }

        // 사망과 전투 종료가 같은 우선순위이며 둘 다 모든 것에 앞선다
        // TODO(기웅): 기획 미정 - 같은 틱에 둘이 겹치면 어느 사유를 기록할지, 승패는 세션이 판정한다
        private EBossInterruptReason DetectStop(double serverTime)
        {
            if (_dependencies.DeathPolicy.IsDeathConfirmed(_dependencies.Vitals, serverTime))
            {
                return EBossInterruptReason.Death;
            }

            if (_combatEndRequested)
            {
                return EBossInterruptReason.CombatEnd;
            }

            return EBossInterruptReason.None;
        }
    }
}
