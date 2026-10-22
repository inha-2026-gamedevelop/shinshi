#if UNITY_EDITOR
// System
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEditor;
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Tests.EditMode.Art
{
    public sealed class MageknightVfxAssetTests
    {
        private const string ROOT = "Assets/02.Prefab/VFX/Mageknight/PF_MageknightVfx_";
        private const string PLAYER = "Assets/02.Prefab/Network/NetPlayer/PF_NetPlayer.prefab";
        private const string SHIELD = "Shield";
        private const string FIELD  = "Field";

        private const double FIELD_START_AT = 100.0;
        private const double BEFORE_END_GAP = 0.1;
        private const int    POOL_SIZE      = 1;

        // 생성기(Editor 어셈블리)는 시험 어셈블리가 직접 참조하지 못해 이름으로 찾아 실제 적용 함수를 호출한다
        private const string BUILDER_TYPE = "Shinshi.Editor.MageknightVfxBuilder";
        private const BindingFlags STATIC_MEMBER = BindingFlags.NonPublic | BindingFlags.Static;

        // Hovl 원본 입자의 길이와 방출 개수, 생성기가 시간표를 고치기 전 값이다
        private const float SOURCE_DURATION = 3f;
        private const short SOURCE_BURST_COUNT = 1;
        private const float SOURCE_END_BURST_AT = 0.8f;
        private const float TIME_TOLERANCE = 0.001f;

        private const int FIELD_LOOPING_COUNT = 9;
        private static readonly string[] G_FieldOneShot = { "ShockWave", "DarkSparksEnd", "DarkSmokeEnd", "Flare" };

        // Hovl "Spatial section" 원본의 입자 이름별 방출 시각. 장판 사본에서 Flash와 Distortion은 생성기가 지운다
        private static readonly Dictionary<string, float[]> G_FieldSourceBursts = new Dictionary<string, float[]>
        {
            { "Field", new[] { 0.18f } },
            { "ShockWave", new[] { 0f } },
            { "DarkSparksEnd", new[] { 0.8f } },
            { "GroundWind", new[] { 0.1f } },
            { "DarkSmokeEnd", new[] { 0.8f } },
            { "Slashes", new[] { 0.8f } },
            { "DarkSparks", new[] { 0f } },
            { "Sparks", new[] { 0.1f } },
            { "Borders", new[] { 0.1f } },
            { "DarkSmoke", new[] { 0f } },
            { "SparksCenter", new[] { 0f } },
            { "Flare", new[] { 0f, 0.8f } },
            { "Light", new[] { 0f } }
        };

        [TestCase("Basic")]
        [TestCase("Shield")]
        [TestCase("Wave")]
        [TestCase("Field")]
        [TestCase("Jump")]
        public void Prefab_Generated_HasControlledParticlesAndSupportedMaterials(string name)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + name + ".prefab");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Light>(true), Is.Empty);
            ParticleSystem[] particles = prefab.GetComponentsInChildren<ParticleSystem>(true);
            Assert.That(particles, Is.Not.Empty);
            foreach (ParticleSystem particle in particles)
            {
                Assert.That(particle.main.playOnAwake, Is.False);
                // 방벽은 전부 반복하고, 장판은 반복 입자와 시작·끝 한 번 입자가 섞여 있어 아래 장판 시험이 따로 본다. 나머지는 한 번만 재생한다
                if (name != FIELD)
                {
                    Assert.That(particle.main.loop, Is.EqualTo(name == SHIELD));
                }

                // 반복 입자도 스스로 꺼지거나 오브젝트를 없애지 않는다. 끝내는 일은 풀만 한다
                Assert.That(particle.main.stopAction, Is.EqualTo(ParticleSystemStopAction.None));
                Assert.That(particle.main.maxParticles, Is.LessThanOrEqualTo(64));
                Assert.That(particle.name.IndexOf("Distortion", System.StringComparison.OrdinalIgnoreCase), Is.LessThan(0));

                if (name == "Wave")
                {
                    Assert.That(particle.main.startSpeed.constantMax, Is.Zero);
                    Assert.That(particle.main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.Local));
                    Assert.That(particle.velocityOverLifetime.enabled, Is.False);
                    Assert.That(particle.forceOverLifetime.enabled, Is.False);
                }

                // 원본에서 꺼 둔 렌더러는 재질 없이 하위 방출만 맡는다
                ParticleSystemRenderer renderer = particle.GetComponent<ParticleSystemRenderer>();
                if (!renderer.enabled)
                {
                    continue;
                }

                Assert.That(renderer.sharedMaterial, Is.Not.Null);
                if (particle.trails.enabled)
                {
                    Assert.That(renderer.trailMaterial, Is.Not.Null);
                }

                foreach (Material material in renderer.sharedMaterials)
                {
                    // 원본은 꺼진 Trails의 두 번째 재질 슬롯을 비워 둔다.
                    if (material == null)
                    {
                        continue;
                    }

                    Assert.That(material.shader, Is.Not.Null);
                    Assert.That(material.shader.isSupported, Is.True);
                    Assert.That(material.shader.name, Is.Not.EqualTo("Hidden/InternalErrorShader"));
                }
            }
        }

        [Test]
        public void Field_HasLoopingBodyAndOneShotBookends()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + FIELD + ".prefab");
            Assert.That(prefab, Is.Not.Null);

            int looping = 0;
            int oneShot = 0;
            foreach (ParticleSystem particle in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (particle.main.loop)
                {
                    ++looping;
                }
                else
                {
                    ++oneShot;
                }
            }

            // 장판 본체는 서버 지속 시간 동안 이어지려고 반복하고, 시작 충격과 끝 마무리는 한 번만 재생한다
            Assert.That(looping, Is.GreaterThan(0));
            Assert.That(oneShot, Is.GreaterThan(0));
        }

        [Test]
        public void Field_PlayedTimedThroughPool_StopsLoopingParticlesAtServerEndTime()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + FIELD + ".prefab");
            Assert.That(prefab, Is.Not.Null);

            var root = new GameObject("MageknightFieldPoolRoot");
            try
            {
                var pool = new MageknightVfxPool(prefab, root.transform, POOL_SIZE);
                GameObject instance = root.transform.GetChild(0).gameObject;
                Assert.That(instance.activeSelf, Is.False);

                double endAt = FIELD_START_AT + GetServerFieldSeconds();
                pool.PlayTimed(Vector3.zero, Quaternion.identity, endAt);
                Assert.That(pool.ActiveCount, Is.EqualTo(1));
                Assert.That(instance.activeSelf, Is.True);

                // 반복 입자는 스스로 끝나지 않으므로 서버 종료 시각 직전까지 풀이 계속 유지한다
                pool.Tick(endAt - BEFORE_END_GAP);
                Assert.That(pool.ActiveCount, Is.EqualTo(1));
                Assert.That(instance.activeSelf, Is.True);

                pool.Tick(endAt);
                Assert.That(pool.ActiveCount, Is.EqualTo(0));
                Assert.That(instance.activeSelf, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void FieldBuilder_AppliedToSourceParticles_ReproducesCommittedPrefabTimeline()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + FIELD + ".prefab");
            Assert.That(prefab, Is.Not.Null);
            ParticleSystem[] committed = prefab.GetComponentsInChildren<ParticleSystem>(true);
            Assert.That(committed.Length, Is.EqualTo(G_FieldSourceBursts.Count));

            int fieldIndex = GetBuilderKindIndex(FIELD);
            float serverSeconds = GetServerFieldSeconds();
            var fixtures = new List<GameObject>();
            try
            {
                int looping = 0;
                int oneShot = 0;
                foreach (ParticleSystem expected in committed)
                {
                    Assert.That(G_FieldSourceBursts.TryGetValue(expected.name, out float[] sourceBursts), Is.True, expected.name);
                    ParticleSystem generated = CreateSourceParticle(expected.name, sourceBursts, fixtures);
                    ApplyBuilder(generated, fieldIndex);

                    Assert.That(generated.main.loop, Is.EqualTo(expected.main.loop), expected.name);
                    Assert.That(generated.main.duration, Is.EqualTo(expected.main.duration).Within(TIME_TOLERANCE), expected.name);
                    Assert.That(generated.emission.rateOverTime.constant, Is.EqualTo(expected.emission.rateOverTime.constant).Within(TIME_TOLERANCE), expected.name);
                    Assert.That(generated.emission.burstCount, Is.EqualTo(expected.emission.burstCount), expected.name);
                    for (int b = 0; b < expected.emission.burstCount; ++b)
                    {
                        Assert.That(generated.emission.GetBurst(b).time, Is.EqualTo(expected.emission.GetBurst(b).time).Within(TIME_TOLERANCE), expected.name + " burst " + b);
                    }

                    // 반복 입자는 서버 지속 시간 안에서 끝나는 입자와 구분되고, 한 번 입자의 끝 방출은 서버 지속 시간 안에 들어온다
                    bool isOneShot = System.Array.IndexOf(G_FieldOneShot, expected.name) >= 0;
                    Assert.That(generated.main.loop, Is.EqualTo(!isOneShot), expected.name);
                    if (isOneShot)
                    {
                        ++oneShot;
                        Assert.That(generated.main.duration, Is.EqualTo(serverSeconds).Within(TIME_TOLERANCE), expected.name);
                        for (int b = 0; b < generated.emission.burstCount; ++b)
                        {
                            Assert.That(generated.emission.GetBurst(b).time, Is.LessThan(serverSeconds), expected.name);
                        }
                    }
                    else
                    {
                        ++looping;
                    }
                }

                Assert.That(looping, Is.EqualTo(FIELD_LOOPING_COUNT));
                Assert.That(oneShot, Is.EqualTo(G_FieldOneShot.Length));
            }
            finally
            {
                foreach (GameObject fixture in fixtures)
                {
                    Object.DestroyImmediate(fixture);
                }
            }
        }

        [TestCase("Basic")]
        [TestCase("Shield")]
        [TestCase("Wave")]
        [TestCase("Jump")]
        public void OtherBuilderKinds_AppliedToSourceParticle_KeepOnlyLoopPolicy(string kind)
        {
            var fixtures = new List<GameObject>();
            try
            {
                // 장판 이름 표와 같은 이름을 줘도 다른 종류는 시간표를 바꾸지 않는다
                ParticleSystem generated = CreateSourceParticle("ShockWave", new[] { 0f, SOURCE_END_BURST_AT }, fixtures);
                ApplyBuilder(generated, GetBuilderKindIndex(kind));

                Assert.That(generated.main.loop, Is.EqualTo(kind == SHIELD));
                Assert.That(generated.main.duration, Is.EqualTo(SOURCE_DURATION).Within(TIME_TOLERANCE));
                Assert.That(generated.emission.burstCount, Is.EqualTo(2));
                Assert.That(generated.emission.GetBurst(1).time, Is.EqualTo(SOURCE_END_BURST_AT).Within(TIME_TOLERANCE));
            }
            finally
            {
                foreach (GameObject fixture in fixtures)
                {
                    Object.DestroyImmediate(fixture);
                }
            }
        }

        [Test]
        public void FieldBuilder_AppliedToUnlistedParticle_LoopsAsBody()
        {
            var fixtures = new List<GameObject>();
            try
            {
                ParticleSystem generated = CreateSourceParticle("UnlistedBodyParticle", new[] { 0.1f }, fixtures);
                ApplyBuilder(generated, GetBuilderKindIndex(FIELD));

                Assert.That(generated.main.loop, Is.True);
                Assert.That(generated.emission.GetBurst(0).time, Is.EqualTo(0.1f).Within(TIME_TOLERANCE));
            }
            finally
            {
                foreach (GameObject fixture in fixtures)
                {
                    Object.DestroyImmediate(fixture);
                }
            }
        }

        private static float GetServerFieldSeconds()
        {
            var catalog = ScriptableObject.CreateInstance<SkillCatalogSO>();
            try
            {
                bool found = catalog.TryGetSkillBySlot(EPlayerClass.Mageknight, Constants.Combat.SKILL_SLOT_E, out SkillCatalogSO.SkillDefinition skill);
                Assert.That(found, Is.True);
                return skill.Effect.Duration;
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        private static System.Type GetBuilderType()
        {
            foreach (Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                System.Type type = assembly.GetType(BUILDER_TYPE, false);
                if (type != null)
                {
                    return type;
                }
            }

            Assert.Fail(BUILDER_TYPE + " 형식을 어셈블리에서 찾지 못했다");
            return null;
        }

        private static int GetBuilderKindIndex(string kind)
        {
            FieldInfo names = GetBuilderType().GetField("G_Names", STATIC_MEMBER);
            Assert.That(names, Is.Not.Null, "G_Names");
            int index = System.Array.IndexOf((string[])names.GetValue(null), kind);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), kind);
            return index;
        }

        // 실제 생성기가 원본 입자마다 호출하는 적용 함수를 그대로 부른다
        private static void ApplyBuilder(ParticleSystem particle, int kindIndex)
        {
            MethodInfo configure = GetBuilderType().GetMethod("ConfigureParticle", STATIC_MEMBER, null, new[] { typeof(ParticleSystem), typeof(int) }, null);
            Assert.That(configure, Is.Not.Null, "ConfigureParticle");
            configure.Invoke(null, new object[] { particle, kindIndex });
        }

        // Hovl 원본 입자와 같은 길이 3초, 반복, 이름별 방출 시각으로 만든 임시 입자
        private static ParticleSystem CreateSourceParticle(string name, float[] burstTimes, List<GameObject> fixtures)
        {
            var go = new GameObject(name);
            fixtures.Add(go);
            ParticleSystem particle = go.AddComponent<ParticleSystem>();
            particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = particle.main;
            main.duration = SOURCE_DURATION;
            main.loop = true;

            ParticleSystem.EmissionModule emission = particle.emission;
            emission.rateOverTime = 0f;
            var bursts = new ParticleSystem.Burst[burstTimes.Length];
            for (int i = 0; i < burstTimes.Length; ++i)
            {
                bursts[i] = new ParticleSystem.Burst(burstTimes[i], SOURCE_BURST_COUNT);
            }

            emission.SetBursts(bursts);
            return particle;
        }

        [Test]
        public void BasicCombo_EveryGroupStartsImmediately()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "Basic.prefab");
            Transform effect = prefab.transform.GetChild(0);
            Assert.That(effect.childCount, Is.GreaterThanOrEqualTo(3));
            for (int g = 0; g < effect.childCount; ++g)
            {
                float first = float.MaxValue;
                foreach (ParticleSystem particle in effect.GetChild(g).GetComponentsInChildren<ParticleSystem>(true))
                {
                    for (int b = 0; b < particle.emission.burstCount; ++b)
                    {
                        first = Mathf.Min(first, particle.emission.GetBurst(b).time);
                    }
                }

                Assert.That(first, Is.EqualTo(0f).Within(0.001f), effect.GetChild(g).name);
            }
        }

        [Test]
        public void Shield_KeepsSpawnAnimationWithoutSourceScript()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "Shield.prefab");
            Animation animation = prefab.GetComponentInChildren<Animation>(true);
            Assert.That(animation, Is.Not.Null);
            Assert.That(animation.clip, Is.Not.Null);
            Assert.That(animation.playAutomatically, Is.True);
        }

        [Test]
        public void Player_Prefab_KeepsSwordsmanAndConnectsFiveMageknightEffects()
        {
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PLAYER);
            Assert.That(player.GetComponents<MageknightVfxView>().Length, Is.EqualTo(1));
            Assert.That(player.GetComponents<SwordsmanMagicSlashView>().Length, Is.EqualTo(1));
            var serialized = new SerializedObject(player.GetComponent<MageknightVfxView>());
            Assert.That(serialized.FindProperty("_combat").objectReferenceValue, Is.SameAs(player.GetComponent<PlayerCombatState>()));
            string[] fields = { "_basicPrefab", "_shieldPrefab", "_wavePrefab", "_fieldPrefab", "_jumpPrefab" };
            string[] names  = { "Basic", "Shield", "Wave", "Field", "Jump" };
            for (int i = 0; i < fields.Length; ++i)
            {
                Object reference = serialized.FindProperty(fields[i]).objectReferenceValue;
                Assert.That(reference, Is.Not.Null, fields[i]);
                Assert.That(AssetDatabase.GetAssetPath(reference), Is.EqualTo(ROOT + names[i] + ".prefab"), fields[i]);
            }
        }
    }
}
#endif
