#if UNITY_EDITOR
// Unity
using UnityEditor;
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Art;

namespace Shinshi.Tests.EditMode.Art
{
    public sealed class SwordsmanMagicSlashAssetTests
    {
        private const string ROOT = "Assets/02.Prefab/VFX/SwordsmanMagicSlash/";

        [Test]
        public void AwakeDoesNotAllocateEffectsBeforeClassIsKnown()
        {
            var owner = new GameObject("VfxInitializationTest");
            try
            {
                var view = owner.AddComponent<SwordsmanMagicSlashView>();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(SwordsmanMagicSlashView).GetMethod("OnViewAwake", flags).Invoke(view, null);
                Assert.That(typeof(SwordsmanMagicSlashView).GetField("_poolRoot", flags).GetValue(view), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [TestCase("Basic")]
        [TestCase("Wide")]
        [TestCase("Dash")]
        [TestCase("Focus")]
        [TestCase("ChainFirst")]
        [TestCase("ChainSecond")]
        public void EffectHasFiniteParticlesAndNoDemoScripts(string name)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "PF_SwordsmanMagicSlash_" + name + ".prefab");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Light>(true), Is.Empty);
            ParticleSystem[] particles = prefab.GetComponentsInChildren<ParticleSystem>(true);
            Assert.That(particles.Length, Is.GreaterThan(0));
            foreach (ParticleSystem particle in particles)
            {
                Assert.That(particle.main.loop, Is.False);
                Assert.That(particle.main.playOnAwake, Is.False);
                Assert.That(particle.main.stopAction, Is.EqualTo(ParticleSystemStopAction.None));
                Assert.That(particle.main.startLifetime.constantMax, Is.LessThanOrEqualTo(0.8f));
                foreach (Material material in particle.GetComponent<ParticleSystemRenderer>().sharedMaterials)
                {
                    Assert.That(material, Is.Not.Null);
                    Assert.That(material.shader, Is.Not.Null);
                    Assert.That(material.shader.isSupported, Is.True);
                }
            }
        }

        [Test]
        public void NetPlayerConnectsCombatAndAllSixEffects()
        {
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/02.Prefab/Network/NetPlayer/PF_NetPlayer.prefab");
            Component target = null;
            foreach (MonoBehaviour component in player.GetComponents<MonoBehaviour>())
            {
                if ((component != null) && (component.GetType().Name == "SwordsmanMagicSlashView"))
                {
                    Assert.That(target, Is.Null, "VFX View 중복 연결");
                    target = component;
                }
            }

            Assert.That(target, Is.Not.Null);
            var serialized = new SerializedObject(target);
            Assert.That(serialized.FindProperty("_combat").objectReferenceValue, Is.SameAs(player.GetComponent<PlayerCombatState>()));
            foreach (string field in new[] { "_basicPrefab", "_widePrefab", "_dashPrefab", "_focusPrefab", "_chainPrefab", "_chainSecondPrefab" })
            {
                Assert.That(serialized.FindProperty(field).objectReferenceValue, Is.Not.Null, field);
            }
        }
    }
}
#endif
