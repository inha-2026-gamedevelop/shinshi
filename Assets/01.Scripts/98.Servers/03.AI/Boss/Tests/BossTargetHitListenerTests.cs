// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    // 부채꼴 패턴으로 확인한다. 나머지 패턴은 같은 한 줄 호출이라 같은 경로를 탄다
    // 시각은 이진 분수(0.25 단위)로만 넘기고, 가중치와 배율은 이 테스트가 정한 가짜 값이다
    public class BossTargetHitListenerTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string P02_ID    = "wolbaek_p02";
        private const float  TEST_HREF = 3541f;
        private const float  TOLERANCE = 0.001f;

        /****************************************
        *               Helpers
        ****************************************/

        private sealed class Query : ICombatTargetQuery
        {
            public List<FakeCombatTarget> Players { get; } = new List<FakeCombatTarget>();

            public int GetLivingPlayers(List<ICombatTarget> results)
            {
                results.Clear();
                for (int i = 0; i < Players.Count; ++i)
                {
                    results.Add(Players[i]);
                }

                return results.Count;
            }

            public int GetLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons,
                List<ICombatTarget> results)
            {
                results.Clear();
                for (int i = 0; i < Players.Count; ++i)
                {
                    Vector3 offset = Players[i].Position - center;
                    offset.y = 0f;
                    if (offset.sqrMagnitude <= (radius * radius))
                    {
                        results.Add(Players[i]);
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
                target = null;
                return false;
            }
        }

        private class FanListener : IBossFanTelegraphListener
        {
            public void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float radius,
                float arcAngle, float telegraphSeconds) { }
            public void OnAimChanged(int castInstanceId, Vector3 direction) { }
            public void OnDirectionLocked(int castInstanceId, Vector3 direction) { }
            public void OnHit(int castInstanceId, int hitCount) { }
            public void OnCancelled(int castInstanceId, EBossInterruptReason reason) { }
        }

        private sealed class HitFanListener : FanListener, IBossTargetHitListener
        {
            public List<int>     TargetIds  = new List<int>();
            public List<Vector3> Positions  = new List<Vector3>();
            public List<Vector3> Directions = new List<Vector3>();

            public void OnTargetHit(int castInstanceId, int targetId, Vector3 position, Vector3 direction)
            {
                TargetIds.Add(targetId);
                Positions.Add(position);
                Directions.Add(direction);
            }
        }

        private static int RunOneStrike(FanListener listener, Vector3 playerPosition)
        {
            FakeCombatTarget self   = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy };
            FakeCombatTarget player = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = playerPosition };
            Query query = new Query();
            query.Players.Add(player);

            FakeDamageResolver resolver = new FakeDamageResolver();
            BossDamageGate     gate     = new BossDamageGate(resolver);
            gate.Open();

            PatternDefinition definition = new PatternDefinition(P02_ID, BossTestFixtures.BOSS_ID, 1, 15f,
                new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                1f, 0.5f, 120f, 0f, 0.5f, EnemyCatalogSO.AttackShapeSpec.Fan(6f, 100f), 0.24f, true);

            BossFanBlastPattern pattern = new BossFanBlastPattern(TEST_HREF, listener);
            BossPatternContext  context = new BossPatternContext(definition, gate.IssueInstanceId(), player.CombatId, self, query, gate);
            BossAttackFsm       fsm     = new BossAttackFsm();
            Assert.IsTrue(fsm.Start(definition, pattern, in context, 0.0));

            fsm.Tick(0.5);
            fsm.Tick(1.0);
            return resolver.Requests.Count;
        }

        /****************************************
        *                 Tests
        ****************************************/

        [Test]
        public void Tick_ListenerImplementsTargetHit_ReportsHitTargetOnce()
        {
            HitFanListener listener = new HitFanListener();

            int requests = RunOneStrike(listener, new Vector3(0f, 0f, 3f));

            Assert.AreEqual(1, requests);
            Assert.AreEqual(1, listener.TargetIds.Count);
            Assert.AreEqual(10, listener.TargetIds[0]);
            Assert.AreEqual(3f, listener.Positions[0].z, TOLERANCE);
            Assert.AreEqual(1f, listener.Directions[0].z, TOLERANCE);
        }

        [Test]
        public void Tick_ListenerWithoutTargetHit_StillStrikes()
        {
            int requests = RunOneStrike(new FanListener(), new Vector3(0f, 0f, 3f));

            Assert.AreEqual(1, requests);
        }

        [Test]
        public void Tick_TargetOutsideFan_ReportsNothing()
        {
            HitFanListener listener = new HitFanListener();

            int requests = RunOneStrike(listener, new Vector3(0f, 0f, 20f));

            Assert.AreEqual(0, requests);
            Assert.AreEqual(0, listener.TargetIds.Count);
        }
    }
}
