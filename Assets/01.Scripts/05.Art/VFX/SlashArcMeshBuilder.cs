// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 베기 호 메시를 만든다, U는 호 진행(0 시작, 1 끝)이고 V는 칼끝(0)에서 몸 쪽(1)이다 </summary>
    public static class SlashArcMeshBuilder
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    MIN_SEGMENTS  = 2;
        private const int    ROW_COUNT     = 2;
        private const int    TRIANGLE_STEP = 6;
        private const string MESH_NAME     = "SlashArc Runtime Mesh";

        /****************************************
        *                Methods
        ****************************************/

        // 각도는 정면(+Z) 기준이며 오른쪽(+X)이 양수다. startDegrees에서 endDegrees로 휘두르는 순서대로 U가 커진다
        /// <summary> 정면 기준 각도 구간과 바깥, 안쪽 반지름으로 XZ 평면의 호 띠를 만든다 </summary>
        public static Mesh Build(float startDegrees, float endDegrees, float outerRadius, float innerRadius, int segments)
        {
            int segmentCount = Mathf.Max(segments, MIN_SEGMENTS);
            int columnCount  = segmentCount + 1;

            Vector3[] vertices  = new Vector3[columnCount * ROW_COUNT];
            Vector2[] uvs       = new Vector2[columnCount * ROW_COUNT];
            int[]     triangles = new int[segmentCount * TRIANGLE_STEP];

            for (int i = 0; i < columnCount; ++i)
            {
                float   t       = (float)i / segmentCount;
                float   radians = Mathf.Lerp(startDegrees, endDegrees, t) * Mathf.Deg2Rad;
                Vector3 dir     = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));

                int outer = i * ROW_COUNT;
                int inner = outer + 1;
                vertices[outer] = dir * outerRadius;
                vertices[inner] = dir * innerRadius;
                uvs[outer]      = new Vector2(t, 0f);
                uvs[inner]      = new Vector2(t, 1f);
            }

            for (int i = 0; i < segmentCount; ++i)
            {
                int a   = i * ROW_COUNT;
                int b   = a + 1;
                int c   = a + ROW_COUNT;
                int d   = c + 1;
                int tri = i * TRIANGLE_STEP;
                triangles[tri]     = a;
                triangles[tri + 1] = c;
                triangles[tri + 2] = b;
                triangles[tri + 3] = b;
                triangles[tri + 4] = c;
                triangles[tri + 5] = d;
            }

            Mesh mesh = new Mesh
            {
                name = MESH_NAME
            };
            mesh.vertices  = vertices;
            mesh.uv        = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
