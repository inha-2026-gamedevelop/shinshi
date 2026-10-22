#if UNITY_EDITOR
// System
using System.Collections.Generic;
using System.IO;

// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Shinshi.Art.Editor
{
    /// <summary> 하나로 묶인 메시를 서로 떨어진 덩어리별로 쪼개 씬 오브젝트로 만든다 </summary>
    public static class MeshIslandSplitter
    {
        // 폭포, 연못처럼 OBJ 하나에 월드 좌표로 구워진 메시는 모든 덩어리가 한 오브젝트라 따로 옮길 수 없다.
        // 정점 위치가 같으면 이어진 것으로 보고 덩어리를 찾고, 각 덩어리의 중심을 피벗으로 삼아 새 메시와 오브젝트를 만든다

        /****************************************
        *                Fields
        ****************************************/

        private const string MENU_PATH     = "Shinshi/Art/Environment/선택 메시 덩어리별 분리";
        private const string OUTPUT_FOLDER = "Assets/01.Scripts/05.Art/Environment/VillageCA07/Models/Split";
        private const float  KEY_SCALE     = 1000f; // 1mm 이내 정점은 같은 점으로 본다

        /****************************************
        *               Methods
        ****************************************/

        [MenuItem(MENU_PATH, true)]
        private static bool Validate()
        {
            return (Selection.activeGameObject != null) && (Selection.activeGameObject.GetComponent<MeshFilter>() != null);
        }

        [MenuItem(MENU_PATH)]
        private static void Split()
        {
            GameObject source = Selection.activeGameObject;
            var filter   = source.GetComponent<MeshFilter>();
            var renderer = source.GetComponent<MeshRenderer>();
            Mesh mesh    = filter.sharedMesh;
            if ((mesh == null) || (renderer == null))
            {
                Debug.LogError("[MeshIslandSplitter] MeshFilter와 MeshRenderer가 있는 오브젝트를 골라야 한다", source);
                return;
            }

            List<List<int>>[] islands = FindIslands(mesh);
            int count = islands[0].Count;
            if (count < 2)
            {
                Debug.LogWarning("[MeshIslandSplitter] 떨어진 덩어리가 하나뿐이라 나눌 게 없다", source);
                return;
            }

            Directory.CreateDirectory(OUTPUT_FOLDER);

            // 원본이 모델 프리팹 안쪽이면 그 바깥 루트와 같은 부모 아래에 둔다
            GameObject outer = PrefabUtility.GetOutermostPrefabInstanceRoot(source);
            Transform  host  = (outer != null) ? outer.transform.parent : source.transform.parent;

            var container = new GameObject(source.name + "_Split");
            Undo.RegisterCreatedObjectUndo(container, "메시 덩어리별 분리");
            container.transform.SetParent(host, false);
            container.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            container.transform.localScale = Vector3.one;

            string baseName = (outer != null) ? outer.name : source.name;
            for (int i = 0; i < count; ++i)
            {
                string name = $"{baseName}_{i + 1:00}";
                Mesh   part = BuildMesh(mesh, islands, i, source.transform, out Vector3 worldCenter);
                part.name   = name;

                string path = AssetDatabase.GenerateUniqueAssetPath($"{OUTPUT_FOLDER}/{name}.asset");
                AssetDatabase.CreateAsset(part, path);

                var go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, "메시 덩어리별 분리");
                go.layer = source.layer;
                GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(source));
                go.transform.SetParent(container.transform, false);
                go.transform.SetPositionAndRotation(worldCenter, source.transform.rotation);
                go.transform.localScale = source.transform.lossyScale;

                go.AddComponent<MeshFilter>().sharedMesh = part;
                var partRenderer = go.AddComponent<MeshRenderer>();
                partRenderer.sharedMaterials    = renderer.sharedMaterials;
                partRenderer.shadowCastingMode  = renderer.shadowCastingMode;
                partRenderer.receiveShadows     = renderer.receiveShadows;
                partRenderer.lightProbeUsage    = renderer.lightProbeUsage;
                partRenderer.reflectionProbeUsage = renderer.reflectionProbeUsage;
            }

            // 원본은 지우지 않고 꺼 둔다. 되돌리거나 비교할 때 쓴다
            Undo.RecordObject(source, "메시 덩어리별 분리");
            source.SetActive(false);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(source.scene);
            Selection.activeGameObject = container;
            Debug.Log($"[MeshIslandSplitter] {count}개로 나눴다. 원본은 비활성화했고 메시는 {OUTPUT_FOLDER} 에 저장했다", container);
        }

        // 서브메시마다 삼각형을 덩어리별로 모아 돌려준다. 반환 배열 [서브메시][덩어리] = 삼각형 인덱스 3개씩 나열
        private static List<List<int>>[] FindIslands(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            var       parent   = new int[vertices.Length];
            for (int i = 0; i < parent.Length; ++i)
            {
                parent[i] = i;
            }

            var firstAt = new Dictionary<Vector3Int, int>(vertices.Length);
            for (int i = 0; i < vertices.Length; ++i)
            {
                Vector3Int key = Vector3Int.RoundToInt(vertices[i] * KEY_SCALE);
                if (firstAt.TryGetValue(key, out int first))
                {
                    Union(parent, i, first);
                }
                else
                {
                    firstAt.Add(key, i);
                }
            }

            for (int sub = 0; sub < mesh.subMeshCount; ++sub)
            {
                int[] tris = mesh.GetTriangles(sub);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    Union(parent, tris[t], tris[t + 1]);
                    Union(parent, tris[t], tris[t + 2]);
                }
            }

            // 덩어리 번호는 처음 만난 순서로 매긴다
            var order = new Dictionary<int, int>();
            var result = new List<List<int>>[mesh.subMeshCount];
            for (int sub = 0; sub < mesh.subMeshCount; ++sub)
            {
                result[sub] = new List<List<int>>();
                int[] tris  = mesh.GetTriangles(sub);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    int root = Find(parent, tris[t]);
                    if (!order.TryGetValue(root, out int index))
                    {
                        index = order.Count;
                        order.Add(root, index);
                    }

                    while (result[sub].Count <= index)
                    {
                        result[sub].Add(new List<int>());
                    }

                    result[sub][index].Add(tris[t]);
                    result[sub][index].Add(tris[t + 1]);
                    result[sub][index].Add(tris[t + 2]);
                }
            }

            // 모든 서브메시가 같은 덩어리 수를 갖게 맞춘다
            int count = order.Count;
            for (int sub = 0; sub < result.Length; ++sub)
            {
                while (result[sub].Count < count)
                {
                    result[sub].Add(new List<int>());
                }
            }

            return result;
        }

        private static Mesh BuildMesh(Mesh source, List<List<int>>[] islands, int island, Transform sourceTransform,
            out Vector3 worldCenter)
        {
            Vector3[] vertices = source.vertices;
            Vector3[] normals  = source.normals;
            Vector4[] tangents = source.tangents;
            Vector2[] uv0      = source.uv;
            Vector2[] uv1      = source.uv2;
            Color[]   colors   = source.colors;

            var remap  = new Dictionary<int, int>();
            var outV   = new List<Vector3>();
            var outN   = new List<Vector3>();
            var outT   = new List<Vector4>();
            var outUv0 = new List<Vector2>();
            var outUv1 = new List<Vector2>();
            var outC   = new List<Color>();

            var bounds    = new Bounds();
            bool hasBound = false;
            for (int sub = 0; sub < islands.Length; ++sub)
            {
                List<int> tris = islands[sub][island];
                for (int i = 0; i < tris.Count; ++i)
                {
                    int v = tris[i];
                    if (remap.ContainsKey(v))
                    {
                        continue;
                    }

                    remap.Add(v, outV.Count);
                    outV.Add(vertices[v]);
                    if (normals.Length  == vertices.Length) { outN.Add(normals[v]);   }
                    if (tangents.Length == vertices.Length) { outT.Add(tangents[v]);  }
                    if (uv0.Length      == vertices.Length) { outUv0.Add(uv0[v]);     }
                    if (uv1.Length      == vertices.Length) { outUv1.Add(uv1[v]);     }
                    if (colors.Length   == vertices.Length) { outC.Add(colors[v]);    }

                    if (hasBound)
                    {
                        bounds.Encapsulate(vertices[v]);
                    }
                    else
                    {
                        bounds   = new Bounds(vertices[v], Vector3.zero);
                        hasBound = true;
                    }
                }
            }

            // 피벗은 xz는 덩어리 중심, y는 바닥에 둬서 높이를 맞추기 쉽게 한다
            Vector3 pivotLocal = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            for (int i = 0; i < outV.Count; ++i)
            {
                outV[i] -= pivotLocal;
            }

            worldCenter = sourceTransform.TransformPoint(pivotLocal);

            var mesh = new Mesh { indexFormat = (outV.Count > 65000) ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(outV);
            if (outN.Count   > 0) { mesh.SetNormals(outN);    }
            if (outT.Count   > 0) { mesh.SetTangents(outT);   }
            if (outUv0.Count > 0) { mesh.SetUVs(0, outUv0);   }
            if (outUv1.Count > 0) { mesh.SetUVs(1, outUv1);   }
            if (outC.Count   > 0) { mesh.SetColors(outC);     }

            mesh.subMeshCount = islands.Length;
            for (int sub = 0; sub < islands.Length; ++sub)
            {
                List<int> tris = islands[sub][island];
                var       mapped = new int[tris.Count];
                for (int i = 0; i < mapped.Length; ++i)
                {
                    mapped[i] = remap[tris[i]];
                }

                mesh.SetTriangles(mapped, sub);
            }

            mesh.RecalculateBounds();
            return mesh;
        }

        private static int Find(int[] parent, int a)
        {
            while (parent[a] != a)
            {
                parent[a] = parent[parent[a]];
                a         = parent[a];
            }

            return a;
        }

        private static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a);
            int rb = Find(parent, b);
            if (ra != rb)
            {
                parent[ra] = rb;
            }
        }
    }
}
#endif
