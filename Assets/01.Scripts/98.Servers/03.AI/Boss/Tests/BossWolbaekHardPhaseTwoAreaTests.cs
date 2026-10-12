using System.Collections.Generic;

using UnityEditor;
using UnityEngine;

using NUnit.Framework;

using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    public class BossWolbaekHardPhaseTwoAreaTests
    {
        private const string CATALOG_PATH = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const string P04_ID = BossPatternCatalogSO.WOLBAEK_HARD_P2_P04_ID;
        private const float HREF = 1000f;

        private sealed class Query : ICombatTargetQuery
        {
            public readonly List<FakeCombatTarget> Targets = new List<FakeCombatTarget>();

            public int GetLivingPlayers(List<ICombatTarget> results)
            {
                results.Clear();
                foreach (FakeCombatTarget target in Targets)
                {
                    if ((target.LifeState == EParticipantLifeState.Alive) && (!target.IsSummon))
                    {
                        results.Add(target);
                    }
                }

                return results.Count;
            }

            public int GetLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons,
                List<ICombatTarget> results)
            {
                results.Clear();
                foreach (FakeCombatTarget target in Targets)
                {
                    Vector3 offset = target.Position - center;
                    offset.y = 0f;
                    if ((target.LifeState == EParticipantLifeState.Alive) && (target.Faction == faction)
                        && (includeSummons || (!target.IsSummon)) && (offset.sqrMagnitude <= radius * radius))
                    {
                        results.Add(target);
                    }
                }

                return results.Count;
            }

            public int CountLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons)
            {
                return GetLivingInRadius(center, radius, faction, includeSummons, new List<ICombatTarget>());
            }

            public bool TryGet(int combatId, out ICombatTarget target)
            {
                foreach (FakeCombatTarget candidate in Targets)
                {
                    if (candidate.CombatId == combatId)
                    {
                        target = candidate;
                        return true;
                    }
                }

                target = null;
                return false;
            }
        }

        private sealed class Listener : IBossHardAreaListener, IBossAreaListener, IBossParticipantConnection
        {
            public readonly HashSet<int> Disconnected = new HashSet<int>();
            public readonly List<Vector3> Fixed = new List<Vector3>();
            public readonly List<Vector3> Activated = new List<Vector3>();
            public readonly List<double> ActivatedTimes = new List<double>();
            public readonly List<int> HitIndices = new List<int>();
            public readonly List<int> EndedAreas = new List<int>();
            public int CancelCount;

            public bool IsConnected(int combatId) => !Disconnected.Contains(combatId);

            public void OnMovingStarted(int castInstanceId, int areaId, int targetCombatId, Vector3 center, float radius,
                double startServerTime, float followSeconds, float fixedSeconds) { }

            public void OnMovingUpdated(int castInstanceId, int areaId, Vector3 center, double serverTime) { }

            public void OnCenterFixed(int castInstanceId, int areaId, Vector3 center, double fixedServerTime)
            {
                Fixed.Add(center);
            }

            public void OnCancelled(int castInstanceId, int areaId, EBossInterruptReason reason)
            {
                ++CancelCount;
            }

            public void OnAreaActivated(int castInstanceId, int areaId, Vector3 center, float radius, float durationSeconds,
                float tickInterval, double activateServerTime)
            {
                Activated.Add(center);
                ActivatedTimes.Add(activateServerTime);
            }

            public void OnAreaTick(int castInstanceId, int areaId, int hitIndex, double roundServerTime, int hitCount)
            {
                HitIndices.Add(hitIndex);
            }

            public void OnAreaEnded(int castInstanceId, int areaId, EBossAreaEnd end, double endServerTime)
            {
                EndedAreas.Add(areaId);
            }
        }

        private sealed class Random : IBossRandom
        {
            public int NextInt(int maxExclusive) => 0;
            public float NextFloat01() => 0f;
        }

        private sealed class Run
        {
            public readonly Query Targets = new Query();
            public readonly Listener Events = new Listener();
            public readonly FakeDamageResolver Resolver = new FakeDamageResolver();
            public readonly BossDamageGate Gate;
            public readonly BossAreaManager Areas;
            public readonly BossAttackFsm Fsm = new BossAttackFsm();
            public readonly PatternDefinition Definition;
            public readonly FakeCombatTarget Self = new FakeCombatTarget
            {
                CombatId = 1, Faction = ECombatFaction.Enemy, Position = new Vector3(100f, 0f, 100f)
            };

            public Run()
            {
                Gate = new BossDamageGate(Resolver);
                Gate.Open();
                Areas = new BossAreaManager(Targets, Events);
                BossPatternCatalogSO catalog = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
                Assert.IsNotNull(catalog);
                Assert.IsTrue(catalog.TryGetPattern(P04_ID, out PatternDefinition definition));
                Definition = definition;
            }

            public FakeCombatTarget Add(int id, Vector3 position)
            {
                FakeCombatTarget target = new FakeCombatTarget
                {
                    CombatId = id, Faction = ECombatFaction.Player, Position = position
                };
                Targets.Targets.Add(target);
                return target;
            }

            public BossHardAreaPattern Start(int targetId, double time = 0)
            {
                var factory = new WolbaekPatternFactory(HREF, areas: Areas, hardAreaListener: Events, connections: Events);
                BossHardAreaPattern pattern = (BossHardAreaPattern)factory.Create(Definition);
                Assert.IsNotNull(pattern);
                int cast = Gate.IssueInstanceId();
                BossPatternContext context = new BossPatternContext(Definition, cast, targetId, Self, Targets, Gate, new Random());
                Assert.IsTrue(Fsm.Start(Definition, pattern, in context, time));
                return pattern;
            }

            public void Tick(double time)
            {
                Fsm.Tick(time);
                Areas.Tick(time);
            }
        }

        [Test]
        public void Asset_HardP04_HasTrackingAndSixtyRounds()
        {
            Run run = new Run();
            Assert.AreEqual(4f, run.Definition.TelegraphSeconds);
            Assert.AreEqual(4f, run.Definition.TrackingSeconds);
            Assert.AreEqual(2.5f, run.Definition.ActiveSeconds);
            Assert.AreEqual(1f, run.Definition.RecoverySeconds);
            Assert.AreEqual(6f, run.Definition.Shape.Range);
            Assert.AreEqual(60, BossAreaManager.ComputeHitCount(run.Definition.AreaDurationSeconds,
                run.Definition.AreaTickInterval));
            Assert.AreEqual(2, run.Definition.MaxConcurrentAreas);
        }

        [Test]
        public void Factory_MissingVisualOrConnection_RejectsHardP04()
        {
            Run run = new Run();
            Assert.IsFalse(new WolbaekPatternFactory(HREF, areas: run.Areas).CanCreate(run.Definition));
            Assert.IsNull(new WolbaekPatternFactory(HREF, areas: run.Areas).Create(run.Definition));
            Assert.IsTrue(new WolbaekPatternFactory(HREF, areas: run.Areas, hardAreaListener: run.Events,
                connections: run.Events).CanCreate(run.Definition));
        }

        [Test]
        public void Tracking_FixesAtFourAndActivatesAtSixPointFive()
        {
            Run run = new Run();
            FakeCombatTarget target = run.Add(10, Vector3.zero);
            run.Start(10);
            target.Position = new Vector3(5f, 0f, 0f);
            run.Tick(3.9);
            run.Tick(4.0);
            target.Position = new Vector3(20f, 0f, 0f);
            run.Tick(6.499);
            Assert.AreEqual(0, run.Events.Activated.Count);
            run.Tick(6.5);
            Assert.AreEqual(new Vector3(5f, 0f, 0f), run.Events.Fixed[0]);
            Assert.AreEqual(run.Events.Fixed[0], run.Events.Activated[0]);
            Assert.AreEqual(6.5, run.Events.ActivatedTimes[0]);
            Assert.AreEqual(EBossAttackState.Recover, run.Fsm.State);
            run.Tick(7.5);
            Assert.AreEqual(EBossAttackState.Ready, run.Fsm.State);
        }

        [Test]
        public void Disconnect_FreezesAtLastObservedPosition_AndReconnectDoesNotResume()
        {
            Run run = new Run();
            FakeCombatTarget target = run.Add(10, Vector3.zero);
            run.Start(10);
            target.Position = new Vector3(3f, 0f, 0f);
            run.Tick(1.0);
            run.Events.Disconnected.Add(10);
            target.Position = new Vector3(8f, 0f, 0f);
            run.Tick(2.0);
            run.Events.Disconnected.Remove(10);
            run.Tick(4.0);
            run.Tick(6.5);
            Assert.AreEqual(new Vector3(3f, 0f, 0f), run.Events.Activated[0]);
        }

        [Test]
        public void LargeTickJump_DoesNotUsePositionObservedAfterFreezeBoundary()
        {
            Run run = new Run();
            FakeCombatTarget target = run.Add(10, Vector3.zero);
            run.Start(10);
            target.Position = new Vector3(20f, 0f, 0f);
            run.Tick(10.0);
            Assert.AreEqual(Vector3.zero, run.Events.Fixed[0]);
            Assert.AreEqual(Vector3.zero, run.Events.Activated[0]);
            Assert.AreEqual(6.5, run.Events.ActivatedTimes[0]);
        }

        [Test]
        public void CancelDuringTelegraph_KeepsOldActiveArea()
        {
            Run run = new Run();
            run.Add(10, Vector3.zero);
            run.Start(10);
            run.Tick(6.5);
            Assert.AreEqual(1, run.Areas.ActiveCount);
            run.Tick(7.5);
            run.Start(10, 8.0);
            run.Fsm.Cancel(EBossInterruptReason.PhaseTransition);
            Assert.AreEqual(1, run.Areas.ActiveCount);
            Assert.AreEqual(1, run.Events.CancelCount);
        }

        [Test]
        public void Activation_ReplacesOldestOnlyWhenNewAreaActivates()
        {
            Run run = new Run();
            run.Add(10, Vector3.zero);
            run.Start(10);
            run.Tick(6.5);
            run.Tick(7.5);
            run.Start(10, 8.0);
            Assert.AreEqual(1, run.Areas.ActiveCount);
            run.Tick(14.5);
            Assert.AreEqual(2, run.Areas.ActiveCount);
            run.Tick(15.5);
            run.Start(10, 16.0);
            Assert.AreEqual(2, run.Areas.ActiveCount);
            run.Tick(22.5);
            Assert.AreEqual(2, run.Areas.ActiveCount);
            Assert.AreEqual(1, run.Events.EndedAreas.Count);
        }

        [Test]
        public void TwoTargets_ReceiveDistinctOverlappingAreasAndTwoHitsEach()
        {
            Run run = new Run();
            run.Add(10, Vector3.zero);
            run.Add(11, new Vector3(4f, 0f, 0f));
            BossHardAreaPattern pattern = run.Start(10);
            Assert.AreEqual(2, pattern.AreaCount);
            Assert.AreNotEqual(pattern.GetAreaId(0), pattern.GetAreaId(1));
            run.Tick(6.5);
            Assert.AreEqual(2, run.Areas.ActiveCount);
            Assert.AreEqual(4, run.Resolver.Requests.Count);
            Assert.AreEqual(2, run.Events.HitIndices.Count);
        }

        [Test]
        public void PhaseTransition_ClearsActiveAreasAndStopsFutureHits()
        {
            Run run = new Run();
            run.Add(10, Vector3.zero);
            run.Start(10);
            run.Tick(6.5);
            int hits = run.Resolver.Requests.Count;
            run.Fsm.Cancel(EBossInterruptReason.PhaseTransition);
            run.Areas.Cancel(EBossInterruptReason.PhaseTransition, CombatIds.NONE);
            run.Tick(7.5);
            Assert.AreEqual(0, run.Areas.TotalAreaCount);
            Assert.AreEqual(hits, run.Resolver.Requests.Count);
        }

        [Test]
        public void SixtyRounds_EndBoundaryHasNoExtraHit()
        {
            Run run = new Run();
            run.Add(10, Vector3.zero);
            run.Start(10);
            run.Tick(6.5);
            for (int i = 1; i < 60; ++i)
            {
                run.Tick(6.5 + i);
            }

            Assert.AreEqual(60, run.Events.HitIndices.Count);
            Assert.AreEqual(59, run.Events.HitIndices[59]);
            run.Tick(66.5);
            Assert.AreEqual(60, run.Events.HitIndices.Count);
            Assert.AreEqual(0, run.Areas.ActiveCount);
        }
    }
}
