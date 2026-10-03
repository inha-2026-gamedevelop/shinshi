// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 빔 연출을 미리 만들어 두고 빌려준다, 용량이 차면 null을 돌려준다 </summary>
    public sealed class BeamGroundVFXPool : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int    DEFAULT_CAPACITY = 4;
        private const string STAGING_NAME     = "Beam Ground Pool Prewarm";

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private BeamGroundVFXController _prefab;
        [SerializeField, Min(1)] private int _capacity = DEFAULT_CAPACITY;

        private readonly Queue<BeamGroundVFXController> _available = new Queue<BeamGroundVFXController>();
        private readonly HashSet<BeamGroundVFXController> _leased  = new HashSet<BeamGroundVFXController>();

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
                Debug.LogError("[BeamGroundVFX] 풀에 프리팹이 지정되지 않았다", this);
                return;
            }

            // 미리 만든 오브젝트가 켜지지 않도록 꺼진 부모 밑에서 생성한다
            GameObject staging = new GameObject(STAGING_NAME);
            staging.SetActive(false);

            for (int i = 0; i < _capacity; ++i)
            {
                BeamGroundVFXController instance = Instantiate(_prefab, staging.transform);
                instance.gameObject.SetActive(false);
                instance.transform.SetParent(transform, false);
                _available.Enqueue(instance);
            }

            Destroy(staging);
        }

        private void OnDisable()
        {
            foreach (BeamGroundVFXController instance in _leased)
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
        public BeamGroundVFXController Acquire()
        {
            if ((!isActiveAndEnabled) || (_available.Count == 0))
            {
                return null;
            }

            BeamGroundVFXController instance = _available.Dequeue();
            _leased.Add(instance);
            instance.gameObject.SetActive(true);
            return instance;
        }

        /// <summary> 화면을 지우고 돌려받는다, 빌리지 않은 것이나 두 번째 반환은 무시한다 </summary>
        public void Release(BeamGroundVFXController instance)
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
