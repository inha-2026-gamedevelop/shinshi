// Unity
using UnityEngine;

namespace Shinshi.Art
{
    // 임시 표현이다. 원화와 애니메이션이 정해지기 전에 범위와 시간 흐름을 확인하는 최소 표현이며 최종 연출이 아니다
    // 별형 구조 전체(여우불 꼭짓점과 연결 빔)를 하나의 물체로 그리고, 회전은 서버가 계산한 각도를 받아 전체를 같이 돌린다. 메시는 한 번만 만들고 모양은 변하지 않는다
    // 빔은 두 꼭짓점 사이 직사각형이라 서버 판정(BossStarGeometry)과 같은 영역이다. 겹치는 곳은 더 밝게 보이지만 피해는 한 번이다
    // 스스로 시간을 세지 않는다. 서버 시각으로 계산한 값을 매 프레임 받아 그리므로 늦게 받은 활성도 이어서 그릴 수 있다
    /// <summary> 별형 회전 빔 구조 하나를 그린다, 피해 판정과 네트워크는 다루지 않는다 </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class StarGroundVFXController : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int    MAX_VERTICES      = 8;
        private const int    VERTICES_PER_BEAM = 4;
        private const int    INDICES_PER_BEAM  = 6;
        private const float  GROUND_OFFSET     = 0.03f;
        private const float  ORB_HEIGHT        = 1.2f;  // m, 바닥 위 여우불 높이
        private const float  ORB_SIZE          = 0.7f;  // m, 여우불 지름
        private const float  ORB_PULSE         = 0.5f;  // 예고 중 방향을 알리는 여우불 맥동의 크기 비율
        private const float  WAVE_SPEED        = 0.8f;  // 바퀴/초, 예고 중 맥동이 도는 속도
        private const float  FULL_CIRCLE       = 360f;
        private const float  HALF              = 0.5f;
        private const float  MIN_SPAN          = 0.01f;
        private const string MESH_NAME         = "StarGroundVFX Runtime Mesh";
        private const string ORB_NAME          = "StarGroundVFX Orb";

        private static readonly int _stateId = Shader.PropertyToID("_State");
        private static readonly int _sizeId  = Shader.PropertyToID("_Size");

        /****************************************
        *                Fields
        ****************************************/

        private Mesh                  _mesh;
        private MeshRenderer          _surface;
        private MaterialPropertyBlock _properties;
        private Transform[]           _orbs;
        private Vector3[]             _orbLocal;
        private readonly Vector3[]    _vertices = new Vector3[MAX_VERTICES * VERTICES_PER_BEAM];
        private readonly Vector2[]    _uvs      = new Vector2[MAX_VERTICES * VERTICES_PER_BEAM];
        private readonly Vector3[]    _normals  = new Vector3[MAX_VERTICES * VERTICES_PER_BEAM];
        private readonly int[]        _indices  = new int[MAX_VERTICES * INDICES_PER_BEAM];

        private bool       _initialized;
        private bool       _isShown;
        private int        _vertexCount;
        private float      _width;
        private float      _spinSign;
        private Quaternion _baseRotation;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsShown => _isShown;

        public int VertexCount => _vertexCount;

        public float BeamWidth => _width;

        /// <summary> 가장 최근에 그린 회전각(도), 시험과 진단용이다 </summary>
        public float RotationDegrees { get; private set; }

        /// <summary> 여우불 꼭짓점의 현재 월드 위치, 시험과 진단용이다 </summary>
        public Vector3 GetOrbPosition(int index)
        {
            return _orbs[index].position;
        }

        /// <summary> 보이는 여우불 수 </summary>
        public int ShownOrbCount
        {
            get
            {
                int count = 0;
                for (int i = 0; (_orbs != null) && (i < _orbs.Length); ++i)
                {
                    count += _orbs[i].gameObject.activeSelf ? 1 : 0;
                }

                return count;
            }
        }

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

