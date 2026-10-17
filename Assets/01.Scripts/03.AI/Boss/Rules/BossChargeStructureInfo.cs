// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 구조물 조회(BossChargeStructureField)와 클라이언트 표현이 같은 값으로 구조물을 가리키도록 맵 표식에서 옮긴 값이다.
    // 구조물은 수직 원기둥으로 보고 높이는 쓰지 않는다. 중심의 y는 표현이 바닥 높이로만 쓴다
    /// <summary> 무철 돌진이 부술 수 있는 구조물 하나의 식별자, 중심, 반경, 기믹 지정 여부 </summary>
    public readonly struct BossChargeStructureInfo
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int     _id;
        private readonly Vector3 _center;
        private readonly float   _radius;
        private readonly bool    _isGimmickTarget;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 구조물 식별자, 0보다 커야 한다. 돌진 충돌과 파괴 사건이 이 값으로 구조물을 가리킨다 </summary>
        public int Id => _id;

        /// <summary> 원기둥 바닥 중심의 월드 위치 </summary>
        public Vector3 Center => _center;

        /// <summary> m, 원기둥 수평 반경 </summary>
        public float Radius => _radius;

        /// <summary> 돌진 유도 기믹이 부숴야 하는 지정 구조물이다 </summary>
        public bool IsGimmickTarget => _isGimmickTarget;

        /// <summary> 식별자가 0보다 크고 중심이 유한하며 반경이 0보다 큰 유한값이다 </summary>
        public bool IsValid
        {
            get
            {
                float sum = _center.x + _center.y + _center.z;
                return (_id > 0) && (!float.IsNaN(sum)) && (!float.IsInfinity(sum)) && (_radius > 0f)
                    && (!float.IsInfinity(_radius));
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public BossChargeStructureInfo(int id, Vector3 center, float radius, bool isGimmickTarget)
        {
            _id              = id;
            _center          = center;
            _radius          = radius;
            _isGimmickTarget = isGimmickTarget;
        }
    }
}
