#if UNITY_EDITOR
// Unity
using UnityEditor;
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Common;
using Shinshi.Combat;

namespace Shinshi.Tests.EditMode.Art
{
    public sealed class ArcherVfxAssetTests
    {
        private const string ROOT = "Assets/02.Prefab/VFX/Archer/";
        private const string PLAYER = "Assets/02.Prefab/Network/NetPlayer/PF_NetPlayer.prefab";

        [Test]
        public void Tracker_BurstStep_FiresThreeArrowsAtDataIntervals()
        {
            ArcherVfxTracker tracker = CreateBurstTracker();
            ArcherVfxTracker.Observation obs = BasicObservation();
            tracker.Update(in obs);
            obs.AttackSequence = 1;
            obs.AttackComboStep = 1;
            double start = obs.Now;
            tracker.Update(in obs);
            foreach (double elapsed in new[] { 0.24, 0.41, 0.54 })
            {
                obs.Now = start + elapsed;
                Assert.That(tracker.Update(in obs), Is.EqualTo(2));
                Assert.That(tracker.GetPlay(1).Kind, Is.EqualTo(EArcherVfxKind.Arrow));
                Assert.That(tracker.GetPlay(1).IsHeavy, Is.False);
            }

            obs.Now = start + 0.8;
            Assert.That(tracker.Update(in obs), Is.Zero);
        }

        [Test]
        public void Tracker_BurstCancelledAfterFirstArrow_DoesNotEmitRemainingArrows()
        {
            ArcherVfxTracker tracker = CreateBurstTracker();
            ArcherVfxTracker.Observation obs = BasicObservation();
            tracker.Update(in obs);
            obs.AttackSequence = 1;
            obs.AttackComboStep = 1;
            tracker.Update(in obs);
            obs.Now += 0.24;
            Assert.That(tracker.Update(in obs), Is.EqualTo(2));
            ++obs.ActionCancelSequence;
            Assert.That(tracker.Update(in obs), Is.Zero);
            obs.Now += 0.5;
            Assert.That(tracker.Update(in obs), Is.Zero);
        }

        [Test]
        public void Tracker_NormalArrowHitsDuringHeavyWindup_UsesServerHitFlag()
        {
            ArcherVfxTracker tracker = CreateBurstTracker();
            tracker.HeavyBasicStep = 2;
            ArcherVfxTracker.Observation obs = BasicObservation();
            tracker.Update(in obs);
            obs.AttackSequence = 1;
            obs.AttackComboStep = 2;
            tracker.Update(in obs);
            ++obs.ArrowHitSequence;
            obs.ArrowHitIsHeavy = false;
            Assert.That(tracker.Update(in obs), Is.EqualTo(1));
            Assert.That(tracker.GetPlay(0).IsHeavy, Is.False);
            ++obs.ArrowHitSequence;
            obs.ArrowHitIsHeavy = true;
            Assert.That(tracker.Update(in obs), Is.EqualTo(1));
            Assert.That(tracker.GetPlay(0).IsHeavy, Is.True);
        }

        private static ArcherVfxTracker CreateBurstTracker()
        {
            var tracker = new ArcherVfxTracker();
            var config = new ArcherVfxTracker.Config
            {
                BasicWindup = 0.25f,
                BasicStepWindups = new[] { 0.25f, 0.22f, 0.33f },
                BasicStepShotCounts = new[] { 1, 3, 1 },
                BasicStepShotIntervals = new[] { 0f, 0.15f, 0f },
                BasicCancelOnMove = true,
                MaxLateSeconds = 0.25f,
                ServerTickSeconds = 1f / 30f,
                Basic = new ArcherVfxTracker.ShotSpec { Speed = 24f, Range = 14f }
            };
            tracker.Configure(in config);
            return tracker;
        }

        [TestCase("HeavyArrow")]
        [TestCase("HeavyMuzzle")]
        [TestCase("HeavyHit")]
        public void Pool_ElementTint_PreservesAlphaAndDoesNotRecolorOtherPools(string effect)
        {
            var root = new GameObject("ArcherHeavyTintTest");
            try
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "PF_ArcherVfx_" + effect + ".prefab");
                ParticleSystem[] originals = prefab.GetComponentsInChildren<ParticleSystem>(true);
                Color originalColor = originals[0].main.startColor.color;
                var neutral = new ArcherVfxPool(prefab, root.transform, 1);
                var ice = new ArcherVfxPool(prefab, root.transform, 1, Color.blue);
                var fire = new ArcherVfxPool(prefab, root.transform, 1, Color.red);
                neutral.PlayBrief(Vector3.zero, Quaternion.identity, 0.0, 1f);
                ice.PlayBrief(Vector3.right, Quaternion.identity, 0.0, 1f);
                fire.PlayBrief(Vector3.left, Quaternion.identity, 0.0, 1f);

