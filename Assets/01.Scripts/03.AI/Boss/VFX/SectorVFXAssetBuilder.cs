#if UNITY_EDITOR
// System
using System.IO;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

using Shinshi.Art;

namespace Shinshi.Editor
{
    /// <summary> SectorGroundVFX 프리팹과 공유 머티리얼을 만든다 </summary>
    public static class SectorVFXAssetBuilder
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string MENU_PATH              = "Shinshi/VFX/Create Sector Ground Prefab";
        private const string OUTPUT_ROOT            = "Assets/01.Scripts/03.AI/Boss/VFX/Generated";
        private const string GROUND_SHADER_NAME     = "Shinshi/VFX/SectorGround";
        private const string PARTICLE_SHADER_NAME   = "Shinshi/VFX/SectorParticle";
        private const string GROUND_MATERIAL_NAME   = "M_SectorGround";
        private const string PARTICLE_MATERIAL_NAME = "M_SectorParticle";
        private const string PREFAB_NAME            = "PF_SectorGroundVFX";
        private const string ENERGY_NAME            = "Energy";
        private const string DEBRIS_NAME            = "Debris";
        private const int    MAX_PARTICLES          = 1024;
        private const float  ENERGY_LIFETIME        = 0.32f;
        private const float  DEBRIS_LIFETIME        = 0.55f;
        private const float  DEBRIS_GRAVITY         = 0.25f;
        private const float  SIZE_START_SCALE       = 0.5f;
        private const float  SIZE_PEAK_TIME         = 0.15f;
        private const float  ENERGY_LENGTH_SCALE    = 1.3f;
        private const float  ENERGY_VELOCITY_SCALE  = 0.25f;

        /****************************************
        *                 Menu
        ****************************************/

        [MenuItem(MENU_PATH)]
        public static void CreatePrefab()
        {
            if (GraphicsSettings.defaultRenderPipeline == null)
            {
                Debug.LogError("[SectorVFX] URP 렌더 파이프라인 에셋이 필요하다");
                return;
            }

            Shader groundShader = Shader.Find(GROUND_SHADER_NAME);
            Shader particleShader = Shader.Find(PARTICLE_SHADER_NAME);
            if ((groundShader == null) || (particleShader == null))
            {
                Debug.LogError("[SectorVFX] 셰이더를 찾지 못했다, 임포트가 끝났는지 확인한다");
                return;
            }

            Directory.CreateDirectory(OUTPUT_ROOT);
            AssetDatabase.Refresh();

            Material ground = GetMaterial(GROUND_MATERIAL_NAME, groundShader);
            Material particle = GetMaterial(PARTICLE_MATERIAL_NAME, particleShader);

            GameObject root = new GameObject(PREFAB_NAME);
            try
            {
                SectorVFXController controller = root.AddComponent<SectorVFXController>();
                MeshRenderer surface = root.GetComponent<MeshRenderer>();
                surface.sharedMaterial = ground;
                surface.shadowCastingMode = ShadowCastingMode.Off;
                surface.receiveShadows = false;

                ParticleSystem energy = MakeParticles(root.transform, ENERGY_NAME, particle, true);
                ParticleSystem debris = MakeParticles(root.transform, DEBRIS_NAME, particle, false);

                SerializedObject serialized = new SerializedObject(controller);
                serialized.FindProperty("_energy").objectReferenceValue = energy;
                serialized.FindProperty("_debris").objectReferenceValue = debris;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                string path = AssetDatabase.GenerateUniqueAssetPath(OUTPUT_ROOT + "/" + PREFAB_NAME + ".prefab");
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                AssetDatabase.SaveAssets();
                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
                Debug.Log("[SectorVFX] 프리팹을 만들었다 - " + path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        // 이미 있으면 그대로 돌려줘서 손으로 고친 머티리얼 설정을 보존한다
        private static Material GetMaterial(string name, Shader shader)
        {
            string path = OUTPUT_ROOT + "/" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            Material material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static ParticleSystem MakeParticles(Transform parent, string name, Material material, bool energy)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            ParticleSystem system = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = MAX_PARTICLES;
            main.startLifetime = energy ? ENERGY_LIFETIME : DEBRIS_LIFETIME;
            main.startSpeed = 0f;
            main.gravityModifier = energy ? 0f : DEBRIS_GRAVITY;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;

            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, SIZE_START_SCALE), new Keyframe(SIZE_PEAK_TIME, 1f), new Keyframe(1f, 0f)));

            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = energy ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            renderer.lengthScale = energy ? ENERGY_LENGTH_SCALE : 1f;
            renderer.velocityScale = energy ? ENERGY_VELOCITY_SCALE : 0f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return system;
        }
    }
}
#endif
