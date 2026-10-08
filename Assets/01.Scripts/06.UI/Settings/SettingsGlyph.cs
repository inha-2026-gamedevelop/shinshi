// Unity
using UnityEngine;
using UnityEngine.UI;

namespace Shinshi.UI
{
    /// <summary>설정 창의 작은 기호를 해상도에 맞는 벡터 도형으로 그린다</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SettingsGlyph : MaskableGraphic
    {
        public enum EShape
        {
            Diamond,
            Chevron,
            Check,
            Close,
            Monitor,
            Speaker,
            Moon,
        }

        /****************************************
        *                Fields
        ****************************************/

        private const float STROKE = 0.075f;
        private const int ARC_SEGMENTS = 48;

        [SerializeField] private EShape _shape = EShape.Diamond;

        /****************************************
        *                Methods
        ****************************************/

        public void SetShape(EShape shape)
        {
            _shape = shape;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            switch (_shape)
            {
                case EShape.Diamond:
                    Quad(helper, new Vector2(0.5f, 0.1f), new Vector2(0.9f, 0.5f), new Vector2(0.5f, 0.9f), new Vector2(0.1f, 0.5f));
                    break;
                case EShape.Chevron:
                    Line(helper, 0.18f, 0.68f, 0.5f, 0.35f);
                    Line(helper, 0.5f, 0.35f, 0.82f, 0.68f);
                    break;
                case EShape.Check:
                    Line(helper, 0.18f, 0.47f, 0.4f, 0.25f);
                    Line(helper, 0.4f, 0.25f, 0.85f, 0.78f);
                    break;
                case EShape.Close:
                    Line(helper, 0.2f, 0.2f, 0.8f, 0.8f);
                    Line(helper, 0.2f, 0.8f, 0.8f, 0.2f);
                    break;
                case EShape.Monitor:
                    Line(helper, 0.12f, 0.35f, 0.88f, 0.35f);
                    Line(helper, 0.12f, 0.82f, 0.88f, 0.82f);
                    Line(helper, 0.12f, 0.35f, 0.12f, 0.82f);
                    Line(helper, 0.88f, 0.35f, 0.88f, 0.82f);
                    Line(helper, 0.5f, 0.35f, 0.5f, 0.16f);
                    Line(helper, 0.32f, 0.16f, 0.68f, 0.16f);
                    break;
                case EShape.Speaker:
                    Quad(helper, new Vector2(0.13f, 0.38f), new Vector2(0.13f, 0.62f), new Vector2(0.48f, 0.82f), new Vector2(0.48f, 0.18f));
                    Line(helper, 0.62f, 0.35f, 0.7f, 0.5f);
                    Line(helper, 0.7f, 0.5f, 0.62f, 0.65f);
                    Line(helper, 0.78f, 0.2f, 0.92f, 0.5f);
                    Line(helper, 0.92f, 0.5f, 0.78f, 0.8f);
                    break;
                case EShape.Moon:
                    for (int i = 0; i < ARC_SEGMENTS; ++i)
                    {
                        float from = (float)i / ARC_SEGMENTS;
                        float to = (float)(i + 1) / ARC_SEGMENTS;
                        float outerFrom = Mathf.Lerp(78.8f, 281.2f, from) * Mathf.Deg2Rad;
                        float outerTo = Mathf.Lerp(78.8f, 281.2f, to) * Mathf.Deg2Rad;
                        float innerFrom = Mathf.Lerp(101.2f, 258.8f, from) * Mathf.Deg2Rad;
                        float innerTo = Mathf.Lerp(101.2f, 258.8f, to) * Mathf.Deg2Rad;
                        Quad(helper,
                            new Vector2(0.5f + (Mathf.Cos(outerFrom) * 0.36f), 0.5f + (Mathf.Sin(outerFrom) * 0.36f)),
                            new Vector2(0.64f + (Mathf.Cos(innerFrom) * 0.36f), 0.5f + (Mathf.Sin(innerFrom) * 0.36f)),
                            new Vector2(0.64f + (Mathf.Cos(innerTo) * 0.36f), 0.5f + (Mathf.Sin(innerTo) * 0.36f)),
                            new Vector2(0.5f + (Mathf.Cos(outerTo) * 0.36f), 0.5f + (Mathf.Sin(outerTo) * 0.36f)));
                    }
                    break;
            }
        }

        private void Line(VertexHelper helper, float x1, float y1, float x2, float y2)
        {
            Vector2 start = new Vector2(x1, y1);
            Vector2 end = new Vector2(x2, y2);
            Vector2 direction = (end - start).normalized;
            Vector2 offset = new Vector2(-direction.y, direction.x) * (STROKE * 0.5f);
            Quad(helper, start - offset, start + offset, end + offset, end - offset);
        }

        private void Quad(VertexHelper helper, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            Rect rect = GetPixelAdjustedRect();
            int index = helper.currentVertCount;
            AddVertex(helper, rect, a);
            AddVertex(helper, rect, b);
            AddVertex(helper, rect, c);
            AddVertex(helper, rect, d);
            helper.AddTriangle(index, index + 1, index + 2);
            helper.AddTriangle(index + 2, index + 3, index);
        }

        private void AddVertex(VertexHelper helper, Rect rect, Vector2 point)
        {
            helper.AddVert(new Vector3(rect.xMin + (point.x * rect.width), rect.yMin + (point.y * rect.height)), color, Vector2.zero);
        }
    }
}
