// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.AI
{
    // 근접형과 돌진형이 같은 타깃 규칙을 쓰도록 분리했다. 최초 타깃 기준과 재선택 조건은 기획 미정이라
    // 이번 프로토타입은 가장 가까운 생존 플레이어를 고르고 유효한 동안 유지한다
    /// <summary> 일반 적의 타깃 선택, 소환 동료를 빼고 가장 가까운 생존 플레이어를 유효한 동안 유지한다 </summary>
    public sealed class EnemyTargetSelector
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int INITIAL_BUFFER_CAPACITY = 16;

        /****************************************
        *                Fields
        ****************************************/

        private readonly ICombatTargetQuery  _targets;
        private readonly List<ICombatTarget> _buffer = new List<ICombatTarget>(INITIAL_BUFFER_CAPACITY);

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 현재 추적 중인 타깃 번호, 없으면 CombatIds.NONE </summary>
        public int TargetId { get; private set; } = CombatIds.NONE;

        /****************************************
        *                Methods
        ****************************************/

        public EnemyTargetSelector(ICombatTargetQuery targets)
        {
            _targets = targets ?? throw new System.ArgumentNullException(nameof(targets));
        }

        public void Clear()
        {
            TargetId = CombatIds.NONE;
        }

        /// <summary> 지금 타깃이 유효하면 유지하고, 아니면 from에서 가장 가까운 생존 플레이어로 다시 고른다. 없으면 null </summary>
        public ICombatTarget Resolve(Vector3 from)
        {
            if ((TargetId != CombatIds.NONE) && (_targets.TryGet(TargetId, out ICombatTarget current)) && (IsValidTarget(current)))
            {
                return current;
            }

            TargetId = CombatIds.NONE;

            _targets.GetLivingPlayers(_buffer);

            ICombatTarget best        = null;
            float         bestSqrDist = float.MaxValue;

            for (int i = 0; i < _buffer.Count; ++i)
            {
                ICombatTarget candidate = _buffer[i];
                if (!IsValidTarget(candidate))
                {
                    continue;
                }

                Vector3 offset = candidate.Position - from;
                offset.y = 0f;
                float sqrDist = offset.sqrMagnitude;

                // 같은 거리면 번호가 작은 쪽, 순서가 바뀌어도 같은 타깃을 고르게 한다
                if ((sqrDist < bestSqrDist) || ((sqrDist == bestSqrDist) && (candidate.CombatId < best.CombatId)))
                {
                    best        = candidate;
                    bestSqrDist = sqrDist;
                }
            }

            _buffer.Clear();

            if (best != null)
            {
                TargetId = best.CombatId;
            }

            return best;
        }

        // 소환 동료는 타깃에서 뺀다
        private static bool IsValidTarget(ICombatTarget target)
        {
            return (target.Faction == ECombatFaction.Player) && (!target.IsSummon)
                && (target.LifeState == EParticipantLifeState.Alive);
        }
    }
}
