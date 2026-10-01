// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;

using BossDefinition    = Shinshi.Data.BossPatternCatalogSO.BossDefinition;
using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    /// <summary> 전투 중 상태, 페이즈 FSM과 공격 FSM과 선택 BT를 묶고 한 틱의 순서를 지킨다 </summary>
    public sealed class BossCombat
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly BossBrainDependencies  _dependencies;
        private readonly BossDefinition         _boss;
        private readonly BossPhaseFsm           _phase  = new BossPhaseFsm();
        private readonly BossAttackFsm          _attack = new BossAttackFsm();
        private readonly BossSelectionTree      _tree   = new BossSelectionTree();
        private readonly BossBlackboard         _blackboard = new BossBlackboard();

        private double _controlImmuneUntil;

        /****************************************
        *              Properties
        ****************************************/

        public EBossPhaseState  PhaseState  => _phase.State;
        public EBossAttackState AttackState => _attack.State;
        public int              Phase       => _phase.Phase;

        /// <summary> 진행 중인 패턴의 정의 ID, 진행 중이 아니면 빈 문자열이다 </summary>
        public string ActivePatternId => (_attack.Definition == null) ? string.Empty : _attack.Definition.DefinitionId;

        /****************************************
        *                Methods
        ****************************************/

        public BossCombat(BossBrainDependencies dependencies, BossDefinition boss, double startTime)
        {
            _dependencies = dependencies;
            _boss         = boss;

            _blackboard.Self       = dependencies.Self;
            _blackboard.Boss       = boss;
            _blackboard.Phase      = _phase.Phase;
            _blackboard.Targets    = dependencies.Targets;
            _blackboard.Effects    = dependencies.Effects;
            _blackboard.Catalog    = dependencies.Catalog;
            _blackboard.Random     = dependencies.Random;

            // 기존 데이터 필드의 전투 시작 대기만 반영한다. 오프닝 규칙은 아니다
            _blackboard.NextSelectionTime = startTime + boss.OpeningDelaySeconds;
        }

        public void AddSelectionRule(IBossSelectionRule rule)
        {
            _tree.AddRule(rule);
        }

        /// <summary> 상위 FSM이 사망이나 전투 종료로 끊을 때 부른다, 지속물까지 모두 정리한다 </summary>
        public void Interrupt(EBossInterruptReason reason)
        {
            CancelAll(reason);
        }

        // 순서는 전환, 직접 제어, 공격 진행과 새 선택이다. 사망과 전투 종료는 상위 FSM이 이 앞에서 처리한다
        public void Tick(double serverTime)
        {
            _blackboard.ServerTime = serverTime;

            if (_phase.ShouldBeginTransition(_dependencies.Vitals, _boss))
            {
                _phase.BeginTransition();
                CancelAll(EBossInterruptReason.PhaseTransition);
                _phase.CompleteTransition();
                return;
            }

            if (TryApplyDirectControl(serverTime))
            {
                return;
            }

            if (_attack.State != EBossAttackState.Ready)
            {
                _attack.Tick(serverTime);
                return;
            }

            // BT는 새 행동을 고를 수 있을 때만 평가한다
            if (!_phase.AllowsNewSelection)
            {
                return;
            }

            if (!_tree.Evaluate(_blackboard))
            {
                return;
            }

            StartSelectedPattern(serverTime);
        }

        // 받은 제어가 허용 구간이 아니거나 이미 받았거나 면역이면 버린다. 취소형이 아닌 제어의 보스 동작은 미정이라 표시만 한다
        private bool TryApplyDirectControl(double serverTime)
        {
            if (!_dependencies.Control.TryConsumeDirectControl())
            {
                return false;
            }

            if (_attack.State == EBossAttackState.Ready)
            {
                return false;
            }

            if (_attack.ControlUsed)
            {
                return false;
            }

            if (serverTime < _controlImmuneUntil)
            {
                return false;
            }

            if (!_attack.IsControlWindowOpen(serverTime))
            {
                return false;
            }

            _attack.MarkControlUsed();
            _controlImmuneUntil = serverTime + _boss.DirectControlImmuneSeconds;

            if (!_attack.Definition.ControlCancelsPattern)
            {
                return false;
            }

            // 이 패턴의 지속물만 정리한다, 앞선 패턴이 남긴 장판은 수명 관리자가 사유별로 정한다
            int castInstanceId = _attack.Cancel(EBossInterruptReason.DirectControl);
            _dependencies.Effects.Cancel(EBossInterruptReason.DirectControl, castInstanceId);
            return true;
        }

        private void StartSelectedPattern(double serverTime)
        {
            PatternDefinition definition = _blackboard.SelectedPattern;
            _blackboard.SelectedPattern = null;

            IBossPattern pattern = _dependencies.Patterns.Create(definition);
            if (pattern == null)
            {
                Debug.LogError($"[Boss] 패턴 구현이 없다 - {definition.DefinitionId}");
                _blackboard.NextSelectionTime = serverTime + _boss.EmptyWeightRetrySeconds;
                return;
            }

            int castInstanceId = _dependencies.Damage.IssueInstanceId();
            BossPatternContext context = new BossPatternContext(definition, castInstanceId,
                _blackboard.TargetCombatId, _dependencies.Self, _dependencies.Targets, _dependencies.Damage);

            // 예고를 실제로 시작한 순간 기록한다
            _blackboard.RecordUse(definition.DefinitionId);
            _attack.Start(definition, pattern, in context, serverTime);
        }

        private void CancelAll(EBossInterruptReason reason)
        {
            _attack.Cancel(reason);
            _dependencies.Effects.Cancel(reason, CombatIds.NONE);
        }
    }
}
