// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 부채꼴 연출을 미리 만들어 두고 빌려준다, 용량이 차면 null을 돌려준다 </summary>
    public sealed class SectorVFXPool : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    DEFAULT_CAPACITY = 8;
        private const string STAGING_NAME     = "Sector Pool Prewarm";

        [SerializeField] private SectorVFXController _prefab;
        [SerializeField, Min(1)] private int _capacity = DEFAULT_CAPACITY;

        private readonly Queue<SectorVFXController> _available = new Queue<SectorVFXController>();
        private readonly HashSet<SectorVFXController> _leased = new HashSet<SectorVFXController>();

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
                Debug.LogError("[SectorVFX] 풀에 SectorGroundVFX 프리팹이 지정되지 않았다", this);
                return;
            }

            // 입자 계층을 만드는 동안 미리 만든 오브젝트가 켜지지 않도록 꺼진 부모 밑에서 생성한다
            GameObject staging = new GameObject(STAGING_NAME);
            staging.SetActive(false);

            for (int i = 0; i < _capacity; ++i)
            {
                SectorVFXController instance = Instantiate(_prefab, staging.transform);
                instance.gameObject.SetActive(false);
                instance.transform.SetParent(transform, false);
                instance.Completed += Release;
                _available.Enqueue(instance);
            }

            Destroy(staging);
        }

        private void OnDisable()
        {
            foreach (SectorVFXController instance in _leased)
            {
                if (instance == null)
                {
                    continue;
                }

                instance.ResetForPool();
                instance.gameObject.SetActive(false);
                _available.Enqueue(instance);
            }

            _leased.Clear();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 연출을 재생한다, 풀이 비었으면 null이라 호출자는 연출을 생략할 수 있어야 한다 </summary>
        public SectorVFXController Play(Vector3 position, Quaternion rotation, SectorShape shape, SectorBehaviour behaviour)
        {
            return Play(position, rotation, shape, behaviour, 0f, false);
        }

        /// <summary> 서버 확정을 기다리는 예고를 재생한다, 시작 오프셋과 확정 대기는 SectorVFXController.Play를 따른다 </summary>
        public SectorVFXController Play(Vector3 position, Quaternion rotation, SectorShape shape, SectorBehaviour behaviour,
            float startElapsed, bool holdForConfirm)
        {
            if ((!isActiveAndEnabled) || (_available.Count == 0))
            {
                return null;
            }

            SectorVFXController instance = _available.Dequeue();
            _leased.Add(instance);
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.gameObject.SetActive(true);
            instance.Play(shape, behaviour, startElapsed, holdForConfirm);
            return instance;
        }

        /// <summary> 재생 중인 연출을 취소하고 돌려받는다, 정상 종료는 자동으로 반환된다 </summary>
        public void Release(SectorVFXController instance)
        {
            if ((instance == null) || (!_leased.Remove(instance)))
            {
                return;
            }

            instance.ResetForPool();
            instance.gameObject.SetActive(false);
            _available.Enqueue(instance);
        }
    }
}
