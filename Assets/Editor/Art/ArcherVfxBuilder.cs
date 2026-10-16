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
    /// <summary> 보유 얼음, 바람, 불, 에너지 폭발 효과를 궁수 속성 표현용 사본으로 가공하고 플레이어 프리팹에 연결한다. 원본 프리팹은 고치지 않는다 </summary>
    public static class ArcherVfxBuilder
    {
        private const string SOURCE = "Assets/99.ThirdParty/VFX/MasterStylizedProjectiles/Projectiles/";
        private const string OUTPUT = "Assets/02.Prefab/VFX/Archer";
        private const string PLAYER = "Assets/02.Prefab/Network/NetPlayer/PF_NetPlayer.prefab";
        private const int MAX_PARTICLES = 24;
        private const int MAX_BURST_PARTICLES = 64;
        private const float EFFECT_HEIGHT = 1f;
        private const float BURST_HEIGHT = 0.5f;
        private const float BRIEF_LIFETIME = 0.5f;
        private const float LONG_LIFETIME = 1.5f;
        private const float TRAIL_SECONDS = 0.12f;
        private const float TRAIL_WIDTH = 0.065f;
        private const float IMPACT_SPEED = 3f;
        private const float BURST_SPEED = 12f;

        private enum EMode
        {
            Projectile,
            Brief,
            Long
        }

        private struct Entry
        {
            public string Name;
            public string Source;
            public string Field;
            public float  Scale;
            public EMode  Mode;
            public float  Height;

            public Entry(string name, string source, string field, float scale, EMode mode, float height)
            {
                Name   = name;
                Source = source;
                Field  = field;
                Scale  = scale;
                Mode   = mode;
                Height = height;
            }
        }

        // 속성 없는 화살, 총구, 적중은 ReleaseVfxBuilder가 만든 프로젝트 소유 사본(외부 패키지가 없어도 보이는 것)을 그대로 연결만 한다.
        // 여기서 다시 만들면 그 사본을 외부 재질 의존 사본으로 덮어쓴다
        private static readonly string[] G_LegacyNames  = { "Arrow", "Muzzle", "Impact" };
        private static readonly string[] G_LegacyFields = { "_arrowPrefab", "_muzzlePrefab", "_impactPrefab" };

        // 기획서의 "Q 얼음 windshoot, W 윈드 ICEBullet"은 이름이 엇갈린 것으로 보고 속성에 맞춰 얼음=SmallIceBullet, 바람=WindShoot로 짝짓는다.
        // 불은 FireShoot가 없어 SmallFireBullet을 쓴다. 속성 효과는 마검사처럼 원본 재질과 색을 살리므로 해당 패키지 설치가 필요하다
        private static readonly Entry[] G_Entries =
        {
            new Entry("IceArrow", "SmallIceBullet/Prefabs/SmallIceBullet.prefab", "_iceArrowPrefab", 0.4f, EMode.Projectile, EFFECT_HEIGHT),
            new Entry("IceMuzzle", "SmallIceBullet/Prefabs/SmallIceMuzzle.prefab", "_iceMuzzlePrefab", 0.3f, EMode.Brief, EFFECT_HEIGHT),
            new Entry("IceHit", "SmallIceBullet/Prefabs/SmallIceBulletHit.prefab", "_iceHitPrefab", 0.3f, EMode.Brief, EFFECT_HEIGHT),
            new Entry("WindArrow", "WindShoot/Prefabs/WindBullet.prefab", "_windPrefab", 0.5f, EMode.Projectile, EFFECT_HEIGHT),
            new Entry("WindStart", "WindShoot/Prefabs/WindStart.prefab", "_windMuzzlePrefab", 0.3f, EMode.Brief, EFFECT_HEIGHT),
            new Entry("WindHit", "WindShoot/Prefabs/WindHit.prefab", "_windHitPrefab", 0.3f, EMode.Brief, EFFECT_HEIGHT),
            new Entry("FireArrow", "SmallFireBullet/Prefabs/SmallFireBullet.prefab", "_fireArrowPrefab", 0.4f, EMode.Projectile, EFFECT_HEIGHT),
            new Entry("FireMuzzle", "SmallFireBullet/Prefabs/SmallFireMuzzle.prefab", "_fireMuzzlePrefab", 0.3f, EMode.Brief, EFFECT_HEIGHT),
            new Entry("FireHit", "SmallFireBullet/Prefabs/SmallFireBulletHit.prefab", "_fireHitPrefab", 0.3f, EMode.Brief, EFFECT_HEIGHT),
            // 원본 Circle 입자가 지름 약 10m라 배율 1이 반경 5m 폭발과 맞는다. 차지는 시전자 몸 크기로 줄인다
            new Entry("Charge", "EnergyExplosion/Prefabs/EnergyExplosionCharge.prefab", "_chargePrefab", 0.25f, EMode.Long, EFFECT_HEIGHT),
            new Entry("Burst", "EnergyExplosion/Prefabs/EnergyExplosionHit.prefab", "_burstPrefab", 1f, EMode.Long, BURST_HEIGHT)
        };

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
                for (int i = 0; i < G_LegacyNames.Length; ++i)
                {
                    GameObject legacy = AssetDatabase.LoadAssetAtPath<GameObject>(OUTPUT + "/PF_ArcherVfx_" + G_LegacyNames[i] + ".prefab");
                    if (legacy == null)
                    {
                        throw new InvalidOperationException("ReleaseVfxBuilder가 만든 궁수 사본이 없다: " + G_LegacyNames[i]);
                    }

                    serialized.FindProperty(G_LegacyFields[i]).objectReferenceValue = legacy;
                }

                for (int i = 0; i < G_Entries.Length; ++i)
                {
                    serialized.FindProperty(G_Entries[i].Field).objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(GetPath(i));
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
            foreach (Entry entry in G_Entries)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(SOURCE + entry.Source) == null)
                {
                    throw new InvalidOperationException("VFX 설치 누락: " + entry.Source);
                }
            }

            EnsureFolder(OUTPUT);
            for (int i = 0; i < G_Entries.Length; ++i)
            {
                BuildEffect(i);
            }
        }

        private static void BuildEffect(int index)
        {
            Entry entry = G_Entries[index];
            var root = new GameObject("PF_ArcherVfx_" + entry.Name);
            try
            {
                GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SOURCE + entry.Source);
                GameObject effect = UnityEngine.Object.Instantiate(source, root.transform);
                effect.name = entry.Name;
                effect.transform.localPosition = Vector3.up * entry.Height;
                effect.transform.localRotation = Quaternion.identity;
                effect.transform.localScale = Vector3.one * entry.Scale;
                foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    UnityEngine.Object.DestroyImmediate(component);
                }

                foreach (Light light in root.GetComponentsInChildren<Light>(true))
                {
                    UnityEngine.Object.DestroyImmediate(light);
                }

                foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                {
                    UnityEngine.Object.DestroyImmediate(collider);
                }

                foreach (ParticleSystem particle in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ConfigureParticle(particle, entry.Mode);
                }

                foreach (TrailRenderer trail in root.GetComponentsInChildren<TrailRenderer>(true))
                {
                    trail.autodestruct = false;
                    trail.time = TRAIL_SECONDS;
                    trail.widthMultiplier = TRAIL_WIDTH;
                    trail.Clear();
                }

                PrefabUtility.SaveAsPrefabAsset(root, GetPath(index));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void ConfigureParticle(ParticleSystem particle, EMode mode)
        {
            particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = particle.main;
            main.playOnAwake = false;
            main.loop = mode == EMode.Projectile;
            main.stopAction = ParticleSystemStopAction.None;
            main.maxParticles = Mathf.Min(main.maxParticles, (mode == EMode.Long) ? MAX_BURST_PARTICLES : MAX_PARTICLES);
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            switch (mode)
            {
                case EMode.Projectile:
                    // 발사체 이동은 공개 시각 기반 View가 맡으며 입자가 따로 전진하지 않는다.
                    main.startSpeed = 0f;
                    main.simulationSpace = ParticleSystemSimulationSpace.Local;
                    ParticleSystem.VelocityOverLifetimeModule velocity = particle.velocityOverLifetime;
                    velocity.enabled = false;
                    ParticleSystem.ForceOverLifetimeModule force = particle.forceOverLifetime;
                    force.enabled = false;
                    ParticleSystem.InheritVelocityModule inherit = particle.inheritVelocity;
                    inherit.enabled = false;
                    break;
                case EMode.Brief:
                    main.startLifetime = Mathf.Min(main.startLifetime.constantMax, BRIEF_LIFETIME);
                    main.startSpeed = Mathf.Min(main.startSpeed.constantMax, IMPACT_SPEED);
                    break;
                default:
                    main.startLifetime = Mathf.Min(main.startLifetime.constantMax, LONG_LIFETIME);
                    main.startSpeed = Mathf.Clamp(main.startSpeed.constantMax, -BURST_SPEED, BURST_SPEED);
                    break;
            }

            // 원본 일부 입자는 URP에서 지원하지 않는 내장 셰이더를 써서 분홍색 오류로 그려진다. 그 입자만 끈다
            ParticleSystemRenderer renderer = particle.GetComponent<ParticleSystemRenderer>();
            if ((renderer.sharedMaterial == null) || (!renderer.sharedMaterial.shader.isSupported)
                || (renderer.sharedMaterial.shader.name.StartsWith("Legacy Shaders/", StringComparison.Ordinal)))
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
            return OUTPUT + "/PF_ArcherVfx_" + G_Entries[index].Name + ".prefab";
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
