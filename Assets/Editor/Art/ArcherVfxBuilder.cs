#if UNITY_EDITOR
// System
using System;

// Unity
using UnityEditor;
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;

namespace Shinshi.Editor
{
    /// <summary> 보유 화살과 바람 효과를 궁수 표현용 사본으로 가공하고 연결한다 </summary>
    public static class ArcherVfxBuilder
    {
        private const string SOURCE = "Assets/99.ThirdParty/VFX/MasterStylizedProjectiles/Projectiles/";
        private const string OUTPUT = "Assets/02.Prefab/VFX/Archer";
        private const string PLAYER = "Assets/02.Prefab/Network/NetPlayer/PF_NetPlayer.prefab";
        private const string ARROW_MATERIAL = OUTPUT + "/Materials/M_ArcherArrow.mat";
        private const int ARROW_INDEX = 0;
        private const int WIND_INDEX = 2;
        private const int IMPACT_INDEX = 4;
        private const int MAX_PARTICLES = 24;
        private const float EFFECT_HEIGHT = 1f;
        private const float BRIEF_LIFETIME = 0.25f;
        private const float TRAIL_SECONDS = 0.12f;
        private const float TRAIL_WIDTH = 0.065f;
        private const float PARTICLE_ALPHA = 0.65f;
        private const float IMPACT_SPEED = 3f;
        private const string RAMP_NAME = "ArcherNeutralRamp";

        private static readonly string[] G_Sources = { "Arrow/Prefabs/ArrowBullet.prefab", "Arrow/Prefabs/ArrowMuzzle.prefab",
            "WindShoot/Prefabs/WindBullet.prefab", "WindShoot/Prefabs/WindStart.prefab", "WindShoot/Prefabs/WindHit.prefab" };
        private static readonly string[] G_Names = { "Arrow", "Muzzle", "Wind", "WindMuzzle", "Impact" };
        private static readonly string[] G_Fields = { "_arrowPrefab", "_muzzlePrefab", "_windPrefab", "_windMuzzlePrefab" };
        private static readonly float[] G_Scales = { 0.65f, 0.2f, 0.65f, 0.2f, 0.18f };
        private static readonly Color ARROW_COLOR = new Color(0.82f, 0.9f, 0.87f, PARTICLE_ALPHA);
        private static readonly Color WIND_COLOR = new Color(0.63f, 0.85f, 0.79f, PARTICLE_ALPHA);

        [MenuItem("Shinshi/Art/VFX/Build And Wire Archer")]
        public static void BuildAndWire()
        {
            Type viewType = null;
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                viewType = assembly.GetType("Shinshi.Art.ArcherVfxView");
                if (viewType != null)
                {
                    break;
                }
            }

            if (viewType == null)
            {
                throw new InvalidOperationException("궁수 VFX View를 먼저 컴파일해야 한다.");
            }

            BuildAssets();
            GameObject player = PrefabUtility.LoadPrefabContents(PLAYER);
            try
            {
                Component view = player.GetComponent(viewType);
                if (view == null)
                {
                    view = player.AddComponent(viewType);
                }

                var serialized = new SerializedObject(view);
                serialized.FindProperty("_combat").objectReferenceValue = player.GetComponent<PlayerCombatState>();
                for (int i = 0; i < G_Fields.Length; ++i)
                {
                    serialized.FindProperty(G_Fields[i]).objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(GetPath(i));
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(player, PLAYER);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(player);
            }

            AssetDatabase.SaveAssets();
        }

        public static void BuildAssets()
        {
            foreach (string path in G_Sources)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(SOURCE + path) == null)
                {
                    throw new InvalidOperationException("VFX 설치 누락: " + path);
                }
            }

