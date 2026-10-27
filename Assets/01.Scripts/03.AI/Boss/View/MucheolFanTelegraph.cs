// Unity
using UnityEngine;

namespace Shinshi.AI
{
    /// <summary> 무철 부채꼴 빨간 예고, 중심에서 바깥으로 차오르다 타격 순간 번쩍이고 사라진다 </summary>
    public sealed class MucheolFanTelegraph : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int   ARC_SEGMENTS     = 48;
        private const float OUTLINE_WIDTH    = 0.08f;
        private const float MIN_FILL_SECONDS = 0.1f;
        private const float MIN_RADIUS       = 0.1f;
        private const float MIN_ANGLE        = 1f;
        private const float MAX_ANGLE        = 359f;
        private const float OUTLINE_LIFT     = 0.01f;   // m, 면과 선이 겹쳐 깜빡이지 않게 선을 살짝 띄운다
        private static readonly int G_BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private MeshRenderer _area;      // 범위 전체를 옅게 깐 면
        [SerializeField] private MeshRenderer _fill;      // 중심에서 차오르는 면
        [SerializeField] private LineRenderer _outline;   // 범위 가장자리 선
        [SerializeField] private Color        _areaColor    = new Color(0.9f, 0.05f, 0.03f, 0.18f);
        [SerializeField] private Color        _fillColor    = new Color(0.95f, 0.08f, 0.04f, 0.45f);
        [SerializeField] private Color        _flashColor   = new Color(1f, 0.55f, 0.4f, 0.95f);
        [SerializeField] private Color        _outlineColor = new Color(1f, 0.15f, 0.08f, 0.9f);
        [SerializeField, Min(0.1f)] private float _radius       = 5f;
        [SerializeField, Range(1f, 359f)] private float _angle  = 120f;
        [SerializeField, Min(0.1f)] private float _fillSeconds  = 1.78f;   // 시작부터 타격까지
        [SerializeField, Min(0f)] private float _flashSeconds   = 0.08f;
        [SerializeField, Min(0f)] private float _fadeSeconds    = 0.25f;

