// System
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Dev;

namespace Shinshi.Editor
{
    /// <summary> Blender 봉인핵 원본을 독립 표현 에셋으로 가져온다 </summary>
    public static class WolbaekSealCoreBuilder
    {
        /****************************************
        *                Fields
        ****************************************/
        public const string ROOT                         = "Assets/01.Scripts/05.Art/VFX/Wolbaek/SealCore";
        public const string PREFABS                      = "Assets/02.Prefab/VFX/Wolbaek/SealCore";
        public const string EVIDENCE                     = ".claude/docs/2026-10-14/wolbaek-seal-core-blender";
        public const string SCENE                        = "Assets/00.Scenes/Dev_VFX/Dev_Wolbaek_SealCore.unity";
        private const string FLOOR                       = "Assets/02.Prefab/Environment/StoneCourtyard/PF_StoneCourtyardTile.prefab";
        private const string PARTICLE                    = "Shinshi/VFX/Particle";
        private const string SPECTRAL                    = "Shinshi/VFX/WolbaekSpectral";
        private const string LIT                         = "Universal Render Pipeline/Lit";
        private const string NOISE_NAME                  = "T_SealCore_CloudNoise";
        private const int NOISE_SIZE                     = 256;
        private const int NOISE_SEED                     = 1706;
        private const int NOISE_OCTAVES                  = 5;
        private const int NOISE_GRID_BASE                = 4;
        private const int NOISE_CHANNELS                 = 2;
        private const float NOISE_CONTRAST               = 1.8f;
        private const float NOISE_FALLOFF                = 0.62f;
        private const float SPECTRAL_EROSION             = 0.18f;
        private const float SPECTRAL_DISTORTION          = 0.09f;
        private const float SPECTRAL_SOFT_FADE           = 0.14f;
        private const float CORE_EROSION                 = 0.4f;
        private const float CORE_BRIGHTNESS              = 0.9f;
        private const float FLOW_BRIGHTNESS              = 3.5f;
        private const float FLOW_VEIL_OPACITY            = 0.24f;
        private const float FLOW_VEIL_WIDTH              = 0.24f;
        private const float FLAME_VEIL_OPACITY           = 0.24f;
        private const float FLAME_VEIL_WIDTH             = 0.09f;
        private const float FLOW_VEIL_PHASE_OFFSET       = 0.37f;
        private const float FLAME_VEIL_PHASE_OFFSET      = 0.41f;
        private const float FLAME_EROSION                = 0.26f;
        private const float FLAME_BRIGHTNESS             = 1.3f;
        private const float PREVIEW_FLOOR_TINT           = 0.4f;
        private const int CORE_COUNT                     = 2;
        private const float CORE_SPACING                 = 3f;
        private const float DECORATIVE_CULL              = 0.035f;
        private const int FLOOR_GRID_MIN                 = -2;
        private const int FLOOR_GRID_MAX                 = 2;
        private const float FLOOR_TILE_SPACING           = 6f;
        private const float FLOOR_HEIGHT                 = -0.03f;
        private const float KEY_LIGHT_RED                = 0.78f;
        private const float KEY_LIGHT_GREEN              = 0.87f;
        private const float KEY_LIGHT_BLUE               = 1f;
        private const float KEY_LIGHT_INTENSITY          = 1.3f;
        private const float KEY_LIGHT_PITCH              = 48f;
        private const float KEY_LIGHT_YAW                = -32f;
        private const float AMBIENT_SKY_RED              = 0.2f;
        private const float AMBIENT_SKY_GREEN            = 0.25f;
        private const float AMBIENT_SKY_BLUE             = 0.34f;
        private const float AMBIENT_EQUATOR_RED          = 0.12f;
        private const float AMBIENT_EQUATOR_GREEN        = 0.15f;
        private const float AMBIENT_EQUATOR_BLUE         = 0.2f;
        private const float AMBIENT_GROUND_RED           = 0.06f;
        private const float AMBIENT_GROUND_GREEN         = 0.08f;
        private const float AMBIENT_GROUND_BLUE          = 0.12f;
        private const float PREVIEW_CAMERA_X             = 8f;
        private const float PREVIEW_CAMERA_Y             = 11f;
        private const float PREVIEW_CAMERA_Z             = -16f;
        private const float PREVIEW_CAMERA_FOV           = 38f;
        private const float PREVIEW_NEAR_CLIP            = 0.1f;
        private const float PREVIEW_FAR_CLIP             = 100f;
        private const float PREVIEW_BACKGROUND_RED       = 0.01f;
        private const float PREVIEW_BACKGROUND_GREEN     = 0.015f;
        private const float PREVIEW_BACKGROUND_BLUE      = 0.025f;
        private const float PREVIEW_BLOOM_INTENSITY      = 0.65f;
        private const float PREVIEW_BLOOM_THRESHOLD      = 1.1f;
        private const float PREVIEW_BLOOM_SCATTER        = 0.65f;
        private const int VECTOR_COMPONENT_COUNT         = 3;
        private const int UV_COMPONENT_COUNT             = 2;
        private const int COLOR_COMPONENT_COUNT          = 4;
        private const int TRIANGLE_INDEX_COUNT           = 3;
        private const float MAX_MESH_ABSOLUTE_VALUE      = 1000f;
        private const int MAX_IMPORTED_TEXTURE_SIZE      = 2048;
        private const int SOLID_ROLE_COUNT               = 2;
        private const int PNG_HEADER_LENGTH              = 24;
        private const int PNG_SIGNATURE_BYTE_0           = 137;
        private const int PNG_SIGNATURE_BYTE_1           = 80;
        private const int PNG_SIGNATURE_BYTE_2           = 78;
        private const int PNG_SIGNATURE_BYTE_3           = 71;
        private const int PNG_WIDTH_HIGH_BYTE            = 16;
        private const int PNG_WIDTH_SECOND_BYTE          = 17;
        private const int PNG_WIDTH_THIRD_BYTE           = 18;
        private const int PNG_WIDTH_LOW_BYTE             = 19;
        private const int PNG_HEIGHT_HIGH_BYTE           = 20;
        private const int PNG_HEIGHT_SECOND_BYTE         = 21;
        private const int PNG_HEIGHT_THIRD_BYTE          = 22;
        private const int PNG_HEIGHT_LOW_BYTE            = 23;
        private const int PNG_HIGH_BYTE_SHIFT            = 24;
        private const int PNG_SECOND_BYTE_SHIFT          = 16;
        private const int PNG_THIRD_BYTE_SHIFT           = 8;
        private const int MIN_SOURCE_TEXTURE_SIZE        = 2;
        private const int MAX_SOURCE_TEXTURE_SIZE        = 4096;
        private const float VEIL_BOUNDS_DIAMETER_SCALE   = 2f;
        private const float SOLID_NORMAL_SCALE           = 0.65f;
        private const float SOLID_OCCLUSION_STRENGTH     = 1f;
        private const float SOLID_SMOOTHNESS             = 1f;
        private const float PARTICLE_SOFT_FADE           = 0.001f;
        private const float SPECTRAL_EDGE_RED            = 0.1f;
        private const float SPECTRAL_EDGE_GREEN          = 0.26f;
        private const float SPECTRAL_EDGE_BLUE           = 0.58f;
        private const float CORE_SHADER_MODE             = 1f;
        private const float FLAME_SHADER_MODE            = 2f;
        private const float FLASH_SHADER_MODE            = 3f;
        private const float FLOW_SHADER_MODE             = 0f;
        private const float FLASH_BRIGHTNESS             = 1f;
        private const float CORE_NOISE_SCALE             = 3f;
        private const float FLAME_NOISE_SCALE            = 2f;
        private const float FLOW_NOISE_SCALE_U           = 1f;
        private const float FLOW_NOISE_SCALE_V           = 4f;
        private const float INITIAL_MATERIAL_OPACITY     = 1f;
        private const float INITIAL_VEIL_WIDTH           = 0f;
        private const float NOISE_MIDPOINT               = 0.5f;
        private const float INITIAL_CORE_LIGHT_INTENSITY = 0f;
        private const int FIRST_DECORATIVE_KIND          = 2;
        private static readonly string[] G_Roles         = { "Stone", "Band", "Core", "Flow", "Petal", "Ground", "DropFlame", "Flash" };

