// Unity
using UnityEngine;
using UnityEngine.UI;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary>수련 배분을 5축 오각형과 반투명 영역으로 표시한다</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TrainingRadarGraphic : MaskableGraphic
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int   AXIS_COUNT       = Constants.Combat.TRAINING_STAT_COUNT;
        private const int   GRID_RING_COUNT  = 5;
        private const int   NO_SELECTED_AXIS = -1;
        private const float FULL_CIRCLE      = 360f;
        private const float TOP_ANGLE        = 90f;
        private const float HALF             = 0.5f;
        private const float MAX_RADIUS_RATIO = 0.49f;
        private const float MAX_FILL_ALPHA   = 0.14f;
        private const float MIN_LINE_LENGTH  = 0.001f;
        private const float MIN_RADIUS_RATIO = 0.1f;
        private const int   THIRD_VERTEX     = 2;
        private const int   FOURTH_VERTEX    = 3;

        [Header("크기")]
        [Range(MIN_RADIUS_RATIO, MAX_RADIUS_RATIO)]
        [SerializeField] private float _radiusRatio      = 0.4f;
        [SerializeField] private float _gridThickness    = 1f;
        [SerializeField] private float _spokeThickness   = 0.8f;
        [SerializeField] private float _borderThickness  = 1.4f;
        [SerializeField] private float _valueThickness   = 2.5f;
        [SerializeField] private float _selectedVertSize = 6f;

        [Header("색")]
        [SerializeField] private Color _gridColor     = new Color(0.55f, 0.63f, 0.65f, 0.32f);
        [SerializeField] private Color _spokeColor    = new Color(0.55f, 0.63f, 0.65f, 0.28f);
        [SerializeField] private Color _borderColor   = new Color(0.68f, 0.6f, 0.43f, 0.75f);
        [SerializeField] private Color _fillColor     = new Color(0.26f, 0.62f, 0.58f, 0.14f);
        [SerializeField] private Color _valueColor    = new Color(0.44f, 0.84f, 0.74f, 1f);
        [SerializeField] private Color _selectedColor = new Color(0.84f, 0.75f, 0.57f, 1f);

        private readonly float[]   _values        = new float[AXIS_COUNT];
        private readonly Vector2[] _axisPoints    = new Vector2[AXIS_COUNT];
        private readonly Vector2[] _valuePoints   = new Vector2[AXIS_COUNT];
        private int                _selectedIndex = NO_SELECTED_AXIS;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>공격, 체력, 방어, 치명타, 기력 순서의 단계를 항목 상한에 맞춰 표시한다</summary>
        public void SetValues(int[] values, int cap, int selectedIndex)
        {
            int nextSelected = ((selectedIndex >= 0) && (selectedIndex < AXIS_COUNT))
                ? selectedIndex
                : NO_SELECTED_AXIS;
            bool hasChanged = _selectedIndex != nextSelected;
            _selectedIndex = nextSelected;

            for (int i = 0; i < AXIS_COUNT; ++i)
            {
                float normalized = 0f;
                if ((values != null) && (i < values.Length) && (cap > 0))
                {
                    normalized = (float)Mathf.Clamp(values[i], 0, cap) / cap;
                }

                if (_values[i] != normalized)
                {
                    _values[i] = normalized;
                    hasChanged = true;
                }
            }

            if (hasChanged)
            {
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            Rect rect = GetPixelAdjustedRect();
            float radius = Mathf.Min(rect.width, rect.height) * Mathf.Clamp(_radiusRatio, 0f, MAX_RADIUS_RATIO);
            if (radius <= 0f)
            {
                return;
            }

            Vector2 center = rect.center;
            for (int i = 0; i < AXIS_COUNT; ++i)
            {
                float angle = (TOP_ANGLE - (FULL_CIRCLE * i / AXIS_COUNT)) * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                _axisPoints[i] = center + (direction * radius);
                _valuePoints[i] = center + (direction * radius * _values[i]);
            }

            // 가운데 원화가 배분 영역 아래에 있어도 보이도록 채움의 불투명도를 제한한다
            Color fill = _fillColor;
            fill.a = Mathf.Clamp(fill.a, 0f, MAX_FILL_ALPHA);
            for (int i = 0; i < AXIS_COUNT; ++i)
            {
                int next = (i + 1) % AXIS_COUNT;
                AddTriangle(helper, center, _valuePoints[i], _valuePoints[next], fill);
            }

            for (int ring = 1; ring <= GRID_RING_COUNT; ++ring)
            {
                float fraction = (float)ring / (GRID_RING_COUNT + 1);
                for (int i = 0; i < AXIS_COUNT; ++i)
                {
                    int next = (i + 1) % AXIS_COUNT;
                    Vector2 start = center + ((_axisPoints[i] - center) * fraction);
                    Vector2 end = center + ((_axisPoints[next] - center) * fraction);
                    AddLine(helper, start, end, _gridThickness, _gridColor);
                }
            }

            for (int i = 0; i < AXIS_COUNT; ++i)
            {
                int next = (i + 1) % AXIS_COUNT;
                AddLine(helper, center, _axisPoints[i], _spokeThickness, _spokeColor);
                AddLine(helper, _axisPoints[i], _axisPoints[next], _borderThickness, _borderColor);
                AddLine(helper, _valuePoints[i], _valuePoints[next], _valueThickness, _valueColor);
            }

            if (_selectedIndex != NO_SELECTED_AXIS)
            {
                AddSelectedVertex(helper, _axisPoints[_selectedIndex]);
            }
        }

        private void AddSelectedVertex(VertexHelper helper, Vector2 center)
        {
            float halfSize = Mathf.Max(0f, _selectedVertSize) * HALF;
            AddQuad(helper,
                center + (Vector2.up * halfSize),
                center + (Vector2.right * halfSize),
                center + (Vector2.down * halfSize),
                center + (Vector2.left * halfSize),
                _selectedColor);
        }

        private void AddLine(VertexHelper helper, Vector2 start, Vector2 end, float thickness, Color lineColor)
        {
            Vector2 delta = end - start;
            float length = delta.magnitude;
            if ((length < MIN_LINE_LENGTH) || (thickness <= 0f))
            {
                return;
            }

            Vector2 offset = new Vector2(-delta.y, delta.x) * (thickness * HALF / length);
            AddQuad(helper, start - offset, start + offset, end + offset, end - offset, lineColor);
        }

        private void AddTriangle(VertexHelper helper, Vector2 a, Vector2 b, Vector2 c, Color triangleColor)
        {
            int first = helper.currentVertCount;
            AddVertex(helper, a, triangleColor);
            AddVertex(helper, b, triangleColor);
            AddVertex(helper, c, triangleColor);
            helper.AddTriangle(first, first + 1, first + THIRD_VERTEX);
        }

        private void AddQuad(VertexHelper helper, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color quadColor)
        {
            int first = helper.currentVertCount;
            AddVertex(helper, a, quadColor);
            AddVertex(helper, b, quadColor);
            AddVertex(helper, c, quadColor);
            AddVertex(helper, d, quadColor);
            helper.AddTriangle(first, first + 1, first + THIRD_VERTEX);
            helper.AddTriangle(first + THIRD_VERTEX, first + FOURTH_VERTEX, first);
        }

        private void AddVertex(VertexHelper helper, Vector2 point, Color vertexColor)
        {
            helper.AddVert(new Vector3(point.x, point.y, 0f), vertexColor * color, Vector2.zero);
        }
    }
}