        /// <summary> 재질을 정하고 여우불 물체를 미리 만든다, 한 번만 부른다. 재질이 비어 있으면 기본 재질로 보인다 </summary>
        public void Initialize(Material beamMaterial, Material orbMaterial)
        {
            if (_initialized)
            {
                return;
            }

            _surface    = GetComponent<MeshRenderer>();
            _properties = new MaterialPropertyBlock();
            _mesh       = new Mesh { name = MESH_NAME };
            GetComponent<MeshFilter>().sharedMesh = _mesh;

            if (beamMaterial != null)
            {
                _surface.sharedMaterial = beamMaterial;
            }

            _surface.enabled = false;

            _orbs     = new Transform[MAX_VERTICES];
            _orbLocal = new Vector3[MAX_VERTICES];
            for (int i = 0; i < MAX_VERTICES; ++i)
            {
                GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                orb.name = ORB_NAME;
                if (orb.TryGetComponent(out Collider collider))
                {
                    Destroy(collider);
                }

                if ((orbMaterial != null) && orb.TryGetComponent(out Renderer renderer))
                {
                    renderer.sharedMaterial = orbMaterial;
                }

                _orbs[i] = orb.transform;
                _orbs[i].SetParent(transform, false);
                _orbs[i].localScale = Vector3.one * ORB_SIZE;
                orb.SetActive(false);
            }

            _initialized = true;
        }

        // 오브젝트를 전장 중심에 놓고 기준 방향을 앞(+Z)으로 돌린다. 꼭짓점 k는 지역 좌표에서 기준 방향으로부터 오른쪽으로 k x (360 / 개수)도에 있다
        /// <summary> 배치로 켠다, 이전 재생의 상태는 모두 지운다 </summary>
        public void Show(Vector3 arenaCenter, Vector3 baseDirection, float ringRadius, int vertexCount, int connectStep,
            float width, bool rotatesRight)
        {
            baseDirection.y = 0f;
            _baseRotation = (baseDirection.sqrMagnitude > (MIN_SPAN * MIN_SPAN))
                ? Quaternion.LookRotation(baseDirection.normalized, Vector3.up)
                : Quaternion.identity;
            transform.SetPositionAndRotation(arenaCenter, _baseRotation);
            transform.localScale = Vector3.one;

            _width    = Mathf.Max(MIN_SPAN, width);
            _spinSign = rotatesRight ? 1f : -1f;
            int count = Mathf.Clamp(vertexCount, 0, MAX_VERTICES);
            BuildMesh(count, connectStep, ringRadius);

            for (int i = 0; i < MAX_VERTICES; ++i)
            {
                _orbs[i].gameObject.SetActive(i < count);
                _orbs[i].localPosition = _orbLocal[i];
            }

            _isShown = true;
            _surface.enabled = count > 0;
            Render(false, 0f, float.MaxValue, 1f, 0f, 0.0);
        }

        // mode 거짓은 예고, 참은 활성이다. progress는 예고면 진행 비율, 활성이면 남은 수명 1~0, pulseAge는 마지막 서버 타격 뒤 흐른 시간, fade는 1(보임)~0(사라짐)이다
        // rotationDegrees는 서버 시각으로 계산한 현재 회전각이다. time은 예고 중 여우불 맥동이 방향을 알리는 데 쓰는 서버 시각이다
        /// <summary> 한 프레임을 그린다 </summary>
        public void Render(bool isActive, float progress, float pulseAge, float fade, float rotationDegrees, double time)
        {
            if (!_isShown)
            {
                return;
            }

            RotationDegrees = rotationDegrees;
            transform.rotation = Quaternion.AngleAxis(rotationDegrees, Vector3.up) * _baseRotation;

            _surface.GetPropertyBlock(_properties);
            _properties.SetVector(_stateId, new Vector4(isActive ? 1f : 0f, Mathf.Clamp01(progress), pulseAge, Mathf.Clamp01(fade)));
            _properties.SetVector(_sizeId, new Vector4(_width, GROUND_OFFSET, 0f, 0f));
            _surface.SetPropertyBlock(_properties);

            // 예고 중에는 여우불이 회전 방향으로 차례로 커졌다 작아져 어느 쪽으로 도는지 알린다. 위치는 바꾸지 않는다
            for (int i = 0; i < _vertexCount; ++i)
            {
                float scale = 1f;
                if (!isActive)
                {
                    float phase = (float)(time * WAVE_SPEED) - (_spinSign * i / _vertexCount);
                    scale = 1f + (ORB_PULSE * Mathf.Max(0f, Mathf.Cos(phase * FULL_CIRCLE * Mathf.Deg2Rad)));
                }

                _orbs[i].localScale = Vector3.one * (ORB_SIZE * scale * Mathf.Max(HALF, fade));
            }
        }

