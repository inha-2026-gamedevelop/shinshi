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

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Dev;

namespace Shinshi.Editor
{
    public static class WolbaekBeamBuilder
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string ROOT     = "Assets/01.Scripts/05.Art/VFX/Wolbaek/Beams";
        public const string PREFABS  = "Assets/02.Prefab/VFX/Wolbaek/Beams";
        public const string SCENE    = "Assets/00.Scenes/Dev_VFX/Dev_Wolbaek_Beams.unity";
        public const string EVIDENCE = ".claude/docs/2026-10-14/wolbaek-beams-blender";
        private const int STATIONS  = 9;

        private static readonly string[] G_Names =
        {
            "Release", "Sustain", "Star", "Grid"
        };
        private static readonly int[] G_Capacities =
        {
            1, 2, 10, 12
        };

        [Serializable]
        private sealed class Export
        {
            public Part[] _meshes;
        }

        [Serializable]
        private sealed class Part
        {
            public string _name, _role;
            public float[] _vertices, _uv, _colors;
            public int[] _pieceIds, _triangles;
        }

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem("Shinshi/VFX/Wolbaek/Build Beams")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("PlayMode 생성 금지");
            }

            Directory.CreateDirectory(ROOT + "/Models");
            Directory.CreateDirectory(ROOT + "/Materials");
            Directory.CreateDirectory(ROOT + "/Textures");
            Directory.CreateDirectory(PREFABS);
            AssetDatabase.Refresh();
            Export data = JsonUtility.FromJson<Export>(File.ReadAllText(EVIDENCE + "/beam_geometry.json"));
            Material flow = MaterialAsset("M_Beam_Flow", true);
            Material coreMaterial = MaterialAsset("M_Beam_CoreSoft", false);
            Material cloudMaterial = MaterialAsset("M_Beam_VeilCloud", true);
            Material solid = MaterialAsset("M_Beam_Boundary", false);
            Scene original = SceneManager.GetActiveScene();
            Scene temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(temporary);
            GameObject template = new GameObject("Line");
            Renderer[] renderers = new Renderer[4];
            List<Transform> bones = new List<Transform>();
            List<Vector3> rest = new List<Vector3>();
            List<Vector4> motion = new List<Vector4>();

            try
            {
                foreach (Part part in data._meshes)
                {
                    int role = part._role == "Core" ? 0 : part._role == "Veil" ? 1 : part._role == "Ground" ? 2 : 3;
                    GameObject layer = new GameObject(part._role);
                    layer.transform.SetParent(template.transform, false);
                    int count = part._vertices.Length / 3;
                    Vector3[] positions = new Vector3[count];
                    Vector2[] uv = new Vector2[count];
                    Color[] colors = new Color[count];

                    for (int i = 0; i < count; ++i)
                    {
                        positions[i] = new Vector3(part._vertices[i * 3], part._vertices[i * 3 + 1], part._vertices[i * 3 + 2]);
                        uv[i] = new Vector2(part._uv[i * 2], part._uv[i * 2 + 1]);
                        colors[i] = new Color(part._colors[i * 4], part._colors[i * 4 + 1], part._colors[i * 4 + 2], part._colors[i * 4 + 3]);

                        if (role == 0)
                        {
                            colors[i] = new Color(9f, 10f, 12f, colors[i].a);
                        }
                        else if (role == 1)
                        {
                            colors[i] = new Color(0.9f, 1.35f, 2.1f, colors[i].a * 1.6f);
                        }
                        else if (role == 3)
                        {
                            colors[i] = new Color(1.2f, 0.55f, 0.9f, 0.55f);
                        }
                    }

                    Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(ROOT + "/Models/" + part._name + ".asset");
                    bool fresh = mesh == null;

                    if (fresh)
                    {
                        mesh = new Mesh();
                    }

                    mesh.Clear();
                    mesh.name = part._name;
                    mesh.indexFormat = IndexFormat.UInt32;
                    mesh.vertices = positions;
                    mesh.uv = uv;
                    mesh.colors = colors;
                    mesh.triangles = part._triangles;

                    if (role == 2)
                    {
                        BuildBoundary(mesh);
                        layer.AddComponent<MeshFilter>().sharedMesh = mesh;
                        renderers[role] = layer.AddComponent<MeshRenderer>();
                    }
                    else
                    {
                        int pieces = 0;

                        foreach (int piece in part._pieceIds)
                        {
                            pieces = Math.Max(pieces, piece + 1);
                        }

                        int stations = role == 3 ? 1 : STATIONS;
                        int total = pieces * stations;
                        Vector3[] centers = new Vector3[total];
                        float[] sums = new float[total];
                        BoneWeight[] weights = new BoneWeight[count];

                        for (int i = 0; i < count; ++i)
                        {
                            float along = role == 3 ? 0f : uv[i].y * (stations - 1);
                            int a = Mathf.Clamp(Mathf.FloorToInt(along), 0, stations - 1);
                            int b = Mathf.Min(stations - 1, a + 1);
                            float factor = along - a;
                            a += part._pieceIds[i] * stations;
                            b += part._pieceIds[i] * stations;
                            centers[a] += positions[i] * (1f - factor);
                            sums[a] += 1f - factor;
                            centers[b] += positions[i] * factor;
                            sums[b] += factor;
                            weights[i] = new BoneWeight
                            {
                                boneIndex0 = a,
                                boneIndex1 = b,
                                weight0 = 1f - factor,
                                weight1 = factor
                            };
                        }

                        Transform[] local = new Transform[total];
                        Matrix4x4[] bind = new Matrix4x4[total];

                        for (int i = 0; i < total; ++i)
                        {
                            centers[i] /= Mathf.Max(0.0001f, sums[i]);
                            local[i] = new GameObject("Flow_" + i).transform;
                            local[i].SetParent(layer.transform, false);
                            local[i].localPosition = centers[i];
                            bind[i] = local[i].worldToLocalMatrix * layer.transform.localToWorldMatrix;
                            bones.Add(local[i]);
                            rest.Add(centers[i]);
                            motion.Add(new Vector4(centers[i].z, (i / stations) * 2.399963f,
                                role == 3 ? 2f : role, 0.65f + Mathf.Repeat(i * 0.618034f, 0.7f)));
                        }

                        mesh.boneWeights = weights;
                        mesh.bindposes = bind;
                        SkinnedMeshRenderer skin = layer.AddComponent<SkinnedMeshRenderer>();
                        skin.sharedMesh = mesh;
                        skin.bones = local;
                        skin.rootBone = layer.transform;
                        skin.localBounds = new Bounds(new Vector3(0f, 0.4f, 0.5f), Vector3.one * 3f);
                        skin.quality = SkinQuality.Bone2;
                        renderers[role] = skin;
                    }

                    mesh.RecalculateNormals();
                    mesh.RecalculateBounds();

                    if (fresh)
                    {
                        AssetDatabase.CreateAsset(mesh, ROOT + "/Models/" + part._name + ".asset");
                    }
                    else
                    {
                        EditorUtility.SetDirty(mesh);
                    }

                    renderers[role].sharedMaterial = role == 2 || role == 3 ? solid : role == 1 ? cloudMaterial : coreMaterial;
                    renderers[role].shadowCastingMode = ShadowCastingMode.Off;
                    renderers[role].receiveShadows = false;
                    renderers[role].enabled = false;
                }

                template.AddComponent<WolbaekBeamLineView>().Configure(renderers, bones.ToArray(), rest.ToArray(), motion.ToArray());

                for (int kind = 0; kind < G_Names.Length; ++kind)
                {
                    string settingsPath = ROOT + "/SO_WolbaekBeam_" + G_Names[kind] + ".asset";
                    WolbaekBeamSettingsSO settings = AssetDatabase.LoadAssetAtPath<WolbaekBeamSettingsSO>(settingsPath);

                    if (settings == null)
                    {
                        settings = ScriptableObject.CreateInstance<WolbaekBeamSettingsSO>();

                        if (kind == 0)
                        {
                            SerializedObject s = new SerializedObject(settings);
                            s.FindProperty("_previewWidth").floatValue = 3f;
                            s.FindProperty("_veilOpacity").floatValue = 0.38f;
                            s.ApplyModifiedPropertiesWithoutUndo();
                        }

                        AssetDatabase.CreateAsset(settings, settingsPath);
                    }

                    GameObject root = new GameObject("PF_WolbaekBeam_" + G_Names[kind]);
                    WolbaekBeamPlayer player = root.AddComponent<WolbaekBeamPlayer>();
                    SerializedObject serialized = new SerializedObject(player);
                    serialized.FindProperty("_kind").enumValueIndex = kind;
                    serialized.FindProperty("_settings").objectReferenceValue = settings;
                    SerializedProperty slots = serialized.FindProperty("_views");
                    slots.arraySize = G_Capacities[kind];

                    for (int i = 0; i < slots.arraySize; ++i)
                    {
                        GameObject line = UnityEngine.Object.Instantiate(template, root.transform);
                        line.name = "LineSlot_" + i;
                        SerializedObject style = new SerializedObject(line.GetComponent<WolbaekBeamLineView>());
                        style.FindProperty("_releaseStyle").boolValue = kind == 0;
                        style.ApplyModifiedPropertiesWithoutUndo();

                        if (kind == 0)
                        {
                            BuildReleaseWave(line);
                        }

                        slots.GetArrayElementAtIndex(i).objectReferenceValue = line.GetComponent<WolbaekBeamLineView>();
                    }

                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(kind));
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(template);
                EditorSceneManager.CloseScene(temporary, true);

                if (original.IsValid())
                {
                    SceneManager.SetActiveScene(original);
                }
            }

            AssetDatabase.SaveAssets();
            BuildScene();
        }

        public static string PrefabPath(int kind) => PREFABS + "/PF_WolbaekBeam_" + G_Names[kind] + ".prefab";

        private static void BuildReleaseWave(GameObject line)
        {
            SkinnedMeshRenderer skin = line.transform.Find("Core").GetComponent<SkinnedMeshRenderer>();
            Mesh source = skin.sharedMesh;
            string path = ROOT + "/Models/SM_Beam_ReleaseWave.asset";
            Mesh wave = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool fresh = wave == null;

            if (fresh)
            {
                wave = new Mesh();
            }

            EditorUtility.CopySerialized(source, wave);
            wave.name = "SM_Beam_ReleaseWave";
            Vector3[] vertices = source.vertices;

            for (int i = 0; i < vertices.Length; ++i)
            {
                Vector3 p = vertices[i];
                vertices[i] = new Vector3(p.z - 0.5f, p.y, 0.5f + p.x);
            }

            wave.vertices = vertices;
            SerializedObject view = new SerializedObject(line.GetComponent<WolbaekBeamLineView>());
            SerializedProperty rests = view.FindProperty("_rest");
            Matrix4x4[] bind = new Matrix4x4[skin.bones.Length];

            for (int i = 0; i < skin.bones.Length; ++i)
            {
                Vector3 p = skin.bones[i].localPosition;
                Vector3 rest = new Vector3(p.z - 0.5f, p.y, 0.5f + p.x);
                skin.bones[i].localPosition = rest;
                rests.GetArrayElementAtIndex(i).vector3Value = rest;
                bind[i] = skin.bones[i].worldToLocalMatrix * skin.transform.localToWorldMatrix;
            }

            view.ApplyModifiedPropertiesWithoutUndo();
            wave.bindposes = bind;
            wave.RecalculateNormals();
            wave.RecalculateBounds();

            if (fresh)
            {
                AssetDatabase.CreateAsset(wave, path);
            }
            else
            {
                EditorUtility.SetDirty(wave);
            }

            skin.sharedMesh = wave;
        }

        private static Material MaterialAsset(string name, bool noise)
        {
            string path = ROOT + "/Materials/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material != null)
            {
                return material;
            }

            Texture2D texture = new Texture2D(256, 256, TextureFormat.RGBA32, false, true);
            Color[] pixels = new Color[256 * 256];

            for (int y = 0; y < 256; ++y)
            {
                for (int x = 0; x < 256; ++x)
                {
                    float u = x / 255f, v = y / 255f;
                    float cloud = Mathf.PerlinNoise(u * 11f + Mathf.Sin(v * Mathf.PI * 2f), 6f + Mathf.Cos(v * Mathf.PI * 2f) * 3f);
                    float alpha = noise ? Mathf.Clamp01((cloud - 0.23f) * 1.5f) * Mathf.Sin(u * Mathf.PI) : 1f;

                    if (name == "M_Beam_CoreSoft")
                    {
                        float distance = u - 0.5f;
                        alpha = Mathf.Exp(-distance * distance * 70f);
                    }

                    if (name == "M_Beam_VeilCloud")
                    {
                        float coarse = Mathf.PerlinNoise(u * 4f + Mathf.Sin(v * Mathf.PI * 2f) * 1.7f, 4f + Mathf.Cos(v * Mathf.PI * 2f) * 1.7f);
                        float fine = Mathf.PerlinNoise(u * 12f + Mathf.Sin(v * Mathf.PI * 2f) * 4f, 10f + Mathf.Cos(v * Mathf.PI * 2f) * 4f);
                        float density = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((coarse * 0.7f + fine * 0.3f - 0.28f) * 2.2f));
                        float edge = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Min(u, 1f - u) * 6f));
                        alpha = density * edge * 0.8f;
                    }

                    pixels[y * 256 + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            string png = ROOT + "/Textures/" + name + ".png";
            File.WriteAllBytes(png, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(png);
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(png);
            importer.sRGBTexture = false;
            importer.alphaIsTransparency = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            material = new Material(Shader.Find("Shinshi/VFX/Particle"));
            material.name = name;
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(png));
            material.SetColor("_Color", Color.white);
            material.SetFloat("_SrcBlend", (float)BlendMode.One);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SoftFade", 0.002f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void BuildBoundary(Mesh mesh)
        {
            List<Vector3> verts = new List<Vector3>();
            List<int> tris = new List<int>();

            for (int cap = 0; cap < 2; ++cap)
            {
                for (int i = 0; i < 32; ++i)
                {
                    float a = -0.5f + i / 32f, b = -0.5f + (i + 1) / 32f;
                    Quad(verts, tris, new Vector3(a, 0.01f, cap - 0.003f), new Vector3(b, 0.01f, cap - 0.003f),
                        new Vector3(b, 0.01f, cap + 0.003f), new Vector3(a, 0.01f, cap + 0.003f));
                }
            }

            for (int side = 0; side < 2; ++side)
            {
                float x = side - 0.5f;
                Quad(verts, tris, new Vector3(x - 0.003f, 0.01f, 0f), new Vector3(x + 0.003f, 0.01f, 0f),
                    new Vector3(x + 0.003f, 0.01f, 1f), new Vector3(x - 0.003f, 0.01f, 1f));
            }

            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            Color[] colors = new Color[verts.Count];
            Vector2[] uv = new Vector2[verts.Count];

            for (int i = 0; i < colors.Length; ++i)
            {
                colors[i] = new Color(0.45f, 1.3f, 2f, 0.85f);
                uv[i] = Vector2.one * 0.5f;
            }

            mesh.colors = colors;
            mesh.uv = uv;
        }

        private static void Quad(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 1);
            triangles.Add(start);
            triangles.Add(start + 3);
            triangles.Add(start + 2);
        }

        public static void BuildScene()
        {
            Scene original = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Neutral_Stage";
            floor.transform.localScale = Vector3.one * 5f;
            UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());
            Material stage = AssetDatabase.LoadAssetAtPath<Material>(ROOT + "/Materials/M_Beam_Stage.mat");

            if (stage == null)
            {
                stage = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                stage.SetColor("_BaseColor", new Color(0.28f, 0.29f, 0.31f));
                AssetDatabase.CreateAsset(stage, ROOT + "/Materials/M_Beam_Stage.mat");
            }

            stage.SetTexture("_BaseMap", StageTexture());
            stage.SetTextureScale("_BaseMap", new Vector2(8f, 8f));
            EditorUtility.SetDirty(stage);
            floor.GetComponent<Renderer>().sharedMaterial = stage;
            floor.transform.position = Vector3.down * 0.035f;
            Light light = new GameObject("Preview_Fill").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.8f;
            light.transform.rotation = Quaternion.Euler(48f, 20f, 0f);
            Camera camera = new GameObject("Beams_GameplayCamera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(14f, 20f, -20f);
            camera.transform.LookAt(new Vector3(0f, 0f, 3f));
            camera.fieldOfView = 34f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.005f, 0.008f, 0.013f);
            camera.allowHDR = true;
            camera.GetUniversalAdditionalCameraData().requiresDepthTexture = true;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            Volume volume = new GameObject("Preview_Glow").AddComponent<Volume>();
            volume.isGlobal = true;
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ROOT + "/VP_BeamPreview.asset");

            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.Add<Bloom>().intensity.Override(0.6f);
                profile.components[0].active = true;
                profile.Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
                AssetDatabase.CreateAsset(profile, ROOT + "/VP_BeamPreview.asset");

                foreach (VolumeComponent component in profile.components)
                {
                    AssetDatabase.AddObjectToAsset(component, profile);
                }
            }

            volume.sharedProfile = profile;
            WolbaekBeamPlayer[] players = new WolbaekBeamPlayer[4];

            for (int i = 0; i < 4; ++i)
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(i)), scene);
                instance.name = "Beam_" + G_Names[i];
                players[i] = instance.GetComponent<WolbaekBeamPlayer>();
            }

            WolbaekBeamPreview preview = new GameObject("Development_Only_Patterns").AddComponent<WolbaekBeamPreview>();
            SerializedObject serialized = new SerializedObject(preview);
            SerializedProperty list = serialized.FindProperty("_players");
            list.arraySize = 4;

            for (int i = 0; i < 4; ++i)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = players[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            if ((original.IsValid()) && (original.path == SCENE))
            {
                if (original.isDirty)
                {
                    throw new InvalidOperationException("기존 검수 씬 미저장 변경 보존");
                }

                EditorSceneManager.CloseScene(original, true);
            }

            if (!EditorSceneManager.SaveScene(scene, SCENE))
            {
                throw new InvalidOperationException("빔 검수 씬 저장 실패");
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.CloseScene(scene, true);

            if (original.IsValid())
            {
                SceneManager.SetActiveScene(original);
            }
        }

        private static Texture2D StageTexture()
        {
            string path = ROOT + "/Textures/T_BeamPreview_Slate.png";
            Texture2D asset = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

            if (asset != null)
            {
                return asset;
            }

            const int size = 512;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGB24, false);
            Color[] pixels = new Color[size * size];

            for (int y = 0; y < size; ++y)
            {
                for (int x = 0; x < size; ++x)
                {
                    int row = y / 64;
                    int localX = (x + row % 2 * 64) % 128;
                    int localY = y % 64;
                    float noise = Mathf.PerlinNoise(x * 0.045f, y * 0.045f);
                    float shade = 0.65f + noise * 0.3f;

                    if ((localX < 3) || (localY < 3))
                    {
                        shade *= 0.38f;
                    }

                    pixels[y * size + x] = new Color(shade, shade, shade);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
