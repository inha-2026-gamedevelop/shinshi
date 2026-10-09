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
        [TestCase("Arcane")]
        [TestCase("Field")]
        [TestCase("Seal")]
        [TestCase("Wave")]
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
                Assert.That(particle.main.loop, Is.EqualTo(name == "Field"));
                Assert.That(particle.main.stopAction, Is.EqualTo(ParticleSystemStopAction.None));
                Assert.That(particle.main.maxParticles, Is.LessThanOrEqualTo(64));
                if (name == "Field")
                {
                    Assert.That(particle.emission.rateOverTime.constantMax, Is.GreaterThan(0f));
                    Assert.That(particle.emission.burstCount, Is.Zero);
                }

                if (name == "Wave")
                {
                    Assert.That(particle.main.startSpeed.constantMax, Is.Zero);
                    Assert.That(particle.main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.Local));
                    Assert.That(particle.velocityOverLifetime.enabled, Is.False);
                    Assert.That(particle.forceOverLifetime.enabled, Is.False);
                }

                ParticleSystemRenderer renderer = particle.GetComponent<ParticleSystemRenderer>();
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
        public void Player_Prefab_KeepsSwordsmanAndConnectsFiveMageknightEffects()
        {
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PLAYER);
            Assert.That(player.GetComponents<MageknightVfxView>().Length, Is.EqualTo(1));
            Assert.That(player.GetComponents<SwordsmanMagicSlashView>().Length, Is.EqualTo(1));
            var serialized = new SerializedObject(player.GetComponent<MageknightVfxView>());
            Assert.That(serialized.FindProperty("_combat").objectReferenceValue, Is.SameAs(player.GetComponent<PlayerCombatState>()));
            foreach (string field in new[] { "_basicPrefab", "_arcanePrefab", "_fieldPrefab", "_sealPrefab", "_wavePrefab" })
            {
                Assert.That(serialized.FindProperty(field).objectReferenceValue, Is.Not.Null, field);
            }
        }
    }
}
#endif
