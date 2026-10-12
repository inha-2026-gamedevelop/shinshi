// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 전방위를 같은 각도의 구역으로 나누고 구역마다 정확히 한 발을 배정한다. 각도는 월드 정면(+Z)에서 시계 방향(위에서 볼 때 오른쪽)이고
    // 구역 s는 s x 구역각 이상 (s + 1) x 구역각 미만이다. 발 순서는 구역을 무작위로 섞은 순열이라 발 k는 구역 순열[k]에서 나간다
    //
    // 난수 사용 순서는 고정이다. 같은 난수열은 같은 배치를 낸다
    //   1) 구역 순열: 마지막 칸부터 내려오며 NextInt(i + 1)로 교환한다(Fisher-Yates), 구역이 하나면 호출이 없다
    //   2) 구역 안 각도: 발 0부터 순서대로 NextFloat01() 한 번씩
    //
    // 원점은 시전 시작 때 한 번 고정한 값이다. 보스가 시전 중에 움직여도 배치는 바뀌지 않는다
    /// <summary> 전방위 투사체 난사의 배치, 구역 순서와 구역 안 각도와 원점을 시전 시작 순간에 고정한 값 </summary>
    public sealed class BossBarrageLayout
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float FULL_TURN        = 360f;
        private const float MAX_FRACTION     = 0.9999f; // 구역 끝 경계는 다음 구역이라 난수가 1에 가까워도 구역 안에 머물게 한다

        /****************************************
        *                Fields
        ****************************************/

        private readonly Vector3   _origin;
        private readonly float     _sectorArc;
        private readonly int[]     _sectors;
        private readonly float[]   _angles;
        private readonly Vector3[] _directions;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 발사 원점, 시전 시작 순간의 보스 위치다 </summary>
        public Vector3 Origin => _origin;

        /// <summary> 구역 수이자 발 수 </summary>
        public int ShotCount => _sectors.Length;

        /// <summary> 구역 하나의 각도(도) </summary>
        public float SectorArc => _sectorArc;

        /****************************************
        *                Methods
        ****************************************/

        // random이 비어 있으면 배치를 임의로 대신하지 않는다. 부르는 쪽이 먼저 확인한다
        public BossBarrageLayout(Vector3 origin, int shotCount, IBossRandom random)
        {
            if (shotCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(shotCount));
            }

            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            _origin     = origin;
            _sectorArc  = FULL_TURN / shotCount;
            _sectors    = new int[shotCount];
            _angles     = new float[shotCount];
            _directions = new Vector3[shotCount];

            for (int i = 0; i < shotCount; ++i)
            {
                _sectors[i] = i;
            }

            for (int i = shotCount - 1; i > 0; --i)
            {
                int swapIndex = random.NextInt(i + 1);
                int held = _sectors[i];
                _sectors[i] = _sectors[swapIndex];
                _sectors[swapIndex] = held;
            }

            for (int i = 0; i < shotCount; ++i)
            {
                float fraction = Mathf.Clamp(random.NextFloat01(), 0f, MAX_FRACTION);
                _angles[i]     = (_sectors[i] + fraction) * _sectorArc;
                _directions[i] = (Quaternion.AngleAxis(_angles[i], Vector3.up) * Vector3.forward).normalized;
            }
        }

        /// <summary> 발 k가 나가는 구역 번호 </summary>
        public int GetSector(int shotIndex)
        {
            return _sectors[shotIndex];
        }

        /// <summary> 발 k의 방향 각도(도), 월드 정면에서 시계 방향이다 </summary>
        public float GetAngle(int shotIndex)
        {
            return _angles[shotIndex];
        }

        /// <summary> 발 k의 수평 단위 방향 </summary>
        public Vector3 GetDirection(int shotIndex)
        {
            return _directions[shotIndex];
        }
    }
}
