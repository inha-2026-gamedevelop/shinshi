// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 직사각형 예고를 미리 만들어 두고 빌려준다, 용량이 차면 null을 돌려준다 </summary>
    public sealed class RectTelegraphVFXPool : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int    DEFAULT_CAPACITY = 4;
        private const string STAGING_NAME     = "Rect Telegraph Pool Prewarm";

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private RectTelegraphVFXController _prefab;
        [SerializeField, Min(1)] private int _capacity = DEFAULT_CAPACITY;

        private readonly Queue<RectTelegraphVFXController> _available = new Queue<RectTelegraphVFXController>();
        private readonly HashSet<RectTelegraphVFXController> _leased  = new HashSet<RectTelegraphVFXController>();

        /****************************************
        *              Properties
        ****************************************/

        public int AvailableCount => _available.Count;
        public int LeasedCount    => _leased.Count;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_prefab == null)
            {
                Debug.LogError("[RectTelegraphVFX] 풀에 프리팹이 지정되지 않았다", this);
                return;
            }

            // 미리 만든 오브젝트가 켜지지 않도록 꺼진 부모 밑에서 생성한다
            GameObject staging = new GameObject(STAGING_NAME);
            staging.SetActive(false);

            for (int i = 0; i < _capacity; ++i)
            {
                RectTelegraphVFXController instance = Instantiate(_prefab, staging.transform);
                instance.gameObject.SetActive(false);
                instance.transform.SetParent(transform, false);
                _available.Enqueue(instance);
            }

            Destroy(staging);
        }

        private void OnDisable()
        {
            foreach (RectTelegraphVFXController instance in _leased)
            {
                if (instance == null)
                {
                    continue;
                }

                instance.Hide();
                instance.gameObject.SetActive(false);
                _available.Enqueue(instance);
            }

            _leased.Clear();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 하나를 빌려 켠다, 풀이 비었으면 null이라 호출자는 표현을 생략할 수 있어야 한다 </summary>
        public RectTelegraphVFXController Acquire()
        {
            if ((!isActiveAndEnabled) || (_available.Count == 0))
            {
                return null;
            }

            RectTelegraphVFXController instance = _available.Dequeue();
            _leased.Add(instance);
            instance.gameObject.SetActive(true);
            return instance;
        }

        /// <summary> 화면을 지우고 돌려받는다, 빌리지 않은 것이나 두 번째 반환은 무시한다 </summary>
        public void Release(RectTelegraphVFXController instance)
        {
            if ((instance == null) || (!_leased.Remove(instance)))
            {
                return;
            }

            instance.Hide();
            instance.gameObject.SetActive(false);
            _available.Enqueue(instance);
        }
    }
}
