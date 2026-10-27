// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.AI
{
    // 월백 평행 빔의 활성 구간 표현. 예고 바닥 선은 기존 재생기가 그대로 그리고, 서버가 활성을 확정한 줄만 이 빛 기둥으로 그린다
    // 위치와 방향과 길이와 폭은 서버 빔 사건 값에서 받는다. 생성할 때는 가는 선에서 굵어지고 사라질 때는 다시 가늘어진다
    /// <summary> 활성 확정된 빔 줄마다 빛 기둥 한 개를 맡는 표현 </summary>
    public sealed class WolbaekBeamActiveView : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float MODEL_LENGTH   = 16f;   // m, 프리팹 몸통 길이
        private const float MODEL_DIAMETER = 0.88f; // m, 프리팹 몸통 지름
        private const float MIN_THICKNESS  = 0.04f; // 가는 선의 굵기 배율
        private const float MIN_LENGTH     = 0.01f; // m, 이보다 짧은 줄은 그리지 않는다

        /****************************************
        *                Fields
        ****************************************/

        [Header("연결")]
        [SerializeField] private GameObject _prefab;
        [SerializeField] private WolbaekBeamPlayer _legacy; // 있으면 기존 활성 빔 메시를 숨기고 예고 바닥 선만 남긴다
        [SerializeField] private bool _hideLegacy = true;   // 끄면 기존 재생기의 표현을 그대로 둔다(기존 재생기 타이밍 시험용)
        [SerializeField] private bool _hideLegacyGround;    // 켜면 기존 예고 바닥 선까지 숨긴다, 예고는 기본 붉은 예고가 맡는다

        [SerializeField, Min(1)] private int _capacity = 8; // 동시에 그릴 수 있는 줄 수

        [Header("굵기 변화")]
        [SerializeField, Min(0.01f)] private float _appearSeconds = 0.35f;
        [SerializeField, Min(0.01f)] private float _fadeSeconds   = 0.5f;
        [SerializeField, Min(0.01f)] private float _widthScale    = 1f; // 서버 폭 대비 빛 기둥 지름 배율

        private struct Line
        {
            public int       BeamId;
            public bool      IsUsed;
            public double    Start;
            public double    End;
            public float     Radial;
            public Transform Root;
            public Transform Visual;
            public Transform Body;
        }

        private Line[]     _lines;
        private Transform  _container;
        private Renderer[] _legacyRenderers;

        /****************************************
        *              Properties
        ****************************************/

        public bool HidesLegacy
        {
            get => _hideLegacy;
            set => _hideLegacy = value;
        }

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
#if UNITY_SERVER
            // 헤드리스 서버는 화면이 없으므로 풀을 만들지 않는다
            enabled = false;
#else
            BuildPool();
#endif
        }

        private void OnDestroy()
        {
            if (_container != null)
            {
                Destroy(_container.gameObject);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void BuildPool()
        {
            if (_prefab == null)
            {
                enabled = false;
                return;
            }

            // 효과는 서버 월드 좌표로 움직이므로 보스 회전과 크기를 받지 않는 월드 루트에 둔다
            _container = new GameObject("WolbaekBeamActive").transform;
            _lines     = new Line[_capacity];

            for (int i = 0; i < _capacity; ++i)
            {
                GameObject instance = Instantiate(_prefab, _container);
                instance.SetActive(false);
                Transform visual = instance.transform.Find("Visual");
                _lines[i].Root   = instance.transform;
                _lines[i].Visual = visual;
                _lines[i].Body   = visual != null ? visual.Find("Body") : null;
            }

            if (_legacy != null)
            {
                _legacyRenderers = _legacy.GetComponentsInChildren<Renderer>(true);
            }
        }

        /// <summary> 서버가 활성을 확정한 줄을 시작 시각부터 끝 시각까지 그린다 </summary>
        public void Activate(int beamId, Vector3 from, Vector3 to, float width, double start, double end)
        {
            if ((_lines == null) || (width <= 0f) || (end <= start))
            {
                return;
            }

            Vector3 delta = to - from;
            float length = delta.magnitude;

            if (length < MIN_LENGTH)
            {
                return;
            }

            int index = Find(beamId);

            if (index < 0)
            {
                index = FreeIndex();
            }

            if ((index < 0) || (_lines[index].Visual == null) || (_lines[index].Body == null))
            {
                return;
            }

            ref Line line = ref _lines[index];
            line.BeamId = beamId;
            line.IsUsed = true;
            line.Start  = start;
            line.End    = end;
            line.Radial = (width * _widthScale) / MODEL_DIAMETER;
            line.Root.SetPositionAndRotation(from, Quaternion.LookRotation(delta / length, Vector3.up));
            line.Body.localScale = new Vector3(length / MODEL_LENGTH, 1f, 1f);
            line.Root.gameObject.SetActive(false);
        }

        /// <summary> 회전하는 별처럼 활성 중에 끝점이 바뀌는 줄의 위치와 방향과 길이를 다시 맞춘다 </summary>
        public void UpdateGeometry(int beamId, Vector3 from, Vector3 to)
        {
            int index = (_lines != null) ? Find(beamId) : -1;

            if (index < 0)
            {
                return;
            }

            Vector3 delta = to - from;
            float length = delta.magnitude;

            if (length < MIN_LENGTH)
            {
                return;
            }

            _lines[index].Root.SetPositionAndRotation(from, Quaternion.LookRotation(delta / length, Vector3.up));
            _lines[index].Body.localScale = new Vector3(length / MODEL_LENGTH, 1f, 1f);
        }

        /// <summary> 서버 시각에 맞춰 보임과 굵기를 갱신한다 </summary>
        public void Tick(double now)
        {
            if (_lines == null)
            {
                return;
            }

            for (int i = 0; i < _lines.Length; ++i)
            {
                ref Line line = ref _lines[i];

                if (!line.IsUsed)
                {
                    continue;
                }

                if (now >= line.End)
                {
                    Release(i);
                    continue;
                }

                bool started = now >= line.Start;
                line.Root.gameObject.SetActive(started);

                if (!started)
                {
                    continue;
                }

                float appear = Mathf.Clamp01((float)(now - line.Start) / _appearSeconds);
                float fade   = Mathf.Clamp01((float)(line.End - now) / _fadeSeconds);
                float ramp   = Mathf.Max(MIN_THICKNESS, Mathf.SmoothStep(0f, 1f, Mathf.Min(appear, fade)));
                float radial = line.Radial * ramp;
                line.Visual.localScale = new Vector3(1f, radial, radial);
            }

            HideLegacy();
        }

        public void Stop(int beamId)
        {
            int index = Find(beamId);

            if (index >= 0)
            {
                Release(index);
            }
        }

        public void StopAll()
        {
            if (_lines == null)
            {
                return;
            }

            for (int i = 0; i < _lines.Length; ++i)
            {
                if (_lines[i].IsUsed)
                {
                    Release(i);
                }
            }
        }

        // 기존 재생기가 활성에서 켠 메시를 이 프레임에 다시 끈다. 바닥 예고 선은 일반 메시 렌더러라 건드리지 않는다
        public void HideLegacy()
        {
            if ((!_hideLegacy) || (_legacyRenderers == null))
            {
                return;
            }

            for (int i = 0; i < _legacyRenderers.Length; ++i)
            {
                Renderer renderer = _legacyRenderers[i];

                if ((renderer != null) && (_hideLegacyGround || (renderer is SkinnedMeshRenderer)))
                {
                    renderer.enabled = false;
                }
            }
        }

        private void Release(int index)
        {
            _lines[index].IsUsed = false;

            // 보스가 파괴되는 중이면 컨테이너가 먼저 사라졌을 수 있다
            if (_lines[index].Root != null)
            {
                _lines[index].Root.gameObject.SetActive(false);
            }
        }

        private int Find(int beamId)
        {
            for (int i = 0; i < _lines.Length; ++i)
            {
                if (_lines[i].IsUsed && (_lines[i].BeamId == beamId))
                {
                    return i;
                }
            }

            return -1;
        }

        private int FreeIndex()
        {
            for (int i = 0; i < _lines.Length; ++i)
            {
                if (!_lines[i].IsUsed)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
