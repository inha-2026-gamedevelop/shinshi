// Unity
using UnityEngine;
using UnityEngine.UI;

namespace Shinshi.UI
{
    /// <summary> 기본 UI 이미지의 메시를 둥근 금속 테두리와 수직 음영으로 만든다 </summary>
    [RequireComponent(typeof(Image))]
    public sealed class EquipmentGrowthSurface : BaseMeshEffect
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int GRADIENT_ROWS = 64;
        private const float SHADOW_OFFSET = 3f;
        private const float BORDER_INSET = 1f;
        private const float FACE_INSET = 2f;
        private const float DEFAULT_RADIUS = 10f;

        [SerializeField] private float _radius = DEFAULT_RADIUS;
        [SerializeField] private bool _windowFrame;

        /****************************************
        *                Methods
        ****************************************/

        public void SetWindowFrame()
        {
            _windowFrame = true;
            graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive())
            {
                return;
            }

            Rect rect = graphic.rectTransform.rect;
            if ((rect.width <= 0f) || (rect.height <= 0f))
            {
                return;
            }

            mesh.Clear();
            Color tint = graphic.color;
            // Image와 같은 CanvasRenderer를 써서 Button의 상태 전환이 테두리와 바탕에도 적용된다.
            Color edgeTop = Color.Lerp(tint, new Color(0.40f, 0.62f, 0.76f, tint.a), 0.45f);
            Color edgeBottom = Color.Lerp(tint, new Color(0.22f, 0.36f, 0.47f, tint.a), 0.7f);
            Rect shadow = rect;
            shadow.y -= SHADOW_OFFSET;
            Fill(mesh, Grow(shadow, 4f), _radius + 4f, new Color(0f, 0f, 0f, 0.08f), new Color(0f, 0f, 0f, 0.18f));
            Fill(mesh, Grow(shadow, 2f), _radius + 2f, new Color(0f, 0f, 0f, 0.14f), new Color(0f, 0f, 0f, 0.3f));
            Fill(mesh, rect, _radius, edgeTop, edgeBottom);
            Fill(mesh, Grow(rect, -BORDER_INSET), _radius - BORDER_INSET,
                Color.Lerp(tint, Color.black, 0.58f), Color.Lerp(tint, Color.black, 0.72f));
            bool buttonFace = (tint.r > 0.95f) && (tint.g > 0.95f) && (tint.b > 0.95f);
            Color faceTop = buttonFace ? tint : Color.Lerp(tint, new Color(0.10f, 0.23f, 0.32f, tint.a), 0.18f);
            Color faceBottom = Color.Lerp(tint, Color.black, buttonFace ? 0.36f : 0.18f);
            Fill(mesh, Grow(rect, -FACE_INSET), _radius - FACE_INSET, faceTop, faceBottom);
            if (_windowFrame)
            {
                Fill(mesh, Grow(rect, -6f), _radius - 2f, new Color(0.20f, 0.31f, 0.40f, 1f), new Color(0.09f, 0.17f, 0.23f, 1f));
                Fill(mesh, Grow(rect, -7f), _radius - 3f,
                    Color.Lerp(tint, new Color(0.08f, 0.16f, 0.22f, 1f), 0.5f), Color.Lerp(tint, Color.black, 0.2f));
            }
        }

        private static Rect Grow(Rect rect, float amount)
        {
            return new Rect(rect.x - amount, rect.y - amount, rect.width + amount * 2f, rect.height + amount * 2f);
        }

        private static void Fill(VertexHelper mesh, Rect rect, float radius, Color top, Color bottom)
        {
            radius = Mathf.Clamp(radius, 0f, Mathf.Min(rect.width, rect.height) * 0.5f);
            int start = mesh.currentVertCount;
            for (int row = 0; row <= GRADIENT_ROWS; ++row)
            {
                float ratio = 0.5f - Mathf.Cos(Mathf.PI * row / GRADIENT_ROWS) * 0.5f;
                float y = Mathf.Lerp(rect.yMin, rect.yMax, ratio);
                float edgeDistance = Mathf.Min(y - rect.yMin, rect.yMax - y);
                float curve = Mathf.Max(0f, radius - edgeDistance);
                float inset = radius - Mathf.Sqrt(Mathf.Max(0f, radius * radius - curve * curve));
                Color tint = Gradient(bottom, top, ratio);
                mesh.AddVert(new Vector2(rect.xMin + inset, y), tint, Vector2.zero);
                mesh.AddVert(new Vector2(rect.xMax - inset, y), tint, Vector2.zero);
            }
            for (int row = 0; row < GRADIENT_ROWS; ++row)
            {
                int index = start + row * 2;
                mesh.AddTriangle(index, index + 1, index + 3);
                mesh.AddTriangle(index, index + 3, index + 2);
            }
        }

        private static Color Gradient(Color bottom, Color top, float ratio)
        {
            // 선형 색 공간의 UI 셰이더와 같은 보간을 써서 중앙 삼각형 경계가 드러나지 않게 한다.
            return QualitySettings.activeColorSpace == ColorSpace.Linear
                ? Color.Lerp(bottom.linear, top.linear, ratio).gamma
                : Color.Lerp(bottom, top, ratio);
        }
    }
}
