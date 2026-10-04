// Unity
using UnityEngine;

namespace Shinshi.Combat
{
    /// <summary> 맵 디자인이 지정한 안전 복귀 지점 목록을 씬에 표시한다, 서버 부활 단계가 읽기만 한다 </summary>
    public sealed class SafeRevivePointSet : MonoBehaviour
    {
        // 서버 판정이 씬 탐색 없이 찾도록 켜질 때 자신을 정적 자리에 올린다. 던전 씬 하나에 하나만 둔다.
        // 표현이 아니라 판정 입력이므로 ClientView를 상속하지 않는다. 서버 빌드에서도 켜져 있어야 한다

        /****************************************
        *                Fields
        ****************************************/

        [Header("복귀 지점")]
        [SerializeField] private Transform[] _points = null;

        private static SafeRevivePointSet _current;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 지금 켜져 있는 목록, 없으면 null </summary>
        public static SafeRevivePointSet Current => _current;

        public int Count => (_points == null) ? 0 : _points.Length;

        /****************************************
        *              Unity Event
        ****************************************/

        // 도메인 리로드를 끈 에디터에서 이전 플레이의 목록이 남지 않게 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            _current = null;
        }

        private void OnEnable()
        {
            if ((_current != null) && (_current != this))
            {
                Debug.LogError("[Combat] 안전 복귀 지점 목록이 씬에 둘 이상이다. 먼저 켜진 목록만 쓴다", this);
                return;
            }

            _current = this;
        }

        private void OnDisable()
        {
            if (_current == this)
            {
                _current = null;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> index번째 지점의 월드 위치, 비어 있는 칸이면 false </summary>
        public bool TryGetPosition(int index, out Vector3 position)
        {
            position = Vector3.zero;
            if ((index < 0) || (index >= Count))
            {
                return false;
            }

            Transform point = _points[index];
            if (point == null)
            {
                return false;
            }

            position = point.position;
            return true;
        }
    }
}