                for (int slot = 1; slot <= 2; ++slot)
                {
                    ParticleSystem[] systems = root.transform.GetChild(slot).GetComponentsInChildren<ParticleSystem>(true);
                    for (int i = 0; i < systems.Length; ++i)
                    {
                        ParticleSystem system = systems[i];
                        Color color = system.colorOverLifetime.enabled
                            ? system.colorOverLifetime.color.Evaluate(0.5f)
                            : system.main.startColor.Evaluate(0.5f);
                        Assert.That(color.g, Is.EqualTo(0f).Within(0.0001f), system.name);
                        Assert.That(slot == 1 ? color.r : color.b, Is.EqualTo(0f).Within(0.0001f), system.name);
                        Assert.That(slot == 1 ? color.b : color.r, Is.GreaterThan(0f), system.name);
                        Assert.That(system.main.startColor.Evaluate(0.5f).a,
                            Is.EqualTo(originals[i].main.startColor.Evaluate(0.5f).a).Within(0.0001f), system.name);
                        if (system.colorOverLifetime.enabled)
                        {
                            Assert.That(system.colorOverLifetime.color.Evaluate(0.5f).a,
                                Is.EqualTo(originals[i].colorOverLifetime.color.Evaluate(0.5f).a).Within(0.0001f), system.name);
                        }
                    }
                }

                Assert.That(originals[0].main.startColor.color, Is.EqualTo(originalColor));
                Assert.That(root.transform.GetChild(0).GetComponentInChildren<ParticleSystem>(true).main.startColor.color,
                    Is.EqualTo(originalColor));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Pool_ReusedFarAway_ClearsPreviousTrail()
        {
            var root = new GameObject("ArcherTrailTest");
            try
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "PF_ArcherVfx_Arrow.prefab");
                var pool = new ArcherVfxPool(prefab, root.transform, 1);
                pool.PlayProjectile(Vector3.zero, Vector3.forward, 100.0, 24f, 10f, 100.0);
                TrailRenderer trail = root.GetComponentInChildren<TrailRenderer>(true);
                Assert.That(trail, Is.Not.Null);
                trail.AddPosition(Vector3.zero);
                trail.AddPosition(Vector3.forward);
                Assert.That(trail.positionCount, Is.GreaterThan(0));
                pool.PlayProjectile(new Vector3(50f, 0f, 0f), Vector3.forward, 101.0, 24f, 10f, 101.0);
                Assert.That(trail.positionCount, Is.Zero);
                Assert.That(trail.emitting, Is.True);
                pool.StopAll();
                Assert.That(trail.positionCount, Is.Zero);
                Assert.That(trail.emitting, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Basic_FacingChangesAfterWindupStart_KeepsAcceptedAim()
        {
            var tracker = CreateBasicTracker();
            ArcherVfxTracker.Observation obs = BasicObservation();
            tracker.Update(in obs);
            obs.AttackSequence = 1;
            tracker.Update(in obs);
            obs.Now += 0.36;
            obs.Facing = Vector3.right;
            int count = tracker.Update(in obs);
            Assert.That(count, Is.EqualTo(2));
            Assert.That(tracker.GetPlay(1).Direction, Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void Basic_MovementDuringFirstTenthSecond_CancelsPendingShot()
        {
            var tracker = CreateBasicTracker();
            ArcherVfxTracker.Observation obs = BasicObservation();
            tracker.Update(in obs);
            obs.AttackSequence = 1;
            tracker.Update(in obs);
            obs.Now += 0.05;
            obs.LocomotionSpeed = 5f;
            Assert.That(tracker.Update(in obs), Is.Zero);
            obs.Now += 0.35;
            obs.LocomotionSpeed = 0f;
            Assert.That(tracker.Update(in obs), Is.Zero);
        }

        private static ArcherVfxTracker CreateBasicTracker()
        {
            var tracker = new ArcherVfxTracker();
            var config = new ArcherVfxTracker.Config
            {
                BasicWindup = 0.35f,
                BasicCancelOnMove = true,
                MaxLateSeconds = 0.25f,
                Basic = new ArcherVfxTracker.ShotSpec { Speed = 24f, Range = 10f }
            };
            tracker.Configure(in config);
            return tracker;
        }

        private static ArcherVfxTracker.Observation BasicObservation()
        {
            return new ArcherVfxTracker.Observation
            {
                PlayerClass = EPlayerClass.Archer,
                LifeState = EParticipantLifeState.Alive,
                SkillPhase = ESkillPhase.Idle,
                Facing = Vector3.forward,
                Now = 100.0
            };
        }

        [TestCase("Arrow", true, 0.25f)]
        [TestCase("Muzzle", false, 0.25f)]
        [TestCase("Wind", true, 0.25f)]
        [TestCase("WindMuzzle", false, 0.25f)]
        [TestCase("Impact", false, 0.25f)]
        [TestCase("WindArrow", true, 0.5f)]
        [TestCase("WindStart", false, 0.5f)]
        [TestCase("WindHit", false, 0.5f)]
        [TestCase("IceArrow", true, 0.5f)]
        [TestCase("IceMuzzle", false, 0.5f)]
        [TestCase("IceHit", false, 0.5f)]
        [TestCase("FireArrow", true, 0.5f)]
        [TestCase("FireMuzzle", false, 0.5f)]
        [TestCase("FireHit", false, 0.5f)]
        [TestCase("Charge", false, 1.5f)]
        [TestCase("Burst", false, 1.5f)]
        public void Prefab_Generated_HasSafeRenderingAndControlledMotion(string name, bool isProjectile, float maxLifetime)
        {
            int maxParticles = ((name == "Charge") || (name == "Burst")) ? 64 : 24;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "PF_ArcherVfx_" + name + ".prefab");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Light>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty);
            foreach (ParticleSystem particle in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                Assert.That(particle.main.playOnAwake, Is.False);
                Assert.That(particle.main.stopAction, Is.EqualTo(ParticleSystemStopAction.None));
                Assert.That(particle.main.loop, Is.EqualTo(isProjectile));
                Assert.That(particle.main.maxParticles, Is.LessThanOrEqualTo(maxParticles));
                if (isProjectile)
                {
                    Assert.That(particle.main.startSpeed.constantMax, Is.Zero);
                    Assert.That(particle.main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.Local));
                    Assert.That(particle.velocityOverLifetime.enabled, Is.False);
                    Assert.That(particle.forceOverLifetime.enabled, Is.False);
                }
                else
                {
                    Assert.That(particle.main.startLifetime.constantMax, Is.LessThanOrEqualTo(maxLifetime));
                }
            }

            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled)
                {
                    continue;
                }

