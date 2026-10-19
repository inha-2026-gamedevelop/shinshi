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

        public const float REEVALUATE_SECONDS    = 0.5f; // 재평가 주기
        public const float HOLD_SECONDS          = 2f;   // 새 타깃을 고른 뒤 바꾸지 않는 시간
        public const float SWITCH_MARGIN_METERS  = 3f;   // 바꾸려면 새 경로가 이만큼 더 짧아야 한다

        private const int INITIAL_BUFFER_CAPACITY = 16;

        /****************************************
        *                Fields
        ****************************************/

        private readonly IEnemyNavigation    _navigation;
        private double                       _nextEvaluate = double.NegativeInfinity;
        private double                       _holdUntil    = double.NegativeInfinity;
        private bool                         _forceReevaluate;
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

        // navigation이 null이면 기존 규칙(직선 거리, 유효한 동안 유지)을 그대로 쓴다
        public EnemyTargetSelector(ICombatTargetQuery targets, IEnemyNavigation navigation = null)
        {
            _targets    = targets ?? throw new System.ArgumentNullException(nameof(targets));
            _navigation = navigation;
        }

        public void Clear()
        {
            TargetId         = CombatIds.NONE;
            _nextEvaluate    = double.NegativeInfinity;
            _holdUntil       = double.NegativeInfinity;
            _forceReevaluate = false;
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

        /// <summary> 길찾기 정책 선택, 도달 가능한 가장 가까운(경로 길이) 생존 원 참가자를 고르고 0.5초마다 다시 본다. 없으면 null </summary>
        public ICombatTarget Resolve(Vector3 from, double serverTime)
        {
            if (_navigation == null)
            {
                return Resolve(from);
            }

            ICombatTarget current   = null;
            bool          hasTarget = (TargetId != CombatIds.NONE) && _targets.TryGet(TargetId, out current) && IsValidTarget(current);

            if (hasTarget && (!_forceReevaluate) && (serverTime < _nextEvaluate))
            {
                return current;
            }

            // 도달 가능한 타깃이 없던 상태에서는 주기마다만 다시 찾아 경로 계산을 아낀다
            if ((TargetId == CombatIds.NONE) && (!_forceReevaluate) && (serverTime < _nextEvaluate))
            {
                return null;
            }

            bool forced = _forceReevaluate;
            _forceReevaluate = false;
            _nextEvaluate    = serverTime + REEVALUATE_SECONDS;

            _targets.GetLivingPlayers(_buffer);

            // 현재 타깃이 생존 목록에서 빠졌거나(정산 이탈) 도달 불가면 유지 시간과 무관하게 버린다.
            // 경로 질의는 후보마다 한 번이고 현재 타깃의 길이도 같은 질의 결과를 쓴다
            bool          currentListed = false;
            float         currentLength = float.MaxValue;
            ICombatTarget best          = null;
            float         bestLength    = float.MaxValue;

            for (int i = 0; i < _buffer.Count; ++i)
            {
                ICombatTarget candidate = _buffer[i];
                if (!IsValidTarget(candidate))
                {
                    continue;
                }

                bool isCurrent = hasTarget && (candidate.CombatId == TargetId);

                if (!_navigation.TryMeasurePath(from, candidate.Position, out float length))
                {
                    continue;
                }

                if (isCurrent)
                {
                    currentListed = true;
                    currentLength = length;
                }

                if ((length < bestLength) || ((length == bestLength) && (candidate.CombatId < best.CombatId)))
                {
                    best       = candidate;
                    bestLength = length;
                }
            }

            _buffer.Clear();

            if (hasTarget && (!currentListed))
            {
                hasTarget = false;
            }

            if (best == null)
            {
                TargetId = CombatIds.NONE;
                return null;
            }

            if (hasTarget)
            {
                bool holdExpired  = serverTime >= _holdUntil;
                bool betterEnough = bestLength <= (currentLength - SWITCH_MARGIN_METERS);
                if ((best.CombatId == TargetId) || (!betterEnough) || ((!holdExpired) && (!forced)))
                {
                    return current;
                }
            }

            TargetId   = best.CombatId;
            _holdUntil = serverTime + HOLD_SECONDS;
            return best;
        }

        /// <summary> 유효한 참가자에게 맞았을 때 다음 호출에서 바로 다시 평가한다, 유지 시간은 무시하고 전환 거리 조건은 지킨다 </summary>
        public void RequestReevaluate()
        {
            _forceReevaluate = true;
        }

        // 소환 동료는 타깃에서 뺀다
        private static bool IsValidTarget(ICombatTarget target)
        {
            return (target.Faction == ECombatFaction.Player) && (!target.IsSummon)
                && (target.LifeState == EParticipantLifeState.Alive);
        }
    }
}
