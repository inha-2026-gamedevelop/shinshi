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
    /// <summary> 보유 VFX에서 마검사 전용 사본을 생성하고 플레이어 프리팹에 연결한다 </summary>
    public static class MageknightVfxBuilder
    {
        private const string HOVL = "Assets/99.ThirdParty/VFX/Hovl Studio/Sword slash VFX/Prefabs/";
        private const string FIRE = "Assets/99.ThirdParty/VFX/PixPlays/ElementalAOE/FireAOE/Version_BuiltIn/FireAoeVFX.prefab";
        private const string OUTPUT = "Assets/02.Prefab/VFX/Mageknight";
        private const string PLAYER = "Assets/02.Prefab/Network/NetPlayer/PF_NetPlayer.prefab";
        private const float FIELD_CYCLE = 1f;
        private const float SHORT_LIFETIME = 0.65f;
        private const float FIELD_LIFETIME = 1.2f;
        private const float SLASH_HEIGHT = 0.8f;
        private const int MAX_PARTICLES = 64;
        private const int FIELD_INDEX = 2;
        private const int WAVE_INDEX = 4;

        private static readonly string[] G_Sources = { HOVL + "Sword Slash 13.prefab", HOVL + "Sword Slash 13.prefab", FIRE,
            HOVL + "Spatial section.prefab", HOVL + "Slash wave.prefab" };
        private static readonly string[] G_Names = { "Basic", "Arcane", "Field", "Seal", "Wave" };
        private static readonly string[] G_Fields = { "_basicPrefab", "_arcanePrefab", "_fieldPrefab", "_sealPrefab", "_wavePrefab" };
        private static readonly float[] G_Scales = { 0.5f, 0.75f, 2f, 0.35f, 0.4f };

        [MenuItem("Shinshi/Art/VFX/Build And Wire Mageknight")]
        public static void BuildAndWire()
        {
            Type viewType = null;
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                viewType = assembly.GetType("Shinshi.Art.MageknightVfxView");
                if (viewType != null)
                {
                    break;
                }
            }

            if (viewType == null)
            {
                throw new InvalidOperationException("마검사 VFX View를 먼저 컴파일해야 한다.");
            }

            foreach (string source in G_Sources)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(source) == null)
                {
                    throw new InvalidOperationException("VFX 설치 누락: " + source);
                }
            }

            EnsureFolder(OUTPUT);
            var prefabs = new GameObject[G_Names.Length];
            for (int i = 0; i < G_Names.Length; ++i)
            {
                prefabs[i] = BuildEffect(i);
            }

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
                    serialized.FindProperty(G_Fields[i]).objectReferenceValue = prefabs[i];
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(player, PLAYER);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(player);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[VFX] 마검사 기본 공격과 Q/W/E/R 연결 완료");
        }

        private static GameObject BuildEffect(int index)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(G_Sources[index]);
            var root = new GameObject("PF_MageknightVfx_" + G_Names[index]);
            try
            {
                GameObject effect = UnityEngine.Object.Instantiate(source, root.transform);
                effect.name = G_Names[index];
                effect.transform.localPosition = Vector3.up * ((index == FIELD_INDEX) ? 0f : SLASH_HEIGHT);
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

                foreach (ParticleSystem particle in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    // 원본의 반복 섬광과 화면 왜곡은 협동 전투에서 가독성을 해치므로 게임 사본에서 끈다.
                    if ((particle.name.IndexOf("Distortion", StringComparison.OrdinalIgnoreCase) >= 0)
                        || (particle.name.IndexOf("Flash", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        particle.gameObject.SetActive(false);
                    }

                    ParticleSystem.MainModule main = particle.main;
                    main.playOnAwake = false;
                    main.loop = index == FIELD_INDEX;
                    main.stopAction = ParticleSystemStopAction.None;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    main.maxParticles = Mathf.Min(main.maxParticles, MAX_PARTICLES);
                    if (index == FIELD_INDEX)
                    {
                        main.duration = FIELD_CYCLE;
                        // 일회 폭발의 버스트를 지속 방출로 바꾸어 반복 주기 사이의 빈 장판을 없앤다.
                        ParticleSystem.EmissionModule emission = particle.emission;
                        float rate = 0f;
                        for (int burstIndex = 0; burstIndex < emission.burstCount; ++burstIndex)
                        {
                            rate += emission.GetBurst(burstIndex).count.constantMax / FIELD_CYCLE;
                        }

                        emission.burstCount = 0;
                        emission.rateOverTime = rate;
                    }

                    float maxLifetime = (index == FIELD_INDEX) ? FIELD_LIFETIME : SHORT_LIFETIME;
                    ParticleSystem.MinMaxCurve lifetime = main.startLifetime;
                    lifetime.constantMin = Mathf.Min(lifetime.constantMin, maxLifetime);
                    lifetime.constantMax = Mathf.Min(lifetime.constantMax, maxLifetime);
                    main.startLifetime = lifetime;
                    particle.useAutoRandomSeed = false;
                    particle.randomSeed = (uint)(index + 1);

                    if (index == WAVE_INDEX)
                    {
                        // R은 View가 서버 공개 시각으로 이동한다. 원본 입자 속도를 더하면 사거리보다 멀리 나간다.
                        main.startSpeed = 0f;
                        main.simulationSpace = ParticleSystemSimulationSpace.Local;
                        ParticleSystem.VelocityOverLifetimeModule velocity = particle.velocityOverLifetime;
                        velocity.enabled = false;
                        ParticleSystem.ForceOverLifetimeModule force = particle.forceOverLifetime;
                        force.enabled = false;
                        ParticleSystem.InheritVelocityModule inherit = particle.inheritVelocity;
                        inherit.enabled = false;
                    }
                }

                return PrefabUtility.SaveAsPrefabAsset(root, OUTPUT + "/" + root.name + ".prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
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