                Assert.That(renderer.sharedMaterial, Is.Not.Null);
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null)
                    {
                        continue;
                    }

                    Assert.That(material.shader.isSupported, Is.True);
                    Assert.That(material.shader.name, Is.Not.EqualTo("Hidden/InternalErrorShader"));
                }
            }

            foreach (TrailRenderer trail in prefab.GetComponentsInChildren<TrailRenderer>(true))
            {
                Assert.That(trail.autodestruct, Is.False);
                Assert.That(trail.time, Is.LessThanOrEqualTo(0.12f));
                Assert.That(trail.widthMultiplier, Is.LessThanOrEqualTo(0.065f));
            }
        }

        [Test]
        public void ArrowMaterial_Generated_UsesProjectOwnedSupportedShader()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(ROOT + "Materials/M_ArcherArrow.mat");
            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader.name, Is.EqualTo("Shinshi/VFX/ReleaseParticle"));
            Assert.That(material.shader.isSupported, Is.True);
            Assert.That(material.GetTexture("_BaseMap"), Is.Not.Null);
        }

        [Test]
        public void Player_Prefab_ConnectsArcherWithoutRemovingExistingViews()
        {
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PLAYER);
            Component target = null;
            int existingViews = 0;
            foreach (MonoBehaviour component in player.GetComponents<MonoBehaviour>())
            {
                if (component == null)
                {
                    continue;
                }

                string name = component.GetType().Name;
                if ((name == "MageknightVfxView") || (name == "SwordsmanMagicSlashView"))
                {
                    ++existingViews;
                }

                if (name == "ArcherVfxView")
                {
                    Assert.That(target, Is.Null);
                    target = component;
                }
            }

            Assert.That(existingViews, Is.EqualTo(2));
            Assert.That(target, Is.Not.Null);
            var serialized = new SerializedObject(target);
            Assert.That(serialized.FindProperty("_combat").objectReferenceValue, Is.SameAs(player.GetComponent<PlayerCombatState>()));
            foreach (string field in new[] { "_arrowPrefab", "_muzzlePrefab", "_impactPrefab", "_iceArrowPrefab", "_iceMuzzlePrefab",
                "_iceHitPrefab", "_windPrefab", "_windMuzzlePrefab", "_windHitPrefab", "_fireArrowPrefab", "_fireMuzzlePrefab",
                "_fireHitPrefab", "_chargePrefab", "_burstPrefab", "_heavyArrowPrefab", "_heavyMuzzlePrefab", "_heavyHitPrefab" })
            {
                Assert.That(serialized.FindProperty(field).objectReferenceValue, Is.Not.Null, field);
            }
        }
    }
}
#endif
