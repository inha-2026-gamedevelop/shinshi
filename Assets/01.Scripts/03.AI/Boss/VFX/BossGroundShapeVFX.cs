// Unity
using UnityEngine;

namespace Shinshi.Art
{
    // 임시 표현이다. 최종 VFX 에셋이 없는 동안 위험 구역의 범위와 시간 흐름만 보이는 최소 도형이며 최종 연출이 아니다
    // 같은 메시를 바닥(base)과 채움(fill) 두 렌더러로 그리고, 채움만 충전 비율만큼 줄여 차오르는 모습을 만든다
    // 스스로 시간을 세지 않는다. 서버 시각으로 계산한 값을 매 프레임 받아 그리므로 늦게 받은 예고도 이어서 그릴 수 있다
    /// <summary> 바닥에 놓는 원, 부채꼴, 직사각형, 전장 띠 한 개를 그린다, 피해 판정과 네트워크는 다루지 않는다 </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class BossGroundShapeVFX : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  GROUND_OFFSET       = 0.04f; // m, 바닥 위로 띄우는 높이
        private const float  FILL_OFFSET         = 0.01f; // m, 채움이 바닥 도형 위에 겹쳐 보이게 더 띄우는 높이
        private const int    DISC_SEGMENTS       = 48;
        private const float  SECTOR_STEP_DEGREES = 6f;
        private const int    BAND_SEGMENTS       = 16;
        private const float  FULL_CIRCLE         = 360f;
        private const float  HALF                = 0.5f;
        private const float  MIN_SPAN            = 0.01f;
        private const float  MIN_SCALE           = 0.0001f;
        private const string MESH_NAME           = "BossGroundShapeVFX Runtime Mesh";
        private const string FILL_NAME           = "Fill";

        private static readonly int _colorId = Shader.PropertyToID("_Color");

        /****************************************
        *                Fields
        ****************************************/

        private Mesh                  _mesh;
        private MeshRenderer          _baseRenderer;
        private MeshRenderer          _fillRenderer;
        private Transform             _fill;
        private MaterialPropertyBlock _properties;
        private bool                  _initialized;
        private bool                  _isShown;

        private EBossGroundShapeKind  _kind;
        private Vector3               _origin;
        private Vector3               _direction;
        private float                 _length;
        private float                 _width;
        private float                 _radius;
        private float                 _arcDegrees;
        private float                 _charge;
        private Color                 _baseColor;
        private Color                 _fillColor;
        private Vector3               _baseScale = Vector3.one;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsShown => _isShown;

        public EBossGroundShapeKind Kind => _kind;

        /// <summary> 도형 기준점, 원은 중심이고 부채꼴과 직사각형은 시작점이며 전장 띠는 전장 중심이다 </summary>
        public Vector3 Origin => _origin;

        /// <summary> 도형이 향하는 수평 단위 방향 </summary>
        public Vector3 Direction => _direction;

        /// <summary> 직사각형 길이(m) </summary>
        public float Length => _length;

        /// <summary> 직사각형과 전장 띠의 폭(m) </summary>
        public float Width => _width;

        /// <summary> 원과 부채꼴의 반경, 전장 띠는 전장 반경(m) </summary>
        public float Radius => _radius;

        /// <summary> 부채꼴 전체 각도(도), 원은 360이다 </summary>
        public float ArcDegrees => _arcDegrees;

        /// <summary> 마지막으로 그린 충전 비율 0~1 </summary>
        public float Charge => _charge;

        public Color BaseColor => _baseColor;

        public Color FillColor => _fillColor;

        /// <summary> 채움 렌더러가 켜져 있는가 </summary>
        public bool IsFillVisible => (_fillRenderer != null) && _fillRenderer.enabled;

        /****************************************
        *              Unity Event
        ****************************************/

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

        /// <summary> 재질을 정하고 렌더러를 준비한다, 한 번만 부른다 </summary>
        public void Initialize(Material material)
        {
            if (_initialized)
            {
                return;
            }

            _properties   = new MaterialPropertyBlock();
            _mesh         = new Mesh { name = MESH_NAME };
            _baseRenderer = GetComponent<MeshRenderer>();
            GetComponent<MeshFilter>().sharedMesh = _mesh;

            GameObject fillObject = new GameObject(FILL_NAME, typeof(MeshFilter), typeof(MeshRenderer));
            _fill = fillObject.transform;
            _fill.SetParent(transform, false);
            _fill.localPosition = new Vector3(0f, FILL_OFFSET, 0f);
            fillObject.GetComponent<MeshFilter>().sharedMesh = _mesh;
            _fillRenderer = fillObject.GetComponent<MeshRenderer>();

            PrepareRenderer(_baseRenderer, material);
            PrepareRenderer(_fillRenderer, material);
            _initialized = true;
        }