        /// <summary> 즉시 화면을 지운다, 풀로 돌려보내기 전과 비활성화 때 부른다 </summary>
        public void Hide()
        {
            _isShown = false;

            if (_surface != null)
            {
                _surface.enabled = false;
            }

            for (int i = 0; (_orbs != null) && (i < _orbs.Length); ++i)
            {
                _orbs[i].gameObject.SetActive(false);
            }
        }

        private void BuildMesh(int count, int connectStep, float ringRadius)
        {
            _vertexCount = count;
            _mesh.Clear();
            if (count <= 0)
            {
                return;
            }

            float half = _width * HALF;
            for (int i = 0; i < count; ++i)
            {
                _orbLocal[i] = VertexLocal(i, count, ringRadius) + (Vector3.up * ORB_HEIGHT);
            }

            for (int beam = 0; beam < count; ++beam)
            {
                Vector3 start = VertexLocal(beam, count, ringRadius);
                Vector3 end   = VertexLocal((beam + connectStep) % count, count, ringRadius);
                Vector3 axis  = end - start;
                Vector3 unit  = axis.normalized;
                Vector3 side  = new Vector3(-unit.z, 0f, unit.x) * half;

                int v = beam * VERTICES_PER_BEAM;
                _vertices[v]     = start - side;
                _vertices[v + 1] = start + side;
                _vertices[v + 2] = end - side;
                _vertices[v + 3] = end + side;
                _uvs[v]          = new Vector2(0f, 0f);
                _uvs[v + 1]      = new Vector2(1f, 0f);
                _uvs[v + 2]      = new Vector2(0f, 1f);
                _uvs[v + 3]      = new Vector2(1f, 1f);
                for (int k = 0; k < VERTICES_PER_BEAM; ++k)
                {
                    _normals[v + k] = Vector3.up;
                }

                int i0 = beam * INDICES_PER_BEAM;
                _indices[i0]     = v;
                _indices[i0 + 1] = v + 2;
                _indices[i0 + 2] = v + 3;
                _indices[i0 + 3] = v;
                _indices[i0 + 4] = v + 3;
                _indices[i0 + 5] = v + 1;
            }

            int vertexTotal = count * VERTICES_PER_BEAM;
            int indexTotal  = count * INDICES_PER_BEAM;
            Vector3[] vertices = new Vector3[vertexTotal];
            Vector2[] uvs      = new Vector2[vertexTotal];
            Vector3[] normals  = new Vector3[vertexTotal];
            int[]     indices  = new int[indexTotal];
            System.Array.Copy(_vertices, vertices, vertexTotal);
            System.Array.Copy(_uvs, uvs, vertexTotal);
            System.Array.Copy(_normals, normals, vertexTotal);
            System.Array.Copy(_indices, indices, indexTotal);

            _mesh.vertices  = vertices;
            _mesh.uv        = uvs;
            _mesh.normals   = normals;
            _mesh.triangles = indices;
            _mesh.RecalculateBounds();
        }

        private static Vector3 VertexLocal(int index, int count, float ringRadius)
        {
            float angle = index * (FULL_CIRCLE / count);
            return (Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward) * ringRadius;
        }
    }
}
