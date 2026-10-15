// System
using System;
using System.Collections.Generic;
using System.IO;

// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Editor
{
    /// <summary> Blender가 구운 무철 01 부채꼴 타격 V4 텍스처, 바위 궤적을 Unity 프리팹, 프로필, 시험 씬으로 조립한다 </summary>
    public static class MucheolImpactVfxBuilder
    {
        /****************************************
        *                Types
        ****************************************/

        [Serializable]
        private sealed class ExportRoot
        {
            public RockExport[] rocks;
        }

        [Serializable]
        private sealed class RockExport
        {
            public string      name;
            public Vector3[]   vertices;
            public int[]       triangles;
            public RockSample[] samples;
        }

        [Serializable]
        private sealed class RockSample
        {
            public Vector3    position;
            public Quaternion rotation;
            public Vector3    scale;
            public bool       visible;
        }

        /****************************************
        *                Fields
        ****************************************/

        private const string ROOT_FOLDER      = "Assets/01.Scripts/05.Art/VFX/Mucheol/ImpactV4";
        private const string TEXTURES_FOLDER  = ROOT_FOLDER + "/Textures";
        private const string MESHES_FOLDER    = ROOT_FOLDER + "/Meshes";
        private const string MATERIALS_FOLDER = ROOT_FOLDER + "/Materials";
        private const string EXPORT_JSON_PATH = ROOT_FOLDER + "/blender-export.json";
        private const string PROFILE_PATH     = ROOT_FOLDER + "/Timing_01.asset";
        private const string VOLUME_PROFILE_PATH = ROOT_FOLDER + "/VolumeProfile_DevMucheol.asset";

        private const string PREFAB_FOLDER = "Assets/02.Prefab/VFX/Mucheol";
        private const string PREFAB_PATH   = PREFAB_FOLDER + "/PF_Mucheol_01_ImpactV4.prefab";

        private const string SCENE_FOLDER = "Assets/00.Scenes/Dev_VFX";
        private const string SCENE_PATH   = SCENE_FOLDER + "/Dev_Mucheol_01_ImpactV4.unity";

        private const string LAYER_SHADER_NAME = "Shinshi/VFX/MucheolImpactLayer";
        private const string LIT_SHADER_NAME   = "Universal Render Pipeline/Lit";

        private const int   TEXTURE_MAX_SIZE        = 1024;
        private const int   EXPECTED_ROCK_COUNT     = 21;
        private const float GROUND_HALF_EXTENT      = 5f;
        private const float GROUND_EMISSIVE_BOOST   = 1f;
        private const float OTHER_EMISSIVE_BOOST    = 1.4f;
        private const float ROCK_ROUGHNESS          = 0.85f;
        private const float ROCK_METALLIC           = 0.1f;

        private static readonly string[] LAYER_NAMES = { "warning", "ground", "cracks", "shock", "flash", "root" };

        private static readonly int MAIN_TEX_ID        = Shader.PropertyToID("_MainTex");
        private static readonly int TINT_ID             = Shader.PropertyToID("_Tint");
        private static readonly int EMISSIVE_BOOST_ID   = Shader.PropertyToID("_EmissiveBoost");
        private static readonly int LAYER_OPACITY_ID    = Shader.PropertyToID("_Opacity");
        private static readonly int SMOOTHNESS_ID       = Shader.PropertyToID("_Smoothness");
        private static readonly int METALLIC_ID         = Shader.PropertyToID("_Metallic");

        /****************************************
        *                 Menu
        ****************************************/

        [MenuItem("Shinshi/VFX/Build Mucheol 01 Impact V4")]
        public static void Build()
        {
            GuardNoDirtyScenes();
            EnsureFolder(ROOT_FOLDER);
            EnsureFolder(TEXTURES_FOLDER);
            EnsureFolder(MESHES_FOLDER);
            EnsureFolder(MATERIALS_FOLDER);
            EnsureFolder(PREFAB_FOLDER);
            EnsureFolder(SCENE_FOLDER);

            // Unity는 빈 미저장 씬이 열려 있어도 부가 씬 생성을 거절한다.
            Scene active = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(active.path))
            {
                if (active.rootCount != 0)
                {
                    throw new InvalidOperationException("열려 있는 이름 없는 씬을 먼저 저장해야 한다.");
                }
                if (File.Exists(AbsolutePath(SCENE_PATH)))
                {
                    EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Single);
                }
                else
                {
                    EditorSceneManager.SaveScene(active, SCENE_PATH);
                }
            }

            ExportRoot export = ParseExport();
            Dictionary<string, Texture2D> textures = ImportTextures();
            Dictionary<string, Material> materials = BuildLayerMaterials(textures);
            Mesh groundQuad = BuildOrLoadGroundQuad();
            Mesh[] rockMeshes = BuildRockMeshes(export);
            Material rockMaterial = BuildRockMaterial();
            MucheolPatternVfxSettingsSO profile = LoadOrCreateProfile();

            GameObject prefabAsset = BuildPrefab(materials, groundQuad, rockMeshes, rockMaterial, export, profile);

            BuildOrUpdateScene(prefabAsset);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[MucheolImpactVfxBuilder] 무철 01 부채꼴 타격 V4 빌드 완료");
        }

        /****************************************
        *           Scene Safety
        ****************************************/

        private static void GuardNoDirtyScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty)
                {
                    throw new InvalidOperationException(
                        $"[MucheolImpactVfxBuilder] 저장하지 않은 씬이 있다: {scene.path}, 먼저 저장하거나 되돌린 뒤 다시 실행한다");
                }
            }
        }

        /****************************************
        *            Export Parsing
        ****************************************/

        private static ExportRoot ParseExport()
        {
            string absolutePath = AbsolutePath(EXPORT_JSON_PATH);
            if (!File.Exists(absolutePath))
            {
                throw new FileNotFoundException(
                    "[MucheolImpactVfxBuilder] blender-export.json이 없다, 부모가 먼저 Blender 내보내기를 해야 한다", absolutePath);
            }

            string json = File.ReadAllText(absolutePath);
            ExportRoot export = JsonUtility.FromJson<ExportRoot>(json);
            int count = (export?.rocks != null) ? export.rocks.Length : 0;
            if (count != EXPECTED_ROCK_COUNT)
            {
                throw new InvalidOperationException(
                    $"[MucheolImpactVfxBuilder] blender-export.json의 바위가 {EXPECTED_ROCK_COUNT}개가 아니라 {count}개다, 대체 바위를 만들지 않는다");
            }

            return export;
        }

        /****************************************
        *            Textures
        ****************************************/

        private static Dictionary<string, Texture2D> ImportTextures()
        {
            var textures = new Dictionary<string, Texture2D>(LAYER_NAMES.Length);
            for (int i = 0; i < LAYER_NAMES.Length; ++i)
            {
                string name = LAYER_NAMES[i];
                string assetPath = $"{TEXTURES_FOLDER}/{name}.png";
                if (!File.Exists(AbsolutePath(assetPath)))
                {
                    throw new FileNotFoundException(
                        $"[MucheolImpactVfxBuilder] 레이어 텍스처가 없다: {assetPath}, 부모가 먼저 내보내야 한다", assetPath);
                }

                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer)
                {
                    importer.textureType         = TextureImporterType.Default;
                    importer.alphaSource         = TextureImporterAlphaSource.FromInput;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled       = true;
                    importer.wrapMode            = TextureWrapMode.Clamp;
                    importer.maxTextureSize      = TEXTURE_MAX_SIZE;
                    importer.sRGBTexture         = true;
                    importer.SaveAndReimport();
                }

                textures[name] = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            }

            return textures;
        }

        /****************************************
        *            Materials
        ****************************************/

        private static Dictionary<string, Material> BuildLayerMaterials(Dictionary<string, Texture2D> textures)
        {
            Shader shader = Shader.Find(LAYER_SHADER_NAME);
            if (shader == null)
            {
                throw new InvalidOperationException($"[MucheolImpactVfxBuilder] 셰이더를 찾지 못했다: {LAYER_SHADER_NAME}");
            }

            var materials = new Dictionary<string, Material>(LAYER_NAMES.Length);
            for (int i = 0; i < LAYER_NAMES.Length; ++i)
            {
                string name = LAYER_NAMES[i];
                string label = Capitalize(name);
                string path = $"{MATERIALS_FOLDER}/M_MucheolImpact_{label}.mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader) { name = $"M_MucheolImpact_{label}" };
                    material.SetTexture(MAIN_TEX_ID, textures[name]);
                    material.SetColor(TINT_ID, Color.white);
                    material.SetFloat(EMISSIVE_BOOST_ID, (name == "ground") ? GROUND_EMISSIVE_BOOST : OTHER_EMISSIVE_BOOST);
                    material.SetFloat(LAYER_OPACITY_ID, 1f);
                    AssetDatabase.CreateAsset(material, path);
                }

                materials[name] = material;
            }

            return materials;
        }

        private static Material BuildRockMaterial()
        {
            string path = $"{MATERIALS_FOLDER}/M_MucheolRockDebris.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            Shader shader = Shader.Find(LIT_SHADER_NAME);
            if (shader == null)
            {
                throw new InvalidOperationException($"[MucheolImpactVfxBuilder] 셰이더를 찾지 못했다: {LIT_SHADER_NAME}");
            }

            material = new Material(shader) { name = "M_MucheolRockDebris" };
            material.color = new Color(0.33f, 0.26f, 0.16f, 1f);
            if (material.HasProperty(SMOOTHNESS_ID))
            {
                material.SetFloat(SMOOTHNESS_ID, 1f - ROCK_ROUGHNESS);
            }
            if (material.HasProperty(METALLIC_ID))
            {
                material.SetFloat(METALLIC_ID, ROCK_METALLIC);
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /****************************************
        *            Meshes
        ****************************************/

        private static Mesh BuildOrLoadGroundQuad()
        {
            string path = $"{MESHES_FOLDER}/SM_MucheolImpactGroundQuad.asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                return existing;
            }

            Mesh mesh = new Mesh { name = "SM_MucheolImpactGroundQuad" };
            mesh.SetVertices(new[]
            {
                new Vector3(-GROUND_HALF_EXTENT, 0f, -GROUND_HALF_EXTENT),
                new Vector3( GROUND_HALF_EXTENT, 0f, -GROUND_HALF_EXTENT),
                new Vector3( GROUND_HALF_EXTENT, 0f,  GROUND_HALF_EXTENT),
                new Vector3(-GROUND_HALF_EXTENT, 0f,  GROUND_HALF_EXTENT),
            });
            mesh.SetNormals(new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            mesh.SetUVs(0, new List<Vector2>
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static Mesh[] BuildRockMeshes(ExportRoot export)
        {
            Mesh[] meshes = new Mesh[export.rocks.Length];
            for (int i = 0; i < export.rocks.Length; ++i)
            {
                RockExport rock = export.rocks[i];
                string safeName = Sanitize(string.IsNullOrEmpty(rock.name) ? $"Rock_{i:00}" : rock.name);
                string path = $"{MESHES_FOLDER}/SM_MucheolRock_{i:00}_{safeName}.asset";

                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                bool isNew = mesh == null;
                if (isNew)
                {
                    mesh = new Mesh();
                }

                mesh.name = $"SM_MucheolRock_{i:00}_{safeName}";
                mesh.Clear();
                mesh.SetVertices(rock.vertices);
                mesh.SetTriangles(rock.triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                if (isNew)
                {
                    AssetDatabase.CreateAsset(mesh, path);
                }
                else
                {
                    EditorUtility.SetDirty(mesh);
                }

                meshes[i] = mesh;
            }

            return meshes;
        }

        /****************************************
        *            Profile
        ****************************************/

        private static MucheolPatternVfxSettingsSO LoadOrCreateProfile()
        {
            MucheolPatternVfxSettingsSO profile = AssetDatabase.LoadAssetAtPath<MucheolPatternVfxSettingsSO>(PROFILE_PATH);
            if (profile != null)
            {
                return profile;
            }

            profile = ScriptableObject.CreateInstance<MucheolPatternVfxSettingsSO>();
            AssetDatabase.CreateAsset(profile, PROFILE_PATH);
            return profile;
        }

        /****************************************
        *            Prefab
        ****************************************/

        // 조립은 사용자가 열어 둔 씬을 건드리지 않도록 임시 부가 씬에서 하고 끝나면 저장 없이 닫는다
        private static GameObject BuildPrefab(Dictionary<string, Material> materials, Mesh groundQuad,
            Mesh[] rockMeshes, Material rockMaterial, ExportRoot export, MucheolPatternVfxSettingsSO profile)
        {
            Scene previousActive = SceneManager.GetActiveScene();
            Scene buildScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(buildScene);

            GameObject savedPrefab;
            try
            {
                GameObject root = BuildPrefabContents(materials, groundQuad, rockMeshes, rockMaterial, export, profile);
                savedPrefab = SavePrefab(root);
            }
            finally
            {
                SceneManager.SetActiveScene(previousActive);
                EditorSceneManager.CloseScene(buildScene, true);
            }

            return savedPrefab;
        }

        private static GameObject BuildPrefabContents(Dictionary<string, Material> materials, Mesh groundQuad,
            Mesh[] rockMeshes, Material rockMaterial, ExportRoot export, MucheolPatternVfxSettingsSO profile)
        {
            GameObject root = new GameObject("PF_Mucheol_01_ImpactV4");
            MucheolImpactVfxPlayer player = root.AddComponent<MucheolImpactVfxPlayer>();

            MeshRenderer warning = BuildLayerQuad(root.transform, "Warning", groundQuad, materials["warning"], 0.006f);
            MeshRenderer ground  = BuildLayerQuad(root.transform, "Ground", groundQuad, materials["ground"], 0.001f);
            MeshRenderer cracks  = BuildLayerQuad(root.transform, "Cracks", groundQuad, materials["cracks"], 0.002f);
            MeshRenderer rootDim = BuildLayerQuad(root.transform, "Root", groundQuad, materials["root"], 0.003f);
            MeshRenderer shock   = BuildLayerQuad(root.transform, "Shock", groundQuad, materials["shock"], 0.004f);
            MeshRenderer flash   = BuildLayerQuad(root.transform, "Flash", groundQuad, materials["flash"], 0.005f);

            GameObject rocksParent = new GameObject("Rocks");
            rocksParent.transform.SetParent(root.transform, false);

            SerializedObject serialized = new SerializedObject(player);
            serialized.FindProperty("_profile").objectReferenceValue = profile;
            serialized.FindProperty("_warning").objectReferenceValue = warning;
            serialized.FindProperty("_ground").objectReferenceValue  = ground;
            serialized.FindProperty("_cracks").objectReferenceValue  = cracks;
            serialized.FindProperty("_shock").objectReferenceValue   = shock;
            serialized.FindProperty("_flash").objectReferenceValue   = flash;
            serialized.FindProperty("_root").objectReferenceValue    = rootDim;

            SerializedProperty rocksProp = serialized.FindProperty("_rocks");
            rocksProp.arraySize = export.rocks.Length;

            for (int i = 0; i < export.rocks.Length; ++i)
            {
                RockExport rock = export.rocks[i];
                GameObject rockGo = new GameObject(string.IsNullOrEmpty(rock.name) ? $"Rock_{i:00}" : rock.name);
                rockGo.transform.SetParent(rocksParent.transform, false);
                rockGo.AddComponent<MeshFilter>().sharedMesh = rockMeshes[i];
                MeshRenderer rockRenderer = rockGo.AddComponent<MeshRenderer>();
                rockRenderer.sharedMaterial = rockMaterial;
                rockGo.SetActive(false);

                SerializedProperty element = rocksProp.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("_target").objectReferenceValue = rockGo.transform;

                int sampleCount = (rock.samples != null) ? rock.samples.Length : 0;
                SerializedProperty positions = element.FindPropertyRelative("_positions");
                SerializedProperty rotations = element.FindPropertyRelative("_rotations");
                SerializedProperty scales    = element.FindPropertyRelative("_scales");
                SerializedProperty visible   = element.FindPropertyRelative("_visible");
                positions.arraySize = sampleCount;
                rotations.arraySize = sampleCount;
                scales.arraySize    = sampleCount;
                visible.arraySize   = sampleCount;

                for (int s = 0; s < sampleCount; ++s)
                {
                    RockSample sample = rock.samples[s];
                    positions.GetArrayElementAtIndex(s).vector3Value    = sample.position;
                    rotations.GetArrayElementAtIndex(s).quaternionValue = sample.rotation;
                    scales.GetArrayElementAtIndex(s).vector3Value       = sample.scale;
                    visible.GetArrayElementAtIndex(s).boolValue         = sample.visible;
                }
            }

            ParticleSystem[] particles = MucheolImpactParticleBuilder.Build(root.transform);
            SerializedProperty particlesProperty = serialized.FindProperty("_particles");
            particlesProperty.arraySize = particles.Length;
            for (int i = 0; i < particles.Length; ++i)
            {
                particlesProperty.GetArrayElementAtIndex(i).objectReferenceValue = particles[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return root;
        }

        private static MeshRenderer BuildLayerQuad(Transform parent, string name, Mesh quad, Material material, float yOffset)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, yOffset, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
            return renderer;
        }

        private static GameObject SavePrefab(GameObject root)
        {
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH, out bool success);
            UnityEngine.Object.DestroyImmediate(root);
            if (!success)
            {
                throw new InvalidOperationException($"[MucheolImpactVfxBuilder] 프리팹 저장 실패: {PREFAB_PATH}");
            }

            return saved;
        }

        /****************************************
        *            Dev Scene
        ****************************************/

        private static void BuildOrUpdateScene(GameObject prefabAsset)
        {
            bool isNewScene = !File.Exists(AbsolutePath(SCENE_PATH));
            Scene scene = isNewScene
                ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single)
                : EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Single);

            EnsureCamera(scene);
            EnsureLight(scene);
            EnsureGroundPlane(scene);
            EnsureVolume(scene);
            EnsureEffectInstance(scene, prefabAsset);

            if (isNewScene)
            {
                EditorSceneManager.SaveScene(scene, SCENE_PATH);
            }
            else
            {
                EditorSceneManager.SaveScene(scene);
            }
        }

        private static void EnsureCamera(Scene scene)
        {
            GameObject camGo = FindRoot(scene, "Dev Camera");
            if (camGo == null)
            {
                camGo = new GameObject("Dev Camera");
                camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }

            Vector3 position = new Vector3(5.5f, 7f, -5.5f);
            Vector3 lookAt = new Vector3(0f, 0f, 2.3f);
            camGo.transform.position = position;
            camGo.transform.rotation = Quaternion.LookRotation((lookAt - position).normalized, Vector3.up);

            Camera camera = camGo.GetComponent<Camera>();
            camGo.tag = "MainCamera";
            camera.fieldOfView = 42f;
            camera.orthographic    = false;
            camera.allowHDR        = true;
            camera.clearFlags      = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.05f, 0.06f, 1f);

            UniversalAdditionalCameraData cameraData = camGo.GetComponent<UniversalAdditionalCameraData>();
            if (cameraData == null)
            {
                cameraData = camGo.AddComponent<UniversalAdditionalCameraData>();
            }
            cameraData.renderPostProcessing = true;
        }

        private static void EnsureLight(Scene scene)
        {
            if (FindRoot(scene, "Dev Directional Light") != null)
            {
                return;
            }

            GameObject lightGo = new GameObject("Dev Directional Light");
            Light light = lightGo.AddComponent<Light>();
            light.type      = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static void EnsureGroundPlane(Scene scene)
        {
            if (FindRoot(scene, "Dev Ground") != null)
            {
                return;
            }

            GameObject groundGo = GameObject.CreatePrimitive(PrimitiveType.Plane);
            groundGo.name = "Dev Ground";
            groundGo.transform.localScale = new Vector3(3f, 1f, 3f);
            UnityEngine.Object.DestroyImmediate(groundGo.GetComponent<Collider>());

            string materialPath = $"{MATERIALS_FOLDER}/M_MucheolDevGround.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                Shader shader = Shader.Find(LIT_SHADER_NAME);
                material = new Material(shader) { name = "M_MucheolDevGround" };
                material.color = new Color(0.08f, 0.08f, 0.09f, 1f);
                AssetDatabase.CreateAsset(material, materialPath);
            }

            groundGo.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        // URP Bloom은 패키지가 실제로 컴파일되어 있을 때만 쓰는 선택 요소다
        private static void EnsureVolume(Scene scene)
        {
            if (FindRoot(scene, "Dev Post Volume") != null)
            {
                return;
            }

            try
            {
                GameObject volumeGo = new GameObject("Dev Post Volume");
                Volume volume = volumeGo.AddComponent<Volume>();
                volume.isGlobal = true;

                VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VOLUME_PROFILE_PATH);
                if (profile == null)
                {
                    profile = ScriptableObject.CreateInstance<VolumeProfile>();
                    AssetDatabase.CreateAsset(profile, VOLUME_PROFILE_PATH);
                }

                if (!profile.TryGet(out Bloom bloom))
                {
                    bloom = profile.Add<Bloom>(true);
                    AssetDatabase.AddObjectToAsset(bloom, profile);
                }
                bloom.intensity.overrideState  = true;
                bloom.intensity.value          = 0.35f;
                bloom.threshold.overrideState  = true;
                bloom.threshold.value          = 1f;

                volume.sharedProfile = profile;
                EditorUtility.SetDirty(profile);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MucheolImpactVfxBuilder] 블룸 볼륨 설정을 건너뛴다: {ex.Message}");
            }
        }

        private static void EnsureEffectInstance(Scene scene, GameObject prefabAsset)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            GameObject instance = null;
            for (int i = 0; i < roots.Length; ++i)
            {
                if (PrefabUtility.GetCorrespondingObjectFromSource(roots[i]) == prefabAsset)
                {
                    instance = roots[i];
                    break;
                }
            }

            if (instance == null)
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, scene);
            }
            instance.transform.position = Vector3.zero;

            MucheolImpactVfxPlayer player = instance.GetComponent<MucheolImpactVfxPlayer>();
            SerializedObject serialized = new SerializedObject(player);
            serialized.FindProperty("_playOnStart").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Selection.activeGameObject = instance;
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; ++i)
            {
                if (roots[i].name == name)
                {
                    return roots[i];
                }
            }

            return null;
        }

        /****************************************
        *            Shared Helpers
        ****************************************/

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            string parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, Path.GetFileName(assetPath));
        }

        private static string AbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, assetPath);
        }

        private static string Capitalize(string value)
        {
            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        private static string Sanitize(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; ++i)
            {
                name = name.Replace(invalid[i], '_');
            }
            return name;
        }
    }
}
