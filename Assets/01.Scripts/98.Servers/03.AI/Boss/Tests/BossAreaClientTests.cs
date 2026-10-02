// System
using System;
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
    // 장판 클라이언트 모델(BossAreaTrack)의 전이 규칙과 시간 계산을 확인하고, 실제 서버 패턴과 장판 관리자가 내는 알림을
    // 그대로 모델에 넘겨 표시한 원과 서버 타격 범위가 같은지 비교한다. 이 테스트는 Mirror와 실제 서버, 화면 재생을 거치지 않는다
    public class BossAreaClientTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int    BOSS_ID   = 1;
        private const float  TEST_HREF = 3541f;
        private const float  STEP      = 0.01f;
        private const float  TOLERANCE = 0.0001f;
        private const string P05_ID    = "wolbaek_p05";

        private static readonly Vector3 Center = new Vector3(4f, 0f, 5f);

        /****************************************
        *               Helpers
        ****************************************/

        private static BossAreaStart Area(int id = 7, double activate = 10.0)
        {
            return new BossAreaStart(BOSS_ID, id, Center, 3f, 6f, 1f, activate);
        }

        /****************************************
        *            Area Track Model
        ****************************************/

        [Test]
        public void Age_FromServerActivationTime_AndLifeRatio()
        {
            BossAreaTrack track = new BossAreaTrack(Area());

            Assert.AreEqual(0f, track.GetAge(9.0), TOLERANCE);
            Assert.AreEqual(0f, track.GetAge(10.0), TOLERANCE);
            Assert.AreEqual(2.5f, track.GetAge(12.5), TOLERANCE);
            Assert.AreEqual(1f, track.GetLifeRatio(10.0), TOLERANCE);
            Assert.AreEqual(0.5f, track.GetLifeRatio(13.0), TOLERANCE);
            Assert.AreEqual(0f, track.GetLifeRatio(99.0), TOLERANCE);
            Assert.AreEqual(16.0, track.EndServerTime, 0.0001);
        }

        [Test]
        public void ExpiredByTime_AtDurationEnd_WithoutServerMessage()
        {
            BossAreaTrack track = new BossAreaTrack(Area());

            Assert.IsFalse(track.HasExpiredByTime(15.999));
            Assert.IsTrue(track.HasExpiredByTime(16.0));
        }

        [Test]
        public void Tick_AcceptedInOrderOnly_DuplicateAndOlderIndexRejected_PulseStartsAtActivation()
        {
            BossAreaTrack track = new BossAreaTrack(Area());
            Assert.AreEqual(10.0, track.LastPulseServerTime, 0.0001, "활성 순간이 첫 타격이라 펄스가 활성 시각에서 시작한다");
            Assert.AreEqual(-1, track.LastTickIndex);

            Assert.IsTrue(track.TryTick(0, 10.0));
            Assert.IsTrue(track.TryTick(1, 11.0));
            Assert.IsFalse(track.TryTick(1, 11.0));
            Assert.IsFalse(track.TryTick(0, 10.0));
            Assert.IsTrue(track.TryTick(3, 13.0), "유실된 회차가 있어도 더 큰 회차는 받는다");

            Assert.AreEqual(3, track.LastTickIndex);
            Assert.AreEqual(0.5f, track.GetPulseAge(13.5), TOLERANCE);
        }

        [Test]
        public void End_ExpiredOnce_ThenNothingReopensIt()
        {
            BossAreaTrack track = new BossAreaTrack(Area());

            Assert.IsTrue(track.TryEnd(EBossAreaFate.Expired));

            Assert.IsFalse(track.TryEnd(EBossAreaFate.Cancelled));
            Assert.IsFalse(track.TryCancel());
            Assert.IsFalse(track.TryTick(4, 14.0));
            Assert.IsFalse(track.TryEnd(EBossAreaFate.Active));
            Assert.AreEqual(EBossAreaFate.Expired, track.Fate);
            Assert.IsTrue(track.IsEnded);
        }

        [Test]
        public void Snapshot_RoundTrip_KeepsTickIndexSoRestoredAreaDoesNotReplayTicks()
        {
            BossAreaTrack original = new BossAreaTrack(Area());
            original.TryTick(0, 10.0);
            original.TryTick(2, 12.0);

            BossAreaTrack restored = BossAreaTrack.FromSnapshot(original.ToSnapshot());

            Assert.AreEqual(2, restored.LastTickIndex);
            Assert.AreEqual(12.0, restored.LastPulseServerTime, 0.0001);
            Assert.IsFalse(restored.TryTick(2, 12.0));
            Assert.IsTrue(restored.TryTick(3, 13.0));
            Assert.AreEqual(original.GetLifeRatio(12.4), restored.GetLifeRatio(12.4), TOLERANCE);
        }

        /****************************************
        *      Server Pattern To Client Model
        ****************************************/

        // 네트워크 수신부가 할 변환의 예시다. 서버 알림에 서버 시각을 붙여 모델에 넘긴다
        private sealed class ClientBridge : IBossAreaTelegraphListener, IBossAreaListener
        {
            public double Now;

            public BossTelegraphCast Cast;
            public readonly Dictionary<int, BossAreaTrack> Areas = new Dictionary<int, BossAreaTrack>();
            public List<int> TickLog = new List<int>();

            public void OnTelegraphStarted(int castInstanceId, int areaId, Vector3 center, float radius, float telegraphSeconds)
            {
                Cast = new BossTelegraphCast(BossTelegraphStart.Circle(BOSS_ID, castInstanceId, P05_ID, center, radius, Now, telegraphSeconds));
            }

            public void OnCancelled(int castInstanceId, int areaId, EBossInterruptReason reason)
            {
                Cast.TryCancel();
            }

            public void OnAreaActivated(int castInstanceId, int areaId, Vector3 center, float radius, float durationSeconds, float tickInterval,
                double activateServerTime)
            {
                Areas[castInstanceId] = new BossAreaTrack(new BossAreaStart(BOSS_ID, castInstanceId, center, radius, durationSeconds,
                    tickInterval, activateServerTime));
                Cast.TryStrike(activateServerTime);
            }

            public void OnAreaTick(int castInstanceId, int areaId, int hitIndex, double roundServerTime, int hitCount)
            {
                Areas[castInstanceId].TryTick(hitIndex, roundServerTime);
                TickLog.Add(hitIndex);
            }

            public void OnAreaEnded(int castInstanceId, int areaId, EBossAreaEnd end, double endServerTime)
            {
                if (end == EBossAreaEnd.Expired)
                {
                    Areas[castInstanceId].TryEnd(EBossAreaFate.Expired);
                }
                else
                {
                    Areas[castInstanceId].TryCancel();
                }
            }
        }

        private sealed class GridQuery : ICombatTargetQuery
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
                    if ((Targets[i].LifeState == EParticipantLifeState.Alive) && (Targets[i].Faction == faction)
                        && (includeSummons || !Targets[i].IsSummon) && (offset.sqrMagnitude <= (radius * radius)))
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

        // 서버 패턴 하나를 공격 FSM에 올리고 알림을 모델로 흘린다. 타깃은 예고 시작 때 중심에 서 있다
        private sealed class ServerRun
        {
            public FakeCombatTarget   Self     { get; } = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy };
            public FakeCombatTarget   Tracked  { get; } = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = Center };
            public GridQuery          Query    { get; } = new GridQuery();
            public FakeDamageResolver Resolver { get; } = new FakeDamageResolver();
            public ClientBridge       Bridge   { get; } = new ClientBridge();
            public BossAttackFsm      Fsm      { get; } = new BossAttackFsm();
            public BossDamageGate     Gate     { get; }
            public BossAreaManager    Manager  { get; }
            public BossAreaPattern    Pattern  { get; }
            public PatternDefinition  Definition { get; }

            public ServerRun()
            {
                Query.Targets.Add(Tracked);
                Gate = new BossDamageGate(Resolver);
                Gate.Open();
                Definition = new PatternDefinition(P05_ID, BossTestFixtures.BOSS_ID, 1, 15f,
                    new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                    1f, 0f, 0f, 0f, 0.5f, EnemyCatalogSO.AttackShapeSpec.TargetArea(3f), 0.08f, false,
                    EBossActionState.SelectTarget, 0f, false, 6f, 1f, 3);
                Manager = new BossAreaManager(Query, Bridge);
                Pattern = new BossAreaPattern(TEST_HREF, Manager, Bridge);
            }

            public void FillGrid()
            {
                int id = 100;
                for (int i = 0; i < 30; ++i)
                {
                    for (int j = 0; j < 30; ++j)
                    {
                        Query.Targets.Add(new FakeCombatTarget
                        {
                            CombatId = id, Faction = ECombatFaction.Player,
                            Position = Center + new Vector3(-5.03f + (0.347f * i), 0f, -4.97f + (0.331f * j))
                        });
                        ++id;
                    }
                }
            }

            public void Start(double serverTime)
            {
                Bridge.Now = serverTime;
                BossPatternContext context = new BossPatternContext(Definition, Gate.IssueInstanceId(), Tracked.CombatId, Self, Query, Gate);
                Assert.IsTrue(Fsm.Start(Definition, Pattern, in context, serverTime));
            }

            public void TickTo(double from, double to)
            {
                int count = (int)Math.Round((to - from) / STEP);
                for (int i = 1; i <= count; ++i)
                {
                    double time = from + (i * (double)STEP);
                    Bridge.Now = time;
                    Fsm.Tick(time);
                    Manager.Tick(time);
                }
            }
        }

        // 서버가 맞힌 대상 집합과 모델이 가진 값으로 그린 원 안의 집합이 같다(첫 회차)
        [Test]
        public void ServerRound_DisplayedCircleEqualsServerHitSet_AndModelValuesMatchServer()
        {
            ServerRun run = new ServerRun();
            run.FillGrid();
            run.Start(10.0);
            run.TickTo(10.0, 11.05);

            BossTelegraphCast telegraph = run.Bridge.Cast;
            Assert.AreEqual(EBossTelegraphShape.Circle, telegraph.Shape);
            Assert.AreEqual(Center, telegraph.Origin);
            Assert.AreEqual(3f, telegraph.Length);
            Assert.AreEqual(EBossTelegraphPhase.Execute, telegraph.Phase, "활성과 함께 예고는 마무리된다");

            BossAreaTrack area = run.Bridge.Areas[run.Bridge.Cast.CastId];
            Assert.AreEqual(Center, area.Center);
            Assert.AreEqual(3f, area.Radius);
            Assert.AreEqual(6f, area.DurationSeconds);
            Assert.AreEqual(1f, area.TickInterval);
            Assert.AreEqual(11.0, area.ActivateServerTime, 0.0001);

            for (int i = 0; i < run.Query.Targets.Count; ++i)
            {
                FakeCombatTarget target = run.Query.Targets[i];
                bool shown = BossTelegraphRules.IsInside(EBossTelegraphShape.Circle, area.Center, Vector3.forward, area.Radius, 0f, 0f, target.Position);
                int hits = 0;
                for (int r = 0; r < run.Resolver.Requests.Count; ++r)
                {
                    if (run.Resolver.Requests[r].HitTargetId == target.CombatId)
                    {
                        ++hits;
                    }
                }

                Assert.AreEqual(shown ? 1 : 0, hits, $"target {target.CombatId} at {target.Position}");
            }
        }

        [Test]
        public void ServerFullRun_ClientTicksMatchSixServerRounds_ThenExpiresWithNoSeventh()
        {
            ServerRun run = new ServerRun();
            run.Start(10.0);
            run.TickTo(10.0, 18.5);

            BossAreaTrack area = run.Bridge.Areas[run.Bridge.Cast.CastId];
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5 }, run.Bridge.TickLog);
            Assert.AreEqual(5, area.LastTickIndex);
            Assert.AreEqual(EBossAreaFate.Expired, area.Fate);
            Assert.AreEqual(6, run.Resolver.Requests.Count);
        }

        [Test]
        public void ServerCancelBeforeActivation_ClientTelegraphCancelledAndNoAreaModel()
        {
            ServerRun run = new ServerRun();
            run.Start(10.0);
            run.TickTo(10.0, 10.4);

            run.Fsm.Cancel(EBossInterruptReason.PhaseTransition);

            Assert.AreEqual(EBossTelegraphPhase.Cancelled, run.Bridge.Cast.Phase);
            Assert.AreEqual(0, run.Bridge.Areas.Count);
        }

        [Test]
        public void ServerManagerCancel_WhileActive_ClientAreaCancelled_PatternFinishIndependent()
        {
            ServerRun run = new ServerRun();
            run.Start(10.0);
            run.TickTo(10.0, 13.0);
            BossAreaTrack area = run.Bridge.Areas[run.Bridge.Cast.CastId];
            Assert.IsFalse(area.IsEnded);
            Assert.IsTrue(run.Bridge.Cast.TryFinish(), "패턴 정상 종료는 장판과 별개다");
            Assert.IsFalse(area.IsEnded);

            run.Manager.Cancel(EBossInterruptReason.Death, CombatIds.NONE);

            Assert.AreEqual(EBossAreaFate.Cancelled, area.Fate);
        }
    }
}
