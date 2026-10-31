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
    // 돌진하는 보스 몸통(밀침 반경 + 대상 반경)과 겹친 대상이 경로 직사각형 밖이어도 한 번 맞고 옆으로 밀리는지 본다.
    // 서버 틱, 실제 밀림 적용(PlayerServer.Displace), NavMesh, 틱 끝 밀어내기는 거치지 않는다
    public class BossDashBodyPushTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string P01_ID         = "wolbaek_p01";
        private const float  TEST_HREF      = 3541f;
        private const float  RANGE          = 8f;
        private const float  WIDTH          = 2f;   // 직사각형 반폭 1m
        private const float  PUSH_RADIUS    = 1f;   // 막히는 거리 1.5m(플레이어 반경 0.5m)
        private const float  PLAYER_RADIUS  = 0.5f;
        private const float  BOSS_RADIUS    = 2f;
        private const float  STEP           = 0.01f;
        private const double END_TIME       = 2.3;  // 예고 1.6초 + 실행 0.45초 뒤
        private const float  TOLERANCE      = 0.0001f;

        /****************************************
        *               Fakes
        ****************************************/

        private sealed class BlockingSelf : ICombatTarget, IBossBodyBlocker
        {
            public int                   CombatId       => 1;
            public ECombatFaction        Faction        => ECombatFaction.Enemy;
            public EParticipantLifeState LifeState      => EParticipantLifeState.Alive;
            public bool                  IsSummon       => false;
            public int                   OwnerCombatId  => CombatIds.NONE;
            public Vector3               Position       { get; set; }
            public Vector3               Forward        => Vector3.forward;
            public float                 BodyRadius     => BOSS_RADIUS;
            public float                 BodyPushRadius => PUSH_RADIUS;
            public bool                  BlocksBodies   => true;
        }

        private sealed class PlainSelf : ICombatTarget
        {
            public int                   CombatId      => 1;
            public ECombatFaction        Faction       => ECombatFaction.Enemy;
            public EParticipantLifeState LifeState     => EParticipantLifeState.Alive;
            public bool                  IsSummon      => false;
            public int                   OwnerCombatId => CombatIds.NONE;
            public Vector3               Position      { get; set; }
            public Vector3               Forward       => Vector3.forward;
            public float                 BodyRadius    => BOSS_RADIUS;
        }

        // 벽 없이 그대로 옮기고 민 요청만 기록한다
        private sealed class PushingMovement : IBossMovement, IBossHitDisplacer
        {
            public System.Action<Vector3> MoveSelf;
            public List<int>     PushedIds        { get; } = new List<int>();
            public List<Vector3> PushedDirections { get; } = new List<Vector3>();
            public List<float>   PushedDistances  { get; } = new List<float>();

            public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
            {
                return maxDistance;
            }

            public Vector3 Move(Vector3 from, Vector3 to, float bodyRadius)
            {
                MoveSelf(to);
                return to;
            }

            public float TryPush(int combatId, Vector3 direction, float maxDistance)
            {
                PushedIds.Add(combatId);
                PushedDirections.Add(direction);
                PushedDistances.Add(maxDistance);
                return maxDistance;
            }
        }

        private sealed class ListQuery : ICombatTargetQuery
        {
            public List<FakeCombatTarget> Targets { get; } = new List<FakeCombatTarget>();

            public int GetLivingPlayers(List<ICombatTarget> results)
            {
                results.Clear();
                results.AddRange(Targets);
                return results.Count;
            }

            public int GetLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons,
                List<ICombatTarget> results)
            {
                results.Clear();
                for (int i = 0; i < Targets.Count; ++i)
                {
                    Vector3 offset = Targets[i].Position - center;
                    offset.y = 0f;
                    if ((Targets[i].Faction == faction) && (offset.sqrMagnitude <= (radius * radius)))
                    {
                        results.Add(Targets[i]);
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
                for (int i = 0; i < Targets.Count; ++i)
                {
                    if (Targets[i].CombatId == combatId)
                    {
                        target = Targets[i];
                        return true;
                    }
                }

                target = null;
                return false;
            }
        }

        /****************************************
        *               Helpers
        ****************************************/

        private static PatternDefinition MakeP01()
        {
            return new PatternDefinition(P01_ID, BossTestFixtures.BOSS_ID, 1, 20f,
                new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                1.6f, 0.4f, 180f, 0.45f, 1.6f, EnemyCatalogSO.AttackShapeSpec.Dash(RANGE, WIDTH), 0.18f, true);
        }

        private static FakeCombatTarget AddPlayer(ListQuery query, int id, Vector3 position)
        {
            var player = new FakeCombatTarget
            {
                CombatId = id, Faction = ECombatFaction.Player, Position = position, BodyRadius = PLAYER_RADIUS
            };
            query.Targets.Add(player);
            return player;
        }

        private static int CountHits(List<DamageRequest> requests, int targetId)
        {
            int count = 0;
            for (int i = 0; i < requests.Count; ++i)
            {
                if (requests[i].HitTargetId == targetId)
                {
                    ++count;
                }
            }

            return count;
        }

        // 정면 멀리 있는 대상을 쫓게 해 조준을 +z로 둔다
        private static FakeDamageResolver RunDash(ICombatTarget self, PushingMovement movement, ListQuery query)
        {
            FakeCombatTarget tracked  = AddPlayer(query, 99, new Vector3(0f, 0f, 30f));
            var              resolver = new FakeDamageResolver();
            var              gate     = new BossDamageGate(resolver);
            gate.Open();

            var pattern    = new BossDashPattern(TEST_HREF, movement);
            var definition = MakeP01();
            var context    = new BossPatternContext(definition, gate.IssueInstanceId(), tracked.CombatId, self, query, gate);
            var fsm        = new BossAttackFsm();
            Assert.IsTrue(fsm.Start(definition, pattern, in context, 0.0));

            int count = (int)System.Math.Round(END_TIME / STEP);
            for (int i = 1; i <= count; ++i)
            {
                fsm.Tick(i * (double)STEP);
            }

            return resolver;
        }

        /****************************************
        *                Tests
        ****************************************/

        // 옆 1.3m는 직사각형(반폭 1m) 밖이지만 몸통 막힘 거리 1.5m 안이다
        [Test]
        public void Dash_BodyOverlapOutsideRectangle_HitsOnceAndPushesAside()
        {
            var query    = new ListQuery();
            var self     = new BlockingSelf();
            var movement = new PushingMovement { MoveSelf = p => self.Position = p };
            FakeCombatTarget side = AddPlayer(query, 10, new Vector3(1.3f, 0f, 4f));
            FakeCombatTarget far  = AddPlayer(query, 11, new Vector3(-1.6f, 0f, 5f));

            FakeDamageResolver resolver = RunDash(self, movement, query);

            Assert.AreEqual(1, CountHits(resolver.Requests, side.CombatId));
            Assert.AreEqual(0, CountHits(resolver.Requests, far.CombatId));
            Assert.AreEqual(1, movement.PushedIds.Count);
            Assert.AreEqual(side.CombatId, movement.PushedIds[0]);
            Assert.AreEqual(1f, movement.PushedDirections[0].x, TOLERANCE);
            Assert.AreEqual(0f, movement.PushedDirections[0].z, TOLERANCE);
            Assert.AreEqual(2f, movement.PushedDistances[0], TOLERANCE);
        }

        // 경로 왼쪽 직사각형 안 대상은 왼쪽(-x)으로 밀린다
        [Test]
        public void Dash_TargetOnLeft_PushesLeft()
        {
            var query    = new ListQuery();
            var self     = new BlockingSelf();
            var movement = new PushingMovement { MoveSelf = p => self.Position = p };
            FakeCombatTarget left = AddPlayer(query, 10, new Vector3(-0.5f, 0f, 3f));

            FakeDamageResolver resolver = RunDash(self, movement, query);

            Assert.AreEqual(1, CountHits(resolver.Requests, left.CombatId));
            Assert.AreEqual(1, movement.PushedIds.Count);
            Assert.AreEqual(-1f, movement.PushedDirections[0].x, TOLERANCE);
        }

        // 멈춘 자리 바로 앞(몸통 원 안)은 맞고, 앞쪽 모서리처럼 원 밖은 맞지 않는다
        [Test]
        public void Dash_FrontCap_UsesBodyCircle()
        {
            var query    = new ListQuery();
            var self     = new BlockingSelf();
            var movement = new PushingMovement { MoveSelf = p => self.Position = p };
            FakeCombatTarget ahead  = AddPlayer(query, 10, new Vector3(0f, 0f, RANGE + 1.4f));
            FakeCombatTarget corner = AddPlayer(query, 11, new Vector3(1.4f, 0f, RANGE + 1.4f));

            FakeDamageResolver resolver = RunDash(self, movement, query);

            Assert.AreEqual(1, CountHits(resolver.Requests, ahead.CombatId));
            Assert.AreEqual(0, CountHits(resolver.Requests, corner.CombatId));
        }

        // 무철 연속 돌진도 같은 규칙이다. 돌진 1회, 실행 0.5초로 몸통 띠의 대상을 한 번 맞히고 옆으로 민다
        [Test]
        public void MultiDash_BodyOverlapOutsideRectangle_HitsOnceAndPushesAside()
        {
            var query    = new ListQuery();
            var self     = new BlockingSelf();
            var movement = new PushingMovement { MoveSelf = p => self.Position = p };
            FakeCombatTarget side    = AddPlayer(query, 10, new Vector3(1.3f, 0f, 4f));
            FakeCombatTarget tracked = AddPlayer(query, 99, new Vector3(0f, 0f, 30f));

            var resolver = new FakeDamageResolver();
            var gate     = new BossDamageGate(resolver);
            gate.Open();

            var definition = new PatternDefinition("mucheol_ph2_p03", BossTestFixtures.BOSS_ID, 2, 20f,
                new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                1.6f, 0.4f, 180f, 0.5f, 1.6f, EnemyCatalogSO.AttackShapeSpec.Dash(RANGE, WIDTH), 0.18f, true);
            var pattern = new MucheolMultiDashPattern(TEST_HREF, movement, new MucheolMultiDashSpec(1, 1, 0.5f, 0.4f), 1);
            var context = new BossPatternContext(definition, gate.IssueInstanceId(), tracked.CombatId, self, query, gate);
            var fsm     = new BossAttackFsm();
            Assert.IsTrue(fsm.Start(definition, pattern, in context, 0.0));

            int count = (int)System.Math.Round(END_TIME / STEP);
            for (int i = 1; i <= count; ++i)
            {
                fsm.Tick(i * (double)STEP);
            }

            Assert.AreEqual(1, CountHits(resolver.Requests, side.CombatId));
            Assert.AreEqual(1, movement.PushedIds.Count);
            Assert.AreEqual(side.CombatId, movement.PushedIds[0]);
            Assert.AreEqual(1f, movement.PushedDirections[0].x, TOLERANCE);
        }

        // 몸통 계약이 없는 보스는 예전처럼 직사각형만 맞힌다
        [Test]
        public void Dash_WithoutBodyBlocker_KeepsRectangleOnly()
        {
            var query    = new ListQuery();
            var self     = new PlainSelf();
            var movement = new PushingMovement { MoveSelf = p => self.Position = p };
            FakeCombatTarget side = AddPlayer(query, 10, new Vector3(1.3f, 0f, 4f));

            FakeDamageResolver resolver = RunDash(self, movement, query);

            Assert.AreEqual(0, CountHits(resolver.Requests, side.CombatId));
            Assert.AreEqual(0, movement.PushedIds.Count);
        }
    }
}
