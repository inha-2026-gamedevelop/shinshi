// Unity
using UnityEngine;
using UnityEngine.UI;

namespace Shinshi.UI
{
    /// <summary> 성장 창의 원형 눈금 장식. 상태가 바뀔 때만 UI 메시를 만든다 </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class EquipmentGrowthOrbit : MaskableGraphic
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int SEGMENTS = 128;
        private const int TICKS = 48;
        private const float OUTER_RADIUS = 0.48f;
        private const float INNER_RADIUS = 0.43f;
        private const float CORE_RADIUS = 0.39f;
        private const float LINE_WIDTH = 1.2f;
        private const float MAJOR_LENGTH = 12f;
        private const float MINOR_LENGTH = 5f;
        private const int MAJOR_STEP = 4;
        private const int GLOW_BANDS = 6;
        private const float GLOW_WIDTH = 16f;
        private const float DIAMOND_RADIUS = 7f;

        /****************************************
        *                Methods
        ****************************************/

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            Vector2 center = rect.center;
            float size = Mathf.Min(rect.width, rect.height);
            for (int i = GLOW_BANDS; i > 0; --i)
            {
                Color glow = color;
                glow.a *= 0.025f;
                Ring(mesh, center, size * INNER_RADIUS, GLOW_WIDTH * i / GLOW_BANDS, glow);
            }
            Color innerGlow = color;
            innerGlow.a *= 0.015f;
            Ring(mesh, center, size * 0.27f, size * 0.35f, innerGlow);
            Ring(mesh, center, size * OUTER_RADIUS, LINE_WIDTH, color);
            Ring(mesh, center, size * INNER_RADIUS, LINE_WIDTH * 2f, color * new Color(1f, 1f, 1f, 0.8f));
            Ring(mesh, center, size * CORE_RADIUS, LINE_WIDTH, color * new Color(1f, 1f, 1f, 0.25f));
            for (int i = 0; i < TICKS; ++i)
            {
                float angle = i * Mathf.PI * 2f / TICKS;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float length = ((i % MAJOR_STEP) == 0) ? MAJOR_LENGTH : MINOR_LENGTH;
                Vector2 end = center + direction * size * OUTER_RADIUS;
                Stroke(mesh, end, end - direction * length, LINE_WIDTH, color);
            }
            for (int i = 0; i < 4; ++i)
            {
                float angle = i * Mathf.PI * 0.5f;
                Vector2 position = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * size * OUTER_RADIUS;
                Diamond(mesh, position, DIAMOND_RADIUS, color);
                Diamond(mesh, position, DIAMOND_RADIUS * 0.45f, Color.Lerp(color, Color.white, 0.65f));
            }
        }

        private static void Ring(VertexHelper mesh, Vector2 center, float radius, float width, Color tint)
        {
            for (int i = 0; i < SEGMENTS; ++i)
            {
                float a = i * Mathf.PI * 2f / SEGMENTS;
                float b = (i + 1) * Mathf.PI * 2f / SEGMENTS;
                Vector2 first = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 second = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                int start = mesh.currentVertCount;
                mesh.AddVert(center + first * (radius - width * 0.5f), tint, Vector2.zero);
                mesh.AddVert(center + first * (radius + width * 0.5f), tint, Vector2.zero);
                mesh.AddVert(center + second * (radius + width * 0.5f), tint, Vector2.zero);
                mesh.AddVert(center + second * (radius - width * 0.5f), tint, Vector2.zero);
                mesh.AddTriangle(start, start + 1, start + 2);
                mesh.AddTriangle(start, start + 2, start + 3);
            }
        }

        private static void Stroke(VertexHelper mesh, Vector2 from, Vector2 to, float width, Color tint)
        {
            Vector2 direction = (to - from).normalized;
            Vector2 side = new Vector2(-direction.y, direction.x) * width * 0.5f;
            int start = mesh.currentVertCount;
            mesh.AddVert(from - side, tint, Vector2.zero);
            mesh.AddVert(from + side, tint, Vector2.zero);
            mesh.AddVert(to + side, tint, Vector2.zero);
            mesh.AddVert(to - side, tint, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }

        private static void Diamond(VertexHelper mesh, Vector2 position, float radius, Color tint)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(position + Vector2.up * radius, tint, Vector2.zero);
            mesh.AddVert(position + Vector2.right * radius, tint, Vector2.zero);
            mesh.AddVert(position + Vector2.down * radius, tint, Vector2.zero);
            mesh.AddVert(position + Vector2.left * radius, tint, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