        [Serializable]
        private sealed class Export
        {
            [SerializeField] private Part[] _meshes = null;
            public Part[] Meshes => _meshes;
        }

        [Serializable]
        private sealed class Part
        {
            [SerializeField] private string _name      = null;
            [SerializeField] private string _role      = null;
            [SerializeField] private float[] _position = null;
            [SerializeField] private float[] _vertices = null;
            [SerializeField] private float[] _normals  = null;
            [SerializeField] private float[] _uv       = null;
            [SerializeField] private float[] _colors   = null;
            [SerializeField] private int[] _triangles  = null;
            public string Name => _name;
            public string Role => _role;
            public float[] Position => _position;
            public float[] Vertices => _vertices;
            public float[] Normals => _normals;
            public float[] Uv => _uv;
            public float[] Colors => _colors;
            public int[] Triangles => _triangles;
        }

        /****************************************
        *                Methods
        ****************************************/
        /// <summary> 기존 설정과 GUID를 보존하며 아트 전용 프리팹을 생성한다 </summary>
        [MenuItem("Shinshi/VFX/Wolbaek/Build Seal Core")]
        public static void Build()
        {
            Guard();
            Export data = JsonUtility.FromJson<Export>(File.ReadAllText(EVIDENCE + "/seal_core_geometry.json"));
            Validate(data);
            ValidateTextures();
            if ((Shader.Find(PARTICLE) == null) || (Shader.Find(LIT) == null) || (Shader.Find(SPECTRAL) == null))
            {
                throw new InvalidOperationException("봉인핵 셰이더가 없습니다");
            }
            foreach (string folder in new[] { ROOT + "/Models", ROOT + "/Materials", ROOT + "/Textures", PREFABS })
            {
                Directory.CreateDirectory(folder);
            }
            AssetDatabase.Refresh();
            ImportTextures();
            MakeCloudNoise();
            Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
            foreach (Part part in data.Meshes)
            {
                meshes.Add(part.Name, MakeMesh(part));
            }
            Dictionary<string, Material> materials = new Dictionary<string, Material>();
            foreach (string role in G_Roles)
            {
                materials.Add(role, MakeMaterial(role));
            }
            materials.Add("FlowVeil", MakeFlowVeil(materials["Flow"]));
            materials.Add("DropVeil", MakeDropVeil(materials["DropFlame"]));
            Scene original = SceneManager.GetActiveScene();
            Scene temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(temp);
            try
            {
                BuildCore(data, meshes, materials);
                BuildDrop(data, meshes, materials);
            }
            finally
            {
                EditorSceneManager.CloseScene(temp, true);
                if (original.IsValid())
                {
                    SceneManager.SetActiveScene(original);
                }
            }
            SaveOwnedAssets();
        }

