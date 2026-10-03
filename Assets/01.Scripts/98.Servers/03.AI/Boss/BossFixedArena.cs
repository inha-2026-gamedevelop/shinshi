// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 값이 고정된 전장 입력이다. 테스트와 로컬 리그가 쓰며 실제 서버 세션의 전장 정보 연결을 대신하지 않는다
    /// <summary> 중심과 반경이 고정된 전장 </summary>
    public sealed class BossFixedArena : IBossArenaSource
    {
        /****************************************
        *                Fields
        ****************************************/

        private Vector3 _center;
        private float   _radius;

        /****************************************
        *              Properties
        ****************************************/

        public Vector3 Center => _center;
        public float   Radius => _radius;

        /****************************************
        *                Methods
        ****************************************/

        public BossFixedArena(Vector3 center, float radius)
        {
            _center = center;
            _radius = radius;
        }

        /// <summary> 값을 바꾼다, 다음 시전부터 적용되고 이미 설치한 빔은 시작 때 고정한 값을 쓴다 </summary>
        public void Set(Vector3 center, float radius)
        {
            _center = center;
            _radius = radius;
        }

        public bool TryGetArena(out Vector3 center, out float radius)
        {
            center = _center;
            radius = _radius;
            return radius > 0f;
        }
    }
}
