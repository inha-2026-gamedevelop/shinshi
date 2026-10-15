// System
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Network;
using Shinshi.Systems;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public class BossGimmickSessionTests
    {
        // 보스 기믹이 세션에 알리는 정지, 실패, 최종 성공의 종료 사유와 같은 틱 우선순위를 웨이브 없는 보스 세션으로 본다.
        // GameDB 없이 세션과 호스트를 직접 만든다

        /****************************************
        *                Fields
        ****************************************/

        private const string SESSION_ID     = "boss-gimmick-session";
        private const int    QUEUE_CAPACITY = 64;
        private const int    PLAYER_ONE     = 1;
        private const int    PLAYER_TWO     = 2;
        private const int    BOSS_ESSENCE   = 1000;
        private const int    BOSS_CURRENCY  = 200;

        private const int    REGISTRY_CAPACITY = 16;
        private const string SORTIE_ID         = "6f1c2d3e-4a5b-4c6d-8e7f-001122334466";
        private const string API_ONE           = "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc11";
        private const string BOSS_WOLBAEK      = "boss_wolbaek";
        private const string MAP_ID            = "map_forest";
        private const string BATTLE_ID         = "battle_forest";

        private SessionConfigSO   _config;
        private ParticipantRoster _roster;
        private RecordingSink     _sink;
        private DungeonSession    _session;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<SessionConfigSO>();
            _roster = new ParticipantRoster(_config);
            _roster.TryAdd(PLAYER_ONE, "player_one");
            _roster.TryAdd(PLAYER_TWO, "player_two");
            _sink    = new RecordingSink();
            _session = new DungeonSession(SESSION_ID, _roster, new SessionEventQueue(SESSION_ID, _sink, QUEUE_CAPACITY,
                QUEUE_CAPACITY), _config, null);
            Assert.IsTrue(_session.RegisterBossReward(new RewardCatalogSO.BossRewardSpec(BOSS_WOLBAEK, EBossDifficulty.Hard,
                BOSS_ESSENCE, BOSS_CURRENCY, "item_boss_material_epic", 1, 1f, 1, null, 0f, 0)));
            Assert.IsTrue(_session.Begin(0.0));
        }

        [TearDown]
        public void TearDown()
        {
            _session.Dispose();
            Object.DestroyImmediate(_config);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void CancelService_GimmickHaltedInTick_EndsServiceCancelledWithoutReward()
        {
            Assert.IsTrue(_session.BeginTick(1.0));
            Assert.IsTrue(_session.CancelService(1.0), "기믹 정지는 그 틱 시각의 서비스 취소로 닫는다");
            _session.CompleteTick();

            Assert.AreEqual(ESessionEndReason.ServiceCancelled, _session.EndReason);
            Assert.AreEqual(0L, _session.Ledger.GetBossEssence(0));
            Assert.AreEqual(1, _sink.Count(ESessionEventType.SessionEnded));
        }

        [TestCase(ESessionEndReason.Timeout)]
        [TestCase(ESessionEndReason.AllDead)]
        public void ReportBossFightFailed_InTick_EndsWithThatReasonAtCompleteTick(ESessionEndReason reason)
        {
            Assert.IsTrue(_session.BeginTick(1.0));
            Assert.IsTrue(_session.ReportBossFightFailed(reason));
            Assert.IsTrue(_session.IsRunning, "종료는 CompleteTick이 판정한다");

            _session.CompleteTick();

            Assert.IsTrue(_session.IsEnded);
            Assert.AreEqual(reason, _session.EndReason);
            Assert.AreEqual(0L, _session.Ledger.GetBossEssence(0), "실패는 처치 보상을 주지 않는다");
        }

        [TestCase(ESessionEndReason.Success)]
        [TestCase(ESessionEndReason.ServiceCancelled)]
        [TestCase(ESessionEndReason.Abandon)]
        [TestCase(ESessionEndReason.DisconnectExpired)]
        public void ReportBossFightFailed_OtherReason_Rejected(ESessionEndReason reason)
        {
            Assert.IsTrue(_session.BeginTick(1.0));
            Assert.IsFalse(_session.ReportBossFightFailed(reason));
            _session.CompleteTick();

            Assert.IsTrue(_session.IsRunning);
        }

        [Test]
        public void ReportBossFightFailed_TwiceSameTick_FirstReasonWins()
        {
            Assert.IsTrue(_session.BeginTick(1.0));
            Assert.IsTrue(_session.ReportBossFightFailed(ESessionEndReason.Timeout));
            Assert.IsTrue(_session.ReportBossFightFailed(ESessionEndReason.AllDead));
            _session.CompleteTick();

            Assert.AreEqual(ESessionEndReason.Timeout, _session.EndReason);
        }

        [Test]
        public void ReportBossFightFailed_SameTickWipe_FailureReasonWins()
        {
            Assert.IsTrue(_session.BeginTick(1.0));
            Assert.IsTrue(_session.ReportBossFightFailed(ESessionEndReason.Timeout));
            KillEveryone();
            _session.CompleteTick();

            Assert.AreEqual(ESessionEndReason.Timeout, _session.EndReason, "기믹 실패가 전멸보다 먼저다");
        }

        [Test]
        public void ReportBossFightFailed_SameTickNormalDefeat_FailureWins()
        {
            Assert.IsTrue(_session.BeginTick(1.0));
            Assert.IsTrue(_session.ReportBossDefeated());
            Assert.IsTrue(_session.ReportBossFightFailed(ESessionEndReason.Timeout));
            _session.CompleteTick();

            Assert.AreEqual(ESessionEndReason.Timeout, _session.EndReason, "기믹 실패가 일반 처치 성공보다 먼저다");
        }

        [Test]
        public void ReportBossFinalSuccess_SameTickWipe_SuccessWins()
        {
            Assert.IsTrue(_session.BeginTick(1.0));
            KillEveryone();
            Assert.IsTrue(_session.ReportBossFinalSuccess());
            _session.CompleteTick();

            Assert.AreEqual(ESessionEndReason.Success, _session.EndReason, "기믹 최종 성공은 같은 틱 전멸보다 먼저다");
            Assert.AreEqual((long)BOSS_ESSENCE, _session.Ledger.GetBossEssence(0));
        }

        [Test]
        public void ReportBossDefeated_SameTickWipe_StillAllDead()
        {
            Assert.IsTrue(_session.BeginTick(1.0));
            Assert.IsTrue(_session.ReportBossDefeated());
            KillEveryone();
            _session.CompleteTick();

            Assert.AreEqual(ESessionEndReason.AllDead, _session.EndReason, "일반 처치는 지금처럼 전멸 실패에 진다");
            Assert.AreEqual(0L, _session.Ledger.GetBossEssence(0));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ReportBossFinalSuccess_SameTickGimmickFailure_SuccessWins(bool failureFirst)
        {
            Assert.IsTrue(_session.BeginTick(1.0));
            if (failureFirst)
            {
                Assert.IsTrue(_session.ReportBossFightFailed(ESessionEndReason.Timeout));
            }

            Assert.IsTrue(_session.ReportBossFinalSuccess());
            if (!failureFirst)
            {
                Assert.IsTrue(_session.ReportBossFightFailed(ESessionEndReason.Timeout));
            }

            _session.CompleteTick();

            Assert.AreEqual(ESessionEndReason.Success, _session.EndReason);
        }

        [Test]
        public void ReportBossFinalSuccess_OutsideTickOrAfterEnd_Rejected()
        {
            Assert.IsFalse(_session.ReportBossFinalSuccess(), "틱 밖에서는 받지 않는다");
            Assert.IsFalse(_session.ReportBossFightFailed(ESessionEndReason.Timeout), "틱 밖에서는 받지 않는다");

            Assert.IsTrue(_session.BeginTick(1.0));
            Assert.IsTrue(_session.ReportBossFightFailed(ESessionEndReason.AllDead));
            _session.CompleteTick();
            Assert.AreEqual(ESessionEndReason.AllDead, _session.EndReason);

            Assert.IsTrue(_session.BeginTick(2.0));
            Assert.IsFalse(_session.ReportBossFinalSuccess(), "끝난 출정은 받지 않는다");
            Assert.IsFalse(_session.ReportBossFightFailed(ESessionEndReason.Timeout), "끝난 출정은 받지 않는다");
            _session.CompleteTick();

            Assert.AreEqual(ESessionEndReason.AllDead, _session.EndReason, "첫 종료 사유가 바뀌지 않는다");
        }

        [Test]
        public void ReportBossFinalSuccess_NormalSession_Rejected()
        {
            _session.Dispose();
            var waves = ScriptableObject.CreateInstance<WaveConfigSO>();
            var rewards = ScriptableObject.CreateInstance<RewardCatalogSO>();
            try
            {
                typeof(WaveConfigSO).GetField("_waves", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(waves,
                    new[]
                    {
                        new WaveConfigSO.WaveDefinition("one", new[] { new WaveConfigSO.WaveEnemyEntry("enemy_melee", 1) },
                            new[] { new WaveConfigSO.SpawnGroup(0f, 1) })
                    });
                var roster = new ParticipantRoster(_config);
                roster.TryAdd(PLAYER_ONE, "player_one");
                _session = new DungeonSession(SESSION_ID, roster, new SessionEventQueue(SESSION_ID, _sink, QUEUE_CAPACITY,
                    QUEUE_CAPACITY), _config, waves, new IdleWave());
                Assert.IsTrue(_session.RegisterRewards(rewards));
                Assert.IsTrue(_session.RegisterEnemySource(0, "source_one",
                    new RewardCatalogSO.EnemyDropSpec("enemy_melee", 5, 1f, 1, 1f, 1)));
                Assert.IsTrue(_session.RegisterParticipantLoot(PLAYER_ONE, new[] { "T01-1A" }));
                Assert.IsTrue(_session.Begin(0.0));

                Assert.IsTrue(_session.BeginTick(1.0));
                Assert.IsFalse(_session.ReportBossFinalSuccess(), "보스전이 아니면 받지 않는다");
                Assert.IsFalse(_session.ReportBossFightFailed(ESessionEndReason.Timeout), "보스전이 아니면 받지 않는다");
                _session.CompleteTick();
            }
            finally
            {
                Object.DestroyImmediate(waves);
                Object.DestroyImmediate(rewards);
            }
        }

        [Test]
        public void ReportBossFinalSuccess_ThenDefeatNextTick_RewardsAndEndsOnce()
        {
            Assert.IsTrue(_session.BeginTick(1.0));
            Assert.IsTrue(_session.ReportBossFinalSuccess());
            Assert.IsTrue(_session.ReportBossDefeated(), "같은 틱 처치 보고가 겹쳐도 종료는 한 번이다");
            _session.CompleteTick();
            long essence  = _session.Ledger.GetBossEssence(0);
            long currency = _session.Ledger.GetBossCurrency(0);

            // 다음 틱에 두뇌가 사망을 확정해 처치 보고가 다시 와도 끝난 출정은 받지 않는다
            Assert.IsTrue(_session.BeginTick(2.0));
            Assert.IsFalse(_session.ReportBossDefeated());
            Assert.IsFalse(_session.ReportBossFinalSuccess());
            _session.CompleteTick();

            Assert.AreEqual(ESessionEndReason.Success, _session.EndReason);
            Assert.AreEqual((long)BOSS_ESSENCE, essence);
            Assert.AreEqual((long)BOSS_CURRENCY, currency);
            Assert.AreEqual(essence, _session.Ledger.GetBossEssence(0), "보상은 한 번만 적는다");
            Assert.AreEqual(essence, _session.Ledger.GetBossEssence(1), "참가자마다 한 번씩 같은 보상이다");
            Assert.AreEqual(1, _sink.Count(ESessionEventType.SessionEnded), "종료 정산 요청은 하나다");
            Assert.IsTrue(_sink.Last(ESessionEventType.SessionEnded).Data.IsBossSession);
        }

        [Test]
        public void Host_ReportsOutsideTick_RejectedAndInsideTick_EndSessionByPriority()
        {
            var registry = new ServerSessionRegistry(REGISTRY_CAPACITY);
            var warnings = new List<string>();
            using (var host = new DungeonSessionHost(_config, registry, null, true, warnings.Add, new RecordingSink()))
            {
                var sortie = new SortieDescriptor(GameSessionCodes.Mode.BOSS, MAP_ID, BATTLE_ID, BOSS_WOLBAEK,
                    GameSessionCodes.Difficulty.HARD);
                JoinTokenClaims claims = new JoinTokenClaims(Constants.Network.PARTICIPANT_KEY_DUNGEON_PREFIX + API_ONE, null, null,
                    SORTIE_ID, null, EPlayerClass.Warrior).WithSortie(in sortie);
                Assert.AreEqual(ERejectReason.None, registry.TryAdmit(in claims, out SessionBinding binding));
                Assert.AreEqual(ERejectReason.None, host.Admit(in claims, binding));

                double time = _config.ReadyCountdownSeconds;
                Assert.IsFalse(host.BeginTick(0.0));
                Assert.IsTrue(host.BeginTick(time));
                host.CompleteTick();
                Assert.IsTrue(host.IsSessionRunning);

                Assert.IsFalse(host.ReportBossFinalSuccess(), "틱 밖에서는 받지 않는다");
                Assert.IsFalse(host.ReportBossFightFailed(ESessionEndReason.Timeout), "틱 밖에서는 받지 않는다");

                Assert.IsTrue(host.BeginTick(time + 1.0));
                Assert.IsTrue(host.ReportBossFightFailed(ESessionEndReason.Timeout));
                Assert.IsTrue(host.ReportBossFinalSuccess());
                host.CompleteTick();

                Assert.IsFalse(host.IsSessionRunning);
                Assert.AreEqual(ESessionEndReason.Success, host.Session.EndReason);
                Assert.IsFalse(host.ReportBossFinalSuccess(), "끝난 출정은 받지 않는다");
            }
        }

        /****************************************
        *                Helpers
        ****************************************/

        private void KillEveryone()
        {
            _roster.TrySetLifeState(PLAYER_ONE, EParticipantLifeState.Dead);
            _roster.TrySetLifeState(PLAYER_TWO, EParticipantLifeState.Dead);
        }

        private sealed class IdleWave : IWaveSession
        {
            public bool                 IsRunning  { get; private set; }
            public bool                 IsFinished => !IsRunning;
            public WaveProgressSnapshot Progress   => default;
            public void SetListener(IWaveSessionListener listener) { }
            public void Begin(double serverTime) { IsRunning = true; }
            public void Tick(double serverTime) { }
            public void Abort() { IsRunning = false; }
        }

        private sealed class RecordingSink : IBackendEventSink
        {
            private readonly List<SessionEvent> _received = new List<SessionEvent>(QUEUE_CAPACITY);

            public bool TrySend(string sessionId, long attemptId, SessionEvent[] events, int count,
                IBackendEventSinkListener listener)
            {
                for (int i = 0; i < count; ++i)
                {
                    _received.Add(events[i]);
                }

                listener.OnBatchCompleted(attemptId, EEventBatchResult.Committed);
                return true;
            }

            public int Count(ESessionEventType type)
            {
                int count = 0;
                for (int i = 0; i < _received.Count; ++i)
                {
                    if (_received[i].Data.Type == type)
                    {
                        ++count;
                    }
                }

                return count;
            }

            public SessionEvent Last(ESessionEventType type)
            {
                for (int i = _received.Count - 1; i >= 0; --i)
                {
                    if (_received[i].Data.Type == type)
                    {
                        return _received[i];
                    }
                }

                return default;
            }
        }
    }
}
