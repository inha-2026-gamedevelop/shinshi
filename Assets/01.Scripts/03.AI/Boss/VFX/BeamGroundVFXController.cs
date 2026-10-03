// Unity
using UnityEngine;

namespace Shinshi.Art
{
    // 임시 표현이다. 원화와 애니메이션이 정해지기 전에 범위와 시간 흐름을 확인하는 최소 표현이며 최종 연출이 아니다
    // 폭 띠와 전장 원의 교집합을 그대로 메시로 만든다. 두 끝이 전장 경계의 호를 따르므로 서버 판정(폭 띠와 전장 원의 교집합)과 같은 영역이다
    //   중심선만 전장에 맞춰 자르지 않고 띠 전체를 전장 원으로 자른다. 호는 일정 개수의 선분으로 근사하므로 판정 영역보다 호 구간마다 아주 조금 작게 보일 수 있다
    // 스스로 시간을 세지 않는다. 서버 시각으로 계산한 값을 매 프레임 받아 그리므로 늦게 받은 활성도 이어서 그릴 수 있다
    // 폭발형 직사각형 예고(RectTelegraphVFX)를 지속 빔으로 쓰지 않고 별도 메시와 셰이더로 그린다
    /// <summary> 전장에 고정된 평행 빔 한 줄을 그린다, 피해 판정과 네트워크는 다루지 않는다 </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class BeamGroundVFXController : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  DEFAULT_GROUND_OFFSET = 0.03f;
        private const float  DEFAULT_FADE_SECONDS  = 0.3f;
        private const float  HALF                  = 0.5f;
        private const float  MIN_SPAN              = 0.01f;
        private const int    ARC_SEGMENTS          = 16;   // 폭 방향 표본 구간 수, 호를 근사하는 선분 수다
        private const int    SAMPLE_COUNT          = ARC_SEGMENTS + 1;
        private const int    VERTEX_COUNT          = SAMPLE_COUNT * 2;
        private const int    INDEX_COUNT           = ARC_SEGMENTS * 6;
        private const string MESH_NAME             = "BeamGroundVFX Runtime Mesh";

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
        private readonly Vector3[]    _vertices = new Vector3[VERTEX_COUNT];
        private readonly Vector2[]    _uvs      = new Vector2[VERTEX_COUNT];
        private readonly Vector3[]    _normals  = new Vector3[VERTEX_COUNT];
        private readonly int[]        _indices  = new int[INDEX_COUNT];
        private bool                  _initialized;
        private bool                  _isShown;
        private bool                  _hasArea;
        private float                 _width;
        private Vector3               _arenaCenter;
        private float                 _arenaRadius;
        private float                 _lateralOffset;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsShown => _isShown;

        /// <summary> 보이는 영역이 있다(빔이 전장과 겹친다) </summary>
        public bool HasArea => _hasArea;

        /// <summary> 사라질 때 옅어지는 시간(초) </summary>
        public float FadeSeconds => _fadeSeconds;

        public float Width => _width;

        public float ArenaRadius => _arenaRadius;

        public float LateralOffset => _lateralOffset;

        public Vector3 ArenaCenter => _arenaCenter;

        /// <summary> 메시의 정점 수, 시험용이다 </summary>
        public int VertexCount => VERTEX_COUNT;

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

        // 오브젝트를 전장 중심에 놓고 빔 방향을 앞(+Z)으로 돌린다. 지역 X는 방향의 오른쪽이라 정점의 X가 빔 중심선까지의 좌우 거리다
        /// <summary> 전장 기준 배치로 켠다, 이전 재생의 상태는 모두 지운다 </summary>
        public void Show(Vector3 arenaCenter, float arenaRadius, Vector3 direction, float lateralOffset, float width)
        {
            Initialize();

            direction.y = 0f;
            Quaternion rotation = (direction.sqrMagnitude > MIN_SPAN * MIN_SPAN)
                ? Quaternion.LookRotation(direction.normalized, Vector3.up)
                : Quaternion.identity;
            transform.SetPositionAndRotation(arenaCenter, rotation);
            transform.localScale = Vector3.one;

            _arenaCenter   = arenaCenter;
            _arenaRadius   = Mathf.Max(MIN_SPAN, arenaRadius);
            _lateralOffset = lateralOffset;
            _width         = Mathf.Max(MIN_SPAN, width);

            BuildMesh();

            _isShown = true;
            _surface.enabled = _hasArea;
            Render(false, 0f, float.MaxValue, 1f);
        }

        // mode 거짓은 예고, 참은 활성이다. progress는 예고면 진행 비율, 활성이면 남은 수명 1~0, pulseAge는 마지막 서버 타격 뒤 흐른 시간, fade는 1(보임)~0(사라짐)이다
        /// <summary> 한 프레임을 그린다 </summary>
        public void Render(bool isActive, float progress, float pulseAge, float fade)
        {
            if (!_isShown)
            {
                return;
            }

            _surface.GetPropertyBlock(_properties);
            _properties.SetVector(_stateId, new Vector4(isActive ? 1f : 0f, Mathf.Clamp01(progress), pulseAge, Mathf.Clamp01(fade)));
            _properties.SetVector(_sizeId, new Vector4(_width, _groundOffset, 0f, 0f));
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

        // 폭 띠의 양 변을 전장 안쪽으로 자르고 그 사이를 같은 간격으로 나눠 각 표본마다 전장 원과 만나는 앞뒤 두 점을 만든다
        // 띠가 전장과 전혀 겹치지 않으면 영역이 없다
        private void BuildMesh()
        {
            float left  = _lateralOffset - (_width * HALF);
            float right = _lateralOffset + (_width * HALF);
            float from  = Mathf.Max(left, -_arenaRadius);
            float to    = Mathf.Min(right, _arenaRadius);

            _hasArea = to > from;
            _mesh.Clear();
            if (!_hasArea)
            {
                return;
            }

            for (int i = 0; i < SAMPLE_COUNT; ++i)
            {
                float ratio = (float)i / ARC_SEGMENTS;
                float x     = Mathf.Lerp(from, to, ratio);
                float z     = Mathf.Sqrt(Mathf.Max(0f, (_arenaRadius * _arenaRadius) - (x * x)));
                float u     = Mathf.InverseLerp(left, right, x);

                int back  = i * 2;
                int front = back + 1;
                _vertices[back]  = new Vector3(x, 0f, -z);
                _vertices[front] = new Vector3(x, 0f, z);
                _uvs[back]       = new Vector2(u, 0f);
                _uvs[front]      = new Vector2(u, 1f);
                _normals[back]   = Vector3.up;
                _normals[front]  = Vector3.up;
            }

            int index = 0;
            for (int i = 0; i < ARC_SEGMENTS; ++i)
            {
                int back  = i * 2;
                int front = back + 1;
                int nextBack  = back + 2;
                int nextFront = back + 3;

                _indices[index++] = back;
                _indices[index++] = front;
                _indices[index++] = nextFront;
                _indices[index++] = back;
                _indices[index++] = nextFront;
                _indices[index++] = nextBack;
            }

            _mesh.vertices  = _vertices;
            _mesh.uv        = _uvs;
            _mesh.normals   = _normals;
            _mesh.triangles = _indices;
            _mesh.RecalculateBounds();
        }

        private void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _surface    = GetComponent<MeshRenderer>();
            _properties = new MaterialPropertyBlock();
            _mesh       = new Mesh { name = MESH_NAME };

            GetComponent<MeshFilter>().sharedMesh = _mesh;
            _surface.enabled = false;
            _initialized = true;
        }
    }
}
