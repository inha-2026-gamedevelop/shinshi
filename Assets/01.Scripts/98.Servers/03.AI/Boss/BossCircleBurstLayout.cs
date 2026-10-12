// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버가 시전 시작에 고정하는 동시 원형 폭발의 배치다. 원은 모두 13개이고 번호는 0부터 12까지다
    //   0번: 보스 중심 원. 중심은 시전 시작 순간 보스의 서버 위치이고 이후 보스가 움직여도 바뀌지 않는다. 반경은 BossRadius다
    //   1~12번: 사분면 원. 중심은 전장 중심에서 월드 축 방향으로 잰 좌표라 전장 중심과 보스 위치를 혼동하지 않는다. 반경은 모두 SpotRadius다
    //     사분면은 (+x,+z), (+x,-z), (-x,-z), (-x,+z) 순서이고, 사분면 하나는 (안쪽,안쪽), (바깥쪽,안쪽), (안쪽,바깥쪽) 순서로 원 세 개를 가진다
    //     안쪽과 바깥쪽은 부호를 뺀 좌표 크기(x,z 순서)이고 부호는 사분면이 정한다
    // 전장을 돌리지 않으므로 월드 축에 정렬된다. 원끼리 겹칠 수 있고 겹친 곳의 대상은 한 번만 맞는다(패턴이 대상 번호로 거른다)
    /// <summary> 동시 원형 폭발의 시전 시작 고정 배치, 13개 원의 중심과 반경과 포함 판정을 만든다 </summary>
    public readonly struct BossCircleBurstLayout
    {
        /****************************************
        *              Constants
        ****************************************/

        public const int  QUADRANT_COUNT     = 4;
        public const int  SPOTS_PER_QUADRANT = 3;

        /// <summary> 보스 중심 원 하나와 사분면 원 열두 개 </summary>
        public const int  CIRCLE_COUNT       = 1 + (QUADRANT_COUNT * SPOTS_PER_QUADRANT);

        private const int BOSS_CIRCLE_INDEX  = 0;
        private const int SPOT_FIRST_INDEX   = 1;

        // 사분면 번호가 이 값 미만이면 +x쪽이고, 번호 1과 2는 -z쪽이다
        private const int   POSITIVE_X_QUADRANTS = 2;
        private const int   NEGATIVE_Z_FIRST     = 1;
        private const int   NEGATIVE_Z_LAST      = 2;
        private const int   FAR_X_SPOT           = 1;
        private const int   FAR_Z_SPOT           = 2;
        private const float POSITIVE_SIGN        = 1f;
        private const float NEGATIVE_SIGN        = -1f;

        /****************************************
        *                Fields
        ****************************************/

        private readonly Vector3 _bossCenter;
        private readonly Vector3 _arenaCenter;
        private readonly float   _bossRadius;
        private readonly float   _spotRadius;
        private readonly float   _nearOffset;
        private readonly float   _farOffset;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 시전 시작 순간 보스의 서버 위치, 0번 원의 중심이다 </summary>
        public Vector3 BossCenter => _bossCenter;

        /// <summary> 전장 중심, 사분면 원 좌표의 원점이다 </summary>
        public Vector3 ArenaCenter => _arenaCenter;

        public float BossRadius => _bossRadius;

        public float SpotRadius => _spotRadius;

        public float NearOffset => _nearOffset;

        public float FarOffset => _farOffset;

        public int Count => CIRCLE_COUNT;

        /****************************************
        *                Methods
        ****************************************/

        public BossCircleBurstLayout(Vector3 bossCenter, Vector3 arenaCenter, float bossRadius, float spotRadius,
            float nearOffset, float farOffset)
        {
            _bossCenter  = bossCenter;
            _arenaCenter = arenaCenter;
            _bossRadius  = bossRadius;
            _spotRadius  = spotRadius;
            _nearOffset  = nearOffset;
            _farOffset   = farOffset;
        }

        /// <summary> 원 하나의 중심(월드), 사분면 원의 높이는 전장 중심을 따른다 </summary>
        public Vector3 GetCenter(int index)
        {
            if ((index < 0) || (index >= CIRCLE_COUNT))
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            if (index == BOSS_CIRCLE_INDEX)
            {
                return _bossCenter;
            }

            int spotIndex = index - SPOT_FIRST_INDEX;
            int quadrant  = spotIndex / SPOTS_PER_QUADRANT;
            int spot      = spotIndex % SPOTS_PER_QUADRANT;

            float signX = (quadrant < POSITIVE_X_QUADRANTS) ? POSITIVE_SIGN : NEGATIVE_SIGN;
            float signZ = ((quadrant == NEGATIVE_Z_FIRST) || (quadrant == NEGATIVE_Z_LAST)) ? NEGATIVE_SIGN : POSITIVE_SIGN;

            float offsetX = _nearOffset;
            float offsetZ = _nearOffset;
            if (spot == FAR_X_SPOT)
            {
                offsetX = _farOffset;
            }
            else if (spot == FAR_Z_SPOT)
            {
                offsetZ = _farOffset;
            }

            return _arenaCenter + new Vector3(signX * offsetX, 0f, signZ * offsetZ);
        }

        /// <summary> 원 하나의 반경(m), 0번만 보스 중심 원의 반경이다 </summary>
        public float GetRadius(int index)
        {
            if ((index < 0) || (index >= CIRCLE_COUNT))
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return (index == BOSS_CIRCLE_INDEX) ? _bossRadius : _spotRadius;
        }

        /// <summary> 위치가 원 하나 안인가, 수평 거리로 보고 경계 위는 안이다 </summary>
        public bool Contains(int index, Vector3 position)
        {
            Vector3 offset = position - GetCenter(index);
            offset.y = 0f;
            return offset.magnitude <= GetRadius(index);
        }

        /// <summary> 위치가 13개 원 중 하나라도 안인가 </summary>
        public bool ContainsAny(Vector3 position)
        {
            for (int i = 0; i < CIRCLE_COUNT; ++i)
            {
                if (Contains(i, position))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
