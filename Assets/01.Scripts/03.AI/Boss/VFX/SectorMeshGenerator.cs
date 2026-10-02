// Unity
using UnityEngine;

namespace Shinshi.Art
{
    public static class SectorMeshGenerator
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int VERTICES_PER_STEP = 2;
        private const int INDICES_PER_STEP  = 6;

        /****************************************
        *                Methods
        ****************************************/

        // 로컬 +Z가 전방, UV.x는 좌에서 우, UV.y는 안쪽에서 바깥쪽
        public static void Rebuild(Mesh mesh, SectorShape shape)
        {
            shape.Clamp();

            int count = shape.Resolution;
            Vector3[] vertices = new Vector3[(count + 1) * VERTICES_PER_STEP];
            Vector2[] uv       = new Vector2[vertices.Length];
            Vector3[] normals  = new Vector3[vertices.Length];
            int[] indices      = new int[count * INDICES_PER_STEP];

            for (int i = 0; i <= count; ++i)
            {
                float t = (float)i / count;
                float a = (t - 0.5f) * shape.Angle * Mathf.Deg2Rad;
                Vector3 ray = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                int inner = i * VERTICES_PER_STEP;
                int outer = inner + 1;

                vertices[inner] = ray * shape.InnerRadius;
                vertices[outer] = ray * shape.Radius;
                uv[inner]       = new Vector2(t, 0f);
                uv[outer]       = new Vector2(t, 1f);
                normals[inner]  = Vector3.up;
                normals[outer]  = Vector3.up;

                if (i == count)
                {
                    continue;
                }

                int k = i * INDICES_PER_STEP;
                indices[k]     = inner;
                indices[k + 1] = outer;
                indices[k + 2] = outer + VERTICES_PER_STEP;
                indices[k + 3] = inner;
                indices[k + 4] = outer + VERTICES_PER_STEP;
                indices[k + 5] = inner + VERTICES_PER_STEP;
            }

            mesh.Clear();
            mesh.vertices  = vertices;
            mesh.uv        = uv;
            mesh.normals   = normals;
            mesh.triangles = indices;
            mesh.RecalculateBounds();
        }
    }
}
