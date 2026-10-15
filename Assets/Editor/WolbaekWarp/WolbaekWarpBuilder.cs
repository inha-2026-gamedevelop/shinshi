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
    public static class WolbaekWarpBuilder
    {
        /****************************************
        *                Fields
        ****************************************/
        public const string ROOT = "Assets/01.Scripts/05.Art/VFX/Wolbaek/Warp";
        public const string PREFABS = "Assets/02.Prefab/VFX/Wolbaek/Warp";
        public const string SCENE = "Assets/00.Scenes/Dev_VFX/Dev_Wolbaek_Warp.unity";
        public const string EVIDENCE = ".claude/docs/2026-10-14/wolbaek-warp-blender";
        private const string BEAMS = "Assets/01.Scripts/05.Art/VFX/Wolbaek/Beams";
        private const string SOURCE_RING = "Assets/99.ThirdParty/VFX/Piloto Studio/Super Realistic FXs/Super Realistic FX Bundle 02/ARPG Spirit Bloom Spells/SB_Shockwave_Ground.prefab";
        private const int STATIONS = 9;
        private const int BODY_SLICES = 16;
        private const int RING_SEGMENTS = 128;
        private const float RING_HALF_WIDTH = 0.003f;
        private const float GOLDEN_PHASE = 2.399963f;
        private const float SOURCE_HEIGHT = 1.92f;
        private static readonly string[] G_Names = { "Breakup", "Reform", "Landing" };

        [Serializable]
        private sealed class Export
        {
            public Part[] _meshes;
        }

        [Serializable]
        private sealed class Part
        {
            public string _name;
            public string _role;
            public int _index;
            public float[] _vertices;
            public float[] _normals;
            public float[] _uv;
            public float[] _colors;
            public int[] _pieceIds;
            public int[] _triangles;
        }

        /****************************************
        *                Methods
        ****************************************/
        [MenuItem("Shinshi/VFX/Wolbaek/Build Warp")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("PlayMode 생성 금지");
            }
            Directory.CreateDirectory(ROOT + "/Models");
            Directory.CreateDirectory(ROOT + "/Materials");
            Directory.CreateDirectory(PREFABS);
            AssetDatabase.Refresh();
            Export export = JsonUtility.FromJson<Export>(File.ReadAllText(EVIDENCE + "/warp_geometry.json"));
            Material body = MaterialAsset("M_Warp_Body", "M_Beam_Boundary");
            Material mist = MaterialAsset("M_Warp_Mist", "M_Beam_VeilCloud");
            Material petals = MaterialAsset("M_Warp_Petals", "M_Beam_Boundary");
            Material core = MaterialAsset("M_Warp_Core", "M_Beam_VeilCloud");
            Material motes = MaterialAsset("M_Warp_Motes", "M_Beam_Boundary");
            motes.SetTexture("_BaseMap", GlowTexture());
            body.SetColor("_BaseColor", new Color(0.7f, 0.72f, 0.75f, 0.55f));
            body.SetColor("_EdgeColor", new Color(1.5f, 2.4f, 2.8f));
            body.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(body);
            EditorUtility.SetDirty(motes);
            Scene original = SceneManager.GetActiveScene();
            Scene temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(temporary);
            try
            {
                for (int kind = 0; kind < G_Names.Length; ++kind)
                {
                    GameObject root = new GameObject("PF_WolbaekWarp_" + G_Names[kind]);
                    WolbaekWarpPlayer player = root.AddComponent<WolbaekWarpPlayer>();
                    SerializedObject serialized = new SerializedObject(player);
                    serialized.FindProperty("_kind").enumValueIndex = kind;
                    serialized.FindProperty("_settings").objectReferenceValue = SettingsAsset(kind);
                    serialized.FindProperty("_bodyNoise").objectReferenceValue = mist.GetTexture("_BaseMap");
                    if (kind < 2)
                    {
                        List<Renderer> slices = new List<Renderer>();
                        List<Renderer> energy = new List<Renderer>();
                        List<Transform> bones = new List<Transform>();
                        List<Vector3> rest = new List<Vector3>();
                        List<Vector4> motion = new List<Vector4>();
                        foreach (Part part in export._meshes)
                        {
                            GameObject layer = new GameObject(part._name);
                            layer.transform.SetParent(root.transform, false);
                            Mesh mesh = MeshAsset(part);
                            Renderer renderer;
                            if (part._role == "Body")
                            {
                                layer.AddComponent<MeshFilter>().sharedMesh = mesh;
                                renderer = layer.AddComponent<MeshRenderer>();
                                renderer.sharedMaterial = body;
                                slices.Add(renderer);
                            }
                            else
                            {
                                SkinnedMeshRenderer skin = Skin(layer, mesh, part, bones, rest, motion);
                                renderer = skin;
                            renderer.sharedMaterial = part._role.Contains("Petals") ? petals : part._role == "Motes" || part._role == "FootCore" ? motes : part._role == "CoreWisps" ? core : mist;
                                energy.Add(renderer);
                            }
                            renderer.enabled = false;
                            renderer.shadowCastingMode = ShadowCastingMode.Off;
                            renderer.receiveShadows = false;
                        }
                        SetReferences(serialized.FindProperty("_bodySlices"), slices);
                        SetReferences(serialized.FindProperty("_energy"), energy);
                        SerializedProperty energyKinds = serialized.FindProperty("_energyKinds");
                        energyKinds.arraySize = energy.Count;
                        int energyIndex = 0;
                        foreach (Part part in export._meshes)
                        {
                            if (part._role != "Body")
                            {
                                energyKinds.GetArrayElementAtIndex(energyIndex).intValue = EnergyKind(part._role);
                                ++energyIndex;
                            }
                        }
                        SerializedProperty boneList = serialized.FindProperty("_bones");
                        SerializedProperty restList = serialized.FindProperty("_rest");
                        SerializedProperty motionList = serialized.FindProperty("_motion");
                        boneList.arraySize = bones.Count;
                        restList.arraySize = rest.Count;
                        motionList.arraySize = motion.Count;
                        for (int i = 0; i < bones.Count; ++i)
                        {
                            boneList.GetArrayElementAtIndex(i).objectReferenceValue = bones[i];
                            restList.GetArrayElementAtIndex(i).vector3Value = rest[i];
                            motionList.GetArrayElementAtIndex(i).vector4Value = motion[i];
                        }
                    }
                    else
                    {
                        GameObject boundary = new GameObject("Exact_Landed_Boundary");
                        boundary.transform.SetParent(root.transform, false);
                        boundary.AddComponent<MeshFilter>().sharedMesh = BoundaryAsset();
                        MeshRenderer renderer = boundary.AddComponent<MeshRenderer>();
                        renderer.sharedMaterial = MaterialAsset("M_Warp_Boundary", "M_Beam_Boundary");
                        renderer.enabled = false;
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                        serialized.FindProperty("_boundary").objectReferenceValue = renderer;
                        GameObject shock = new GameObject("Confirmed_Ground_Shockwave");
                        shock.transform.SetParent(root.transform, false);
                        serialized.FindProperty("_shockRoot").objectReferenceValue = shock.transform;
                        SerializedProperty systems = serialized.FindProperty("_shockwaves");
                        systems.arraySize = 3;
                        for (int ring = 0; ring < 3; ++ring)
                        {
                            ParticleSystem system = SelectedShockwave(shock.transform);
                            ParticleSystem.MainModule main = system.main;
                            main.startSize = 1f - ring * 0.22f;
                            main.startColor = new Color(0.18f, 0.55f, 0.85f, ring == 0 ? 0.5f : ring == 1 ? 0.12f : 0.05f);
                            system.transform.localPosition = new Vector3(0f, 0.025f + ring * 0.04f, 0f);
                            system.GetComponent<ParticleSystemRenderer>().sharedMaterial = WaveMaterial(ring);
                            systems.GetArrayElementAtIndex(ring).objectReferenceValue = system;
                        }
                        List<Renderer> energy = new List<Renderer>();
                        List<Transform> bones = new List<Transform>();
                        List<Vector3> rest = new List<Vector3>();
                        List<Vector4> motion = new List<Vector4>();
                        foreach (Part part in export._meshes)
                        {
                            if (EnergyKind(part._role) < 2)
                            {
                                continue;
                            }
                            GameObject layer = new GameObject(part._name);
                            layer.transform.SetParent(root.transform, false);
                            SkinnedMeshRenderer skin = Skin(layer, MeshAsset(part), part, bones, rest, motion);
                            skin.sharedMaterial = part._role.Contains("Petals") ? petals : part._role == "Motes" || part._role == "FootCore" ? motes : mist;
                            skin.enabled = false;
                            skin.shadowCastingMode = ShadowCastingMode.Off;
                            energy.Add(skin);
                        }
                        SetReferences(serialized.FindProperty("_energy"), energy);
                        SerializedProperty kinds = serialized.FindProperty("_energyKinds");
                        kinds.arraySize = energy.Count;
                        for (int i = 0; i < energy.Count; ++i)
                        {
                            kinds.GetArrayElementAtIndex(i).intValue = energy[i].name.Contains("Petals") ? 3 : energy[i].name.Contains("Motes") ? 4 : energy[i].name.Contains("FootCore") ? 5 : 2;
                        }
                        SerializedProperty boneList = serialized.FindProperty("_bones");
                        SerializedProperty restList = serialized.FindProperty("_rest");
                        SerializedProperty motionList = serialized.FindProperty("_motion");
                        boneList.arraySize = bones.Count;
                        restList.arraySize = rest.Count;
                        motionList.arraySize = motion.Count;
                        for (int i = 0; i < bones.Count; ++i)
                        {
                            boneList.GetArrayElementAtIndex(i).objectReferenceValue = bones[i];
                            restList.GetArrayElementAtIndex(i).vector3Value = rest[i];
                            motionList.GetArrayElementAtIndex(i).vector4Value = motion[i];
                        }
                    }
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(kind));
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(temporary, true);
                if (original.IsValid())
                {
                    SceneManager.SetActiveScene(original);
                }
            }
            AssetDatabase.SaveAssets();
            BuildScene();
        }

        public static string PrefabPath(int kind) => PREFABS + "/PF_WolbaekWarp_" + G_Names[kind] + ".prefab";

        private static int EnergyKind(string role) => role == "Body" ? -1 : role == "Petals" ? 1 : role == "GroundWisps" ? 2 : role == "GroundPetals" ? 3 : role == "Motes" ? 4 : role == "FootCore" ? 5 : 0;

        private static Texture2D GlowTexture()
        {
            string path = ROOT + "/Materials/T_Warp_MoteGlow.png";
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture != null)
            {
                return texture;
            }
            const int SIZE = 64;
            texture = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false);
            for (int y = 0; y < SIZE; ++y)
            {
                for (int x = 0; x < SIZE; ++x)
                {
                    Vector2 p = new Vector2(x / (SIZE - 1f) - 0.5f, y / (SIZE - 1f) - 0.5f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Exp(-p.sqrMagnitude * 38f)));
                }
            }
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static VolumeProfile PreviewProfile()
        {
            string path = ROOT + "/VP_WarpPreview.asset";
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
                Bloom bloom = profile.Add<Bloom>(true);
                bloom.intensity.Override(1.1f);
                bloom.threshold.Override(0.7f);
                bloom.scatter.Override(0.65f);
                AssetDatabase.AddObjectToAsset(bloom, profile);
                EditorUtility.SetDirty(profile);
            }
            return profile;
        }

        private static Material WaveMaterial(int ring)
        {
            string name = "M_Warp_Wave_" + ring;
            Material material = MaterialAsset(name, "M_Beam_VeilCloud");
            Material source = AssetDatabase.LoadAssetAtPath<Material>(ROOT + "/Materials/M_Warp_Shockwave.mat");
            material.shader = source.shader;
            material.CopyPropertiesFromMaterial(source);
            float strength = ring == 0 ? 1f : ring == 1 ? 0.25f : 0.12f;
            material.SetColor("_WhiteColor", new Color(0.35f, 0.65f, 0.8f, 0f));
            material.SetColor("_MidColor", new Color(0.2f, 0.4f, 0.7f, 0f));
            material.SetColor("_LastColor", new Color(0.08f, 0.18f, 0.35f, 0f));
            material.SetColor("_FresnelColor", new Color(0.25f * strength, 0.75f * strength, 1.2f * strength, 0.65f * strength));
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material MaterialAsset(string name, string source)
        {
            string path = ROOT + "/Materials/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                if ((name == "M_Warp_Body") && (material.shader.name != "Shinshi/VFX/WarpProxyDissolve"))
                {
                    material.shader = Shader.Find("Shinshi/VFX/WarpProxyDissolve");
                    material.SetColor("_BaseColor", new Color(0.85f, 0.84f, 0.82f));
                    material.renderQueue = (int)RenderQueue.AlphaTest;
                    EditorUtility.SetDirty(material);
                }
                return material;
            }
            material = new Material(AssetDatabase.LoadAssetAtPath<Material>(BEAMS + "/Materials/" + source + ".mat"));
            material.name = name;
            material.SetColor("_Color", Color.white);
            material.SetFloat("_SoftFade", 0.001f);
            if (name == "M_Warp_Body")
            {
                material.shader = Shader.Find("Shinshi/VFX/WarpProxyDissolve");
                material.SetColor("_BaseColor", new Color(0.85f, 0.84f, 0.82f));
                material.renderQueue = (int)RenderQueue.AlphaTest;
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static WolbaekWarpSettingsSO SettingsAsset(int kind)
        {
            string path = ROOT + "/SO_WolbaekWarp_" + G_Names[kind] + ".asset";
            WolbaekWarpSettingsSO settings = AssetDatabase.LoadAssetAtPath<WolbaekWarpSettingsSO>(path);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<WolbaekWarpSettingsSO>();
                AssetDatabase.CreateAsset(settings, path);
            }
            return settings;
        }

        private static Mesh MeshAsset(Part part)
        {
            string path = ROOT + "/Models/" + part._name + ".asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool fresh = mesh == null;
            if (fresh)
            {
                mesh = new Mesh();
            }
            mesh.Clear();
            mesh.name = part._name;
            mesh.indexFormat = IndexFormat.UInt32;
            int count = part._vertices.Length / 3;
            Vector3[] vertices = new Vector3[count];
            Vector3[] normals = new Vector3[count];
            Vector2[] uv = new Vector2[count];
            Color[] colors = new Color[count];
            for (int i = 0; i < count; ++i)
            {
                int v = i * 3;
                int c = i * 4;
                vertices[i] = new Vector3(part._vertices[v], part._vertices[v + 1], part._vertices[v + 2]);
                normals[i] = new Vector3(part._normals[v], part._normals[v + 1], part._normals[v + 2]);
                uv[i] = new Vector2(part._uv[i * 2], part._uv[i * 2 + 1]);
                colors[i] = new Color(part._colors[c], part._colors[c + 1], part._colors[c + 2], part._colors[c + 3]);
            }
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uv;
            mesh.colors = colors;
            mesh.triangles = part._triangles;
            mesh.RecalculateBounds();
            if (fresh)
            {
                AssetDatabase.CreateAsset(mesh, path);
            }
            else
            {
                EditorUtility.SetDirty(mesh);
            }
            return mesh;
        }

        private static SkinnedMeshRenderer Skin(GameObject layer, Mesh mesh, Part part,
            List<Transform> bones, List<Vector3> rest, List<Vector4> motion)
        {
            bool petals = part._role.Contains("Petals") || part._role == "Motes" || part._role == "FootCore";
            int stations = petals ? 1 : STATIONS;
            int pieces = 0;
            foreach (int piece in part._pieceIds)
            {
                pieces = Math.Max(pieces, piece + 1);
            }
            int total = pieces * stations;
            Vector3[] centers = new Vector3[total];
            float[] sums = new float[total];
            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = new BoneWeight[vertices.Length];
            for (int i = 0; i < vertices.Length; ++i)
            {
                float along = petals ? 0f : part._uv[i * 2 + 1] * (stations - 1);
                int a = Mathf.Clamp(Mathf.FloorToInt(along), 0, stations - 1);
                int b = Mathf.Min(stations - 1, a + 1);
                float mix = along - a;
                a += part._pieceIds[i] * stations;
                b += part._pieceIds[i] * stations;
                centers[a] += vertices[i] * (1f - mix);
                centers[b] += vertices[i] * mix;
                sums[a] += 1f - mix;
                sums[b] += mix;
                weights[i] = new BoneWeight { boneIndex0 = a, boneIndex1 = b, weight0 = 1f - mix, weight1 = mix };
            }
            Transform[] local = new Transform[total];
            Matrix4x4[] bind = new Matrix4x4[total];
            for (int i = 0; i < total; ++i)
            {
                centers[i] /= Mathf.Max(0.001f, sums[i]);
                local[i] = new GameObject("Flow_" + i).transform;
                local[i].SetParent(layer.transform, false);
                local[i].localPosition = centers[i];
                bind[i] = local[i].worldToLocalMatrix * layer.transform.localToWorldMatrix;
                bones.Add(local[i]);
                rest.Add(centers[i]);
                float motionKind = part._role == "GroundPetals" ? 2f : part._role == "Motes" ? 3f : part._role == "FootCore" ? 4f : petals ? 1f : 0f;
                motion.Add(new Vector4(motionKind, centers[i].y / SOURCE_HEIGHT,
                    (i / stations) * GOLDEN_PHASE, 0.6f + Mathf.Repeat(i * 0.618f, 0.4f)));
            }
            mesh.boneWeights = weights;
            mesh.bindposes = bind;
            EditorUtility.SetDirty(mesh);
            SkinnedMeshRenderer renderer = layer.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = local;
            renderer.rootBone = layer.transform;
            renderer.quality = SkinQuality.Bone2;
            renderer.localBounds = new Bounds(Vector3.up, Vector3.one * 5f);
            return renderer;
        }

        private static Mesh BoundaryAsset()
        {
            string path = ROOT + "/Models/SM_Warp_LandedBoundary.asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                return existing;
            }
            Mesh mesh = new Mesh();
            Vector3[] vertices = new Vector3[(RING_SEGMENTS + 1) * 2];
            Color[] colors = new Color[vertices.Length];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[RING_SEGMENTS * 6];
            for (int i = 0; i <= RING_SEGMENTS; ++i)
            {
                float angle = i / (float)RING_SEGMENTS * Mathf.PI * 2f;
                for (int j = 0; j < 2; ++j)
                {
                    float radius = 1f + (j == 0 ? -RING_HALF_WIDTH : RING_HALF_WIDTH);
                    int index = i * 2 + j;
                    vertices[index] = new Vector3(Mathf.Sin(angle) * radius, 0.018f, Mathf.Cos(angle) * radius);
                    colors[index] = new Color(1f, 1.7f, 2.2f, 1f);
                    uv[index] = Vector2.one * 0.5f;
                }
                if (i < RING_SEGMENTS)
                {
                    int start = i * 2;
                    int t = i * 6;
                    triangles[t] = start;
                    triangles[t + 1] = start + 2;
                    triangles[t + 2] = start + 1;
                    triangles[t + 3] = start + 1;
                    triangles[t + 4] = start + 2;
                    triangles[t + 5] = start + 3;
                }
            }
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static ParticleSystem SelectedShockwave(Transform parent)
        {
            GameObject original = AssetDatabase.LoadAssetAtPath<GameObject>(SOURCE_RING);
            ParticleSystem selected = null;
            foreach (ParticleSystem system in original.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (system.name == "SB_Rainbow_1_Shockwave_add_soft")
                {
                    selected = system;
                    break;
                }
            }
            if (selected == null)
            {
                throw new InvalidOperationException("검증된 Ground ring 없음");
            }
            GameObject copy = UnityEngine.Object.Instantiate(selected.gameObject, parent);
            copy.name = "SB_GroundRing_Selected";
            copy.transform.localPosition = new Vector3(0f, 0.035f, 0f);
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = Vector3.one;
            copy.SetActive(true);
            ParticleSystem particle = copy.GetComponent<ParticleSystem>();
            string materialPath = ROOT + "/Materials/M_Warp_Shockwave.mat";
            Material moon = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (moon == null)
            {
                moon = new Material(copy.GetComponent<ParticleSystemRenderer>().sharedMaterial);
                moon.name = "M_Warp_Shockwave";
                moon.SetTexture("_DetailNoise", Texture2D.grayTexture);
                moon.SetColor("_WhiteColor", new Color(0.75f, 1.1f, 1.3f, 0f));
                moon.SetColor("_MidColor", new Color(0.45f, 0.6f, 0.95f, 0f));
                moon.SetColor("_LastColor", new Color(0.1f, 0.2f, 0.4f, 0f));
                moon.SetColor("_FresnelColor", new Color(0.5f, 1.4f, 2f, 1f));
                AssetDatabase.CreateAsset(moon, materialPath);
            }
            copy.GetComponent<ParticleSystemRenderer>().sharedMaterial = moon;
            particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = particle.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startDelay = 0f;
            main.startSpeed = 0f;
            main.startSize3D = false;
            main.startSize = 1f;
            main.startLifetime = 0.65f;
            main.maxParticles = 2;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startRotation3D = true;
            main.startRotationX = Mathf.PI * 0.5f;
            main.startRotationY = 0f;
            main.startRotationZ = 0f;
            main.startColor = new Color(0.75f, 1.5f, 2f, 0.65f);
            particle.useAutoRandomSeed = false;
            particle.randomSeed = 3014;
            ParticleSystem.ShapeModule shape = particle.shape;
            shape.enabled = false;
            ParticleSystem.EmissionModule emission = particle.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            ParticleSystem.SizeOverLifetimeModule size = particle.sizeOverLifetime;
            size.enabled = true;
            size.separateAxes = false;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.08f, 1f, 1f));
            return particle;
        }

        private static void SetReferences(SerializedProperty property, List<Renderer> renderers)
        {
            property.arraySize = renderers.Count;
            for (int i = 0; i < renderers.Count; ++i)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
            }
        }

        private static void BuildScene()
        {
            Scene original = SceneManager.GetActiveScene();
            if ((original.path == SCENE) && original.isDirty)
            {
                throw new InvalidOperationException("검수 씬 미저장 변경 보존");
            }
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.name = "Neutral_Warp_Stage";
                floor.transform.localScale = Vector3.one * 5f;
                floor.transform.position = Vector3.down * 0.02f;
                UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());
                floor.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(BEAMS + "/Materials/M_Beam_Stage.mat");
                Light light = new GameObject("Warp_Fill").AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1f;
                light.transform.rotation = Quaternion.Euler(45f, 25f, 0f);
                Camera camera = new GameObject("Warp_GameplayCamera").AddComponent<Camera>();
                camera.tag = "MainCamera";
                camera.transform.position = new Vector3(-1.6f, 2.6f, -5f);
                camera.transform.LookAt(new Vector3(-5f, 0.9f, 0f));
                camera.fieldOfView = 25f;
                camera.allowHDR = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.008f, 0.012f, 0.02f);
                UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
                data.requiresDepthTexture = true;
                data.renderPostProcessing = true;
                Volume volume = new GameObject("Warp_Glow").AddComponent<Volume>();
                volume.isGlobal = true;
                volume.sharedProfile = PreviewProfile();
                WolbaekWarpPlayer[] players = new WolbaekWarpPlayer[G_Names.Length];
                for (int i = 0; i < G_Names.Length; ++i)
                {
                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(i)), scene);
                    instance.name = "Warp_" + G_Names[i];
                    players[i] = instance.GetComponent<WolbaekWarpPlayer>();
                }
                GameObject neutral = new GameObject("Development_Neutral_LandedBody");
                neutral.transform.position = Vector3.right * 7f;
                for (int i = 0; i < BODY_SLICES; ++i)
                {
                    GameObject layer = new GameObject("Body_" + i);
                    layer.transform.SetParent(neutral.transform, false);
                    layer.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(ROOT + "/Models/SM_Warp_Body_" + i.ToString("D2") + ".asset");
                    layer.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(ROOT + "/Materials/M_Warp_Body.mat");
                }
                WolbaekWarpPreview preview = new GameObject("Development_Warp_Cues").AddComponent<WolbaekWarpPreview>();
                SerializedObject serialized = new SerializedObject(preview);
                SerializedProperty list = serialized.FindProperty("_players");
                list.arraySize = players.Length;
                for (int i = 0; i < players.Length; ++i)
                {
                    list.GetArrayElementAtIndex(i).objectReferenceValue = players[i];
                }
                serialized.FindProperty("_landingBody").objectReferenceValue = neutral;
                GameObject reformed = UnityEngine.Object.Instantiate(neutral);
                reformed.name = "Development_Neutral_ReformedBody";
                reformed.transform.position = Vector3.zero;
                reformed.SetActive(false);
                serialized.FindProperty("_reformBody").objectReferenceValue = reformed;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                if ((original.path == SCENE) && original.IsValid())
                {
                    EditorSceneManager.CloseScene(original, true);
                }
                if (!EditorSceneManager.SaveScene(scene, SCENE))
                {
                    throw new InvalidOperationException("Warp 씬 저장 실패");
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
            AssetDatabase.SaveAssets();
        }
    }
}