        /// <summary> 원을 켠다, 이전 재생의 상태는 모두 지운다 </summary>
        public void ShowDisc(Vector3 center, float radius)
        {
            _kind       = EBossGroundShapeKind.Disc;
            _radius     = Mathf.Max(MIN_SPAN, radius);
            _arcDegrees = FULL_CIRCLE;
            _length     = 0f;
            _width      = 0f;
            _direction  = Vector3.forward;
            BuildSector(FULL_CIRCLE);
            Place(center, Quaternion.identity, new Vector3(_radius, 1f, _radius));
        }

        /// <summary> 부채꼴을 켠다, 꼭짓점이 origin이고 가운데가 direction을 향한다 </summary>
        public void ShowSector(Vector3 origin, Vector3 direction, float radius, float arcDegrees)
        {
            _kind       = EBossGroundShapeKind.Sector;
            _radius     = Mathf.Max(MIN_SPAN, radius);
            _arcDegrees = Mathf.Clamp(arcDegrees, MIN_SPAN, FULL_CIRCLE);
            _length     = 0f;
            _width      = 0f;
            _direction  = Flatten(direction);
            BuildSector(_arcDegrees);
            Place(origin, Quaternion.LookRotation(_direction, Vector3.up), new Vector3(_radius, 1f, _radius));
        }

        /// <summary> 직사각형을 켠다, 시작점에서 direction으로 length만큼 뻗는다 </summary>
        public void ShowRect(Vector3 origin, Vector3 direction, float length, float width)
        {
            _kind       = EBossGroundShapeKind.Rect;
            _length     = Mathf.Max(MIN_SPAN, length);
            _width      = Mathf.Max(MIN_SPAN, width);
            _radius     = 0f;
            _arcDegrees = 0f;
            _direction  = Flatten(direction);
            BuildRect();
            Place(origin, Quaternion.LookRotation(_direction, Vector3.up), new Vector3(_width, 1f, _length));
        }

        /// <summary> 전장 원 안에 갇힌 평행 띠를 켠다, lateralOffset은 direction의 오른쪽을 양수로 잰 중심선 거리다 </summary>
        public void ShowBand(Vector3 arenaCenter, float arenaRadius, Vector3 direction, float lateralOffset, float width)
        {
            _kind       = EBossGroundShapeKind.ArenaBand;
            _radius     = Mathf.Max(MIN_SPAN, arenaRadius);
            _width      = Mathf.Max(MIN_SPAN, width);
            _length     = 0f;
            _arcDegrees = 0f;
            _direction  = Flatten(direction);
            BuildBand(lateralOffset);
            Place(arenaCenter, Quaternion.LookRotation(_direction, Vector3.up), Vector3.one);
        }

        // charge가 0이면 채움을 숨기고 바닥만 그린다. 채움은 원과 부채꼴이면 반경, 직사각형이면 길이, 전장 띠면 중심선에서 양쪽으로 차오른다
        /// <summary> 한 프레임을 그린다 </summary>
        public void Render(float charge, Color baseColor, Color fillColor)
        {
            if (!_isShown)
            {
                return;
            }

            _charge    = Mathf.Clamp01(charge);
            _baseColor = baseColor;
            _fillColor = fillColor;

            SetColor(_baseRenderer, baseColor);
            SetColor(_fillRenderer, fillColor);

            bool showFill = (_charge > 0f) && (fillColor.a > 0f);
            _fillRenderer.enabled = showFill;
            if (showFill)
            {
                _fill.localScale = FillScale(_charge);
            }
        }

        /// <summary> 모양은 그대로 두고 기준점만 옮긴다, 서버가 알린 중심을 따라갈 때 쓴다 </summary>
        public void MoveTo(Vector3 origin)
        {
            if (!_isShown)
            {
                return;
            }

            _origin = origin;
            transform.position = origin + (Vector3.up * GROUND_OFFSET);
        }

        /// <summary> 즉시 화면을 지운다, 풀로 돌려보내기 전과 비활성화 때 부른다 </summary>
        public void Hide()
        {
            _isShown = false;

            if (_baseRenderer != null)
            {
                _baseRenderer.enabled = false;
            }

            if (_fillRenderer != null)
            {
                _fillRenderer.enabled = false;
            }
        }

        private static void PrepareRenderer(MeshRenderer renderer, Material material)
        {
            renderer.sharedMaterial    = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows    = false;
            renderer.enabled           = false;
        }

        private static Vector3 Flatten(Vector3 direction)
        {
            direction.y = 0f;
            return (direction.sqrMagnitude > (MIN_SPAN * MIN_SPAN)) ? direction.normalized : Vector3.forward;
        }

