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
    /// <summary> 기획에서 고른 외부 VFX로 마검사 전용 사본을 생성하고 플레이어 프리팹에 연결한다, 원본 프리팹은 바꾸지 않는다 </summary>
    public static class MageknightVfxBuilder
    {
        private const string HOVL   = "Assets/99.ThirdParty/VFX/Hovl Studio/Sword slash VFX/Prefabs/";
        private const string SHIELD = "Assets/99.ThirdParty/VFX/PixPlays/ElementalShields/WaterShield/Version_BuiltIn/WaterShield.prefab";
        private const string JUMP   = "Assets/99.ThirdParty/VFX/Magic Slashes FX/Prefabs/EffectPrefabs/Eff11_Jump.prefab";
        private const string OUTPUT = "Assets/02.Prefab/VFX/Mageknight";
        private const string PREFIX = "PF_MageknightVfx_";
        private const string PLAYER = "Assets/02.Prefab/Network/NetPlayer/PF_NetPlayer.prefab";

        private const float SHORT_LIFETIME = 0.65f;
        private const float JUMP_LIFETIME  = 1.5f;
        private const float WAVE_HEIGHT    = 0.8f;
        private const int   MAX_PARTICLES  = 64;

        private const int BASIC_INDEX  = 0;
        private const int SHIELD_INDEX = 1;
        private const int WAVE_INDEX   = 2;
        private const int FIELD_INDEX  = 3;
        private const int JUMP_INDEX   = 4;

        private const string SPAWN_CLIP_TOKEN   = "Spawn";
        private const string DESPAWN_CLIP_TOKEN = "Despawn";

        // E 장판 시간표. 서버 장판 지속 시간(skill_mageknight_flame_field, 4초) 동안 반복 본체가 이어지고
        // 시작 충격과 끝 마무리 입자만 한 번 재생한다. 끝내는 일은 MageknightVfxPool.PlayTimed가 서버 종료 시각에 한다
        private const float FIELD_DURATION        = 4f;
        private const float FIELD_LOOP_SECONDS    = 1f;
        private const float FIELD_END_BURST_LEAD  = 1f;
        private const float SOURCE_END_BURST_AT   = 0.8f;
        private const float FIELD_BLADE_RATE      = 45f;
        private const string FIELD_BLADES         = "Slashes";

        // 원본 이름 그대로 구분한다. 시작 충격 ShockWave, 끝 마무리 DarkSparksEnd, DarkSmokeEnd, Flare만 한 번 재생하고 나머지 9개는 반복한다
        private static readonly string[] G_FieldOneShot = { "ShockWave", "DarkSparksEnd", "DarkSmokeEnd", "Flare" };

        // 순서는 View의 EMageknightVfxKind와 같다. 기본 공격, Q 방벽, W 검파, E 장판, R 순간 이동 베기
        private static readonly string[] G_Sources = { HOVL + "Sword Slash Combo 6.prefab", SHIELD, HOVL + "Slash wave.prefab",
            HOVL + "Spatial section.prefab", JUMP };
        private static readonly string[] G_Names  = { "Basic", "Shield", "Wave", "Field", "Jump" };
        private static readonly string[] G_Fields = { "_basicPrefab", "_shieldPrefab", "_wavePrefab", "_fieldPrefab", "_jumpPrefab" };
        // TODO(민성): 크기는 판정 범위(기본 2.5m, W 폭 2m, E 반경 2.5m, R 6m)에 맞춘 초안이다. 실제 화면 대조 뒤 조정
        private static readonly float[] G_Scales = { 0.7f, 1f, 0.5f, 0.8f, 0.75f };

        // 스택형 개편 전 이름. 같은 GUID를 이어받아 플레이어 프리팹의 참조가 그대로 새 효과를 가리킨다
        private static readonly string[] G_Renames = { "Arcane", "Shield", "Seal", "Jump" };

        // 협동 전투 가독성을 해치는 화면 왜곡, 반복 섬광과 기본 공격 뒤 오래 남는 불꽃은 사본에서 지운다
        private static readonly string[] G_Removed = { "Distortion", "Flash" };
        private const string BASIC_REMOVED = "Fire";

        [MenuItem("Shinshi/Art/VFX/Build And Wire Mageknight")]
        public static void BuildAndWire()
        {
            Type viewType = FindViewType();
            GameObject[] prefabs = BuildEffects();

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

        /// <summary> 효과 사본만 다시 만든다, 기존 경로의 GUID를 유지하므로 플레이어 프리팹은 저장하지 않는다 </summary>
        [MenuItem("Shinshi/Art/VFX/Build Mageknight Effects Only")]
        public static GameObject[] BuildEffects()
        {
            foreach (string source in G_Sources)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(source) == null)
                {
                    throw new InvalidOperationException("VFX 설치 누락: " + source);
                }
            }

            EnsureFolder(OUTPUT);
            RenameLegacy();
            var prefabs = new GameObject[G_Names.Length];
            for (int i = 0; i < G_Names.Length; ++i)
            {
                prefabs[i] = BuildEffect(i);
            }

            AssetDatabase.SaveAssets();
            return prefabs;
        }

        private static Type FindViewType()
        {
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type viewType = assembly.GetType("Shinshi.Art.MageknightVfxView");
                if (viewType != null)
                {
                    return viewType;
                }
            }

            throw new InvalidOperationException("마검사 VFX View를 먼저 컴파일해야 한다.");
        }

        private static void RenameLegacy()
        {
            for (int i = 0; i < G_Renames.Length; i += 2)
            {
                string from = OUTPUT + "/" + PREFIX + G_Renames[i] + ".prefab";
                string to   = OUTPUT + "/" + PREFIX + G_Renames[i + 1] + ".prefab";
                if ((AssetDatabase.LoadAssetAtPath<GameObject>(from) == null) || (AssetDatabase.LoadAssetAtPath<GameObject>(to) != null))
                {
                    continue;
                }

                string error = AssetDatabase.MoveAsset(from, to);
                if (!string.IsNullOrEmpty(error))
                {
                    throw new InvalidOperationException("VFX 사본 이름 변경 실패: " + error);
                }
            }
        }

        private static GameObject BuildEffect(int index)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(G_Sources[index]);
            var root = new GameObject(PREFIX + G_Names[index]);
            try
            {
                GameObject effect = UnityEngine.Object.Instantiate(source, root.transform);
                effect.name = G_Names[index];
                effect.transform.localPosition = Vector3.up * ((index == WAVE_INDEX) ? WAVE_HEIGHT : 0f);
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

                RemoveNoisyParticles(root, index);
                if (index == SHIELD_INDEX)
                {
                    PrepareShieldAnimation(root);
                }

                if (index == BASIC_INDEX)
                {
                    RebaseComboGroups(effect.transform);
                }

                foreach (ParticleSystem particle in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ConfigureParticle(particle, index);
                }

                return PrefabUtility.SaveAsPrefabAsset(root, OUTPUT + "/" + root.name + ".prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // 묶음 선택이 다시 켜지 않도록 끄지 않고 지운다
        private static void RemoveNoisyParticles(GameObject root, int index)
        {
            foreach (ParticleSystem particle in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (particle == null)
                {
                    continue;
                }

                string name = particle.name;
                bool remove = (index == BASIC_INDEX) && (string.Equals(name, BASIC_REMOVED, StringComparison.Ordinal));
                for (int i = 0; (!remove) && (i < G_Removed.Length); ++i)
                {
                    remove = name.IndexOf(G_Removed[i], StringComparison.OrdinalIgnoreCase) >= 0;
                }

                if (remove)
                {
                    UnityEngine.Object.DestroyImmediate(particle.gameObject);
                }
            }
        }

        // 원본은 재생 스크립트가 등장 애니메이션을 고르므로 스크립트를 지운 사본은 켜질 때 등장 애니메이션을 스스로 튼다
        private static void PrepareShieldAnimation(GameObject root)
        {
            Animation animation = root.GetComponentInChildren<Animation>(true);
            if (animation == null)
            {
                return;
            }

            foreach (AnimationState state in animation)
            {
                if ((state.name.IndexOf(SPAWN_CLIP_TOKEN, StringComparison.Ordinal) >= 0)
                    && (state.name.IndexOf(DESPAWN_CLIP_TOKEN, StringComparison.Ordinal) < 0))
                {
                    animation.clip = state.clip;
                }
            }

            animation.playAutomatically = true;
        }

        // 원본 콤보는 네 베기가 시간 차를 두고 이어진다. 기본 공격 한 번에 한 묶음만 바로 재생하도록 묶음마다 첫 방출 시각을 0으로 당긴다
        private static void RebaseComboGroups(Transform effect)
        {
            for (int g = 0; g < effect.childCount; ++g)
            {
                ParticleSystem[] systems = effect.GetChild(g).GetComponentsInChildren<ParticleSystem>(true);
                float first = float.MaxValue;
                foreach (ParticleSystem particle in systems)
                {
                    ParticleSystem.EmissionModule emission = particle.emission;
                    for (int b = 0; b < emission.burstCount; ++b)
                    {
                        first = Mathf.Min(first, emission.GetBurst(b).time);
                    }
                }

                if (first == float.MaxValue)
                {
                    continue;
                }

                foreach (ParticleSystem particle in systems)
                {
                    ParticleSystem.EmissionModule emission = particle.emission;
                    for (int b = 0; b < emission.burstCount; ++b)
                    {
                        ParticleSystem.Burst burst = emission.GetBurst(b);
                        burst.time = Mathf.Max(0f, burst.time - first);
                        emission.SetBurst(b, burst);
                    }
                }
            }
        }

        private static void ConfigureParticle(ParticleSystem particle, int index)
        {
            particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = particle.main;
            main.playOnAwake = false;
            main.loop = IsLoopingParticle(index, particle.name);
            main.stopAction = ParticleSystemStopAction.None;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = Mathf.Min(main.maxParticles, MAX_PARTICLES);

            float maxLifetime = 0f;
            if ((index == BASIC_INDEX) || (index == WAVE_INDEX))
            {
                maxLifetime = SHORT_LIFETIME;
            }
            else if (index == JUMP_INDEX)
            {
                maxLifetime = JUMP_LIFETIME;
            }

            if (maxLifetime > 0f)
            {
                ParticleSystem.MinMaxCurve lifetime = main.startLifetime;
                lifetime.constantMin = Mathf.Min(lifetime.constantMin, maxLifetime);
                lifetime.constantMax = Mathf.Min(lifetime.constantMax, maxLifetime);
                main.startLifetime = lifetime;
            }

            particle.useAutoRandomSeed = false;
            particle.randomSeed = (uint)(index + 1);

            if (index == WAVE_INDEX)
            {
                // W는 View가 서버 공개 시각으로 이동한다. 원본 입자 속도를 더하면 사거리보다 멀리 나간다.
                main.startSpeed = 0f;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                ParticleSystem.VelocityOverLifetimeModule velocity = particle.velocityOverLifetime;
                velocity.enabled = false;
                ParticleSystem.ForceOverLifetimeModule force = particle.forceOverLifetime;
                force.enabled = false;
                ParticleSystem.InheritVelocityModule inherit = particle.inheritVelocity;
                inherit.enabled = false;
            }

            if (index == FIELD_INDEX)
            {
                ConfigureFieldTimeline(particle);
            }
        }

        // 방벽은 면역 시간 동안 전부 반복한다. 장판은 한 번 재생하는 4개(G_FieldOneShot)를 뺀 나머지가 서버 지속 시간 동안 반복한다
        private static bool IsLoopingParticle(int index, string particleName)
        {
            if (index == SHIELD_INDEX)
            {
                return true;
            }

            return (index == FIELD_INDEX) && (Array.IndexOf(G_FieldOneShot, particleName) < 0);
        }

        // 반복 본체는 짧은 주기로 돌리고, 한 번 재생하는 입자는 서버 지속 시간 전체를 길이로 삼아 끝 마무리를 그 끝 가까이로 옮긴다.
        // 칼날은 한 번 터뜨리는 대신 지속 시간 내내 일정하게 흩뿌린다
        private static void ConfigureFieldTimeline(ParticleSystem particle)
        {
            bool isOneShot = Array.IndexOf(G_FieldOneShot, particle.name) >= 0;
            ParticleSystem.MainModule main = particle.main;
            main.duration = isOneShot ? FIELD_DURATION : FIELD_LOOP_SECONDS;

            ParticleSystem.EmissionModule emission = particle.emission;
            if (string.Equals(particle.name, FIELD_BLADES, StringComparison.Ordinal))
            {
                emission.SetBursts(Array.Empty<ParticleSystem.Burst>());
                emission.rateOverTime = FIELD_BLADE_RATE;
                return;
            }

            if (!isOneShot)
            {
                return;
            }

            for (int b = 0; b < emission.burstCount; ++b)
            {
                ParticleSystem.Burst burst = emission.GetBurst(b);
                if (Mathf.Approximately(burst.time, SOURCE_END_BURST_AT))
                {
                    burst.time = FIELD_DURATION - FIELD_END_BURST_LEAD;
                    emission.SetBurst(b, burst);
                }
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