        private MaterialPropertyBlock _block;
        private Mesh                  _mesh;
        private float                 _elapsed;
        private bool                  _isAuto = true;   // 시험 장면은 스스로 시간을 센다, 전투에서는 뷰가 서버 시계로 구동한다
        private bool                  _isBuilt;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 차오르기와 번쩍임, 사라짐이 모두 끝났는가 </summary>
        public bool IsFinished => _elapsed >= (_fillSeconds + _flashSeconds + _fadeSeconds);

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            Rebuild();
            _isBuilt = true;
        }

        private void OnEnable()
        {
            _elapsed = 0f;
            Apply();
        }

        private void Update()
        {
            if (!_isAuto)
            {
                return;
            }

            _elapsed += Time.unscaledDeltaTime;
            Apply();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 서버 시계로 구동하도록 바꾼다. 차오르는 시간은 서버가 알린 예고 시작부터 타격까지다 </summary>
        public void Configure(float fillSeconds)
        {
            _isAuto = false;
            _fillSeconds = Mathf.Max(fillSeconds, MIN_FILL_SECONDS);
        }

        /// <summary> 예고 시작부터 흐른 시간을 정한다. 차오르는 시간을 넘기면 번쩍이고 사라진다 </summary>
        public void SetElapsed(float seconds)
        {
            _elapsed = Mathf.Max(seconds, 0f);
            Apply();
        }

        /// <summary> 부채꼴의 사거리와 각도를 바꾼다. 연타는 단계마다 이 값이 달라서 서버 사건의 값을 그대로 넣는다 </summary>
        public void SetShape(float radius, float angle)
        {
            float newRadius = Mathf.Max(radius, MIN_RADIUS);
            float newAngle = Mathf.Clamp(angle, MIN_ANGLE, MAX_ANGLE);
            if (Mathf.Approximately(newRadius, _radius) && Mathf.Approximately(newAngle, _angle))
            {
                return;
            }

            _radius = newRadius;
            _angle = newAngle;
            if (_isBuilt)
            {
                Rebuild();
            }
        }

        // 메쉬는 이 인스턴스가 하나만 갖고 모양이 바뀔 때만 다시 만든다
        private void Rebuild()
        {
            if (_mesh != null)
            {
                Destroy(_mesh);
            }

            _mesh = BuildFanMesh();
            _area.GetComponent<MeshFilter>().sharedMesh = _mesh;
            _fill.GetComponent<MeshFilter>().sharedMesh = _mesh;
            BuildOutline();
        }

        private void OnDestroy()
        {
            if (_mesh != null)
            {
                Destroy(_mesh);
            }
        }

        /// <summary> 공격 범위 배율, 반경과 폭이 같은 비율로 커진다. 이지는 1이다 </summary>
        public void SetRangeScale(float scale)
        {
            transform.localScale = new Vector3(scale, 1f, scale);
        }

        private void Apply()
        {
            float flashEnd = _fillSeconds + _flashSeconds;
            float fadeEnd = flashEnd + _fadeSeconds;
            if (_elapsed >= fadeEnd)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);
            if (_elapsed < _fillSeconds)
            {
                float progress = _elapsed / _fillSeconds;
                _fill.transform.localScale = new Vector3(progress, 1f, progress);
                SetColor(_area, _areaColor);
                SetColor(_fill, _fillColor);
                _outline.startColor = _outlineColor;
                _outline.endColor = _outlineColor;
                return;
            }

            _fill.transform.localScale = Vector3.one;
            float fade = (_elapsed < flashEnd) ? 1f : 1f - ((_elapsed - flashEnd) / _fadeSeconds);
            SetColor(_area, WithAlpha(_areaColor, _areaColor.a * fade));
            SetColor(_fill, WithAlpha(_flashColor, _flashColor.a * fade));
            Color outline = WithAlpha(_outlineColor, _outlineColor.a * fade);
            _outline.startColor = outline;
            _outline.endColor = outline;
        }

        private void SetVisible(bool isVisible)
        {
            _area.enabled = isVisible;
            _fill.enabled = isVisible;
            _outline.enabled = isVisible;
        }

        private void SetColor(MeshRenderer target, Color color)
        {
            target.GetPropertyBlock(_block);
            _block.SetColor(G_BaseColorId, color);
            target.SetPropertyBlock(_block);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        // 중심(원점)에서 +Z 방향으로 열리는 부채꼴, 중심이 원점이라 크기를 줄이면 중심에서 차오른다
        private Mesh BuildFanMesh()
        {
            Vector3[] vertices = new Vector3[ARC_SEGMENTS + 2];
            int[] triangles = new int[ARC_SEGMENTS * 3];
            vertices[0] = Vector3.zero;
            float start = -_angle * 0.5f;
            for (int i = 0; i <= ARC_SEGMENTS; ++i)
            {
                float yaw = (start + (_angle * i / ARC_SEGMENTS)) * Mathf.Deg2Rad;
                vertices[i + 1] = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)) * _radius;
            }
            for (int i = 0; i < ARC_SEGMENTS; ++i)
            {
                triangles[i * 3]     = 0;
                triangles[i * 3 + 1] = i + 2;
                triangles[i * 3 + 2] = i + 1;
            }

            Mesh mesh = new Mesh { name = "MucheolFanTelegraph" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private void BuildOutline()
        {
            int count = ARC_SEGMENTS + 4;
            Vector3[] points = new Vector3[count];
            float start = -_angle * 0.5f;
            points[0] = Vector3.up * OUTLINE_LIFT;
            for (int i = 0; i <= ARC_SEGMENTS; ++i)
            {
                float yaw = (start + (_angle * i / ARC_SEGMENTS)) * Mathf.Deg2Rad;
                points[i + 1] = (new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)) * _radius) + (Vector3.up * OUTLINE_LIFT);
            }
            points[count - 2] = Vector3.up * OUTLINE_LIFT;
            points[count - 1] = points[1];
            _outline.useWorldSpace = false;
            _outline.loop = false;
            _outline.widthMultiplier = OUTLINE_WIDTH;
            _outline.alignment = LineAlignment.View;
            _outline.positionCount = count;
            _outline.SetPositions(points);
        }
    }
}