        private void Place(Vector3 origin, Quaternion rotation, Vector3 scale)
        {
            _origin    = origin;
            _baseScale = scale;
            transform.SetPositionAndRotation(origin + (Vector3.up * GROUND_OFFSET), rotation);
            transform.localScale = scale;

            _isShown = true;
            _charge  = 0f;
            _baseRenderer.enabled = _mesh.vertexCount > 0; // 전장과 겹치지 않는 띠는 메시가 비어 그릴 것이 없다
            _fillRenderer.enabled = false;
            _fill.localScale      = Vector3.one;
            _baseColor            = Color.clear;
            _fillColor            = Color.clear;
            SetColor(_baseRenderer, Color.clear);
            SetColor(_fillRenderer, Color.clear);
        }

        private Vector3 FillScale(float charge)
        {
            float scale = Mathf.Max(MIN_SCALE, charge);
            switch (_kind)
            {
                case EBossGroundShapeKind.Rect:
                case EBossGroundShapeKind.ArenaBand:
                    return new Vector3(1f, 1f, scale);
                default:
                    return new Vector3(scale, 1f, scale);
            }
        }

        private void SetColor(MeshRenderer renderer, Color color)
        {
            renderer.GetPropertyBlock(_properties);
            _properties.SetColor(_colorId, color);
            renderer.SetPropertyBlock(_properties);
        }

        // 단위 반경 부채꼴, 앞(+Z)이 가운데이고 꼭짓점은 원점이다
        private void BuildSector(float arcDegrees)
        {
            bool isFull  = arcDegrees >= (FULL_CIRCLE - MIN_SPAN);
            int  steps   = isFull ? DISC_SEGMENTS : Mathf.Max(1, Mathf.CeilToInt(arcDegrees / SECTOR_STEP_DEGREES));
            float start  = isFull ? 0f : (-arcDegrees * HALF);
            float step   = (isFull ? FULL_CIRCLE : arcDegrees) / steps;
            int   ringCount = isFull ? steps : (steps + 1);

            Vector3[] vertices = new Vector3[1 + ringCount];
            int[]     indices  = new int[steps * 3];
            vertices[0] = Vector3.zero;
            for (int i = 0; i < ringCount; ++i)
            {
                float radians = (start + (step * i)) * Mathf.Deg2Rad;
                vertices[1 + i] = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
            }

            for (int i = 0; i < steps; ++i)
            {
                indices[i * 3]     = 0;
                indices[i * 3 + 1] = 1 + ((i + 1) % ringCount);
                indices[i * 3 + 2] = 1 + i;
            }

            _mesh.Clear();
            _mesh.vertices  = vertices;
            _mesh.triangles = indices;
            _mesh.RecalculateBounds();
        }

        // 시작점에서 전방 +Z로 1m, 폭 1m인 사각형이다. 실제 크기는 localScale이 정한다
        private void BuildRect()
        {
            _mesh.Clear();
            _mesh.vertices  = new[]
            {
                new Vector3(-HALF, 0f, 0f), new Vector3(HALF, 0f, 0f), new Vector3(-HALF, 0f, 1f), new Vector3(HALF, 0f, 1f)
            };
            _mesh.triangles = new[] { 0, 2, 3, 0, 3, 1 };
            _mesh.RecalculateBounds();
        }

        // 폭 띠의 양 변을 전장 안쪽으로 자르고 그 사이를 같은 간격으로 나눠 각 표본마다 전장 원과 만나는 앞뒤 두 점을 만든다. 띠가 전장과 겹치지 않으면 거짓이다
        private bool BuildBand(float lateralOffset)
        {
            float left  = lateralOffset - (_width * HALF);
            float right = lateralOffset + (_width * HALF);
            float from  = Mathf.Max(left, -_radius);
            float to    = Mathf.Min(right, _radius);

            _mesh.Clear();
            if (to <= from)
            {
                return false;
            }

            int sampleCount = BAND_SEGMENTS + 1;
            Vector3[] vertices = new Vector3[sampleCount * 2];
            int[]     indices  = new int[BAND_SEGMENTS * 6];
            for (int i = 0; i < sampleCount; ++i)
            {
                float x = Mathf.Lerp(from, to, (float)i / BAND_SEGMENTS);
                float z = Mathf.Sqrt(Mathf.Max(0f, (_radius * _radius) - (x * x)));
                vertices[i * 2]     = new Vector3(x, 0f, -z);
                vertices[i * 2 + 1] = new Vector3(x, 0f, z);
            }

            for (int i = 0; i < BAND_SEGMENTS; ++i)
            {
                int back = i * 2;
                int index = i * 6;
                indices[index]     = back;
                indices[index + 1] = back + 1;
                indices[index + 2] = back + 3;
                indices[index + 3] = back;
                indices[index + 4] = back + 3;
                indices[index + 5] = back + 2;
            }

            _mesh.vertices  = vertices;
            _mesh.triangles = indices;
            _mesh.RecalculateBounds();
            return true;
        }
    }
}
