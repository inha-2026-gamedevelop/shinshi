// Unity
using UnityEngine;

namespace Shinshi.Art
{
    // 임시 표현이다. 원화와 애니메이션이 정해지기 전에 범위와 시간 흐름을 확인하는 최소 표현이며 최종 연출이 아니다
    // 스스로 시간을 세지 않는다. 서버 시각으로 계산한 값을 매 프레임 받아 그리므로 늦게 받은 예고도 이어서 그릴 수 있다
    /// <summary> 직사각형 지면 예고 한 개를 그린다, 피해 판정과 네트워크는 다루지 않는다 </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class RectTelegraphVFXController : MonoBehaviour, IPrewarmTarget
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  DEFAULT_GROUND_OFFSET = 0.035f;
        private const float  DEFAULT_FLASH_SECONDS = 0.3f;
        private const float  DEFAULT_DISSOLVE      = 0.25f;
        private const float  HALF                  = 0.5f;
        private const float  NOT_EXECUTED          = -1f;
        private const string MESH_NAME             = "RectTelegraphVFX Runtime Mesh";
        private const float  PREWARM_WIDTH         = 1.5f;
        private const float  PREWARM_LENGTH        = 4f;
        private const float  PREWARM_CHARGE        = 0.25f;
        private const float  MIN_DIRECTION         = 0.0001f;

        private static readonly int _stateId  = Shader.PropertyToID("_State");
        private static readonly int _sizeId   = Shader.PropertyToID("_Size");
        private static readonly int _timingId = Shader.PropertyToID("_Timing");

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField, Min(0f)] private float _groundOffset = DEFAULT_GROUND_OFFSET;
        [SerializeField, Min(0.01f)] private float _flashSeconds = DEFAULT_FLASH_SECONDS;
        [SerializeField, Min(0.01f)] private float _dissolveSeconds = DEFAULT_DISSOLVE;

        private Mesh                  _mesh;
        private MeshRenderer          _surface;
        private MaterialPropertyBlock _properties;
        private bool                  _initialized;
        private bool                  _isShown;
        private float                 _width;
        private float                 _length;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 화면에 보이는 중이다 </summary>
        public bool IsShown => _isShown;

        /// <summary> 한 번 타격하는 직사각형이 번쩍이는 시간(초) </summary>
        public float FlashSeconds => _flashSeconds;

        /// <summary> 끝나며 사라지는 시간(초) </summary>
        public float DissolveSeconds => _dissolveSeconds;

        public float Width  => _width;
        public float Length => _length;

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

        /// <summary> 시작점과 방향과 크기를 정하고 켠다, 이전 재생의 상태는 모두 지운다 </summary>
        public void Show(Vector3 origin, Vector3 direction, float width, float length)
        {
            Initialize();

            _width  = Mathf.Max(0f, width);
            _length = Mathf.Max(0f, length);
            transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction, Vector3.up));
            ApplyScale();

            _isShown = true;
            _surface.enabled = true;
            Render(0f, NOT_EXECUTED, 0f, NOT_EXECUTED, false);
        }

        /// <summary> 방향과 길이만 바꾼다, 추적 중 서버가 보낸 값이다 </summary>
        public void SetGeometry(Quaternion rotation, float length)
        {
            _length = Mathf.Max(0f, length);
            transform.rotation = rotation;
            ApplyScale();
        }

        /// <summary> 위치와 방향과 길이를 바꾼다, 날아가는 투사체처럼 움직이는 표현에 쓴다 </summary>
        public void SetPose(Vector3 position, Quaternion rotation, float length)
        {
            _length = Mathf.Max(0f, length);
            transform.SetPositionAndRotation(position, rotation);
            ApplyScale();
        }

        /// <summary> 한 프레임을 그린다. executeAge가 음수면 발동 전, dissolveAge가 음수면 아직 사라지기 전이다 </summary>
        public void Render(float charge, float executeAge, float traveledRatio, float dissolveAge, bool isDash)
        {
            if (!_isShown)
            {
                return;
            }

            float fade = (dissolveAge < 0f) ? 1f : (1f - Mathf.Clamp01(dissolveAge / _dissolveSeconds));

            _surface.GetPropertyBlock(_properties);
            _properties.SetVector(_stateId, new Vector4(Mathf.Clamp01(charge), executeAge, Mathf.Clamp01(traveledRatio), isDash ? 1f : 0f));
            _properties.SetVector(_sizeId, new Vector4(_width, _length, _groundOffset, fade));
            _properties.SetVector(_timingId, new Vector4(_flashSeconds, _dissolveSeconds, 0f, 0f));
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

        /// <summary> 운영 예고와 같은 Show로 켜고 충전, 발동 번쩍임, 소멸을 차례로 그린다 </summary>
        public void BeginPrewarm()
        {
            Vector3 direction = transform.forward;
            direction.y = 0f;
            if (direction.sqrMagnitude < MIN_DIRECTION)
            {
                direction = Vector3.forward;
            }

            Show(transform.position, direction.normalized, PREWARM_WIDTH, PREWARM_LENGTH);
        }

        public void TickPrewarm(float elapsedSeconds)
        {
            if (elapsedSeconds < PREWARM_CHARGE)
            {
                Render(elapsedSeconds / PREWARM_CHARGE, NOT_EXECUTED, 0f, NOT_EXECUTED, false);
                return;
            }

            float executeAge  = elapsedSeconds - PREWARM_CHARGE;
            float dissolveAge = executeAge - _flashSeconds;
            Render(1f, executeAge, 0f, (dissolveAge >= 0f) ? dissolveAge : NOT_EXECUTED, false);
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

            // 시작점에서 전방 +Z로 1m, 폭 1m인 사각형이다. 실제 크기는 localScale이 정한다. UV.x는 좌에서 우, UV.y는 시작에서 끝
            _mesh = new Mesh { name = MESH_NAME };
            _mesh.vertices  = new[]
            {
                new Vector3(-HALF, 0f, 0f), new Vector3(HALF, 0f, 0f), new Vector3(-HALF, 0f, 1f), new Vector3(HALF, 0f, 1f)
            };
            _mesh.uv        = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            _mesh.normals   = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            _mesh.triangles = new[] { 0, 2, 3, 0, 3, 1 };
            _mesh.RecalculateBounds();

            GetComponent<MeshFilter>().sharedMesh = _mesh;
            _surface.enabled = false;
            _initialized = true;
        }

        private void ApplyScale()
        {
            transform.localScale = new Vector3(Mathf.Max(_width, 0.0001f), 1f, Mathf.Max(_length, 0.0001f));
        }
    }
}
