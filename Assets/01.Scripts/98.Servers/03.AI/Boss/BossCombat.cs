// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;

using BossDefinition   = Shinshi.Data.BossPatternCatalogSO.BossDefinition;
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
        private readonly BossDamageGate         _damageGate;
        private readonly BossDefinition         _boss;
        private readonly BossPhaseFsm           _phase  = new BossPhaseFsm();
        private readonly BossAttackFsm          _attack;
        private readonly BossSelectionTree      _tree   = new BossSelectionTree();
        private readonly BossBlackboard         _blackboard = new BossBlackboard();

        private readonly IBossTickableEffects _tickableEffects;

        private double _controlImmuneUntil;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 직접 제어가 보스에 적용됐다, 허용 구간 밖이라 버려진 제어는 알리지 않는다 </summary>
        public event Action<BossDirectControlApplied> DirectControlApplied;

        public EBossPhaseState  PhaseState  => _phase.State;
        public EBossAttackState AttackState => _attack.State;
        public int              Phase       => _phase.Phase;

        /// <summary> 진행 중인 패턴의 정의 ID, 진행 중이 아니면 빈 문자열이다 </summary>
        public string ActivePatternId => (_attack.Definition == null) ? string.Empty : _attack.Definition.DefinitionId;

        /****************************************
        *                Methods
        ****************************************/

        // startTime은 전투가 실제로 시작된 서버 시각이다. 등장 대기가 있었다면 그것이 끝난 시각이라
        // 등장 대기와 OpeningDelaySeconds는 이어서 적용되고 겹쳐 세지 않는다
        public BossCombat(BossBrainDependencies dependencies, BossDamageGate damageGate, BossDefinition boss, double startTime)
        {
            _dependencies = dependencies;
            _attack       = new BossAttackFsm(dependencies.Casts);
            _tickableEffects = dependencies.Effects as IBossTickableEffects;
            _damageGate   = damageGate;
            _boss         = boss;

            _blackboard.Self       = dependencies.Self;
            _blackboard.Boss       = boss;
            _blackboard.Phase      = _phase.Phase;
            _blackboard.Targets    = dependencies.Targets;
            _blackboard.Effects    = dependencies.Effects;
            _blackboard.Catalog    = dependencies.Catalog;
            _blackboard.Random     = dependencies.Random;
            _blackboard.Vitals     = dependencies.Vitals;
            _blackboard.Patterns   = dependencies.Patterns;

            // 전투 시작 뒤 첫 선택까지의 기존 데이터 필드만 반영한다. 고정 오프닝 규칙은 아니다
            _blackboard.NextSelectionTime = startTime + boss.OpeningDelaySeconds;
        }

        public void AddSelectionRule(IBossSelectionRule rule)
        {
            _tree.AddRule(rule);
        }

        /// <summary> 페이즈 전환 뒤 인계를 마친다, 2페이즈에 실행 가능한 패턴이 있으면 새 선택을 열고 없으면 준비를 기다린다. 인계 대기가 아니면 거짓이다 </summary>
        public bool CompleteHandoff()
        {
            if (_phase.State != EBossPhaseState.AwaitingHandoff)
            {
                return false;
            }

            // 후보 확인은 2페이즈 번호가 올라간 뒤의 블랙보드 값으로 한다
            return _phase.CompleteHandoff(_blackboard.HasExecutableCandidate());
        }

        /// <summary> 상위 FSM이 사망이나 전투 종료로 끊을 때 부른다, 지속물까지 모두 정리한다 </summary>
        public void Interrupt(EBossInterruptReason reason)
        {
            CancelAll(reason);
        }

        // 공격 진행 뒤에 지속물을 진행한다. 이번 틱에 발사된 투사체도 발사 시각부터 지금까지 이어서 진행하고, 이 틱에 끊겼다면 이미 정리된 뒤다
        public void Tick(double serverTime)
        {
            TickCombat(serverTime);
            _tickableEffects?.Tick(serverTime);
        }

        // 순서는 전환, 직접 제어, 공격 진행과 새 선택이다. 사망과 전투 종료는 상위 FSM이 이 앞에서 처리한다
        private void TickCombat(double serverTime)
        {
            _blackboard.ServerTime = serverTime;

            if (_phase.ShouldBeginTransition(_dependencies.Vitals, _boss))
            {
                _phase.BeginTransition();
                CancelAll(EBossInterruptReason.PhaseTransition);
                _phase.CompleteTransition();

                // 1페이즈의 사용 기록과 직전 그룹은 2페이즈 선택에 넘기지 않는다
                _blackboard.ResetHistory();
                _blackboard.Phase = _phase.Phase;
                return;
            }

            if ((_phase.State == EBossPhaseState.AwaitingReadiness) && (!TryResumeFromReadiness(serverTime)))
            {
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

            StartSelectedPattern(serverTime, false);
        }

        // 실행 가능한 패턴이 생겼는지 EmptyWeightRetrySeconds 주기로 다시 본다. 없는 동안은 선택도 로그도 없다
        private bool TryResumeFromReadiness(double serverTime)
        {
            if (serverTime < _blackboard.NextSelectionTime)
            {
                return false;
            }

            if (!_blackboard.HasExecutableCandidate())
            {
                _blackboard.NextSelectionTime = serverTime + _boss.EmptyWeightRetrySeconds;
                return false;
            }

            return _phase.ResumeFromReadiness();
        }

        // 받은 제어가 허용 구간이 아니거나 이미 받았거나 면역이면 버린다
        // 취소형이 아닌 제어(속박 등)의 보스 동작은 협의 전이라 구현하지 않고, 적용 표시와 면역 시작과 알림만 한다
        // 제어 종류를 일괄적으로 이동, 회전, 공격 정지로 해석하지 않는다
        private bool TryApplyDirectControl(double serverTime)
        {
            if (!_dependencies.Control.TryConsumeDirectControl(out BossDirectControl control))
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

            string patternId = _attack.Definition.DefinitionId;
            bool cancelsPattern = _attack.Definition.ControlCancelsPattern;

            if (cancelsPattern)
            {
                // 이 패턴의 지속물만 정리한다, 앞선 패턴이 남긴 장판은 수명 관리자가 사유별로 정한다
                int castInstanceId = _attack.Cancel(EBossInterruptReason.DirectControl);
                _dependencies.Effects.Cancel(EBossInterruptReason.DirectControl, castInstanceId);
            }

            // 취소 여부와 상관없이 적용된 제어를 알린다. 속박 등의 동작은 이 알림을 받는 쪽에서 나중에 붙인다
            DirectControlApplied?.Invoke(new BossDirectControlApplied(control, ClampControlSeconds(control.RequestedSeconds),
                cancelsPattern, patternId));
            return cancelsPattern;
        }

        // 요청 시간은 보스 정의의 직접 제어 최대 시간을 넘지 못한다, 요청이 없으면 0이다
        private float ClampControlSeconds(float requestedSeconds)
        {
            if (requestedSeconds <= 0f)
            {
                return 0f;
            }

            if (_boss.DirectControlMaxSeconds <= 0f)
            {
                return requestedSeconds;
            }

            return Mathf.Min(requestedSeconds, _boss.DirectControlMaxSeconds);
        }

        // isRetry는 시작이 거절된 패턴을 대신해 같은 선택에서 다시 고른 패턴이다. 다시 고르는 일은 한 번뿐이라 이 패턴이 거절되면 기존 재조회로 간다
        private void StartSelectedPattern(double serverTime, bool isRetry)
        {
            PatternDefinition definition = _blackboard.SelectedPattern;
            int targetCombatId = _blackboard.TargetCombatId;
            _blackboard.SelectedPattern = null;

            IBossPattern pattern = _dependencies.Patterns.Create(definition);
            if (pattern == null)
            {
                Debug.LogError($"[Boss] 패턴 구현이 없다 - {definition.DefinitionId}");
                _blackboard.NextSelectionTime = serverTime + _boss.EmptyWeightRetrySeconds;
                return;
            }

            int castInstanceId = _damageGate.IssueInstanceId();
            BossPatternContext context = new BossPatternContext(definition, castInstanceId,
                _blackboard.TargetCombatId, _dependencies.Self, _dependencies.Targets, _damageGate, _dependencies.Random);

            _attack.Start(definition, pattern, in context, serverTime);

            // 시작할 수 없는 시전(대상이나 빈 슬롯이 없는 여러 장판)은 기록을 남기지 않고 기존 재조회 시간 뒤에 다시 고른다
            if ((pattern is IBossPatternStartResult startResult) && startResult.IsStartRejected)
            {
                _attack.Cancel(EBossInterruptReason.None);

                // 옵트인한 패턴만 같은 선택에서 그 정의를 빼고 한 번 더 고른다. 대안을 시작하지 못하면 기존 재조회 시간 뒤에 다시 고른다
                bool isReplaced = (!isRetry) && (pattern is IBossPatternRetryOnReject)
                    && TryReselectWithout(definition.DefinitionId, targetCombatId, serverTime);
                if (!isReplaced)
                {
                    _blackboard.NextSelectionTime = serverTime + _boss.EmptyWeightRetrySeconds;
                }

                return;
            }

            // 예고를 실제로 시작한 순간 기록한다
            _blackboard.RecordUse(definition.DefinitionId, definition.Group);
        }

        // 거절된 정의를 이번 평가에서만 빼고 처음 대상으로 한 번 더 고른다. 대안이 실제로 시작했을 때만 참이다
        // 처음 대상이 더는 유효하지 않거나 대안이 없으면 새 대상을 뽑지 않고 거짓을 돌려줘 호출한 쪽이 기존 재조회 대기로 돌아가게 한다
        private bool TryReselectWithout(string rejectedPatternId, int targetCombatId, double serverTime)
        {
            _blackboard.ExcludedPatternId   = rejectedPatternId;
            _blackboard.RetryTargetCombatId = targetCombatId;
            bool isPicked = _tree.Evaluate(_blackboard);
            _blackboard.ExcludedPatternId   = null;
            _blackboard.RetryTargetCombatId = CombatIds.NONE;

            if (!isPicked)
            {
                return false;
            }

            StartSelectedPattern(serverTime, true);
            return _attack.State != EBossAttackState.Ready;
        }

        private void CancelAll(EBossInterruptReason reason)
        {
            _attack.Cancel(reason);
            _dependencies.Effects.Cancel(reason, CombatIds.NONE);
        }
    }
}
