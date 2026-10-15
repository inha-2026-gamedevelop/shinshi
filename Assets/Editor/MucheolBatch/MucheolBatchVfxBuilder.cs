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
    /// <summary> 번호별 Blender 산출물을 개별 무철 프리팹과 전용 확인 씬으로 조립한다 </summary>
    public static class MucheolBatchVfxBuilder
    {
        /****************************************
        *                Types
        ****************************************/

        // JsonUtility가 Blender의 필드 이름을 그대로 읽는 외부 직렬화 계약
#pragma warning disable CS0649
        [Serializable]
        private sealed class ExportRoot
        {
            public RockExport[] rocks;
            public Vector3 layerWorldSize;
            public Vector3 layerCenter;
            public float textureWorldSize;
            public Vector3 textureCenter;
            public float fps;
            public int startFrame;
            public int previewApprovedHitFrame;
            public int[] warningVisibleFrames;
            public ShockOffset[] shockOffsets;
            public Vector3[] spawnPoints;
            public TimingExport previewTiming;
            public ChargeExport charge;
        }

        [Serializable]
        private sealed class RockExport
        {
            public string name;
            public Vector3[] vertices;
            public int[] triangles;
            public Color[] colors;
            public float[] vertexAO;
            public RockSample[] samples;
        }

        [Serializable]
        private sealed class RockSample
        {
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public bool visible;
        }

        [Serializable]
        private sealed class TimingExport
        {
            public float telegraphSeconds;
            public float expansionSeconds;
            public float holdSeconds;
            public float fadeSeconds;
            public float debrisSeconds;
        }

        [Serializable]
        private sealed class ChargeExport
        {
            public float seconds;
            public Vector3 start;
            public Vector3 end;
            public int startFrame;
            public int stopFrame;
            public RockExport head;
            public float headTextureSize;
            public Vector3 headTextureLocalCenter;
        }

        [Serializable]
        private sealed class ShockOffset
        {
            public Vector3 position;
            public Vector3 scale;
        }
#pragma warning restore CS0649

        private struct PrefabOptions
        {
            public bool HasExisting;
            public float ChargeSeconds;
            public bool RepeatPreview;
            public bool PlayOnStart;
            public float PreviewGapSeconds;
            public MucheolPatternVfxSettingsSO Profile;
        }

        /****************************************
        *                Fields
        ****************************************/

        private const string ROOT_FOLDER = "Assets/01.Scripts/05.Art/VFX/Mucheol/ImpactBatch";
        private const string PREFAB_FOLDER = "Assets/02.Prefab/VFX/Mucheol";
        private const string SCENE_FOLDER = "Assets/00.Scenes/Dev_VFX";
        private const string SCENE_PATH = SCENE_FOLDER + "/Dev_Mucheol_02_07.unity";
        private const string EXTERNAL_ROOT = "D:/SHINSHIART/MucheolVFX/2026-10-13/";
        private const string LAYER_SHADER = "Shinshi/VFX/MucheolImpactLayer";
        private const string LIT_SHADER = "Universal Render Pipeline/Lit";
        private const string ROCK_SHADER = "Shinshi/VFX/MucheolBatchRock";
        private const string CAMERA_NAME = "MucheolBatch_PreviewCamera";
        private const string EFFECT_PREFIX = "MucheolBatch_";
        private const string GROUND_NAME = "MucheolBatch_Ground";
        private const string LIGHT_NAME = "MucheolBatch_KeyLight";
        private const string VOLUME_NAME = "MucheolBatch_Bloom";

        private const int FIRST_NUMBER = 2;
        private const int LAST_NUMBER = 7;
        private const int TEXTURE_SIZE = 2048;
        private const int SHAPE_SEGMENTS = 144;
        private const int PARTICLE_SEED = 261014;
        private const float SMALL_RADIUS = 5f;
        private const float LARGE_RADIUS = 6f;
        private const float BATTLEFIELD_RADIUS = 44f;
        private const float LINE_WIDTH = 3f;
        private const float LINE_LENGTH = 18f;
        private const float DANGER_HALF_ANGLE = 157.5f;
        private const float PARTICLE_ANGLE_MARGIN = 5f;
        private const float DEFAULT_CHARGE_SECONDS = 0.3333f;
        private const float GROUND_Y = -0.03f;
        private const float PREVIEW_GROUND_SIZE = 180f;
        private const float PREVIEW_FIELD_OF_VIEW = 45f;
        private const float PREVIEW_FAR_CLIP = 350f;
        private const float PREVIEW_CAMERA_HEIGHT_FACTOR = 2.5f;
        private const float PREVIEW_CAMERA_BACK_FACTOR = 2.1f;
        private const float PREVIEW_MIN_RADIUS = 6f;
        private const float PARTICLE_RADIUS_FACTOR = 0.82f;

        private static readonly string[] G_LayerNames = { "warning", "ground", "cracks", "shock", "flash", "root" };
        private static readonly string[] G_SourceFolders =
        {
            "production-02-04/02-circle/unity-export", "production-02-04/03-line/unity-export",
            "production-02-04/04-small-shockwave/unity-export", "production-05-07/05",
            "production-05-07/06", "production-05-07/07"
        };
        private static readonly string[] G_Labels =
        {
            "02 원형 반경 5m", "03 직선 3x18m", "04 작은 충격파 반경 5m",
            "05 큰 충격파 반경 6m, 독립 두 번째 타", "06 돌진 정지 3x18m", "07 전장 반경 44m, 후방 45도 안전"
        };
        private static readonly int MAIN_TEX_ID = Shader.PropertyToID("_MainTex");
        private static readonly int TINT_ID = Shader.PropertyToID("_Tint");
        private static readonly int EMISSIVE_ID = Shader.PropertyToID("_EmissiveBoost");
        private static readonly int OPACITY_ID = Shader.PropertyToID("_Opacity");

        /****************************************
        *                 Menu
        ****************************************/

        /// <summary> 모든 입력을 먼저 확인하고 02부터 07까지 프리팹만 생성한다, 열린 씬은 저장하지 않는다 </summary>
        [MenuItem("Shinshi/VFX/Mucheol 02-07/Build All Prefabs")]
        public static void BuildAllPrefabs()
        {
            GuardEditorState();
            ExportRoot[] exports = new ExportRoot[LAST_NUMBER - FIRST_NUMBER + 1];
            for (int number = FIRST_NUMBER; number <= LAST_NUMBER; ++number)
            {
                exports[number - FIRST_NUMBER] = ReadAndValidateExport(number);
            }

            for (int number = FIRST_NUMBER; number <= LAST_NUMBER; ++number)
            {
                BuildPrefab(number, exports[number - FIRST_NUMBER]);
            }

            SaveGeneratedAssets();
            Debug.Log("[MucheolBatchVfxBuilder] 02-07 개별 프리팹 생성 완료");
        }

        /// <summary> 해당 번호의 프리팹만 생성한다 </summary>
        public static void BuildNumber(int number)
        {
            GuardEditorState();
            GuardNumber(number);
            BuildPrefab(number, ReadAndValidateExport(number));
            SaveGeneratedAssets();
        }

        /// <summary> 번호별 프리팹 경로를 반환한다 </summary>
        public static string GetPrefabPath(int number)
        {
            GuardNumber(number);
            return $"{PREFAB_FOLDER}/PF_Mucheol_{number:00}.prefab";
        }

        /// <summary> 한 번호씩 활성화하는 전용 씬을 생성한다, 다른 씬은 저장하지 않는다 </summary>
        [MenuItem("Shinshi/VFX/Mucheol 02-07/Build Preview Scene")]
        public static void BuildPreviewScene()
        {
            GuardEditorState();
            GameObject[] prefabs = new GameObject[LAST_NUMBER - FIRST_NUMBER + 1];
            for (int number = FIRST_NUMBER; number <= LAST_NUMBER; ++number)
            {
                prefabs[number - FIRST_NUMBER] = LoadRequiredPrefab(number);
            }

            EnsureFolder(SCENE_FOLDER);
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(SCENE_PATH);
            bool closeAfterBuild = !scene.IsValid();
            if (!scene.IsValid())
            {
                scene = File.Exists(AbsolutePath(SCENE_PATH))
                    ? EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Additive)
                    : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            }

            try
            {
                SceneManager.SetActiveScene(scene);
                EnsurePreviewEnvironment(scene);
                for (int number = FIRST_NUMBER; number <= LAST_NUMBER; ++number)
                {
                    string name = EFFECT_PREFIX + number.ToString("00");
                    GameObject instance = FindRoot(scene, name);
                    if (instance == null)
                    {
                        instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[number - FIRST_NUMBER], scene);
                        instance.name = name;
                    }

                    instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    instance.transform.localScale = Vector3.one;
                    instance.SetActive(number == FIRST_NUMBER);
                    MucheolBatchVfxPlayer player = instance.GetComponent<MucheolBatchVfxPlayer>();
                    SerializedObject serialized = new SerializedObject(player);
                    serialized.FindProperty("_playOnStart").boolValue = true;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                FrameCamera(scene, FIRST_NUMBER);
                if (!EditorSceneManager.SaveScene(scene, SCENE_PATH))
                {
                    throw new InvalidOperationException("전용 무철 확인 씬 저장 실패");
                }

                SaveGeneratedAssets();
            }
            finally
            {
                if (previous.IsValid())
                {
                    SceneManager.SetActiveScene(previous);
                }

                if (closeAfterBuild)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }

            Debug.Log($"[MucheolBatchVfxBuilder] 전용 확인 씬 생성 완료: {SCENE_PATH}");
        }

        /// <summary> 열린 전용 씬에서 해당 번호만 활성화하고 Inspector와 카메라를 맞춘다 </summary>
        public static MucheolBatchVfxPlayer SelectNumber(int number)
        {
            GuardNumber(number);
            Scene scene = SceneManager.GetSceneByPath(SCENE_PATH);
            if ((!scene.IsValid()) || (!scene.isLoaded))
            {
                throw new InvalidOperationException($"먼저 전용 확인 씬을 연다: {SCENE_PATH}");
            }

            MucheolBatchVfxPlayer selected = null;
            for (int current = FIRST_NUMBER; current <= LAST_NUMBER; ++current)
            {
                GameObject instance = FindRoot(scene, EFFECT_PREFIX + current.ToString("00"));
                if (instance == null)
                {
                    throw new InvalidOperationException($"확인 씬에 {current:00} 프리팹이 없다");
                }

                MucheolBatchVfxPlayer player = instance.GetComponent<MucheolBatchVfxPlayer>();
                player.Stop();
                instance.SetActive(current == number);
                if (current == number)
                {
                    selected = player;
                }
            }

            FrameCamera(scene, number);
            Selection.activeGameObject = selected.gameObject;
            if (!Application.isPlaying)
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }

            return selected;
        }

        /// <summary> 번호를 선택하고 초 단위의 정지 시점을 표시한다 </summary>
        public static MucheolBatchVfxPlayer SampleNumber(int number, float elapsedSeconds)
        {
            MucheolBatchVfxPlayer player = SelectNumber(number);
            player.SamplePreview(elapsedSeconds);
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
            return player;
        }

        [MenuItem("Shinshi/VFX/Mucheol 02-07/Select 02")]
        private static void Select02()
        {
            SelectNumber(2);
        }
        [MenuItem("Shinshi/VFX/Mucheol 02-07/Select 03")]
        private static void Select03()
        {
            SelectNumber(3);
        }
        [MenuItem("Shinshi/VFX/Mucheol 02-07/Select 04")]
        private static void Select04()
        {
            SelectNumber(4);
        }
        [MenuItem("Shinshi/VFX/Mucheol 02-07/Select 05")]
        private static void Select05()
        {
            SelectNumber(5);
        }
        [MenuItem("Shinshi/VFX/Mucheol 02-07/Select 06")]
        private static void Select06()
        {
            SelectNumber(6);
        }
        [MenuItem("Shinshi/VFX/Mucheol 02-07/Select 07")]
        private static void Select07()
        {
            SelectNumber(7);
        }

        /****************************************
        *             Validation
        ****************************************/

        private static ExportRoot ReadAndValidateExport(int number)
        {
            string folder = SourceFolder(number);
            string path = Path.Combine(folder, "blender-export.json");
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"{number:00} Blender 내보내기가 없다", path);
            }

            ExportRoot export = JsonUtility.FromJson<ExportRoot>(File.ReadAllText(path));
            if ((export == null) || (export.rocks == null) || (export.rocks.Length == 0))
            {
                throw new InvalidDataException($"{number:00} 바위 메시와 궤적이 없다");
            }

            for (int i = 0; i < G_LayerNames.Length; ++i)
            {
                string texture = Path.Combine(folder, "Textures", G_LayerNames[i] + ".png");
                if (!File.Exists(texture))
                {
                    throw new FileNotFoundException($"{number:00} 레이어 텍스처가 없다", texture);
                }
            }

            if ((number == 6) && (!File.Exists(Path.Combine(folder, "Textures", "charge-head.png"))))
            {
                throw new FileNotFoundException("06의 구운 돌진 머리 텍스처가 없다", Path.Combine(folder, "Textures", "charge-head.png"));
            }

            if ((number == 6) && (!HasCharge(export)))
            {
                throw new InvalidDataException("06의 실제 돌진 시간과 이동 경로가 없다");
            }

            for (int i = 0; i < export.rocks.Length; ++i)
            {
                RockExport rock = export.rocks[i];
                if ((rock == null) || (rock.vertices == null) || (rock.vertices.Length == 0)
                    || (rock.triangles == null) || (rock.triangles.Length == 0)
                    || (rock.triangles.Length % 3 != 0) || (rock.samples == null) || (rock.samples.Length < 2))
                {
                    throw new InvalidDataException($"{number:00} 바위 {i}의 메시 또는 궤적이 잘못됐다");
                }

                for (int vertex = 0; vertex < rock.vertices.Length; ++vertex)
                {
                    if (!IsFinite(rock.vertices[vertex]))
                    {
                        throw new InvalidDataException($"{number:00} 바위 {i} 정점에 유효하지 않은 수가 있다");
                    }
                }

                for (int triangle = 0; triangle < rock.triangles.Length; ++triangle)
                {
                    if ((rock.triangles[triangle] < 0) || (rock.triangles[triangle] >= rock.vertices.Length))
                    {
                        throw new InvalidDataException($"{number:00} 바위 {i} 삼각형 인덱스가 범위를 벗어났다");
                    }
                }

                for (int sample = 0; sample < rock.samples.Length; ++sample)
                {
                    RockSample value = rock.samples[sample];
                    if ((value == null) || (!IsFinite(value.position)) || (!IsFinite(value.scale))
                        || (!IsFinite(value.rotation.x)) || (!IsFinite(value.rotation.y))
                        || (!IsFinite(value.rotation.z)) || (!IsFinite(value.rotation.w)))
                    {
                        throw new InvalidDataException($"{number:00} 바위 {i} 샘플 {sample}이 잘못됐다");
                    }
                }
            }

            return export;
        }

        private static void GuardEditorState()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Play를 종료한 뒤 빌더를 실행한다");
            }

            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if ((scene.isDirty) || ((string.IsNullOrEmpty(scene.path)) && (scene.rootCount > 0)))
                {
                    throw new InvalidOperationException($"저장하지 않은 씬이 있다: {scene.name}, 먼저 사용자가 저장하거나 정리한다");
                }
            }
        }

        private static void GuardNumber(int number)
        {
            if ((number < FIRST_NUMBER) || (number > LAST_NUMBER))
            {
                throw new ArgumentOutOfRangeException(nameof(number), "무철 번호는 02-07이어야 한다");
            }
        }

        /****************************************
        *          Asset Assembly
        ****************************************/

        private static void BuildPrefab(int number, ExportRoot export)
        {
            PrefabOptions options = ReadPrefabOptions(number);
            string rootFolder = AssetFolder(number);
            EnsureFolder(rootFolder + "/Textures");
            EnsureFolder(rootFolder + "/Materials");
            EnsureFolder(rootFolder + "/Meshes");
            EnsureFolder(PREFAB_FOLDER);
            ImportSourceFiles(number);
            Material[] materials = BuildLayerMaterials(number);
            Mesh layerMesh = BuildLayerMesh(number, export);
            Mesh[] rocks = BuildRockMeshes(number, export);
            Material rockMaterial = BuildRockMaterial(number);
            MucheolPatternVfxSettingsSO profile = (options.Profile != null)
                ? options.Profile : LoadOrCreateProfile(number, export);

            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                GameObject root = new GameObject($"PF_Mucheol_{number:00}");
                MucheolBatchVfxPlayer wrapper = root.AddComponent<MucheolBatchVfxPlayer>();
                GameObject impact = new GameObject("Impact");
                impact.transform.SetParent(root.transform, false);
                MucheolImpactVfxPlayer player = impact.AddComponent<MucheolImpactVfxPlayer>();
                SerializedObject serialized = new SerializedObject(player);
                serialized.FindProperty("_profile").objectReferenceValue = profile;
                serialized.FindProperty("_repeatPreview").boolValue = false;
                serialized.FindProperty("_playOnStart").boolValue = false;

                Transform shock = null;
                for (int i = 0; i < G_LayerNames.Length; ++i)
                {
                    string name = G_LayerNames[i];
                    float elevation = (name == "warning") ? 0.006f : (i + 1) * 0.001f;
                    MeshRenderer renderer = BuildLayer(impact.transform, name, layerMesh, materials[i], elevation);
                    serialized.FindProperty("_" + name).objectReferenceValue = renderer;
                    if (name == "shock")
                    {
                        shock = renderer.transform;
                    }
                }

                BindRocks(serialized.FindProperty("_rocks"), impact.transform, export, rocks, rockMaterial);
                ParticleSystem[] particles = MucheolBatchParticleBuilder.Build(impact.transform, rootFolder, number, export.spawnPoints);
                SerializedProperty particleReferences = serialized.FindProperty("_particles");
                particleReferences.arraySize = particles.Length;
                for (int i = 0; i < particles.Length; ++i)
                {
                    particleReferences.GetArrayElementAtIndex(i).objectReferenceValue = particles[i];
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
                SerializedObject batch = new SerializedObject(wrapper);
                batch.FindProperty("_player").objectReferenceValue = player;
                batch.FindProperty("_profile").objectReferenceValue = profile;
                if ((export.shockOffsets != null) && (export.shockOffsets.Length > 0))
                {
                    batch.FindProperty("_shockTransform").objectReferenceValue = shock;
                    SerializedProperty positions = batch.FindProperty("_shockPositions");
                    SerializedProperty scales = batch.FindProperty("_shockScales");
                    int shockCount = export.shockOffsets.Length;
                    if ((HasPreviewTiming(export)) && (export.fps > 0f))
                    {
                        shockCount = Mathf.Clamp(Mathf.RoundToInt(export.previewTiming.expansionSeconds * export.fps) + 1,
                            1, export.shockOffsets.Length);
                    }

                    positions.arraySize = shockCount;
                    scales.arraySize = shockCount;
                    for (int i = 0; i < shockCount; ++i)
                    {
                        Vector3 position = export.shockOffsets[i].position;
                        position.y = 0.005f;
                        positions.GetArrayElementAtIndex(i).vector3Value = position;
                        scales.GetArrayElementAtIndex(i).vector3Value = export.shockOffsets[i].scale;
                    }
                }
                if (number == 6)
                {
                    Transform head = BuildChargeHead(root.transform, rootFolder, export.charge);
                    float seconds = HasCharge(export) ? export.charge.seconds : DEFAULT_CHARGE_SECONDS;
                    Vector3 start = HasCharge(export) ? export.charge.start : Vector3.zero;
                    Vector3 end = HasCharge(export) ? export.charge.end : Vector3.forward * LINE_LENGTH;
                    batch.FindProperty("_chargeHead").objectReferenceValue = head;
                    batch.FindProperty("_chargeSeconds").floatValue = (IsFinite(seconds) && (seconds > 0f)) ? seconds : DEFAULT_CHARGE_SECONDS;
                    batch.FindProperty("_chargeStart").vector3Value = start;
                    batch.FindProperty("_chargeEnd").vector3Value = end;
                    if ((HasCharge(export)) && (export.charge.head != null) && (export.charge.head.samples != null))
                    {
                        BindTrack(batch.FindProperty("_chargeTrack"), head, export.charge.head.samples, 0);
                    }
                }

                if (options.HasExisting)
                {
                    batch.FindProperty("_chargeSeconds").floatValue = options.ChargeSeconds;
                    batch.FindProperty("_repeatPreview").boolValue = options.RepeatPreview;
                    batch.FindProperty("_playOnStart").boolValue = options.PlayOnStart;
                    batch.FindProperty("_previewGapSeconds").floatValue = options.PreviewGapSeconds;
                }

                batch.ApplyModifiedPropertiesWithoutUndo();
                wrapper.Stop();
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, GetPrefabPath(number));
                if (saved == null)
                {
                    throw new InvalidOperationException($"{number:00} 프리팹 저장 실패");
                }

                ValidatePrefab(number, saved);
            }
            finally
            {
                SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void ImportSourceFiles(int number)
        {
            string folder = AssetFolder(number);
            string source = SourceFolder(number);
            CopyAndImport(Path.Combine(source, "blender-export.json"), folder + "/blender-export.json");
            for (int i = 0; i < G_LayerNames.Length; ++i)
            {
                string path = folder + "/Textures/" + G_LayerNames[i] + ".png";
                CopyAndImport(Path.Combine(source, "Textures", G_LayerNames[i] + ".png"), path);
                ConfigureTexture(path);
            }

            if (number == 6)
            {
                string path = folder + "/Textures/charge-head.png";
                CopyAndImport(Path.Combine(source, "Textures", "charge-head.png"), path);
                ConfigureTexture(path);
            }
        }

        private static PrefabOptions ReadPrefabOptions(int number)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(GetPrefabPath(number));
            if ((existing == null) || (!existing.TryGetComponent(out MucheolBatchVfxPlayer wrapper)))
            {
                return default;
            }

            SerializedObject properties = new SerializedObject(wrapper);
            return new PrefabOptions
            {
                HasExisting = true,
                ChargeSeconds = properties.FindProperty("_chargeSeconds").floatValue,
                RepeatPreview = properties.FindProperty("_repeatPreview").boolValue,
                PlayOnStart = properties.FindProperty("_playOnStart").boolValue,
                PreviewGapSeconds = properties.FindProperty("_previewGapSeconds").floatValue,
                Profile = properties.FindProperty("_profile").objectReferenceValue as MucheolPatternVfxSettingsSO
            };
        }

        private static void ConfigureTexture(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"텍스처 importer가 없다: {path}");
            }

            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = TEXTURE_SIZE;
            importer.SaveAndReimport();
        }

        private static void CopyAndImport(string source, string assetPath)
        {
            File.Copy(source, AbsolutePath(assetPath), true);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }

        private static Material[] BuildLayerMaterials(int number)
        {
            Shader shader = RequireShader(LAYER_SHADER);
            Material[] materials = new Material[G_LayerNames.Length];
            string folder = AssetFolder(number);
            for (int i = 0; i < G_LayerNames.Length; ++i)
            {
                string name = G_LayerNames[i];
                string path = $"{folder}/Materials/M_Mucheol_{number:00}_{name}.mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader) { name = $"M_Mucheol_{number:00}_{name}" };
                    material.SetColor(TINT_ID, Color.white);
                    material.SetFloat(EMISSIVE_ID, (name == "ground") ? 1f : 1.4f);
                    material.SetFloat(OPACITY_ID, 1f);
                    AssetDatabase.CreateAsset(material, path);
                }

                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{folder}/Textures/{name}.png");
                if (texture == null)
                {
                    throw new InvalidOperationException($"레이어 텍스처 수입 실패: {number:00} {name}");
                }

                // 재제작한 이미지도 기존 material GUID를 유지하며 최신 텍스처를 참조한다
                material.SetTexture(MAIN_TEX_ID, texture);
                EditorUtility.SetDirty(material);
                materials[i] = material;
            }

            return materials;
        }

        private static Material BuildRockMaterial(int number)
        {
            string path = AssetFolder(number) + $"/Materials/M_Mucheol_{number:00}_Rock.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            material = new Material(RequireShader(ROCK_SHADER)) { name = $"M_Mucheol_{number:00}_Rock" };
            material.SetColor("_BaseColor", new Color(0.33f, 0.26f, 0.16f, 1f));
            material.SetFloat("_Smoothness", 0.15f);
            material.SetFloat("_Metallic", 0.1f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static MucheolPatternVfxSettingsSO LoadOrCreateProfile(int number, ExportRoot export)
        {
            string path = AssetFolder(number) + $"/Timing_{number:00}.asset";
            MucheolPatternVfxSettingsSO profile = AssetDatabase.LoadAssetAtPath<MucheolPatternVfxSettingsSO>(path);
            if (profile != null)
            {
                return profile;
            }

            profile = ScriptableObject.CreateInstance<MucheolPatternVfxSettingsSO>();
            SerializedObject settings = new SerializedObject(profile);
            settings.FindProperty("_patternLabel").stringValue = G_Labels[number - FIRST_NUMBER];
            if ((HasCharge(export)) && (IsFinite(export.fps)) && (export.fps > 0f)
                && (export.charge.startFrame > 0))
            {
                float warning = (export.charge.startFrame - 1) / export.fps;
                SetTiming(settings, "_telegraphSeconds", warning, true);
            }
            if (HasPreviewTiming(export))
            {
                SetTiming(settings, "_telegraphSeconds", export.previewTiming.telegraphSeconds, true);
                SetTiming(settings, "_expansionSeconds", export.previewTiming.expansionSeconds, false);
                SetTiming(settings, "_holdSeconds", export.previewTiming.holdSeconds, true);
                SetTiming(settings, "_fadeSeconds", export.previewTiming.fadeSeconds, false);
                SetTiming(settings, "_debrisSeconds", export.previewTiming.debrisSeconds, false);
            }

            settings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(profile, path);
            return profile;
        }

        // JsonUtility는 없는 중첩 객체도 0으로 생성하므로 null만으로 입력 존재를 판단하지 않는다
        private static bool HasPreviewTiming(ExportRoot export)
        {
            TimingExport timing = export.previewTiming;
            return (timing != null) && (IsFinite(timing.telegraphSeconds)) && (timing.telegraphSeconds >= 0f)
                && (IsFinite(timing.expansionSeconds)) && (timing.expansionSeconds > 0f)
                && (IsFinite(timing.holdSeconds)) && (timing.holdSeconds >= 0f)
                && (IsFinite(timing.fadeSeconds)) && (timing.fadeSeconds > 0f)
                && (IsFinite(timing.debrisSeconds)) && (timing.debrisSeconds > 0f);
        }

        private static bool HasCharge(ExportRoot export)
        {
            ChargeExport charge = export.charge;
            return (charge != null) && (IsFinite(charge.seconds)) && (charge.seconds > 0f)
                && (IsFinite(charge.start)) && (IsFinite(charge.end)) && (charge.start != charge.end);
        }

        private static void SetTiming(SerializedObject settings, string field, float value, bool allowZero)
        {
            if ((IsFinite(value)) && ((value > 0f) || ((allowZero) && (value == 0f))))
            {
                settings.FindProperty(field).floatValue = value;
            }
        }

        /****************************************
        *                Meshes
        ****************************************/

        private static Mesh BuildLayerMesh(int number, ExportRoot export)
        {
            float radius = Radius(number);
            Vector3 size = export.layerWorldSize;
            if (((size.x <= 0f) || (size.z <= 0f)) && (export.textureWorldSize > 0f))
            {
                size = new Vector3(export.textureWorldSize, 0f, export.textureWorldSize);
            }

            if ((!IsFinite(size)) || (size.x <= 0f) || (size.z <= 0f))
            {
                float fallbackSize = ((number == 3) || (number == 6)) ? 20f : radius * 2f;
                size = new Vector3(fallbackSize, 0f, fallbackSize);
            }

            Vector3 center = export.layerCenter;
            if (export.textureWorldSize > 0f)
            {
                center = export.textureCenter;
            }
            if ((!IsFinite(center)) || (((number == 3) || (number == 6)) && (center.z <= 0f)))
            {
                center = ((number == 3) || (number == 6)) ? Vector3.forward * (LINE_LENGTH * 0.5f) : Vector3.zero;
            }

            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            if ((number == 3) || (number == 6))
            {
                float half = LINE_WIDTH * 0.5f;
                vertices.Add(new Vector3(-half, 0f, 0f));
                vertices.Add(new Vector3(half, 0f, 0f));
                vertices.Add(new Vector3(half, 0f, LINE_LENGTH));
                vertices.Add(new Vector3(-half, 0f, LINE_LENGTH));
                triangles.AddRange(new[] { 0, 2, 1, 0, 3, 2 });
            }
            else
            {
                float halfAngle = (number == 7) ? DANGER_HALF_ANGLE : 180f;
                vertices.Add(Vector3.zero);
                for (int i = 0; i <= SHAPE_SEGMENTS; ++i)
                {
                    float angle = Mathf.Lerp(-halfAngle, halfAngle, (float)i / SHAPE_SEGMENTS) * Mathf.Deg2Rad;
                    vertices.Add(new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius));
                    if (i > 0)
                    {
                        triangles.Add(0);
                        triangles.Add(i);
                        triangles.Add(i + 1);
                    }
                }
            }

            List<Vector2> uv = new List<Vector2>(vertices.Count);
            for (int i = 0; i < vertices.Count; ++i)
            {
                uv.Add(new Vector2((vertices[i].x - center.x) / size.x + 0.5f,
                    (vertices[i].z - center.z) / size.z + 0.5f));
            }

            string path = AssetFolder(number) + $"/Meshes/SM_Mucheol_{number:00}_Footprint.asset";
            Mesh mesh = LoadOrNewMesh(path);
            mesh.Clear();
            mesh.name = $"SM_Mucheol_{number:00}_Footprint";
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            SaveMesh(mesh, path);
            return mesh;
        }

        private static Mesh[] BuildRockMeshes(int number, ExportRoot export)
        {
            Mesh[] meshes = new Mesh[export.rocks.Length];
            for (int i = 0; i < export.rocks.Length; ++i)
            {
                RockExport rock = export.rocks[i];
                string path = AssetFolder(number) + $"/Meshes/SM_Mucheol_{number:00}_Rock_{i:00}.asset";
                Mesh mesh = LoadOrNewMesh(path);
                mesh.Clear();
                mesh.name = $"SM_Mucheol_{number:00}_Rock_{i:00}";
                mesh.indexFormat = (rock.vertices.Length > ushort.MaxValue) ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetVertices(rock.vertices);
                mesh.SetTriangles(rock.triangles, 0);
                if ((rock.colors != null) && (rock.colors.Length == rock.vertices.Length))
                {
                    mesh.SetColors(rock.colors);
                }
                else
                {
                    Color[] colors = new Color[rock.vertices.Length];
                    for (int vertex = 0; vertex < colors.Length; ++vertex)
                    {
                        float ao = ((rock.vertexAO != null) && (rock.vertexAO.Length == rock.vertices.Length))
                            ? Mathf.Clamp01(rock.vertexAO[vertex]) : 1f;
                        colors[vertex] = new Color(ao, ao, ao, 1f);
                    }

                    mesh.colors = colors;
                }

                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                SaveMesh(mesh, path);
                meshes[i] = mesh;
            }

            return meshes;
        }

        private static Mesh LoadOrNewMesh(string path)
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            return (mesh != null) ? mesh : new Mesh();
        }

        private static void SaveMesh(Mesh mesh, string path)
        {
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mesh)))
            {
                AssetDatabase.CreateAsset(mesh, path);
            }
            else
            {
                EditorUtility.SetDirty(mesh);
            }
        }

        private static MeshRenderer BuildLayer(Transform parent, string name, Mesh mesh, Material material, float elevation)
        {
            GameObject layer = new GameObject(name);
            layer.transform.SetParent(parent, false);
            layer.transform.localPosition = Vector3.up * elevation;
            layer.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = layer.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
            return renderer;
        }

        private static void BindRocks(SerializedProperty tracks, Transform parent, ExportRoot export,
            Mesh[] meshes, Material material)
        {
            GameObject rocksParent = new GameObject("Rocks");
            rocksParent.transform.SetParent(parent, false);
            tracks.arraySize = export.rocks.Length;
            for (int i = 0; i < export.rocks.Length; ++i)
            {
                RockExport rock = export.rocks[i];
                GameObject item = new GameObject(string.IsNullOrEmpty(rock.name) ? $"Rock_{i:00}" : rock.name);
                item.transform.SetParent(rocksParent.transform, false);
                item.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                item.AddComponent<MeshRenderer>().sharedMaterial = material;
                item.SetActive(false);
                int firstSample = Mathf.Clamp(export.previewApprovedHitFrame - export.startFrame, 0, rock.samples.Length - 1);
                BindTrack(tracks.GetArrayElementAtIndex(i), item.transform, rock.samples, firstSample);
            }
        }

        private static void BindTrack(SerializedProperty track, Transform target, RockSample[] samples, int firstSample)
        {
            track.FindPropertyRelative("_target").objectReferenceValue = target;
            SerializedProperty positions = track.FindPropertyRelative("_positions");
            SerializedProperty rotations = track.FindPropertyRelative("_rotations");
            SerializedProperty scales = track.FindPropertyRelative("_scales");
            SerializedProperty visible = track.FindPropertyRelative("_visible");
            int length = samples.Length - firstSample;
            positions.arraySize = length;
            rotations.arraySize = length;
            scales.arraySize = length;
            visible.arraySize = length;
            for (int sample = 0; sample < length; ++sample)
            {
                RockSample value = samples[firstSample + sample];
                positions.GetArrayElementAtIndex(sample).vector3Value = value.position;
                rotations.GetArrayElementAtIndex(sample).quaternionValue = value.rotation;
                scales.GetArrayElementAtIndex(sample).vector3Value = value.scale;
                visible.GetArrayElementAtIndex(sample).boolValue = value.visible;
            }
        }

        private static Transform BuildChargeHead(Transform parent, string folder, ChargeExport charge)
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/Textures/charge-head.png");
            if (texture == null)
            {
                throw new InvalidOperationException("06 돌진 머리 텍스처를 수입하지 못했다");
            }

            string path = folder + "/Meshes/SM_Mucheol_06_ChargeHead.asset";
            Mesh mesh = LoadOrNewMesh(path);
            float size = ((charge != null) && (IsFinite(charge.headTextureSize)) && (charge.headTextureSize > 0f))
                ? charge.headTextureSize : LINE_WIDTH;
            Vector3 center = (charge != null) ? charge.headTextureLocalCenter : Vector3.zero;
            float half = size * 0.5f;
            mesh.Clear();
            mesh.name = "SM_Mucheol_06_ChargeHead";
            mesh.vertices = new[]
            {
                center + new Vector3(-half, 0.08f, -half), center + new Vector3(half, 0.08f, -half),
                center + new Vector3(half, 0.08f, half), center + new Vector3(-half, 0.08f, half)
            };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            SaveMesh(mesh, path);

            string materialPath = folder + "/Materials/M_Mucheol_06_ChargeCrest.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            Shader shader = RequireShader(LAYER_SHADER);
            if (material == null)
            {
                material = new Material(shader) { name = "M_Mucheol_06_ChargeCrest" };
                AssetDatabase.CreateAsset(material, materialPath);
            }

            material.shader = shader;
            material.SetTexture(MAIN_TEX_ID, texture);
            material.SetColor(TINT_ID, Color.white);
            material.SetFloat(EMISSIVE_ID, 1.4f);
            material.SetFloat(OPACITY_ID, 1f);
            EditorUtility.SetDirty(material);
            GameObject head = new GameObject("ChargeHead");
            head.transform.SetParent(parent, false);
            MeshRenderer renderer = BuildLayer(head.transform, "PressureLayer", mesh, material, 0f);
            renderer.enabled = true;
            head.SetActive(false);
            return head.transform;
        }

        /****************************************
        *             Inspection
        ****************************************/

        /// <summary> 번호별 프리팹의 메시, 재질, 타이밍 참조와 누락 스크립트를 확인한다 </summary>
        public static void ValidateAllPrefabs()
        {
            for (int number = FIRST_NUMBER; number <= LAST_NUMBER; ++number)
            {
                ValidatePrefab(number, LoadRequiredPrefab(number));
            }

            Debug.Log("[MucheolBatchVfxBuilder] 02-07 프리팹 참조 검사 통과");
        }

        private static void ValidatePrefab(int number, GameObject prefab)
        {
            MucheolBatchVfxPlayer player = prefab.GetComponent<MucheolBatchVfxPlayer>();
            if ((player == null) || (player.Profile == null)
                || (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(player.Profile))))
            {
                throw new InvalidOperationException($"{number:00} 재생기 또는 번호별 타이밍 참조가 잘못됐다");
            }

            SerializedObject wrapper = new SerializedObject(player);
            MucheolImpactVfxPlayer impact = wrapper.FindProperty("_player").objectReferenceValue as MucheolImpactVfxPlayer;
            if ((impact == null) || (new SerializedObject(impact).FindProperty("_profile").objectReferenceValue != player.Profile))
            {
                throw new InvalidOperationException($"{number:00} 외부와 내부 타격의 프로필 참조가 다르다");
            }

            Transform[] children = prefab.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; ++i)
            {
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(children[i].gameObject) > 0)
                {
                    throw new InvalidOperationException($"{number:00} 누락 스크립트: {children[i].name}");
                }
            }

            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; ++i)
            {
                if (filters[i].sharedMesh == null)
                {
                    throw new InvalidOperationException($"{number:00} 누락 메시: {filters[i].name}");
                }
            }

            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; ++i)
            {
                Material[] materials = renderers[i].sharedMaterials;
                if (materials.Length == 0)
                {
                    throw new InvalidOperationException($"{number:00} 빈 재질: {renderers[i].name}");
                }

                for (int material = 0; material < materials.Length; ++material)
                {
                    if ((materials[material] == null) || (materials[material].shader == null)
                        || (materials[material].shader.name == "Hidden/InternalErrorShader"))
                    {
                        throw new InvalidOperationException($"{number:00} 누락 재질 또는 셰이더: {renderers[i].name}");
                    }
                }
            }
        }

        /****************************************
        *            Preview Scene
        ****************************************/

        private static void EnsurePreviewEnvironment(Scene scene)
        {
            GameObject ground = FindRoot(scene, GROUND_NAME);
            if (ground == null)
            {
                ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                SceneManager.MoveGameObjectToScene(ground, scene);
                ground.name = GROUND_NAME;
                UnityEngine.Object.DestroyImmediate(ground.GetComponent<Collider>());
                ground.transform.localScale = Vector3.one * (PREVIEW_GROUND_SIZE / 10f);
                ground.transform.position = Vector3.up * GROUND_Y;
                string path = ROOT_FOLDER + "/M_MucheolBatch_PreviewGround.mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(RequireShader(LIT_SHADER));
                    material.color = new Color(0.085f, 0.075f, 0.065f, 1f);
                    material.SetFloat("_Smoothness", 0.1f);
                    AssetDatabase.CreateAsset(material, path);
                }

                ground.GetComponent<MeshRenderer>().sharedMaterial = material;
            }

            GameObject light = FindRoot(scene, LIGHT_NAME);
            if (light == null)
            {
                light = new GameObject(LIGHT_NAME);
                SceneManager.MoveGameObjectToScene(light, scene);
                Light component = light.AddComponent<Light>();
                component.type = LightType.Directional;
                component.intensity = 1.1f;
                light.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            }

            GameObject camera = FindRoot(scene, CAMERA_NAME);
            if (camera == null)
            {
                camera = new GameObject(CAMERA_NAME);
                SceneManager.MoveGameObjectToScene(camera, scene);
                Camera component = camera.AddComponent<Camera>();
                component.clearFlags = CameraClearFlags.SolidColor;
                component.backgroundColor = new Color(0.025f, 0.025f, 0.03f, 1f);
                component.fieldOfView = PREVIEW_FIELD_OF_VIEW;
                component.farClipPlane = PREVIEW_FAR_CLIP;
                component.allowHDR = true;
                camera.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
            }

            GameObject volume = FindRoot(scene, VOLUME_NAME);
            if (volume == null)
            {
                string path = ROOT_FOLDER + "/VolumeProfile_MucheolBatch.asset";
                VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
                if (profile == null)
                {
                    profile = ScriptableObject.CreateInstance<VolumeProfile>();
                    Bloom bloom = profile.Add<Bloom>(true);
                    bloom.intensity.Override(0.35f);
                    bloom.threshold.Override(1f);
                    AssetDatabase.CreateAsset(profile, path);
                    AssetDatabase.AddObjectToAsset(bloom, profile);
                }

                volume = new GameObject(VOLUME_NAME);
                SceneManager.MoveGameObjectToScene(volume, scene);
                Volume component = volume.AddComponent<Volume>();
                component.isGlobal = true;
                component.sharedProfile = profile;
            }
        }

        private static void FrameCamera(Scene scene, int number)
        {
            GameObject camera = FindRoot(scene, CAMERA_NAME);
            if (camera == null)
            {
                throw new InvalidOperationException("전용 확인 씬 카메라가 없다");
            }

            bool line = (number == 3) || (number == 6);
            float radius = Mathf.Max(PREVIEW_MIN_RADIUS, line ? LINE_LENGTH * 0.55f : Radius(number));
            Vector3 center = line ? Vector3.forward * (LINE_LENGTH * 0.5f) : Vector3.zero;
            camera.transform.position = center + new Vector3(0f,
                radius * PREVIEW_CAMERA_HEIGHT_FACTOR, -radius * PREVIEW_CAMERA_BACK_FACTOR);
            camera.transform.LookAt(center);
        }

        /****************************************
        *               Helpers
        ****************************************/

        internal static float Radius(int number)
        {
            return (number == 7) ? BATTLEFIELD_RADIUS : ((number == 5) ? LARGE_RADIUS : SMALL_RADIUS);
        }

        internal static Vector3[] BuildParticlePositions(int number, int count, float margin)
        {
            Vector3[] positions = new Vector3[count];
            System.Random random = new System.Random(PARTICLE_SEED + number);
            for (int i = 0; i < count; ++i)
            {
                float x = (float)random.NextDouble();
                float z = (float)random.NextDouble();
                if ((number == 3) || (number == 6))
                {
                    float half = Mathf.Max(0f, LINE_WIDTH * 0.5f - margin);
                    positions[i] = new Vector3(Mathf.Lerp(-half, half, x),
                        0.08f, Mathf.Lerp(margin, LINE_LENGTH - margin, z));
                }
                else
                {
                    float outer = Mathf.Min(Radius(number) * PARTICLE_RADIUS_FACTOR, Radius(number) - margin);
                    float inner = (number == 7) ? margin : 0f;
                    float radius = Mathf.Sqrt(Mathf.Lerp(inner * inner, outer * outer, z));
                    float safeMargin = (number == 7)
                        ? Mathf.Asin(Mathf.Clamp01(margin / Mathf.Max(radius, margin))) * Mathf.Rad2Deg : 0f;
                    float half = (number == 7) ? DANGER_HALF_ANGLE - Mathf.Max(safeMargin, PARTICLE_ANGLE_MARGIN) : 180f;
                    float angle = Mathf.Lerp(-half, half, x) * Mathf.Deg2Rad;
                    positions[i] = new Vector3(Mathf.Sin(angle) * radius, 0.08f, Mathf.Cos(angle) * radius);
                }
            }

            return positions;
        }

        internal static bool ContainsParticleBounds(int number, Vector3 position, float margin)
        {
            if (!IsFinite(position))
            {
                return false;
            }

            if ((number == 3) || (number == 6))
            {
                return (Mathf.Abs(position.x) <= LINE_WIDTH * 0.5f - margin)
                    && (position.z >= margin) && (position.z <= LINE_LENGTH - margin);
            }

            Vector2 point = new Vector2(position.x, position.z);
            if (point.magnitude > Radius(number) - margin)
            {
                return false;
            }

            if (number != 7)
            {
                return true;
            }

            float angle = Mathf.Atan2(point.x, point.y) * Mathf.Rad2Deg;
            if (Mathf.Abs(angle) > DANGER_HALF_ANGLE)
            {
                return false;
            }

            float radians = DANGER_HALF_ANGLE * Mathf.Deg2Rad;
            Vector2 left = new Vector2(-Mathf.Sin(radians), Mathf.Cos(radians));
            Vector2 right = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
            float leftDistance = Vector2.Distance(point, left * Mathf.Max(0f, Vector2.Dot(point, left)));
            float rightDistance = Vector2.Distance(point, right * Mathf.Max(0f, Vector2.Dot(point, right)));
            return (leftDistance >= margin) && (rightDistance >= margin);
        }

        private static GameObject LoadRequiredPrefab(int number)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GetPrefabPath(number));
            if (prefab == null)
            {
                throw new InvalidOperationException($"먼저 프리팹을 생성한다: {GetPrefabPath(number)}");
            }

            return prefab;
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

        private static Shader RequireShader(string name)
        {
            Shader shader = Shader.Find(name);
            if (shader == null)
            {
                throw new InvalidOperationException($"셰이더가 없다: {name}");
            }

            return shader;
        }

        private static string AssetFolder(int number)
        {
            return ROOT_FOLDER + $"/{number:00}";
        }

        private static string SourceFolder(int number)
        {
            return Path.Combine(EXTERNAL_ROOT, G_SourceFolders[number - FIRST_NUMBER]);
        }

        private static string AbsolutePath(string assetPath)
        {
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static void SaveGeneratedAssets()
        {
            string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { ROOT_FOLDER });
            for (int i = 0; i < guids.Length; ++i)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset != null)
                {
                    AssetDatabase.SaveAssetIfDirty(asset);
                }
            }
        }

        private static bool IsFinite(float value)
        {
            return (!float.IsNaN(value)) && (!float.IsInfinity(value));
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }
    }
}
