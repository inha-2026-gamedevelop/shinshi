#if UNITY_EDITOR
// System
using System;
using System.Collections.Generic;
using System.IO;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shinshi.UI.Editor
{
    /// <summary> 열려 있는 씬을 위에서 내려다본 실루엣 그림으로 찍어 미니맵 스프라이트와 지도 설정 에셋을 만든다 </summary>
    public static class MinimapCaptureTool
    {
        // 렌더러 재질과 레이어를 임시로 바꾸고 끝나면 되돌린다. 씬은 저장하지 않으므로 호출 전에 씬이 깨끗한지 확인한다.
        // 멀리서 보면 LODGroup이 낮은 단계를 골라 큰 건물이 사라지므로 찍는 동안 0단계로 고정한다

        /****************************************
        *                Fields
        ****************************************/

        public const string MAP_FOLDER  = "Assets/01.Scripts/05.Art/UI/HudMinimap/Maps";
        public const string DATA_FOLDER = "Assets/09.Data/UI/Minimap";

        private const string TEXTURE_PREFIX = "T_Minimap_";
        private const string DATA_PREFIX    = "MinimapMap_";
        private const string MENU_PATH      = "Shinshi/UI/Minimap/Capture Active Scene";

        private const int   MASK_LAYER      = 31;
        private const int   SUPERSAMPLE     = 2;
        private const int   DEPTH_BITS      = 24;
        private const float CAMERA_HEIGHT   = 600f;
        private const float CAMERA_NEAR     = 1f;
        private const float CAMERA_FAR      = 1500f;
        private const float PADDING_METERS  = 4f;
        private const float BYTE_MAX        = 255f;

        private static readonly EMinimapLayer[] G_Order = { EMinimapLayer.Rim, EMinimapLayer.Land, EMinimapLayer.Water, EMinimapLayer.Structure };

        private static readonly Color[] G_LayerColors =
        {
            new Color(0.30f, 0.34f, 0.38f),
            new Color(0.80f, 0.82f, 0.78f),
            new Color(0.42f, 0.62f, 0.76f),
            new Color(0.20f, 0.22f, 0.26f)
        };

        private static readonly float[] G_LayerAlphas = { 0.55f, 1f, 1f, 1f };

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem(MENU_PATH)]
        public static void CaptureActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            MinimapCaptureProfile profile = MinimapCaptureProfiles.Find(scene.name);
            if (profile == null)
            {
                Debug.LogError("[Minimap] 이 씬의 캡처 규칙이 없다: " + scene.name);
                return;
            }

            if (TryCapture(scene, profile, out string message))
            {
                Debug.Log("[Minimap] " + message);
                return;
            }

            Debug.LogError("[Minimap] 캡처 실패: " + message);
        }

        public static bool TryCapture(Scene scene, MinimapCaptureProfile profile, out string message)
        {
            if (profile == null)
            {
                message = "캡처 규칙이 없다";
                return false;
            }

            var layers = new Dictionary<EMinimapLayer, List<Renderer>>();
            for (int i = 0; i < G_Order.Length; ++i)
            {
                layers[G_Order[i]] = new List<Renderer>();
            }

            var lodGroups = new List<LODGroup>();
            Collect(scene, profile, layers, lodGroups);

            if (!TryComputeView(layers, out Vector2 center, out float halfExtent))
            {
                message = "땅, 물, 가장자리, 구조물 렌더러를 찾지 못했다";
                return false;
            }

            float[][] masks = RenderMasks(layers, lodGroups, center, halfExtent, profile.TextureSize);
            string texturePath = MAP_FOLDER + "/" + TEXTURE_PREFIX + profile.MapId + ".png";
            WriteTexture(masks, profile.TextureSize, texturePath);
            Sprite sprite = ImportSprite(texturePath);
            if (sprite == null)
            {
                message = "스프라이트를 불러오지 못했다: " + texturePath;
                return false;
            }

            string dataPath = DATA_FOLDER + "/" + DATA_PREFIX + profile.MapId + ".asset";
            WriteMapData(dataPath, profile, sprite, center, halfExtent);

            message = profile.MapId + " 캡처 완료 center=(" + center.x.ToString("F2") + ", " + center.y.ToString("F2") + ") half="
                + halfExtent.ToString("F2") + " 렌더러 " + CountText(layers);
            return true;
        }

        private static void Collect(Scene scene, MinimapCaptureProfile profile, Dictionary<EMinimapLayer, List<Renderer>> layers,
            List<LODGroup> lodGroups)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; ++r)
            {
                lodGroups.AddRange(roots[r].GetComponentsInChildren<LODGroup>(true));
                Renderer[] renderers = roots[r].GetComponentsInChildren<Renderer>(false);
                for (int i = 0; i < renderers.Length; ++i)
                {
                    Renderer renderer = renderers[i];
                    if (!IsCandidate(renderer))
                    {
                        continue;
                    }

                    string path = PathOf(renderer.transform);
                    if (IsLowLod(path))
                    {
                        continue;
                    }

                    for (int k = 0; k < profile.Rules.Count; ++k)
                    {
                        if (Matches(profile.Rules[k], path, renderer))
                        {
                            layers[profile.Rules[k].Layer].Add(renderer);
                            break;
                        }
                    }
                }
            }
        }

        private static bool IsCandidate(Renderer renderer)
        {
            if ((!renderer.enabled) || (renderer is ParticleSystemRenderer) || (renderer is TrailRenderer) || (renderer is LineRenderer))
            {
                return false;
            }

            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; ++i)
            {
                if ((materials[i] != null) && (materials[i].name.Contains("Waterfall")))
                {
                    return false;
                }
            }

            return true;
        }

        private static string PathOf(Transform transform)
        {
            var names = new List<string>();
            for (Transform t = transform; t != null; t = t.parent)
            {
                names.Add(t.name);
            }

            names.Reverse();
            return string.Join("/", names);
        }

        private static bool IsLowLod(string path)
        {
            string[] segments = path.Split('/');
            for (int i = 0; i < segments.Length; ++i)
            {
                if ((segments[i].StartsWith("LOD1", StringComparison.Ordinal)) || (segments[i].StartsWith("LOD2", StringComparison.Ordinal))
                    || (segments[i] == "LOD_Medium") || (segments[i] == "LOD_Low"))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Matches(MinimapCaptureRule rule, string path, Renderer renderer)
        {
            if (!path.StartsWith(rule.PathPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            if ((rule.PathTokens.Length > 0) && (!ContainsAny(path, rule.PathTokens)))
            {
                return false;
            }

            if ((rule.SkipTokens.Length > 0) && (ContainsAny(path, rule.SkipTokens)))
            {
                return false;
            }

            if ((rule.MaterialTokens.Length > 0) && (!MaterialMatches(renderer, rule.MaterialTokens)))
            {
                return false;
            }

            Vector3 size = renderer.bounds.size;
            if ((rule.MinHeight > 0f) && (size.y < rule.MinHeight))
            {
                return false;
            }

            return (rule.MaxFootprint <= 0f) || ((size.x <= rule.MaxFootprint) && (size.z <= rule.MaxFootprint));
        }

        private static bool ContainsAny(string text, string[] tokens)
        {
            for (int i = 0; i < tokens.Length; ++i)
            {
                if (text.Contains(tokens[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MaterialMatches(Renderer renderer, string[] tokens)
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; ++i)
            {
                if (materials[i] == null)
                {
                    continue;
                }

                string shaderName = (materials[i].shader != null) ? materials[i].shader.name : string.Empty;
                if ((ContainsAny(materials[i].name, tokens)) || (ContainsAny(shaderName, tokens)))
                {
                    return true;
                }
            }

            return false;
        }

        // 모든 층의 합집합, 땅 가장자리에 걸친 건물이 잘리지 않게 구조물도 넣는다
        private static bool TryComputeView(Dictionary<EMinimapLayer, List<Renderer>> layers, out Vector2 center, out float halfExtent)
        {
            bool hasBounds = false;
            Bounds total   = default;
            for (int i = 0; i < G_Order.Length; ++i)
            {
                AddBounds(layers[G_Order[i]], ref total, ref hasBounds);
            }

            if (!hasBounds)
            {
                center     = Vector2.zero;
                halfExtent = 0f;
                return false;
            }

            center     = new Vector2(total.center.x, total.center.z);
            halfExtent = (Mathf.Max(total.size.x, total.size.z) * 0.5f) + PADDING_METERS;
            return true;
        }

        private static void AddBounds(List<Renderer> list, ref Bounds total, ref bool hasBounds)
        {
            for (int i = 0; i < list.Count; ++i)
            {
                if (!hasBounds)
                {
                    total     = list[i].bounds;
                    hasBounds = true;
                    continue;
                }

                total.Encapsulate(list[i].bounds);
            }
        }

        private static string CountText(Dictionary<EMinimapLayer, List<Renderer>> layers)
        {
            string text = string.Empty;
            for (int i = 0; i < G_Order.Length; ++i)
            {
                text += G_Order[i] + "=" + layers[G_Order[i]].Count + " ";
            }

            return text.TrimEnd();
        }

        private static float[][] RenderMasks(Dictionary<EMinimapLayer, List<Renderer>> layers, List<LODGroup> lodGroups, Vector2 center,
            float halfExtent, int size)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            var flat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            if (flat.HasProperty("_BaseColor"))
            {
                flat.SetColor("_BaseColor", Color.white);
            }

            if (flat.HasProperty("_Color"))
            {
                flat.SetColor("_Color", Color.white);
            }

            if (flat.HasProperty("_Cull"))
            {
                flat.SetFloat("_Cull", 0f);
            }

            var cameraObject = new GameObject("__MinimapCapture") { hideFlags = HideFlags.HideAndDontSave };
            Camera camera = cameraObject.AddComponent<Camera>();
            DisablePostProcessing(cameraObject);
            camera.orthographic     = true;
            camera.orthographicSize = halfExtent;
            camera.transform.position = new Vector3(center.x, CAMERA_HEIGHT, center.y);
            camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            camera.nearClipPlane = CAMERA_NEAR;
            camera.farClipPlane  = CAMERA_FAR;
            camera.clearFlags      = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask     = 1 << MASK_LAYER;
            camera.allowHDR        = false;
            camera.allowMSAA       = false;

            int big = size * SUPERSAMPLE;
            var target = new RenderTexture(big, big, DEPTH_BITS, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;

            var masks = new float[G_Order.Length][];
            RenderTexture previous = RenderTexture.active;
            for (int i = 0; i < lodGroups.Count; ++i)
            {
                lodGroups[i].ForceLOD(0);
            }

            // 600미터 위에서 보면 밤 안개가 흰색을 어둡게 덮어 덮임 정도가 틀어진다
            bool fogWasOn = RenderSettings.fog;
            RenderSettings.fog = false;

            try
            {
                for (int i = 0; i < G_Order.Length; ++i)
                {
                    masks[i] = RenderLayer(layers[G_Order[i]], camera, target, flat, size);
                }
            }
            finally
            {
                RenderSettings.fog = fogWasOn;
                for (int i = 0; i < lodGroups.Count; ++i)
                {
                    lodGroups[i].ForceLOD(-1);
                }

                RenderTexture.active = previous;
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(flat);
            }

            return masks;
        }

        // 기본 에디터 어셈블리는 URP 어셈블리를 참조하지 못해 타입 이름으로 찾는다
        private static void DisablePostProcessing(GameObject cameraObject)
        {
            Type type = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (type == null)
            {
                return;
            }

            Component extra = cameraObject.AddComponent(type);
            System.Reflection.PropertyInfo property = type.GetProperty("renderPostProcessing");
            if (property != null)
            {
                property.SetValue(extra, false, null);
            }
        }

        private static float[] RenderLayer(List<Renderer> list, Camera camera, RenderTexture target, Material flat, int size)
        {
            var mask = new float[size * size];
            if (list.Count == 0)
            {
                return mask;
            }

            var originalMaterials = new Material[list.Count][];
            var originalLayers    = new int[list.Count];
            for (int i = 0; i < list.Count; ++i)
            {
                originalMaterials[i] = list[i].sharedMaterials;
                originalLayers[i]    = list[i].gameObject.layer;
                var flatMaterials = new Material[originalMaterials[i].Length];
                for (int k = 0; k < flatMaterials.Length; ++k)
                {
                    flatMaterials[k] = flat;
                }

                list[i].sharedMaterials = flatMaterials;
                list[i].gameObject.layer = MASK_LAYER;
            }

            camera.Render();
            RenderTexture.active = target;
            int big = size * SUPERSAMPLE;
            var texture = new Texture2D(big, big, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, big, big), 0, 0);
            Color32[] pixels = texture.GetPixels32();
            UnityEngine.Object.DestroyImmediate(texture);

            // 검은 배경 위의 흰색 면적 비율이 곧 덮임 정도다
            float divisor = BYTE_MAX * SUPERSAMPLE * SUPERSAMPLE;
            for (int y = 0; y < size; ++y)
            {
                for (int x = 0; x < size; ++x)
                {
                    int sum = 0;
                    for (int dy = 0; dy < SUPERSAMPLE; ++dy)
                    {
                        for (int dx = 0; dx < SUPERSAMPLE; ++dx)
                        {
                            sum += pixels[(((y * SUPERSAMPLE) + dy) * big) + ((x * SUPERSAMPLE) + dx)].r;
                        }
                    }

                    mask[(y * size) + x] = sum / divisor;
                }
            }

            for (int i = 0; i < list.Count; ++i)
            {
                list[i].sharedMaterials = originalMaterials[i];
                list[i].gameObject.layer = originalLayers[i];
            }

            RenderTexture.active = null;
            return mask;
        }

        // 층을 순서대로 겹친 한 장의 색 그림, 투명한 곳은 지도 밖이다
        private static void WriteTexture(float[][] masks, int size, string assetPath)
        {
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; ++i)
            {
                float outAlpha = 0f;
                float outR     = 0f;
                float outG     = 0f;
                float outB     = 0f;
                for (int l = 0; l < G_Order.Length; ++l)
                {
                    float alpha = masks[l][i] * G_LayerAlphas[l];
                    if (alpha <= 0f)
                    {
                        continue;
                    }

                    float merged = alpha + (outAlpha * (1f - alpha));
                    float keep   = outAlpha * (1f - alpha);
                    outR     = ((G_LayerColors[l].r * alpha) + (outR * keep)) / merged;
                    outG     = ((G_LayerColors[l].g * alpha) + (outG * keep)) / merged;
                    outB     = ((G_LayerColors[l].b * alpha) + (outB * keep)) / merged;
                    outAlpha = merged;
                }

                pixels[i] = new Color32((byte)(outR * BYTE_MAX), (byte)(outG * BYTE_MAX), (byte)(outB * BYTE_MAX), (byte)(outAlpha * BYTE_MAX));
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string fullPath    = Path.Combine(projectRoot, assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllBytes(fullPath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        private static Sprite ImportSprite(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return null;
            }

            importer.textureType         = TextureImporterType.Sprite;
            importer.spriteImportMode    = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled       = false;
            importer.filterMode          = FilterMode.Bilinear;
            importer.wrapMode            = TextureWrapMode.Clamp;
            importer.maxTextureSize      = 2048;
            importer.textureCompression  = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        private static void WriteMapData(string assetPath, MinimapCaptureProfile profile, Sprite sprite, Vector2 center, float halfExtent)
        {
            EnsureFolder(DATA_FOLDER);
            var data = AssetDatabase.LoadAssetAtPath<MinimapMapSO>(assetPath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<MinimapMapSO>();
                AssetDatabase.CreateAsset(data, assetPath);
            }

            var serialized = new SerializedObject(data);
            serialized.FindProperty("_mapId").stringValue         = profile.MapId;
            serialized.FindProperty("_displayName").stringValue   = profile.DisplayName;
            serialized.FindProperty("_sprite").objectReferenceValue = sprite;
            serialized.FindProperty("_worldCenter").vector2Value  = center;
            serialized.FindProperty("_worldHalfExtent").floatValue = halfExtent;
            serialized.FindProperty("_viewRadiusMeters").floatValue = profile.ViewRadius;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
#endif