        private static void Guard()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("PlayMode 중에는 생성할 수 없습니다");
            }
            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if ((scene.path == SCENE) && (scene.isDirty))
                {
                    throw new InvalidOperationException("봉인핵 검수 씬의 미저장 변경이 있습니다");
                }
            }
        }

        /// <summary> 봉인핵만 검수하는 개발 씬을 별도로 저장한다 </summary>
        [MenuItem("Shinshi/VFX/Wolbaek/Build Seal Core Preview Scene")]
        public static void BuildScene()
        {
            Guard();
            Scene original = SceneManager.GetActiveScene();
            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                if (SceneManager.GetSceneAt(i).path == SCENE)
                {
                    throw new InvalidOperationException("봉인핵 검수 씬을 닫은 뒤 재생성하세요");
                }
            }
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                GameObject floorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FLOOR);
                Material floorSource = floorPrefab.GetComponentInChildren<Renderer>(true).sharedMaterial;
                string floorMaterialPath = ROOT + "/M_SealCorePreviewFloor.mat";
                Material floorMaterial = AssetDatabase.LoadAssetAtPath<Material>(floorMaterialPath);
                bool newFloorMaterial = floorMaterial == null;
                if (newFloorMaterial)
                {
                    floorMaterial = new Material(floorSource);
                }
                floorMaterial.CopyPropertiesFromMaterial(floorSource);
                floorMaterial.SetColor("_BaseColor", floorSource.GetColor("_BaseColor")
                    * new Color(PREVIEW_FLOOR_TINT, PREVIEW_FLOOR_TINT, PREVIEW_FLOOR_TINT, 1f));
                Store(floorMaterial, floorMaterialPath, newFloorMaterial);
                for (int x = FLOOR_GRID_MIN; x <= FLOOR_GRID_MAX; ++x)
                {
                    for (int z = FLOOR_GRID_MIN; z <= FLOOR_GRID_MAX; ++z)
                    {
                        GameObject tile = (GameObject)PrefabUtility.InstantiatePrefab(floorPrefab, scene);
                        tile.transform.position = new Vector3(x * FLOOR_TILE_SPACING, FLOOR_HEIGHT, z * FLOOR_TILE_SPACING);
                        foreach (Renderer renderer in tile.GetComponentsInChildren<Renderer>(true))
                        {
                            renderer.sharedMaterial = floorMaterial;
                        }
                    }
                }
                Light light = new GameObject("SealCore_Key").AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = new Color(KEY_LIGHT_RED, KEY_LIGHT_GREEN, KEY_LIGHT_BLUE);
                light.intensity = KEY_LIGHT_INTENSITY;
                light.shadows = LightShadows.Soft;
                light.transform.rotation = Quaternion.Euler(KEY_LIGHT_PITCH, KEY_LIGHT_YAW, 0f);
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(AMBIENT_SKY_RED, AMBIENT_SKY_GREEN, AMBIENT_SKY_BLUE);
                RenderSettings.ambientEquatorColor = new Color(AMBIENT_EQUATOR_RED, AMBIENT_EQUATOR_GREEN, AMBIENT_EQUATOR_BLUE);
                RenderSettings.ambientGroundColor = new Color(AMBIENT_GROUND_RED, AMBIENT_GROUND_GREEN, AMBIENT_GROUND_BLUE);
                Camera camera = new GameObject("SealCore_GameplayCamera").AddComponent<Camera>();
                camera.tag = "MainCamera";
                camera.transform.position = new Vector3(PREVIEW_CAMERA_X, PREVIEW_CAMERA_Y, PREVIEW_CAMERA_Z);
                camera.transform.LookAt(Vector3.up);
                camera.fieldOfView = PREVIEW_CAMERA_FOV;
                camera.nearClipPlane = PREVIEW_NEAR_CLIP;
                camera.farClipPlane = PREVIEW_FAR_CLIP;
                camera.allowHDR = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(PREVIEW_BACKGROUND_RED, PREVIEW_BACKGROUND_GREEN, PREVIEW_BACKGROUND_BLUE);
                UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
                cameraData.requiresDepthTexture = true;
                cameraData.renderPostProcessing = true;
                VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ROOT + "/VP_SealCorePreview.asset");
                if (profile == null)
                {
                    profile = ScriptableObject.CreateInstance<VolumeProfile>();
                    AssetDatabase.CreateAsset(profile, ROOT + "/VP_SealCorePreview.asset");
                    Bloom bloom = profile.Add<Bloom>(true);
                    bloom.intensity.Override(PREVIEW_BLOOM_INTENSITY);
                    bloom.threshold.Override(PREVIEW_BLOOM_THRESHOLD);
                    bloom.scatter.Override(PREVIEW_BLOOM_SCATTER);
                    AssetDatabase.AddObjectToAsset(bloom, profile);
                }
                Volume volume = new GameObject("SealCore_Bloom").AddComponent<Volume>();
                volume.isGlobal = true;
                volume.sharedProfile = profile;
                WolbaekSealCorePlayer[] cores = new WolbaekSealCorePlayer[CORE_COUNT];
                for (int i = 0; i < CORE_COUNT; ++i)
                {
                    GameObject obj = (GameObject)PrefabUtility.InstantiatePrefab(
                        AssetDatabase.LoadAssetAtPath<GameObject>(PREFABS + "/PF_WolbaekSealCore.prefab"), scene);
                    obj.transform.position = new Vector3(i == 0 ? -CORE_SPACING : CORE_SPACING, 0f, 0f);
                    cores[i] = obj.GetComponent<WolbaekSealCorePlayer>();
                }
                GameObject drop = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(PREFABS + "/PF_WolbaekSealDrop.prefab"), scene);
                WolbaekSealCorePreview preview = new GameObject("Development_External_Seal_Cues").AddComponent<WolbaekSealCorePreview>();
                SerializedObject so = new SerializedObject(preview);
                SetArray(so.FindProperty("_cores"), cores);
                so.FindProperty("_drop").objectReferenceValue = drop.GetComponent<WolbaekSealDropPlayer>();
                so.ApplyModifiedPropertiesWithoutUndo();
                SaveOwnedAssets();
                if (!EditorSceneManager.SaveScene(scene, SCENE))
                {
                    throw new InvalidOperationException("봉인핵 검수 씬 저장 실패");
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (original.IsValid())
                {
                    SceneManager.SetActiveScene(original);
                }
            }
        }

        private static void Validate(Export data)
        {
            if ((data == null) || (data.Meshes == null) || (data.Meshes.Length == 0))
            {
                throw new InvalidDataException("봉인핵 메시 목록이 없습니다");
            }
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Part part in data.Meshes)
            {
                if ((part == null) || (string.IsNullOrEmpty(part.Name)) || (!part.Name.StartsWith("SM_", StringComparison.Ordinal))
                    || (part.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) || (!names.Add(part.Name))
                    || (!G_Roles.Contains(part.Role)))
                {
                    throw new InvalidDataException("봉인핵 메시 이름 또는 역할 오류");
                }
                if ((part.Vertices == null) || (part.Vertices.Length == 0) || (part.Vertices.Length % VECTOR_COMPONENT_COUNT != 0))
                {
                    throw new InvalidDataException("정점 배열 오류: " + part.Name);
                }
                int count = part.Vertices.Length / VECTOR_COMPONENT_COUNT;
                if ((part.Position == null) || (part.Position.Length != VECTOR_COMPONENT_COUNT)
                    || (part.Normals == null) || (part.Normals.Length != count * VECTOR_COMPONENT_COUNT)
                    || (part.Uv == null) || (part.Uv.Length != count * UV_COMPONENT_COUNT)
                    || (part.Colors == null) || (part.Colors.Length != count * COLOR_COMPONENT_COUNT)
                    || (part.Triangles == null) || (part.Triangles.Length == 0) || (part.Triangles.Length % TRIANGLE_INDEX_COUNT != 0))
                {
                    throw new InvalidDataException("메시 채널 길이 오류: " + part.Name);
                }
                foreach (float[] values in new[] { part.Position, part.Vertices, part.Normals, part.Uv, part.Colors })
                {
                    foreach (float value in values)
                    {
                        if ((float.IsNaN(value)) || (float.IsInfinity(value)) || (Mathf.Abs(value) > MAX_MESH_ABSOLUTE_VALUE))
                        {
                            throw new InvalidDataException("메시 유한수 범위 오류: " + part.Name);
                        }
                    }
                }
                foreach (int index in part.Triangles)
                {
                    if ((index < 0) || (index >= count))
                    {
                        throw new InvalidDataException("메시 인덱스 오류: " + part.Name);
                    }
                }
            }
            foreach (string role in G_Roles)
            {
                if (!data.Meshes.Any(part => part.Role == role))
                {
                    throw new InvalidDataException("필수 역할 누락: " + role);
                }
            }
        }

        private static void ImportTextures()
        {
            foreach (string file in Directory.GetFiles(EVIDENCE + "/source-textures", "*.png"))
            {
                string name = Path.GetFileName(file);
                if (name.Contains("Roughness"))
                {
                    continue;
                }
                string path = ROOT + "/Textures/" + name;
                File.Copy(file, path, true);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = name.Contains("Normal") ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = name.Contains("BaseColor");
                importer.isReadable = false;
                importer.mipmapEnabled = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.wrapMode = TextureWrapMode.Clamp;
                if ((name.Contains("Flow")) || (name.Contains("_Core_")) || (name.Contains("DropFlame")))
                {
                    importer.wrapModeV = TextureWrapMode.Repeat;
                }
                if (name.Contains("_Core_"))
                {
                    importer.wrapModeU = TextureWrapMode.Repeat;
                }
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.maxTextureSize = MAX_IMPORTED_TEXTURE_SIZE;
                importer.SaveAndReimport();
            }
        }

        private static void ValidateTextures()
        {
            List<string> required = new List<string>();
            foreach (string role in new[] { "Stone", "Band" })
            {
                foreach (string map in new[] { "BaseColor", "Normal", "AO", "MetallicSmoothness" })
                {
                    required.Add("T_SealCore_" + role + "_" + map + ".png");
                }
            }
            foreach (string role in G_Roles.Skip(SOLID_ROLE_COUNT))
            {
                required.Add("T_SealCore_" + role + "_Mask.png");
            }
            foreach (string name in required)
            {
                string path = EVIDENCE + "/source-textures/" + name;
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("봉인핵 필수 텍스처가 없습니다", path);
                }
                byte[] header = new byte[PNG_HEADER_LENGTH];
                using (FileStream stream = File.OpenRead(path))
                {
                    if ((stream.Read(header, 0, header.Length) != header.Length)
                        || (header[0] != PNG_SIGNATURE_BYTE_0) || (header[1] != PNG_SIGNATURE_BYTE_1) || (header[2] != PNG_SIGNATURE_BYTE_2) || (header[3] != PNG_SIGNATURE_BYTE_3))
                    {
                        throw new InvalidDataException("PNG 헤더 오류: " + name);
                    }
                }
                int width = (header[PNG_WIDTH_HIGH_BYTE] << PNG_HIGH_BYTE_SHIFT) | (header[PNG_WIDTH_SECOND_BYTE] << PNG_SECOND_BYTE_SHIFT) | (header[PNG_WIDTH_THIRD_BYTE] << PNG_THIRD_BYTE_SHIFT) | header[PNG_WIDTH_LOW_BYTE];
                int height = (header[PNG_HEIGHT_HIGH_BYTE] << PNG_HIGH_BYTE_SHIFT) | (header[PNG_HEIGHT_SECOND_BYTE] << PNG_SECOND_BYTE_SHIFT) | (header[PNG_HEIGHT_THIRD_BYTE] << PNG_THIRD_BYTE_SHIFT) | header[PNG_HEIGHT_LOW_BYTE];
                if ((width < MIN_SOURCE_TEXTURE_SIZE) || (height < MIN_SOURCE_TEXTURE_SIZE) || (width > MAX_SOURCE_TEXTURE_SIZE) || (height > MAX_SOURCE_TEXTURE_SIZE))
                {
                    throw new InvalidDataException("PNG 해상도 오류: " + name);
                }
            }
        }

        private static Mesh MakeMesh(Part part)
        {
            string path = ROOT + "/Models/" + part.Name + ".asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool fresh = mesh == null;
            if (fresh)
            {
                mesh = new Mesh();
            }
            mesh.Clear();
            mesh.name = part.Name;
            mesh.indexFormat = IndexFormat.UInt32;
            int count = part.Vertices.Length / VECTOR_COMPONENT_COUNT;
            Vector3[] vertices = new Vector3[count];
            Vector3[] normals = new Vector3[count];
            Vector2[] uv = new Vector2[count];
            Color[] colors = new Color[count];
            for (int i = 0; i < count; ++i)
            {
                vertices[i] = new Vector3(part.Vertices[i * VECTOR_COMPONENT_COUNT], part.Vertices[i * VECTOR_COMPONENT_COUNT + 1], part.Vertices[i * VECTOR_COMPONENT_COUNT + 2]);
                normals[i] = new Vector3(part.Normals[i * VECTOR_COMPONENT_COUNT], part.Normals[i * VECTOR_COMPONENT_COUNT + 1], part.Normals[i * VECTOR_COMPONENT_COUNT + 2]);
                uv[i] = new Vector2(part.Uv[i * UV_COMPONENT_COUNT], part.Uv[i * UV_COMPONENT_COUNT + 1]);
                colors[i] = new Color(part.Colors[i * COLOR_COMPONENT_COUNT], part.Colors[i * COLOR_COMPONENT_COUNT + 1], part.Colors[i * COLOR_COMPONENT_COUNT + 2], part.Colors[i * COLOR_COMPONENT_COUNT + 3]);
            }
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uv;
            mesh.colors = colors;
            mesh.triangles = part.Triangles;
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            if (part.Role == "Flow")
            {
                Bounds bounds = mesh.bounds;
                bounds.Expand(Vector3.up * FLOW_VEIL_WIDTH);
                mesh.bounds = bounds;
            }
            else if (part.Role == "DropFlame")
            {
                Bounds bounds = mesh.bounds;
                bounds.Expand(FLAME_VEIL_WIDTH * VEIL_BOUNDS_DIAMETER_SCALE);
                mesh.bounds = bounds;
            }
            Store(mesh, path, fresh);
            return mesh;
        }

        private static Texture2D Texture(string role, string suffix)
        {
            string[] paths = Directory.GetFiles(ROOT + "/Textures", "*.png");
            string prefix = "T_SealCore_" + role + "_";
            string path = paths.FirstOrDefault(p => (Path.GetFileName(p).StartsWith(prefix, StringComparison.Ordinal))
                && (Path.GetFileName(p).Contains(suffix)));
            return path == null ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(path.Replace('\\', '/'));
        }

        private static Material MakeMaterial(string role)
        {
            bool solid = (role == "Stone") || (role == "Band");
            bool spectral = (role == "Core") || (role == "Flow") || (role == "DropFlame") || (role == "Flash");
            string shaderName = solid ? LIT : spectral ? SPECTRAL : PARTICLE;
            string path = ROOT + "/Materials/M_SealCore_" + role + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool fresh = material == null;
            if (fresh)
            {
                material = new Material(Shader.Find(shaderName));
            }
            material.shader = Shader.Find(shaderName);
            if (solid)
            {
                material.SetTexture("_BaseMap", Texture(role, "BaseColor"));
                material.SetTexture("_BumpMap", Texture(role, "Normal"));
                material.SetTexture("_OcclusionMap", Texture(role, "AO"));
                material.SetTexture("_MetallicGlossMap", Texture(role, "MetallicSmoothness"));
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_BumpScale", SOLID_NORMAL_SCALE);
                material.SetFloat("_OcclusionStrength", SOLID_OCCLUSION_STRENGTH);
                material.SetFloat("_Smoothness", SOLID_SMOOTHNESS);
                material.EnableKeyword("_NORMALMAP");
                material.EnableKeyword("_OCCLUSIONMAP");
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            else
            {
                Texture2D texture = Texture(role, "");
                material.SetTexture("_BaseMap", texture == null ? Texture2D.whiteTexture : texture);
                material.SetColor("_Color", Color.white);
                material.SetFloat("_SoftFade", spectral ? SPECTRAL_SOFT_FADE : PARTICLE_SOFT_FADE);
                if (spectral)
                {
                    material.SetTexture("_NoiseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ROOT + "/Textures/" + NOISE_NAME + ".png"));
                    material.SetColor("_EdgeColor", new Color(SPECTRAL_EDGE_RED, SPECTRAL_EDGE_GREEN, SPECTRAL_EDGE_BLUE, 1f));
                    material.SetFloat("_Mode", role == "Core" ? CORE_SHADER_MODE : role == "DropFlame" ? FLAME_SHADER_MODE : role == "Flash" ? FLASH_SHADER_MODE : FLOW_SHADER_MODE);
                    material.SetFloat("_Erosion", role == "Core" ? CORE_EROSION : role == "DropFlame" ? FLAME_EROSION : SPECTRAL_EROSION);
                    material.SetFloat("_Distortion", SPECTRAL_DISTORTION);
                    material.SetFloat("_Brightness", role == "Core" ? CORE_BRIGHTNESS : role == "DropFlame" ? FLAME_BRIGHTNESS : role == "Flash" ? FLASH_BRIGHTNESS : FLOW_BRIGHTNESS);
                    material.SetVector("_NoiseScale", role == "Core" ? new Vector4(CORE_NOISE_SCALE, CORE_NOISE_SCALE, 0f, 0f)
                        : role == "DropFlame" ? new Vector4(FLAME_NOISE_SCALE, FLAME_NOISE_SCALE, 0f, 0f) : new Vector4(FLOW_NOISE_SCALE_U, FLOW_NOISE_SCALE_V, 0f, 0f));
                    material.SetFloat("_Cull", role == "Core" ? (float)CullMode.Back : (float)CullMode.Off);
                    material.SetFloat("_Opacity", INITIAL_MATERIAL_OPACITY);
                    material.SetFloat("_VeilWidth", INITIAL_VEIL_WIDTH);
                    material.SetFloat("_SrcBlend", (float)BlendMode.One);
                    material.SetFloat("_DstBlend", role == "Flow" ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.One);
                }
                else
                {
                    material.SetFloat("_SrcBlend", (float)BlendMode.One);
                    material.SetFloat("_DstBlend", (float)BlendMode.One);
                }
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            Store(material, path, fresh);
            return material;
        }

        private static Material MakeFlowVeil(Material source)
        {
            string path = ROOT + "/Materials/M_SealCore_FlowVeil.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool fresh = material == null;
            if (fresh)
            {
                material = new Material(source);
            }
            material.shader = source.shader;
            material.CopyPropertiesFromMaterial(source);
            material.SetFloat("_Opacity", FLOW_VEIL_OPACITY);
            material.SetFloat("_VeilWidth", FLOW_VEIL_WIDTH);
            material.SetTextureOffset("_BaseMap", new Vector2(0f, FLOW_VEIL_PHASE_OFFSET));
            Store(material, path, fresh);
            return material;
        }

        private static Material MakeDropVeil(Material source)
        {
            string path = ROOT + "/Materials/M_SealCore_DropVeil.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool fresh = material == null;
            if (fresh)
            {
                material = new Material(source);
            }
            material.shader = source.shader;
            material.CopyPropertiesFromMaterial(source);
            material.SetFloat("_Opacity", FLAME_VEIL_OPACITY);
            material.SetFloat("_VeilWidth", FLAME_VEIL_WIDTH);
            material.SetTextureOffset("_BaseMap", new Vector2(0f, FLAME_VEIL_PHASE_OFFSET));
            Store(material, path, fresh);
            return material;
        }

        private static void MakeCloudNoise()
        {
            float[][] grids = new float[NOISE_OCTAVES][];
            System.Random random = new System.Random(NOISE_SEED);
            float total = 0f;
            for (int octave = 0; octave < NOISE_OCTAVES; ++octave)
            {
                int frequency = NOISE_GRID_BASE << octave;
                float amplitude = Mathf.Pow(NOISE_FALLOFF, octave);
                grids[octave] = new float[frequency * frequency * NOISE_CHANNELS];
                for (int cell = 0; cell < grids[octave].Length; ++cell)
                {
                    grids[octave][cell] = (float)random.NextDouble();
                }
                total += amplitude;
            }
            Texture2D texture = new Texture2D(NOISE_SIZE, NOISE_SIZE, TextureFormat.RGBA32, false, true);
            Color[] pixels = new Color[NOISE_SIZE * NOISE_SIZE];
            try
            {
                for (int y = 0; y < NOISE_SIZE; ++y)
                {
                    for (int x = 0; x < NOISE_SIZE; ++x)
                    {
                        float first = 0f;
                        float second = 0f;
                        for (int octave = 0; octave < NOISE_OCTAVES; ++octave)
                        {
                            int frequency = NOISE_GRID_BASE << octave;
                            float px = x * frequency / (float)NOISE_SIZE;
                            float py = y * frequency / (float)NOISE_SIZE;
                            int ix = Mathf.FloorToInt(px);
                            int iy = Mathf.FloorToInt(py);
                            float tx = Mathf.SmoothStep(0f, 1f, px - ix);
                            float ty = Mathf.SmoothStep(0f, 1f, py - iy);
                            int nextX = (ix + 1) % frequency;
                            int nextY = (iy + 1) % frequency;
                            int a = (iy * frequency + ix) * NOISE_CHANNELS;
                            int b = (iy * frequency + nextX) * NOISE_CHANNELS;
                            int c = (nextY * frequency + ix) * NOISE_CHANNELS;
                            int d = (nextY * frequency + nextX) * NOISE_CHANNELS;
                            float[] grid = grids[octave];
                            float amplitude = Mathf.Pow(NOISE_FALLOFF, octave);
                            first += Mathf.Lerp(Mathf.Lerp(grid[a], grid[b], tx),
                                Mathf.Lerp(grid[c], grid[d], tx), ty) * amplitude;
                            second += Mathf.Lerp(Mathf.Lerp(grid[a + 1], grid[b + 1], tx),
                                Mathf.Lerp(grid[c + 1], grid[d + 1], tx), ty) * amplitude;
                        }
                        float red = Mathf.Clamp01(NOISE_MIDPOINT + (first / total - NOISE_MIDPOINT) * NOISE_CONTRAST);
                        float green = Mathf.Clamp01(NOISE_MIDPOINT + (second / total - NOISE_MIDPOINT) * NOISE_CONTRAST);
                        pixels[y * NOISE_SIZE + x] = new Color(red, green, red, 1f);
                    }
                }
                texture.SetPixels(pixels);
                texture.Apply();
                string path = ROOT + "/Textures/" + NOISE_NAME + ".png";
                byte[] png = texture.EncodeToPNG();
                File.WriteAllBytes(path, png);
                Directory.CreateDirectory(EVIDENCE + "/v04-unity-shader");
                File.WriteAllBytes(EVIDENCE + "/v04-unity-shader/" + NOISE_NAME + ".png", png);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.sRGBTexture = false;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Bilinear;
                importer.isReadable = false;
                importer.mipmapEnabled = true;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static Renderer PartObject(Part part, Transform parent, Dictionary<string, Mesh> meshes, Dictionary<string, Material> materials)
        {
            GameObject obj = new GameObject(part.Name);
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = new Vector3(part.Position[0], part.Position[1], part.Position[2]);
            obj.AddComponent<MeshFilter>().sharedMesh = meshes[part.Name];
            MeshRenderer renderer = obj.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = materials[part.Role];
            bool solid = (part.Role == "Stone") || (part.Role == "Band");
            renderer.shadowCastingMode = solid ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = solid;
            return renderer;
        }

        private static T Settings<T>(string name) where T : ScriptableObject
        {
            string path = ROOT + "/" + name + ".asset";
            T settings = AssetDatabase.LoadAssetAtPath<T>(path);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(settings, path);
            }
            return settings;
        }

        private static void BuildCore(Export data, Dictionary<string, Mesh> meshes, Dictionary<string, Material> materials)
        {
            GameObject root = new GameObject("PF_WolbaekSealCore");
            Transform shape = new GameObject("CoreShape").transform;
            shape.SetParent(root.transform, false);
            Light coreLight = new GameObject("Core_LocalIllumination").AddComponent<Light>();
            coreLight.transform.SetParent(shape, false);
            coreLight.type = LightType.Point;
            coreLight.shadows = LightShadows.None;
            coreLight.enabled = false;
            coreLight.intensity = INITIAL_CORE_LIGHT_INTENSITY;
            WolbaekSealCorePlayer player = root.AddComponent<WolbaekSealCorePlayer>();
            SerializedObject so = new SerializedObject(player);
            so.FindProperty("_settings").objectReferenceValue = Settings<WolbaekSealCoreSettingsSO>("SO_WolbaekSealCore");
            so.FindProperty("_shapeRoot").objectReferenceValue = shape;
            so.FindProperty("_coreLight").objectReferenceValue = coreLight;
            List<Renderer> pieces = new List<Renderer>();
            List<int> kinds = new List<int>();
            List<Renderer> decoration = new List<Renderer>();
            foreach (Part part in data.Meshes)
            {
                if (part.Role == "DropFlame")
                {
                    continue;
                }
                Renderer renderer = PartObject(part, part.Role == "Ground" ? root.transform : shape, meshes, materials);
                int kind = Array.IndexOf(new[] { "Stone", "Band", "Flow", "Petal" }, part.Role);
                if (kind >= 0)
                {
                    pieces.Add(renderer);
                    kinds.Add(kind);
                    if (kind >= FIRST_DECORATIVE_KIND)
                    {
                        decoration.Add(renderer);
                    }
                    if (part.Role == "Flow")
                    {
                        Renderer veil = PartObject(part, shape, meshes, materials);
                        veil.gameObject.name = part.Name + "_Veil";
                        veil.sharedMaterial = materials["FlowVeil"];
                        pieces.Add(veil);
                        kinds.Add(kind);
                        decoration.Add(veil);
                    }
                }
                else
                {
                    so.FindProperty(part.Role == "Core" ? "_core" : part.Role == "Ground" ? "_ground" : "_flash").objectReferenceValue = renderer;
                }
            }
            SetArray(so.FindProperty("_pieces"), pieces.Select(r => r.transform).ToArray());
            SetArray(so.FindProperty("_pieceRenderers"), pieces.ToArray());
            SerializedProperty kindProperty = so.FindProperty("_pieceKinds");
            kindProperty.arraySize = kinds.Count;
            for (int i = 0; i < kinds.Count; ++i)
            {
                kindProperty.GetArrayElementAtIndex(i).intValue = kinds[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            AddDecorativeLod(root, decoration.ToArray());
            PrefabUtility.SaveAsPrefabAsset(root, PREFABS + "/PF_WolbaekSealCore.prefab");
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static void BuildDrop(Export data, Dictionary<string, Mesh> meshes, Dictionary<string, Material> materials)
        {
            GameObject root = new GameObject("PF_WolbaekSealDrop");
            Transform drop = new GameObject("FallingFoxfire").transform;
            drop.SetParent(root.transform, false);
            WolbaekSealDropPlayer player = root.AddComponent<WolbaekSealDropPlayer>();
            SerializedObject so = new SerializedObject(player);
            so.FindProperty("_settings").objectReferenceValue = Settings<WolbaekSealDropSettingsSO>("SO_WolbaekSealDrop");
            so.FindProperty("_dropRoot").objectReferenceValue = drop;
            List<Renderer> flames = new List<Renderer>();
            foreach (Part part in data.Meshes)
            {
                if ((part.Role != "DropFlame") && (part.Role != "Ground") && (part.Role != "Flash"))
                {
                    continue;
                }
                Renderer renderer = PartObject(part, part.Role == "DropFlame" ? drop : root.transform, meshes, materials);
                if (part.Role == "DropFlame")
                {
                    flames.Add(renderer);
                    Renderer veil = PartObject(part, drop, meshes, materials);
                    veil.gameObject.name = part.Name + "_Veil";
                    veil.sharedMaterial = materials["DropVeil"];
                    flames.Add(veil);
                }
                else
                {
                    so.FindProperty(part.Role == "Ground" ? "_ground" : "_flash").objectReferenceValue = renderer;
                }
            }
            SetArray(so.FindProperty("_flame"), flames.ToArray());
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PREFABS + "/PF_WolbaekSealDrop.prefab");
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static void AddDecorativeLod(GameObject root, Renderer[] renderers)
        {
            // 핵과 낙하 경계는 장식 LOD 밖에 남긴다
            LODGroup lod = root.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(DECORATIVE_CULL, renderers) });
            lod.RecalculateBounds();
        }

        private static void SetArray<T>(SerializedProperty property, T[] values) where T : UnityEngine.Object
        {
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; ++i)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        private static void Store(UnityEngine.Object asset, string path, bool fresh)
        {
            if (fresh)
            {
                AssetDatabase.CreateAsset(asset, path);
            }
            else
            {
                EditorUtility.SetDirty(asset);
            }
        }

        private static void SaveOwnedAssets()
        {
            foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { ROOT, PREFABS }))
            {
                UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null)
                {
                    AssetDatabase.SaveAssetIfDirty(asset);
                }
            }
        }
    }
}