            EnsureFolder(OUTPUT);
            BuildArrowMaterial();
            for (int i = 0; i < G_Sources.Length; ++i)
            {
                BuildEffect(i);
            }
        }

        private static void BuildEffect(int index)
        {
            var root = new GameObject("PF_ArcherVfx_" + G_Names[index]);
            try
            {
                GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SOURCE + G_Sources[index]);
                GameObject effect = UnityEngine.Object.Instantiate(source, root.transform);
                effect.name = G_Names[index];
                effect.transform.localPosition = Vector3.up * ((index == IMPACT_INDEX) ? 0f : EFFECT_HEIGHT);
                effect.transform.localRotation = Quaternion.identity;
                effect.transform.localScale = Vector3.one * G_Scales[index];
                foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    UnityEngine.Object.DestroyImmediate(component);
                }

                foreach (Light light in root.GetComponentsInChildren<Light>(true))
                {
                    UnityEngine.Object.DestroyImmediate(light);
                }

                bool isProjectile = (index == ARROW_INDEX) || (index == WIND_INDEX);
                Color tint = (index >= WIND_INDEX) ? WIND_COLOR : ARROW_COLOR;
                foreach (ParticleSystem particle in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ConfigureParticle(particle, tint, isProjectile);
                    ParticleSystemRenderer renderer = particle.GetComponent<ParticleSystemRenderer>();
                    if ((index == ARROW_INDEX) && (renderer.sharedMaterial != null)
                        && (renderer.sharedMaterial.name == "CyanBlueArrow"))
                    {
                        renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(ARROW_MATERIAL);
                    }
                }

                foreach (TrailRenderer trail in root.GetComponentsInChildren<TrailRenderer>(true))
                {
                    trail.autodestruct = false;
                    trail.time = TRAIL_SECONDS;
                    trail.widthMultiplier = TRAIL_WIDTH;
                    var gradient = new Gradient();
                    gradient.SetKeys(new[] { new GradientColorKey(tint, 0f), new GradientColorKey(tint, 1f) },
                        new[] { new GradientAlphaKey(PARTICLE_ALPHA, 0f), new GradientAlphaKey(0f, 1f) });
                    trail.colorGradient = gradient;
                    trail.Clear();
                }

                // Impact는 서버 적중 위치를 받는 연결이 생길 때까지 사본만 준비한다.
                PrefabUtility.SaveAsPrefabAsset(root, GetPath(index));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void ConfigureParticle(ParticleSystem particle, Color tint, bool isProjectile)
        {
            particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = particle.main;
            main.playOnAwake = false;
            main.loop = isProjectile;
            main.stopAction = ParticleSystemStopAction.None;
            main.maxParticles = Mathf.Min(main.maxParticles, MAX_PARTICLES);
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            Color color = tint;
            color.a = Mathf.Min(main.startColor.color.a, PARTICLE_ALPHA);
            main.startColor = color;
            ParticleSystem.ColorBySpeedModule speedColor = particle.colorBySpeed;
            speedColor.enabled = false;

            // 원본의 색 대비는 줄이고 수명에 따른 투명도 변화는 남긴다.
            ParticleSystem.ColorOverLifetimeModule lifetimeColor = particle.colorOverLifetime;
            if (lifetimeColor.enabled)
            {
                Gradient original = lifetimeColor.color.gradient;
                var neutral = new Gradient();
                neutral.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, original.alphaKeys);
                lifetimeColor.color = neutral;
            }

            if (isProjectile)
            {
                // 발사체 이동은 공개 시각 기반 View가 맡으며 입자가 따로 전진하지 않는다.
                main.startSpeed = 0f;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                ParticleSystem.VelocityOverLifetimeModule velocity = particle.velocityOverLifetime;
                velocity.enabled = false;
                ParticleSystem.ForceOverLifetimeModule force = particle.forceOverLifetime;
                force.enabled = false;
                ParticleSystem.InheritVelocityModule inherit = particle.inheritVelocity;
                inherit.enabled = false;
            }
            else
            {
                main.startLifetime = Mathf.Min(main.startLifetime.constantMax, BRIEF_LIFETIME);
                main.startSpeed = Mathf.Min(main.startSpeed.constantMax, IMPACT_SPEED);
            }

            ParticleSystemRenderer renderer = particle.GetComponent<ParticleSystemRenderer>();
            if (renderer.sharedMaterial == null)
            {
                renderer.enabled = false;
                ParticleSystem.EmissionModule emission = particle.emission;
                emission.enabled = false;
            }

            particle.useAutoRandomSeed = false;
            particle.randomSeed = 1;
        }

        private static string GetPath(int index)
        {
            return OUTPUT + "/PF_ArcherVfx_" + G_Names[index] + ".prefab";
        }

        private static void BuildArrowMaterial()
        {
            EnsureFolder(OUTPUT + "/Materials");
            Material source = AssetDatabase.LoadAssetAtPath<Material>(SOURCE + "Arrow/Materials/CyanBlueArrow.mat");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(ARROW_MATERIAL);
            if (material == null)
            {
                material = new Material(source);
                AssetDatabase.CreateAsset(material, ARROW_MATERIAL);
            }
            else
            {
                material.CopyPropertiesFromMaterial(source);
            }

            // 원본 텍스처의 붉은 깃털을 원본 셰이더의 LUT로 중성화한다. 알파와 화살 윤곽은 유지한다.
            material.SetFloat("_LUT", 1f);
            material.EnableKeyword("_LUT");
            Texture2D ramp = null;
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(ARROW_MATERIAL))
            {
                if ((asset is Texture2D texture) && (asset.name == RAMP_NAME))
                {
                    ramp = texture;
                    break;
                }
            }

            if (ramp == null)
            {
                // 런타임 whiteTexture는 저장 참조가 아니므로 두 표본의 상수 LUT를 재질 서브에셋으로 보존한다.
                ramp = new Texture2D(2, 1, TextureFormat.RGBA32, false);
                ramp.name = RAMP_NAME;
                ramp.wrapMode = TextureWrapMode.Clamp;
                ramp.SetPixels(new[] { Color.white, Color.white });
                ramp.Apply();
                AssetDatabase.AddObjectToAsset(ramp, material);
            }

            material.SetTexture("_RampTexture", ramp);
            EditorUtility.SetDirty(material);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            int separator = path.LastIndexOf('/');
            string parent = path.Substring(0, separator);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(separator + 1));
        }
    }
}
#endif
