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
    /// <summary> 새 패턴을 고르는 BT, 대기 게이트 후 타깃 선택, 문맥 읽기, 선택 규칙과 가중 선택 순으로 평가한다 </summary>
    public sealed class BossSelectionTree
    {
        /****************************************
        *                Fields
        ****************************************/

        // 수평 거리나 방향 벡터가 이 값 이하이면 각도를 정의할 수 없어 정면으로 본다
        private const float DIRECTION_EPSILON = 0.0001f;

        private const int FIRST_PHASE = 1;

        private readonly BtNode                     _root;
        private readonly List<IBossSelectionRule>   _rules = new List<IBossSelectionRule>();

        /****************************************
        *                Methods
        ****************************************/

        public BossSelectionTree()
        {
            _root = new BtSequence(
                new BtLeaf(PassWaitGate),
                new BtLeaf(SelectTarget),
                new BtLeaf(ReadContext),
                new BtSelector(
                    new BtLeaf(TryRules),
                    new BtLeaf(SelectWeighted)));
        }

        /// <summary> 일반 가중 선택보다 먼저 평가되는 규칙을 더한다 </summary>
        public void AddRule(IBossSelectionRule rule)
        {
            if (rule == null)
            {
                throw new ArgumentNullException(nameof(rule));
            }

            _rules.Add(rule);
        }

        /// <summary> 한 번 평가한다, 패턴을 골랐으면 true이고 blackboard.SelectedPattern에 들어 있다 </summary>
        public bool Evaluate(BossBlackboard blackboard)
        {
            blackboard.SelectedPattern = null;
            blackboard.Target = null;

            EBtStatus status = _root.Tick(blackboard);

            // 타깃 참조는 이번 평가 안에서만 쓴다
            blackboard.Target = null;
            return (status == EBtStatus.Success) && (blackboard.SelectedPattern != null);
        }

        private bool PassWaitGate(BossBlackboard blackboard)
        {
            return blackboard.ServerTime >= blackboard.NextSelectionTime;
        }

        // 살아 있는 참가 플레이어 중 균등 무작위 한 명, 아무도 없으면 실패한다
        // 같은 선택에서 다시 고르는 평가(RetryTargetCombatId)는 새로 뽑지 않고 처음 대상을 쓰며, 그 대상이 더는 유효하지 않으면 실패한다
        private bool SelectTarget(BossBlackboard blackboard)
        {
            int count = blackboard.Targets.GetLivingPlayers(blackboard.TargetBuffer);
            if (count <= 0)
            {
                return false;
            }

            if (blackboard.RetryTargetCombatId != CombatIds.NONE)
            {
                for (int i = 0; i < count; ++i)
                {
                    if (blackboard.TargetBuffer[i].CombatId == blackboard.RetryTargetCombatId)
                    {
                        blackboard.Target = blackboard.TargetBuffer[i];
                        blackboard.TargetCombatId = blackboard.Target.CombatId;
                        return true;
                    }
                }

                return false;
            }

            blackboard.Target = blackboard.TargetBuffer[blackboard.Random.NextInt(count)];
            blackboard.TargetCombatId = blackboard.Target.CombatId;
            return true;
        }

        private bool ReadContext(BossBlackboard blackboard)
        {
            Vector3 toTarget = blackboard.Target.Position - blackboard.Self.Position;
            toTarget.y = 0f;
            Vector3 forward = blackboard.Self.Forward;
            forward.y = 0f;

            float distance = toTarget.magnitude;
            float angle = 0f;
            if ((distance > DIRECTION_EPSILON) && (forward.sqrMagnitude > DIRECTION_EPSILON))
            {
                angle = Vector3.Angle(forward, toTarget);
            }

            int nearbyCount = blackboard.Targets.CountLivingInRadius(blackboard.Self.Position,
                blackboard.Boss.NearbyRadius, ECombatFaction.Player, false);

            blackboard.DistanceBand = BossPatternSelector.GetDistanceBand(distance, blackboard.Boss);
            blackboard.DirectionBand = BossPatternSelector.GetDirectionBand(angle, blackboard.Boss);
            blackboard.NearbyBand = BossPatternSelector.GetNearbyBand(nearbyCount);
            return true;
        }

        private bool TryRules(BossBlackboard blackboard)
        {
            for (int i = 0; i < _rules.Count; ++i)
            {
                if (_rules[i].TrySelect(blackboard, out PatternDefinition pattern) && (!IsExcluded(blackboard, pattern)))
                {
                    blackboard.SelectedPattern = pattern;
                    return true;
                }
            }

            return false;
        }

        private static bool IsExcluded(BossBlackboard blackboard, PatternDefinition pattern)
        {
            return (!string.IsNullOrEmpty(blackboard.ExcludedPatternId)) && (pattern.DefinitionId == blackboard.ExcludedPatternId);
        }

        private bool SelectWeighted(BossBlackboard blackboard)
        {
            blackboard.Catalog.CollectPatterns(blackboard.Boss.DefinitionId, blackboard.Phase, blackboard.Candidates);

            // 같은 선택에서 다시 고르는 평가가 시작이 거절된 정의를 뺀다
            for (int i = blackboard.Candidates.Count - 1; i >= 0; --i)
            {
                if (IsExcluded(blackboard, blackboard.Candidates[i]))
                {
                    blackboard.Candidates.RemoveAt(i);
                }
            }

            // 동시 장판이 상한만큼 남아 있는 패턴은 후보에서 뺀다, 빈 슬롯이 하나도 없으면 여러 장판 패턴도 뺀다
            for (int i = blackboard.Candidates.Count - 1; i >= 0; --i)
            {
                PatternDefinition candidate = blackboard.Candidates[i];
                // 여러 장판을 설치하는 패턴은 패턴 ID와 상관없는 보스 전체 장판 수로 센다, 알려 주지 않는 관리자면 패턴별 수를 쓴다
                int used = ((candidate.RawAreaTargetCount > 1) && (blackboard.Effects is IBossAreaCapacity capacity))
                    ? capacity.TotalAreaCount
                    : blackboard.Effects.ActiveAreaCount(candidate.DefinitionId);
                if ((candidate.MaxConcurrentAreas > 0) && (used >= candidate.MaxConcurrentAreas))
                {
                    blackboard.Candidates.RemoveAt(i);
                }
            }

            // 2페이즈 이상은 데이터가 비었거나 공격이 구현되지 않은 정의를 미리 뺀다. 1페이즈는 기존 동작을 그대로 둔다
            bool isLaterPhase = blackboard.Phase > FIRST_PHASE;
            if (isLaterPhase)
            {
                for (int i = blackboard.Candidates.Count - 1; i >= 0; --i)
                {
                    if (!blackboard.IsExecutable(blackboard.Candidates[i]))
                    {
                        blackboard.Candidates.RemoveAt(i);
                    }
                }
            }

            // 체력 구간은 선택 시점의 값이다. 진행 중인 공격은 이 계산과 무관하다
            float healthRate = isLaterPhase ? blackboard.Vitals.HealthRate : 0f;

            blackboard.Weights.Clear();
            for (int i = 0; i < blackboard.Candidates.Count; ++i)
            {
                PatternDefinition candidate = blackboard.Candidates[i];
                int recentSlot = blackboard.GetRecentSlot(candidate.DefinitionId);
                float weight = isLaterPhase
                    ? BossPatternSelector.ComputeSecondPhaseWeight(candidate, blackboard.Boss, healthRate, blackboard.LastGroup,
                        blackboard.DistanceBand, blackboard.DirectionBand, blackboard.NearbyBand, recentSlot)
                    : BossPatternSelector.ComputeWeight(candidate, blackboard.DistanceBand, blackboard.DirectionBand,
                        blackboard.NearbyBand, recentSlot);
                blackboard.Weights.Add(weight);
            }

            int index = BossPatternSelector.Pick(blackboard.Weights, blackboard.Random.NextFloat01());
            if (index < 0)
            {
                // 가중치 합이 0이면 새 공격을 만들지 않고 정해진 시간 뒤에 다시 조회한다
                blackboard.NextSelectionTime = blackboard.ServerTime + blackboard.Boss.EmptyWeightRetrySeconds;
                return false;
            }

            blackboard.SelectedPattern = blackboard.Candidates[index];
            return true;
        }
    }
}
