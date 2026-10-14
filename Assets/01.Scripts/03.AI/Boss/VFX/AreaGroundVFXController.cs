// Unity
using UnityEngine;

namespace Shinshi.Art
{
    // 임시 표현이다. 원화와 애니메이션이 정해지기 전에 범위와 시간 흐름을 확인하는 최소 표현이며 최종 연출이 아니다
    // 스스로 시간을 세지 않는다. 서버 시각으로 계산한 값을 매 프레임 받아 그리므로 늦게 받은 활성도 이어서 그릴 수 있다
    /// <summary> 원형 지속 장판 한 개를 그린다, 피해 판정과 네트워크는 다루지 않는다 </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class AreaGroundVFXController : MonoBehaviour, IPrewarmTarget
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  DEFAULT_GROUND_OFFSET = 0.03f;
        private const float  DEFAULT_FADE_SECONDS  = 0.3f;
        private const float  HALF                  = 0.5f;
        private const float  DIAMETER_SCALE        = 2f;
        private const string MESH_NAME             = "AreaGroundVFX Runtime Mesh";
        private const float  PREWARM_RADIUS        = 2f;
        private const float  PREWARM_LIFE          = 0.4f;

        private static readonly int _stateId = Shader.PropertyToID("_State");
        private static readonly int _sizeId  = Shader.PropertyToID("_Size");

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField, Min(0f)] private float _groundOffset = DEFAULT_GROUND_OFFSET;
        [SerializeField, Min(0.01f)] private float _fadeSeconds = DEFAULT_FADE_SECONDS;

        private Mesh                  _mesh;
        private MeshRenderer          _surface;
        private MaterialPropertyBlock _properties;
        private bool                  _initialized;
        private bool                  _isShown;
        private float                 _radius;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsShown => _isShown;

        /// <summary> 사라질 때 옅어지는 시간(초) </summary>
        public float FadeSeconds => _fadeSeconds;

        public float Radius => _radius;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            Initialize();
        }

        private void OnDisable()
        {
            Hide();
        }

        private void OnDestroy()
        {
            if (_mesh == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(_mesh);
            }
            else
            {
                DestroyImmediate(_mesh);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 중심과 반경을 정하고 켠다, 이전 재생의 상태는 모두 지운다 </summary>
        public void Show(Vector3 center, float radius)
        {
            Initialize();

            _radius = Mathf.Max(0.01f, radius);
            transform.SetPositionAndRotation(center, Quaternion.identity);
            transform.localScale = new Vector3(_radius * DIAMETER_SCALE, 1f, _radius * DIAMETER_SCALE);

            _isShown = true;
            _surface.enabled = true;
            Render(1f, float.MaxValue, 1f);
        }

        /// <summary> 한 프레임을 그린다. lifeRatio는 남은 수명 1~0, pulseAge는 마지막 서버 타격 뒤 흐른 시간, fade는 1(보임)~0(사라짐)이다 </summary>
        public void Render(float lifeRatio, float pulseAge, float fade)
        {
            if (!_isShown)
            {
                return;
            }

            _surface.GetPropertyBlock(_properties);
            _properties.SetVector(_stateId, new Vector4(Mathf.Clamp01(lifeRatio), pulseAge, Mathf.Clamp01(fade), _radius));
            _properties.SetVector(_sizeId, new Vector4(_radius, _groundOffset, 0f, 0f));
            _surface.SetPropertyBlock(_properties);
        }

        /// <summary> 즉시 화면을 지운다, 풀로 돌려보내기 전과 비활성화 때 부른다 </summary>
        public void Hide()
        {
            _isShown = false;

            if (_surface != null)
            {
                _surface.enabled = false;
            }
        }

        /// <summary> 운영 표시와 같은 Show로 켜고 유지, 타격 맥동, 옅어짐을 차례로 그린다 </summary>
        public void BeginPrewarm()
        {
            Show(transform.position, PREWARM_RADIUS);
        }

        public void TickPrewarm(float elapsedSeconds)
        {
            float fadeAge = Mathf.Max(0f, elapsedSeconds - PREWARM_LIFE);
            Render(1f - Mathf.Clamp01(elapsedSeconds / PREWARM_LIFE), elapsedSeconds,
                1f - Mathf.Clamp01(fadeAge / _fadeSeconds));
        }

        public void EndPrewarm()
        {
            Hide();
        }

        private void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _surface    = GetComponent<MeshRenderer>();
            _properties = new MaterialPropertyBlock();

            // 중심이 원점이고 한 변이 1m인 사각형, 셰이더가 UV로 원을 오린다. 실제 크기는 localScale이 정한다
            _mesh = new Mesh { name = MESH_NAME };
            _mesh.vertices  = new[]
            {
                new Vector3(-HALF, 0f, -HALF), new Vector3(HALF, 0f, -HALF), new Vector3(-HALF, 0f, HALF), new Vector3(HALF, 0f, HALF)
            };
            _mesh.uv        = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            _mesh.normals   = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            _mesh.triangles = new[] { 0, 2, 3, 0, 3, 1 };
            _mesh.RecalculateBounds();

            GetComponent<MeshFilter>().sharedMesh = _mesh;
            _surface.enabled = false;
            _initialized = true;
        }
    }
}
