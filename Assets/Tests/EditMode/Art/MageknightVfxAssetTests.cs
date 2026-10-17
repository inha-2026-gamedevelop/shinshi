#if UNITY_EDITOR
// Unity
using UnityEditor;
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Combat;

namespace Shinshi.Tests.EditMode.Art
{
    public sealed class MageknightVfxAssetTests
    {
        private const string ROOT = "Assets/02.Prefab/VFX/Mageknight/PF_MageknightVfx_";
        private const string PLAYER = "Assets/02.Prefab/Network/NetPlayer/PF_NetPlayer.prefab";

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
                Assert.That(particle.main.loop, Is.EqualTo(name == "Shield"));
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
