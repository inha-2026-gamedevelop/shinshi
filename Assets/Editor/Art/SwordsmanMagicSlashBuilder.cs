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
    /// <summary> Magic Slashes 원본을 보존하고 검사 전용 파티클 사본을 만들어 플레이어 프리팹에 연결한다 </summary>
    public static class SwordsmanMagicSlashBuilder
    {
        private const string SOURCE = "Assets/99.ThirdParty/VFX/Magic Slashes FX/Prefabs/EffectPrefabs/";
        private const string OUTPUT = "Assets/02.Prefab/VFX/SwordsmanMagicSlash";
        private const string PLAYER = "Assets/02.Prefab/Network/NetPlayer/PF_NetPlayer.prefab";
        private const float MAX_LIFETIME = 0.8f;
        private const int MAX_PARTICLES = 64;

        private static readonly string[] G_Sources = { "Eff1_Left", "Eff1_Right", "Eff1_Right", "Eff7_Slash", "Eff1_Left", "Eff1_Right" };
        private static readonly string[] G_Names = { "Basic", "Wide", "Dash", "Focus", "ChainFirst", "ChainSecond" };
        private static readonly string[] G_Fields = { "_basicPrefab", "_widePrefab", "_dashPrefab", "_focusPrefab", "_chainPrefab", "_chainSecondPrefab" };
        private static readonly float[] G_Scales = { 0.35f, 0.65f, 0.45f, 0.3f, 0.55f, 0.55f };

        [MenuItem("Shinshi/Art/VFX/Build And Wire Swordsman Magic Slashes")]
        public static void BuildAndWire()
        {
            Type viewType = typeof(PlayerCombatState).Assembly.GetType("Shinshi.Art.SwordsmanMagicSlashView");
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (viewType != null)
                {
                    break;
                }

                viewType = assembly.GetType("Shinshi.Art.SwordsmanMagicSlashView");
            }

            if (viewType == null)
            {
                throw new InvalidOperationException("검사 VFX View를 먼저 컴파일해야 한다.");
            }

            // 원본 누락이면 기존 연결을 부분 갱신하지 않는다.
            foreach (string source in G_Sources)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(SOURCE + source + ".prefab") == null)
                {
                    throw new InvalidOperationException("Magic Slashes 설치 누락: " + source);
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
            Debug.Log("[VFX] 검사 기본 공격과 Q/W/E/R Magic Slashes 연결 완료");
        }

        private static GameObject BuildEffect(int index)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SOURCE + G_Sources[index] + ".prefab");
            var root = new GameObject("PF_SwordsmanMagicSlash_" + G_Names[index]);
            try
            {
                GameObject effect = UnityEngine.Object.Instantiate(source, root.transform);
                effect.name = G_Sources[index];
                effect.transform.localPosition = new Vector3(0f, 0.8f, 0f);
                effect.transform.localRotation = Quaternion.identity;
                effect.transform.localScale = Vector3.one * G_Scales[index];

                // 데모의 조명 Update와 수명 스크립트 대신 게임의 고정 풀에서 재생과 정리를 담당한다.
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
                    ParticleSystem.MainModule main = particle.main;
                    main.playOnAwake = false;
                    main.loop = false;
                    main.stopAction = ParticleSystemStopAction.None;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    main.maxParticles = Mathf.Min(main.maxParticles, MAX_PARTICLES);
                    ParticleSystem.MinMaxCurve lifetime = main.startLifetime;
                    lifetime.constantMin = Mathf.Min(lifetime.constantMin, MAX_LIFETIME);
                    lifetime.constantMax = Mathf.Min(lifetime.constantMax, MAX_LIFETIME);
                    main.startLifetime = lifetime;
                    particle.useAutoRandomSeed = false;
                    particle.randomSeed = (uint)(index + 1);
                }

                // 원본의 Unlit 셰이더와 왜곡/컷아웃 데이터를 보존한다. URP에서는 실제 렌더 캡처로 수용한다.
                string path = OUTPUT + "/" + root.name + ".prefab";
                return PrefabUtility.SaveAsPrefabAsset(root, path);
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
