// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 협동 보스 전용. 미리 지정한 복귀 지점 중 안전한 곳을 고른다. 살아 있는 적에서 최소 거리 이상, 위험 영역 밖, 사망 위치에서 가장 가까운 곳이다 </summary>
    public sealed class RevivePointSelector
    {
        // 지점이 모두 위험하면 실패로 돌려주고 부활 단계가 횟수를 쓰지 않은 채 재평가 간격 뒤 다시 묻는다.
        // 거리는 다른 전투 판정과 같이 수평(XZ)으로 잰다. 적 몸체 반경은 더하지 않는다, 기획 수치가 적 위치 기준이다.
        // 정확히 최소 거리에 있는 적은 안전으로 본다("6m 이상"). 같은 거리의 지점이 여럿이면 목록 앞쪽을 고른다.
        // 후보와 조회 버퍼는 만들 때 한 번 잡고 틱에서는 할당하지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private const int INITIAL_CANDIDATES = 4;
        private const int INITIAL_ENEMIES    = 16;

        private readonly ICombatTargetQuery  _targets;
        private readonly IReviveHazardQuery  _hazards;
        private readonly float               _enemyClearance;
        private readonly List<Vector3>       _candidates = new List<Vector3>(INITIAL_CANDIDATES);
        private readonly List<ICombatTarget> _enemies    = new List<ICombatTarget>(INITIAL_ENEMIES);

        /****************************************
        *              Properties
        ****************************************/

        public int   CandidateCount => _candidates.Count;
        public float EnemyClearance => _enemyClearance;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> hazards가 null이면 위험 영역을 보지 않는다. enemyClearance는 m, 0이면 적 거리를 보지 않는다 </summary>
        public RevivePointSelector(ICombatTargetQuery targets, IReviveHazardQuery hazards, float enemyClearance)
        {
            if (targets == null)
            {
                throw new ArgumentNullException(nameof(targets));
            }

            if ((!(enemyClearance >= 0f)) || float.IsInfinity(enemyClearance))
            {
                throw new ArgumentOutOfRangeException(nameof(enemyClearance), "적과의 최소 거리는 0 이상의 유한값이다");
            }

            _targets        = targets;
            _hazards        = hazards;
            _enemyClearance = enemyClearance;
        }

        public void ClearCandidates()             { _candidates.Clear(); }
        public void AddCandidate(Vector3 point)   { _candidates.Add(point); }

        /// <summary> 안전한 지점 중 사망 위치에서 가장 가까운 곳, 후보가 없거나 모두 위험하면 false </summary>
        public bool TrySelect(Vector3 deathPosition, double serverTime, out Vector3 point)
        {
            point = deathPosition;
            bool  found       = false;
            float bestSqrDist = float.MaxValue;
            for (int i = 0; i < _candidates.Count; ++i)
            {
                Vector3 candidate = _candidates[i];
                float   sqrDist   = HorizontalSqrDistance(candidate, deathPosition);
                if ((found) && (sqrDist >= bestSqrDist))
                {
                    continue;
                }

                if (!IsSafe(candidate, serverTime))
                {
                    continue;
                }

                point       = candidate;
                bestSqrDist = sqrDist;
                found       = true;
            }

            return found;
        }

        /// <summary> 살아 있는 적이 최소 거리 안에 없고 위험 영역 밖이면 true </summary>
        public bool IsSafe(Vector3 candidate, double serverTime)
        {
            if (_enemyClearance > 0f)
            {
                float clearanceSqr = _enemyClearance * _enemyClearance;
                int   count        = _targets.GetLivingInRadius(candidate, _enemyClearance, ECombatFaction.Enemy, true, _enemies);
                for (int i = 0; i < count; ++i)
                {
                    // 조회는 경계를 포함하므로 정확히 최소 거리인 적은 여기서 다시 뺀다
                    if (HorizontalSqrDistance(_enemies[i].Position, candidate) < clearanceSqr)
                    {
                        _enemies.Clear();
                        return false;
                    }
                }

                _enemies.Clear();
            }

            return (_hazards == null) || (!_hazards.IsHazardous(candidate, serverTime));
        }

        private static float HorizontalSqrDistance(Vector3 a, Vector3 b)
        {
            float deltaX = a.x - b.x;
            float deltaZ = a.z - b.z;
            return (deltaX * deltaX) + (deltaZ * deltaZ);
        }
    }
}
