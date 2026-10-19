// System
using System;
using System.Collections.Generic;

namespace Shinshi.AI
{
    // 한 세션의 적이 함께 쓴다. 근접, 돌진, 정예는 근접 상한을 합산하고 원거리는 따로 센다.
    // 예고가 끝나는 순간(타격, 발사, 돌진 시작)이나 취소, 사망에 풀어야 하며 같은 적이 다시 얻으면 앞의 점유를 먼저 푼다
    /// <summary> 한 대상에게 동시에 걸리는 적 공격 예고 수의 상한 </summary>
    public sealed class EnemyTelegraphLimiter
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int INITIAL_CAPACITY = 32;

        /****************************************
        *                Types
        ****************************************/

        private struct Hold
        {
            public int  TargetId;
            public bool IsProjectile;
        }

        /****************************************
        *                Fields
        ****************************************/

        private readonly int                   _maxClose;
        private readonly int                   _maxProjectile;
        private readonly Dictionary<int, Hold> _holds = new Dictionary<int, Hold>(INITIAL_CAPACITY);

        /****************************************
        *                Methods
        ****************************************/

        public EnemyTelegraphLimiter(int maxClosePerTarget, int maxProjectilePerTarget)
        {
            if (maxClosePerTarget < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxClosePerTarget), "근접 예고 상한은 1 이상이다");
            }

            if (maxProjectilePerTarget < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxProjectilePerTarget), "투사체 예고 상한은 1 이상이다");
            }

            _maxClose      = maxClosePerTarget;
            _maxProjectile = maxProjectilePerTarget;
        }

        /// <summary> 대상에게 걸린 해당 종류의 예고 수 </summary>
        public int Count(int targetId, bool projectile)
        {
            int count = 0;
            foreach (KeyValuePair<int, Hold> pair in _holds)
            {
                if ((pair.Value.TargetId == targetId) && (pair.Value.IsProjectile == projectile))
                {
                    ++count;
                }
            }

            return count;
        }

        /// <summary> 상한 안이면 점유하고 참, 가득 찼으면 거짓이며 점유하지 않는다 </summary>
        public bool TryAcquire(int attackerId, int targetId, bool projectile)
        {
            _holds.Remove(attackerId);

            if (Count(targetId, projectile) >= (projectile ? _maxProjectile : _maxClose))
            {
                return false;
            }

            _holds[attackerId] = new Hold { TargetId = targetId, IsProjectile = projectile };
            return true;
        }

        /// <summary> 점유를 푼다, 없어도 안전하다 </summary>
        public void Release(int attackerId)
        {
            _holds.Remove(attackerId);
        }
    }
}
