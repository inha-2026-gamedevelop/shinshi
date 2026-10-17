// System
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shinshi.Editor
{
    /// <summary> 석주 옆에 걸린 천을 석재 표면에 맞춰 눌러 붙인 메시 에셋을 만든다 </summary>
    public static class MucheolPillarBannerFitter
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string FITTED_PATH = "Assets/01.Scripts/05.Art/Environment/MucheolPillar/Models/SM_MucheolPillarBanner_Fitted.asset";

        private const string HIGH_MODEL = "Assets/01.Scripts/05.Art/Environment/MucheolPillar/Models/SM_MucheolPillar_High.fbx";
        private const string BANNER_MARK = "RedBanner";

        private const float OFFSET_X = 0.24f;
        private const float OFFSET_Y = 0.16f;
        private const float OFFSET_Z = 0.71f;
        private const float YAW      = 290.52f;

        private const float MIN_GAP        = 0.02f;
        private const float MAX_GAP        = 0.07f;
        private const int   SMOOTH_PASSES  = 2;
        private const float RAY_START      = 8f;
        private const float RAY_LENGTH     = 8f;
        private const float MIN_RADIUS     = 0.001f;
        private const float FAR_OFFSET     = 2000f;

        /****************************************
        *               Methods
        ****************************************/

        /// <summary> 천 꼭짓점을 석주 좌표로 옮긴 뒤 표면과의 간격을 MIN_GAP에서 MAX_GAP 사이로 맞춘 메시를 에셋으로 쓴다 </summary>
        public static Mesh Fit()
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(HIGH_MODEL);
            MeshFilter stone = null;
            MeshFilter banner = null;
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.name.Contains(BANNER_MARK))
                {
                    banner = filter;
                }
                else
                {
                    stone = filter;
                }
            }

            Mesh stoneBaked = BakeToPillarSpace(stone, Matrix4x4.identity);
            Matrix4x4 placement = Matrix4x4.TRS(new Vector3(OFFSET_X, OFFSET_Y, OFFSET_Z), Quaternion.Euler(0f, YAW, 0f), Vector3.one);

            GameObject probe = new GameObject("MucheolPillarBannerFitProbe");
            probe.transform.position = new Vector3(FAR_OFFSET, 0f, FAR_OFFSET);
            probe.AddComponent<MeshCollider>().sharedMesh = stoneBaked;
            Physics.SyncTransforms();

            Mesh source = banner.sharedMesh;
            Matrix4x4 node = banner.transform.localToWorldMatrix;
            Vector3[] vertices = source.vertices;
            Vector3[] normals = source.normals;
            Matrix4x4 full = placement * node;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = full.MultiplyPoint3x4(vertices[i]);
                normals[i] = full.MultiplyVector(normals[i]).normalized;
            }

            float[] shift = new float[vertices.Length];
            Vector3[] radial = new Vector3[vertices.Length];
            Vector3 origin = probe.transform.position;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 flat = new Vector3(vertices[i].x, 0f, vertices[i].z);
                float radius = flat.magnitude;
                if (radius < MIN_RADIUS)
                {
                    continue;
                }

                radial[i] = flat / radius;
                Vector3 axis = new Vector3(origin.x, vertices[i].y, origin.z);
                if (Physics.Raycast(axis + radial[i] * RAY_START, -radial[i], out RaycastHit hit, RAY_LENGTH))
                {
                    float gap = radius - (hit.point - axis).magnitude;
                    shift[i] = Mathf.Clamp(gap, MIN_GAP, MAX_GAP) - gap;
                }
            }

            Smooth(source, shift);

            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] += radial[i] * shift[i];
            }

            // 평균을 낸 뒤에도 돌에 박힌 꼭짓점은 평활화 없이 최소 간격까지 밀어 낸다
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 axis = new Vector3(origin.x, vertices[i].y, origin.z);
                Vector3 flat = new Vector3(vertices[i].x, 0f, vertices[i].z);
                if (flat.sqrMagnitude < MIN_RADIUS * MIN_RADIUS)
                {
                    continue;
                }

                Vector3 direction = flat.normalized;
                if (Physics.Raycast(axis + direction * RAY_START, -direction, out RaycastHit hit, RAY_LENGTH))
                {
                    float gap = flat.magnitude - (hit.point - axis).magnitude;
                    if (gap < MIN_GAP)
                    {
                        vertices[i] += direction * (MIN_GAP - gap);
                    }
                }
            }

            List<Vector2> uvs = new List<Vector2>();
            source.GetUVs(0, uvs);

            Mesh fitted = new Mesh { name = "SM_MucheolPillarBanner_Fitted" };
            fitted.indexFormat = vertices.Length > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            fitted.SetVertices(vertices);
            fitted.SetNormals(normals);
            fitted.SetUVs(0, uvs);
            fitted.SetTriangles(source.triangles, 0);
            fitted.RecalculateBounds();

            Object.DestroyImmediate(probe);
            Object.DestroyImmediate(stoneBaked);

            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(FITTED_PATH);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(fitted, FITTED_PATH);
                return fitted;
            }

            EditorUtility.CopySerialized(fitted, existing);
            return existing;
        }

        /// <summary> 석주 메시를 석주 좌표계의 임시 메시로 굽는다 </summary>
        private static Mesh BakeToPillarSpace(MeshFilter filter, Matrix4x4 extra)
        {
            Matrix4x4 matrix = extra * filter.transform.localToWorldMatrix;
            Vector3[] vertices = filter.sharedMesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
            }

            Mesh baked = new Mesh { indexFormat = IndexFormat.UInt32 };
            baked.vertices = vertices;
            baked.triangles = filter.sharedMesh.triangles;
            return baked;
        }

        /// <summary> 이웃 꼭짓점과 평균을 내어 이동량이 표면 요철을 그대로 따라 울퉁불퉁해지지 않게 한다 </summary>
        private static void Smooth(Mesh mesh, float[] values)
        {
            int[] triangles = mesh.triangles;
            List<int>[] neighbours = new List<int>[values.Length];
            for (int i = 0; i < neighbours.Length; i++)
            {
                neighbours[i] = new List<int>();
            }

            for (int i = 0; i < triangles.Length; i += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    int a = triangles[i + k];
                    int b = triangles[i + (k + 1) % 3];
                    neighbours[a].Add(b);
                    neighbours[b].Add(a);
                }
            }

            float[] next = new float[values.Length];
            for (int pass = 0; pass < SMOOTH_PASSES; pass++)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    float sum = values[i];
                    for (int n = 0; n < neighbours[i].Count; n++)
                    {
                        sum += values[neighbours[i][n]];
                    }

                    next[i] = sum / (neighbours[i].Count + 1);
                }

                next.CopyTo(values, 0);
            }
        }
    }
}
