// System
using System;
using System.IO;
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Dev;

namespace Shinshi.Editor
{
    /// <summary> 승인한 Blender 메시를 가져와 여우불 전용 에셋과 안전한 검수 씬을 만든다 </summary>
    public static class WolbaekFoxfireBuilder
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string ASSET_ROOT = "Assets/01.Scripts/05.Art/VFX/Wolbaek/Foxfire";
        public const string PREFAB     = "Assets/02.Prefab/VFX/Wolbaek/Foxfire/PF_WolbaekFoxfire.prefab";
        public const string SCENE      = "Assets/00.Scenes/Dev_VFX/Dev_Wolbaek_Foxfire.unity";
        public const string EVIDENCE   = ".claude/docs/2026-10-14/wolbaek-foxfire-blender";
        private const string PREFAB_ROOT = "Assets/02.Prefab/VFX/Wolbaek/Foxfire";
        private const int ROOT_COUNT  = 7;
        private const int PREVIEW_CELLS = 5;
        private const float CELL_SPACING = 3.6f;
        private const float GROUND_OFFSET = -0.035f;
        private const int FLOW_STATIONS = 9;
        private const int SPARK_VERTICES = 24;
        private const int GOLD_ROOT_INDEX = 3;
        private const int RED_ROOT_INDEX = 4;
        private static readonly string[] G_States = { "Flight", "Locked", "Impact", "Gold", "Red", "GoldImpact", "RedImpact" };
        private static readonly string[] G_IndividualNames = { "Flight", "Locked", "Impact", "GoldTarget", "RedHazard" };

        [Serializable]
        private sealed class ExportData
        {
            public ExportMesh[] _meshes = null;
            public string _source = null;
            public float _scale = 0f;
            public float[] _groundRadii = null;
        }

        [Serializable]
        private sealed class ExportMesh
        {
            public string _name = null;
            public string _state = null;
            public string _role = null;
            public float[] _vertices = null;
            public float[] _uv = null;
            public float[] _colors = null;
            public float[] _ripple = null;
            public int[] _triangles = null;
            public int[] _pieceIds = null;
            public string[] _pieceNames = null;
        }

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem("Shinshi/VFX/Wolbaek/Build Foxfire")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("PlayMode 중에는 프리팹을 재생성하지 않습니다.");
            }

            string source = Path.Combine(EVIDENCE, "foxfire_unity_meshes.json");
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("먼저 보존된 export_unity.py로 승인 원본을 export하세요.", source);
            }

            ExportData data = JsonUtility.FromJson<ExportData>(File.ReadAllText(source));
            if ((data == null) || (data._meshes == null) || (data._meshes.Length == 0))
            {
                throw new InvalidDataException("Blender 메시 export가 비어 있습니다.");
            }

            EnsureFolders();
            ConfigureTextures();
            WolbaekFoxfireSettingsSO settings = AssetDatabase.LoadAssetAtPath<WolbaekFoxfireSettingsSO>(
                ASSET_ROOT + "/SO_WolbaekFoxfire.asset");
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<WolbaekFoxfireSettingsSO>();
                AssetDatabase.CreateAsset(settings, ASSET_ROOT + "/SO_WolbaekFoxfire.asset");
            }

            Scene original = SceneManager.GetActiveScene();
            Scene temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(temporary);
            GameObject root = new GameObject("PF_WolbaekFoxfire");
            try
            {
                Transform[] stateRoots = new Transform[ROOT_COUNT];
                for (int i = 0; i < ROOT_COUNT; ++i)
                {
                    stateRoots[i] = new GameObject(G_States[i]).transform;
                    stateRoots[i].SetParent(root.transform, false);
                }

                Material flow = MaterialAsset("M_Foxfire_Flow", "Flow", false);
                Material solid = MaterialAsset("M_Foxfire_Marks", "Solid", true);
                Material petals = MaterialAsset("M_Foxfire_Petals", "Solid", false);
                foreach (ExportMesh exported in data._meshes)
                {
                    Validate(exported);
                    Mesh mesh = MeshAsset(exported);
                    int index = Array.IndexOf(G_States, exported._state);
                    if (index < 0)
                    {
                        throw new InvalidDataException("알 수 없는 상태: " + exported._state);
                    }

                    GameObject layer = new GameObject(exported._role + "_" + exported._name);
                    layer.transform.SetParent(stateRoots[index], false);
                    Renderer renderer;
                    bool moving = (exported._role == "Core") || (exported._role == "Flame")
                        || (exported._role == "Petals") || (exported._role == "Sparks");
                    if (moving)
                    {
                        SkinnedMeshRenderer skin = layer.AddComponent<SkinnedMeshRenderer>();
                        skin.sharedMesh = mesh;
                        BuildRig(layer, skin, mesh, exported);
                        renderer = skin;
                    }
                    else
                    {
                        layer.AddComponent<MeshFilter>().sharedMesh = mesh;
                        renderer = layer.AddComponent<MeshRenderer>();
                    }
                    renderer.sharedMaterial = exported._role == "Petals" ? petals
                        : ((exported._role == "Marks") || (exported._role == "Ground") || (exported._role == "Sparks")) ? solid : flow;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    GameObject afterimage = null;
                    if (exported._role == "Petals")
                    {
                        afterimage = UnityEngine.Object.Instantiate(layer, stateRoots[index]);
                        afterimage.name = layer.name + "_Afterimage";
                        SerializedObject ghost = new SerializedObject(afterimage.GetComponent<WolbaekFoxfireMotionRig>());
                        ghost.FindProperty("_sampleDelay").floatValue = 0.12f;
                        ghost.FindProperty("_opacityMultiplier").floatValue = 0.18f;
                        ghost.ApplyModifiedPropertiesWithoutUndo();
                    }
                    if (exported._state == "Impact")
                    {
                        for (int variant = 0; variant < 2; ++variant)
                        {
                            GameObject colored = UnityEngine.Object.Instantiate(layer, stateRoots[5 + variant]);
                            colored.name = layer.name;
                            MeshFilter filter = colored.GetComponent<MeshFilter>();
                            if (filter != null)
                            {
                                filter.sharedMesh = ColorVariantMesh(mesh, variant == 0);
                            }
                            else
                            {
                                colored.GetComponent<SkinnedMeshRenderer>().sharedMesh = ColorVariantMesh(mesh, variant == 0);
                            }
                            if (afterimage != null)
                            {
                                GameObject ghost = UnityEngine.Object.Instantiate(afterimage, stateRoots[5 + variant]);
                                ghost.name = afterimage.name;
                                ghost.GetComponent<SkinnedMeshRenderer>().sharedMesh = ColorVariantMesh(mesh, variant == 0);
                            }
                        }
                    }
                }

                for (int variant = GOLD_ROOT_INDEX; variant <= RED_ROOT_INDEX; ++variant)
                {
                    foreach (Transform layer in stateRoots[1])
                    {
                        if (layer.name.StartsWith("Petals_"))
                        {
                            GameObject copy = UnityEngine.Object.Instantiate(layer.gameObject, stateRoots[variant]);
                            copy.name = layer.name;
                        }
                    }
                }

                WolbaekFoxfirePlayer player = root.AddComponent<WolbaekFoxfirePlayer>();
                SerializedObject serialized = new SerializedObject(player);
                serialized.FindProperty("_settings").objectReferenceValue = settings;
                SerializedProperty roots = serialized.FindProperty("_stateRoots");
                SerializedProperty radii = serialized.FindProperty("_baseGroundRadii");
                roots.arraySize = ROOT_COUNT;
                radii.arraySize = ROOT_COUNT;
                for (int i = 0; i < ROOT_COUNT; ++i)
                {
                    roots.GetArrayElementAtIndex(i).objectReferenceValue = stateRoots[i];
                    radii.GetArrayElementAtIndex(i).floatValue = data._groundRadii[i];
                    stateRoots[i].gameObject.SetActive(false);
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PREFAB);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                EditorSceneManager.CloseScene(temporary, true);
                if (original.IsValid())
                {
                    SceneManager.SetActiveScene(original);
                }
            }

            AssetDatabase.SaveAssets();
            BuildIndividualPrefabs();
            BuildScene();
            Debug.Log("[WolbaekFoxfire] Build 완료: " + PREFAB);
        }

        public static string IndividualPrefab(int index)
        {
            if ((index < 0) || (index >= G_IndividualNames.Length))
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            return PREFAB_ROOT + "/PF_WolbaekFoxfire_" + G_IndividualNames[index] + ".prefab";
        }

        [MenuItem("Shinshi/VFX/Wolbaek/Build Foxfire Individual Prefabs")]
        public static void BuildIndividualPrefabs()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("PlayMode 중에는 프리팹을 생성하지 않습니다.");
            }
            WolbaekFoxfireSettingsSO original = AssetDatabase.LoadAssetAtPath<WolbaekFoxfireSettingsSO>(ASSET_ROOT + "/SO_WolbaekFoxfire.asset");
            for (int preset = 0; preset < G_IndividualNames.Length; ++preset)
            {
                string profilePath = ASSET_ROOT + "/SO_WolbaekFoxfire_" + G_IndividualNames[preset] + ".asset";
                WolbaekFoxfireSettingsSO settings = AssetDatabase.LoadAssetAtPath<WolbaekFoxfireSettingsSO>(profilePath);
                if (settings == null)
                {
                    settings = UnityEngine.Object.Instantiate(original);
                    settings.name = "SO_WolbaekFoxfire_" + G_IndividualNames[preset];
                    AssetDatabase.CreateAsset(settings, profilePath);
                }
                GameObject contents = PrefabUtility.LoadPrefabContents(PREFAB);
                try
                {
                    contents.name = "PF_WolbaekFoxfire_" + G_IndividualNames[preset];
                    SerializedObject player = new SerializedObject(contents.GetComponent<WolbaekFoxfirePlayer>());
                    player.FindProperty("_settings").objectReferenceValue = settings;
                    player.FindProperty("_previewMode").enumValueIndex = preset + 1;
                    SerializedProperty roots = player.FindProperty("_stateRoots");
                    for (int root = 0; root < ROOT_COUNT; ++root)
                    {
                        bool keep = root == preset || ((preset == 2) && ((root == 5) || (root == 6)));
                        if (!keep)
                        {
                            Transform removed = roots.GetArrayElementAtIndex(root).objectReferenceValue as Transform;
                            roots.GetArrayElementAtIndex(root).objectReferenceValue = null;
                            if (removed != null)
                            {
                                UnityEngine.Object.DestroyImmediate(removed.gameObject);
                            }
                        }
                    }
                    player.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(contents, IndividualPrefab(preset));
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
            AssetDatabase.SaveAssets();
        }

        public static void BuildScene()
        {
            Scene original = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            GameObject stage = GameObject.CreatePrimitive(PrimitiveType.Plane);
            stage.name = "Preview_NeutralGround";
            stage.transform.position = new Vector3(0f, GROUND_OFFSET, 0f);
            stage.transform.localScale = new Vector3(3f, 1f, 3f);
            UnityEngine.Object.DestroyImmediate(stage.GetComponent<Collider>());
            Material floor = AssetDatabase.LoadAssetAtPath<Material>(ASSET_ROOT + "/Materials/M_Foxfire_Stage.mat");
            if (floor == null)
            {
                floor = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                floor.name = "M_Foxfire_Stage";
                floor.SetColor("_BaseColor", new Color(0.024f, 0.032f, 0.045f));
                floor.SetFloat("_Smoothness", 0.15f);
                AssetDatabase.CreateAsset(floor, ASSET_ROOT + "/Materials/M_Foxfire_Stage.mat");
            }

            stage.GetComponent<Renderer>().sharedMaterial = floor;
            GameObject lightObject = new GameObject("Preview_MainLight");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.65f;
            light.color = new Color(0.68f, 0.76f, 1f);
            light.transform.rotation = Quaternion.Euler(48f, -25f, 0f);
            Camera camera = new GameObject("Foxfire_GameplayCamera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 13f, -17f);
            camera.transform.LookAt(new Vector3(0f, 1f, 0f));
            // 재사용 Particle 셰이더의 LinearEyeDepth는 원근 깊이를 전제로 한다.
            camera.orthographic = false;
            camera.fieldOfView = 32f;
            camera.depthTextureMode = DepthTextureMode.Depth;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.006f, 0.009f, 0.016f);
            camera.allowHDR = true;
            camera.tag = "MainCamera";
            UniversalAdditionalCameraData additional = camera.GetUniversalAdditionalCameraData();
            additional.requiresDepthTexture = true;
            additional.renderPostProcessing = true;
            GameObject volumeObject = new GameObject("Preview_BloomAndExposure");
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ASSET_ROOT + "/VP_FoxfirePreview.asset");
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                Bloom bloom = profile.Add<Bloom>();
                bloom.threshold.Override(0.7f);
                bloom.intensity.Override(0.75f);
                Tonemapping tone = profile.Add<Tonemapping>();
                tone.mode.Override(TonemappingMode.ACES);
                AssetDatabase.CreateAsset(profile, ASSET_ROOT + "/VP_FoxfirePreview.asset");
                foreach (VolumeComponent component in profile.components)
                {
                    AssetDatabase.AddObjectToAsset(component, profile);
                }
            }

            volume.sharedProfile = profile;
            WolbaekFoxfirePlayer[] players = new WolbaekFoxfirePlayer[PREVIEW_CELLS];
            for (int i = 0; i < PREVIEW_CELLS; ++i)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(IndividualPrefab(i));
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.name = "Foxfire_" + G_States[i];
                instance.transform.position = new Vector3((i - 2) * CELL_SPACING, 0f, 0f);
                players[i] = instance.GetComponent<WolbaekFoxfirePlayer>();
                GameObject caption = new GameObject("Label_" + G_States[i]);
                TextMeshPro label = caption.AddComponent<TextMeshPro>();
                label.text = G_States[i].ToUpperInvariant();
                label.fontSize = 3.6f;
                label.alignment = TextAlignmentOptions.Center;
                label.color = new Color(0.68f, 0.74f, 0.82f);
                label.rectTransform.anchoredPosition3D = new Vector3((i - 2) * CELL_SPACING, 0.4f, -2.4f);
                caption.transform.rotation = camera.transform.rotation;
            }

            WolbaekFoxfirePreviewBoard board = new GameObject("Preview_ParallelFiveStates").AddComponent<WolbaekFoxfirePreviewBoard>();
            SerializedObject boardSettings = new SerializedObject(board);
            SerializedProperty boardPlayers = boardSettings.FindProperty("_players");
            boardPlayers.arraySize = PREVIEW_CELLS;
            for (int i = 0; i < PREVIEW_CELLS; ++i)
            {
                boardPlayers.GetArrayElementAtIndex(i).objectReferenceValue = players[i];
            }

            boardSettings.ApplyModifiedPropertiesWithoutUndo();

            // 같은 경로의 이전 검수 씬이 열려 있으면 Unity가 새 additive 씬 저장을 거절한다.
            if ((original.IsValid()) && (original.path == SCENE))
            {
                if (original.isDirty)
                {
                    EditorSceneManager.CloseScene(scene, true);
                    throw new InvalidOperationException("기존 여우불 씬의 미저장 변경을 보존합니다.");
                }

                EditorSceneManager.CloseScene(original, true);
            }

            if (!EditorSceneManager.SaveScene(scene, SCENE))
            {
                throw new InvalidOperationException("새 여우불 씬 저장에 실패했습니다.");
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.CloseScene(scene, true);
            if (original.IsValid())
            {
                SceneManager.SetActiveScene(original);
            }
        }

        private static void EnsureFolders()
        {
            Directory.CreateDirectory(ASSET_ROOT + "/Models");
            Directory.CreateDirectory(ASSET_ROOT + "/Materials");
            Directory.CreateDirectory(ASSET_ROOT + "/Textures");
            Directory.CreateDirectory(Path.GetDirectoryName(PREFAB));
            Directory.CreateDirectory(Path.GetDirectoryName(SCENE));
            AssetDatabase.Refresh();
        }

        private static void ConfigureTextures()
        {
            foreach (string name in new[] { "Flow", "Solid" })
            {
                TextureImporter importer = AssetImporter.GetAtPath(ASSET_ROOT + "/Textures/T_Foxfire_" + name + ".png") as TextureImporter;
                if (importer == null)
                {
                    throw new InvalidDataException("텍스처 임포터 없음: " + name);
                }

                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.isReadable = false;
                importer.SaveAndReimport();
            }
        }

        private static Material MaterialAsset(string name, string texture, bool additive)
        {
            string path = ASSET_ROOT + "/Materials/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            material = new Material(Shader.Find("Shinshi/VFX/Particle"));
            material.name = name;
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ASSET_ROOT + "/Textures/T_Foxfire_" + texture + ".png"));
            material.SetColor("_Color", Color.white);
            material.SetFloat("_SrcBlend", (float)BlendMode.One);
            material.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SoftFade", 0.008f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Mesh MeshAsset(ExportMesh source)
        {
            string path = ASSET_ROOT + "/Models/" + source._name + ".asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool exists = mesh != null;
            if (!exists)
            {
                mesh = new Mesh();
            }

            int count = source._vertices.Length / 3;
            Vector3[] positions = new Vector3[count];
            Vector2[] uv = new Vector2[count];
            Color[] colors = new Color[count];
            for (int i = 0; i < count; ++i)
            {
                positions[i] = new Vector3(source._vertices[i * 3], source._vertices[i * 3 + 1], source._vertices[i * 3 + 2]);
                uv[i] = new Vector2(source._uv[i * 2], source._uv[i * 2 + 1]);
                colors[i] = new Color(source._colors[i * 4], source._colors[i * 4 + 1], source._colors[i * 4 + 2], source._colors[i * 4 + 3]);
            }

            mesh.Clear();
            mesh.ClearBlendShapes();
            mesh.name        = source._name;
            mesh.indexFormat = count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices    = positions;
            mesh.uv          = uv;
            mesh.colors      = colors;
            mesh.triangles   = source._triangles;
            if ((source._ripple != null) && (source._ripple.Length == source._vertices.Length)
                && ((source._role == "Flame") || (source._role == "Core")))
            {
                Vector3[] deltas = new Vector3[count];
                for (int i = 0; i < count; ++i)
                {
                    deltas[i] = new Vector3(source._ripple[i * 3], source._ripple[i * 3 + 1], source._ripple[i * 3 + 2]);
                }

                mesh.AddBlendShapeFrame("Airflow", 100f, deltas, null, null);
            }
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (!exists)
            {
                AssetDatabase.CreateAsset(mesh, path);
            }
            else
            {
                EditorUtility.SetDirty(mesh);
            }

            return mesh;
        }

        private static void BuildRig(GameObject layer, SkinnedMeshRenderer skin, Mesh mesh, ExportMesh source)
        {
            bool particles = (source._role == "Petals") || (source._role == "Sparks");
            bool sparks = source._role == "Sparks";
            int pieces = sparks ? source._vertices.Length / (SPARK_VERTICES * 3) : source._pieceNames.Length;
            int stations = particles ? 1 : FLOW_STATIONS;
            int count = pieces * stations;
            Transform[] bones = new Transform[count];
            Vector3[] centers = new Vector3[count];
            Vector4[] motion = new Vector4[count];
            float[] totals = new float[count];
            Vector3[] vertices = mesh.vertices;
            Vector2[] uv = mesh.uv;
            BoneWeight[] weights = new BoneWeight[vertices.Length];
            Matrix4x4[] bindposes = new Matrix4x4[count];
            for (int i = 0; i < vertices.Length; ++i)
            {
                int piece = sparks ? i / SPARK_VERTICES : source._pieceIds[i];
                float along = particles ? 0f : Mathf.Clamp01(uv[i].y) * (stations - 1);
                int lower = Mathf.Min(stations - 1, Mathf.FloorToInt(along));
                int upper = Mathf.Min(stations - 1, lower + 1);
                float mix = along - lower;
                int a = piece * stations + lower;
                int b = piece * stations + upper;
                weights[i] = new BoneWeight { boneIndex0 = a, boneIndex1 = b, weight0 = 1f - mix, weight1 = mix };
                centers[a] += vertices[i] * (1f - mix);
                totals[a] += 1f - mix;
                centers[b] += vertices[i] * mix;
                totals[b] += mix;
            }

            for (int i = 0; i < count; ++i)
            {
                int piece = i / stations;
                centers[i] /= Mathf.Max(0.0001f, totals[i]);
                Transform bone = new GameObject("FlowBone_" + i.ToString("D3")).transform;
                bone.SetParent(layer.transform, false);
                bone.localPosition = centers[i];
                bones[i] = bone;
                bindposes[i] = bone.worldToLocalMatrix * layer.transform.localToWorldMatrix;
                float coordinate = stations == 1 ? 1f : (float)(i % stations) / (stations - 1);
                bool tail = (source._state == "Flight") && (((!particles) && (source._pieceNames[piece].StartsWith("TAIL_"))) || (particles));
                float phase = Mathf.Repeat(piece * 2.399963f + source._name.Length * 0.53f, Mathf.PI * 2f);
                float variation = 0.55f + Mathf.Repeat(piece * 0.618034f, 1f);
                motion[i] = new Vector4(coordinate, phase, tail ? 1f : source._role == "Core" ? -1f : 0f,
                    particles ? variation : variation * (0.25f + coordinate * 0.75f));
            }

            mesh.boneWeights = weights;
            mesh.bindposes = bindposes;
            EditorUtility.SetDirty(mesh);
            skin.bones = bones;
            skin.rootBone = layer.transform;
            skin.quality = SkinQuality.Bone2;
            skin.localBounds = new Bounds(mesh.bounds.center, mesh.bounds.size + Vector3.one * 8f);
            layer.AddComponent<WolbaekFoxfireMotionRig>().Configure(bones, centers, motion,
                source._state == "Flight", particles, source._state == "Impact");
        }

        private static void Validate(ExportMesh mesh)
        {
            if ((mesh == null) || (mesh._vertices == null) || (mesh._uv == null)
                || (mesh._colors == null) || (mesh._triangles == null)
                || (mesh._vertices.Length % 3 != 0) || (mesh._triangles.Length % 3 != 0))
            {
                throw new InvalidDataException("메시 필수 배열이 없거나 길이가 잘못되었습니다.");
            }

            int count = mesh._vertices.Length / 3;
            if ((mesh._uv.Length != count * 2) || (mesh._colors.Length != count * 4))
            {
                throw new InvalidDataException("정점/UV/색 배열 길이가 일치하지 않습니다.");
            }

            foreach (float value in mesh._vertices)
            {
                if ((float.IsNaN(value)) || (float.IsInfinity(value)))
                {
                    throw new InvalidDataException("메시 정점에 비유한 값이 있습니다.");
                }
            }

            foreach (int index in mesh._triangles)
            {
                if ((index < 0) || (index >= count))
                {
                    throw new InvalidDataException("메시 인덱스가 정점 범위를 벗어났습니다.");
                }
            }
        }

        private static Mesh ColorVariantMesh(Mesh original, bool gold)
        {
            string name = original.name + (gold ? "_GoldImpact" : "_RedImpact");
            string path = ASSET_ROOT + "/Models/" + name + ".asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool exists = mesh != null;
            if (!exists)
            {
                mesh = UnityEngine.Object.Instantiate(original);
            }
            else
            {
                EditorUtility.CopySerialized(original, mesh);
            }

            mesh.name = name;
            Gradient gradient = new Gradient();
            Color bright = gold ? new Color(1f, 0.96f, 0.73f) : new Color(1f, 0.74f, 0.67f);
            Color middle = gold ? new Color(1f, 0.55f, 0.08f) : new Color(0.95f, 0.07f, 0.055f);
            Color dark   = gold ? new Color(0.40f, 0.14f, 0.012f) : new Color(0.28f, 0.008f, 0.018f);
            gradient.SetKeys(new[] { new GradientColorKey(bright, 0f), new GradientColorKey(middle, 0.42f),
                new GradientColorKey(dark, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            Color[] colors = mesh.colors;
            Vector2[] uv = mesh.uv;
            for (int i = 0; i < colors.Length; ++i)
            {
                float gain = Mathf.Max(colors[i].r, Mathf.Max(colors[i].g, colors[i].b));
                Color color = gradient.Evaluate(uv[i].y) * gain;
                color.a = colors[i].a;
                colors[i] = color;
            }

            mesh.colors = colors;
            if (!exists)
            {
                AssetDatabase.CreateAsset(mesh, path);
            }
            else
            {
                EditorUtility.SetDirty(mesh);
            }

            return mesh;
        }
    }
}
