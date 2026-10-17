// System
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

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
    public class SettlementRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SESSION_ID     = "session";
        private const string BOSS_ID        = "boss_mucheol";
        private const string PLAYER_ONE_KEY = "player_one";
        private const string PLAYER_TWO_KEY = "player_two";
        private const string EFFECT_ONE     = "T01-1A";
        private const string EFFECT_TWO     = "T02-1A";
        private const string ALLOWED_ITEM   = "item_boss_material_common";
        private const string BINDING        = Constants.Session.SETTLEMENT_ITEM_BINDING_KIND;
        private const int    PLAYER_ONE     = 1;
        private const int    PLAYER_TWO     = 2;
        private const int    KILL_ESSENCE   = 8;
        private const int    QUEUE_CAPACITY = 256;
        private const double LIMIT          = 420.0;
        private const double DRAIN_SECONDS  = 10.0;
        private const byte   SALT_SEED      = 7;

        private SessionConfigSO   _config;
        private WaveConfigSO      _waves;
        private RewardCatalogSO   _rewards;
        private ParticipantRoster _roster;
        private RecordingSink     _sink;
        private DungeonSession    _session;
        private double            _time;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _config  = ScriptableObject.CreateInstance<SessionConfigSO>();
            _waves   = ScriptableObject.CreateInstance<WaveConfigSO>();
            _rewards = ScriptableObject.CreateInstance<RewardCatalogSO>();
            typeof(WaveConfigSO).GetField("_waves", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_waves,
                new[]
                {
                    new WaveConfigSO.WaveDefinition("one", new[] { new WaveConfigSO.WaveEnemyEntry("enemy_melee", 1) },
                        new[] { new WaveConfigSO.SpawnGroup(0f, 1) }),
                    new WaveConfigSO.WaveDefinition("two", new[] { new WaveConfigSO.WaveEnemyEntry("enemy_melee", 1) },
                        new[] { new WaveConfigSO.SpawnGroup(0f, 1) })
                });
        }

        [TearDown]
        public void TearDown()
        {
            _session?.Dispose();
            _session = null;
            Object.DestroyImmediate(_config);
            Object.DestroyImmediate(_waves);
            Object.DestroyImmediate(_rewards);
        }

        /****************************************
        *            Pure rule tests
        ****************************************/

        [Test]
        public void CalculateEssence_Success_PaysBaseAndFullExtra()
        {
            const long BASE    = 600;
            const long ACCRUED = 1500;

            SettlementBreakdown result = SettlementRules.CalculateEssence(ESessionEndReason.Success, BASE, ACCRUED, 0);

            Assert.AreEqual(BASE + ACCRUED, result.Essence, "6웨이브 완주 2100");
            Assert.AreEqual(ACCRUED, result.PaidExtraEssence);
            Assert.IsFalse(result.IsEssenceClamped);
        }

        [TestCase(ESessionEndReason.Timeout)]
        [TestCase(ESessionEndReason.AllDead)]
        [TestCase(ESessionEndReason.Abandon)]
        [TestCase(ESessionEndReason.DisconnectExpired)]
        public void CalculateEssence_NonSuccess_KeepsBaseAndHalfExtraRoundedDown(ESessionEndReason reason)
        {
            const long BASE      = 200;
            const long ACCRUED   = 101;
            const long HALF_DOWN = 50;

            SettlementBreakdown result = SettlementRules.CalculateEssence(reason, BASE, ACCRUED, 0);

            Assert.AreEqual(HALF_DOWN, result.PaidExtraEssence);
            Assert.AreEqual(BASE + HALF_DOWN, result.Essence);
            Assert.AreEqual(ACCRUED, result.AccruedExtraEssence);
        }

        [Test]
        public void CalculateEssence_ServiceCancelled_PaysBaseAndFullOddExtraWithoutBoss()
        {
            const long BASE    = 200;
            const long ACCRUED = 101;
            const long BOSS    = 600;

            SettlementBreakdown result = SettlementRules.CalculateEssence(ESessionEndReason.ServiceCancelled, BASE, ACCRUED, BOSS);

            Assert.AreEqual(ACCRUED, result.PaidExtraEssence, "홀수 적립도 전액");
            Assert.AreEqual(BASE + ACCRUED, result.Essence, "보스 정기는 성공만");
            Assert.AreEqual(ACCRUED, result.AccruedExtraEssence);
            Assert.IsFalse(result.IsEssenceClamped);
        }

        [Test]
        public void CalculateEssence_BossEssence_OnlyPaidOnSuccess()
        {
            const long BOSS = 600;

            Assert.AreEqual(BOSS, SettlementRules.CalculateEssence(ESessionEndReason.Success, 0, 0, BOSS).Essence);
            Assert.AreEqual(0L, SettlementRules.CalculateEssence(ESessionEndReason.AllDead, 0, 0, BOSS).Essence);
        }

        [Test]
        public void CalculateEssence_OverSanityLimit_ClampsToLimit()
        {
            long max = Constants.Session.SETTLEMENT_MAX_SESSION_ESSENCE;

            SettlementBreakdown result = SettlementRules.CalculateEssence(ESessionEndReason.Success, 1, max, 0);

            Assert.AreEqual(max, result.Essence);
            Assert.IsTrue(result.IsEssenceClamped);
        }

        [TestCase("item_boss_material_common", true)]
        [TestCase("item_essence", false)]
        [TestCase("item_refine_shard", false)]
        [TestCase("item_boss_ticket", false)]
        [TestCase("item_yeopjeon", false)]
        [TestCase("T01-1A", false)]
        [TestCase("item_", false)]
        [TestCase("item_Upper", false)]
        [TestCase(null, false)]
        public void IsAllowedItem_MixedDefinitions_MatchesApiRules(string definitionId, bool expected)
        {
            Assert.AreEqual(expected, SettlementRules.IsAllowedItem(definitionId));
        }

        [Test]
        public void SanitizeItems_ForbiddenInvalidDuplicateAndOverLimit_FixesToApiRules()
        {
            long maxQuantity = Constants.Session.SETTLEMENT_MAX_ITEM_QUANTITY;
            var  candidates  = new List<SettlementItemGrant>
            {
                new SettlementItemGrant("item_refine_shard", BINDING, 20),
                new SettlementItemGrant(EFFECT_ONE, BINDING, 1),
                new SettlementItemGrant(ALLOWED_ITEM, BINDING, maxQuantity),
                new SettlementItemGrant(ALLOWED_ITEM, BINDING, 1),
                new SettlementItemGrant("item_a", BINDING, 1)
            };
            var  report      = new SettlementSanityReport();

            SettlementItemGrant[] items = SettlementRules.SanitizeItems(candidates, ref report);

            Assert.AreEqual(2, items.Length);
            Assert.AreEqual("item_a", items[0].DefinitionId, "ID 순서로 정렬한다");
            Assert.AreEqual(ALLOWED_ITEM, items[1].DefinitionId);
            Assert.AreEqual(maxQuantity, items[1].Quantity, "합친 수량도 상한으로 자른다");
            Assert.AreEqual(1, report.DroppedForbidden);
            Assert.AreEqual(1, report.DroppedInvalidId);
            Assert.AreEqual(1, report.ClampedQuantity);
            Assert.IsTrue(report.HasCorrections);
        }

        [Test]
        public void SanitizeItems_MoreLinesThanLimit_KeepsFirstLinesInOrder()
        {
            int extra      = 3;
            int total      = Constants.Session.SETTLEMENT_MAX_GRANT_ITEMS + extra;
            var candidates = new List<SettlementItemGrant>(total);
            for (int i = total - 1; i >= 0; --i)
            {
                candidates.Add(new SettlementItemGrant("item_m" + i.ToString("D3"), BINDING, 1));
            }

            var                   report = new SettlementSanityReport();
            SettlementItemGrant[] items  = SettlementRules.SanitizeItems(candidates, ref report);

            Assert.AreEqual(Constants.Session.SETTLEMENT_MAX_GRANT_ITEMS, items.Length);
            Assert.AreEqual("item_m000", items[0].DefinitionId);
            Assert.AreEqual(extra, report.DroppedOverflow);
        }

        [Test]
        public void AccrueExtra_OverSessionLimit_AcceptsOnlyRemainingRoom()
        {
            long       max    = Constants.Session.SETTLEMENT_MAX_SESSION_ESSENCE;
            const long ROOM   = 5;
            var        ledger = new SettlementLedger(1);

            Assert.AreEqual(max - ROOM, ledger.AccrueExtra(0, max - ROOM));
            Assert.AreEqual(ROOM, ledger.AccrueExtra(0, ROOM * 2));
            Assert.AreEqual(0L, ledger.AccrueExtra(0, 1));
            Assert.AreEqual(max, ledger.GetExtraEssence(0));
            Assert.IsTrue(ledger.WasAccrualClamped(0));
        }

        [Test]
        public void AddWaveBase_OverSessionLimit_AcceptsOnlyRemainingRoomAndKeepsShards()
        {
            long       max    = Constants.Session.SETTLEMENT_MAX_SESSION_ESSENCE;
            const long ROOM   = 5;
            const long SHARDS = 20;
            var        ledger = new SettlementLedger(1);

            Assert.AreEqual(max - ROOM, ledger.AddWaveBase(0, max - ROOM, SHARDS));
            Assert.AreEqual(ROOM, ledger.AddWaveBase(0, ROOM * 2, SHARDS));
            Assert.AreEqual(max, ledger.GetBaseEssence(0));
            Assert.AreEqual(2 * SHARDS, ledger.GetRefineShards(0), "정련편은 정기 상한과 무관하게 쌓는다");
            Assert.IsTrue(ledger.WasAccrualClamped(0));
        }

        [TestCase("T01-1A", true)]
        [TestCase("T15-3Z", true)]
        [TestCase("t01-1A", false)]
        [TestCase("T01-1a", false)]
        [TestCase("T1-1A", false)]
        [TestCase("T01_1A", false)]
        [TestCase("T01-1AB", false)]
        [TestCase("item_card_a", false)]
        [TestCase(null, false)]
        public void IsTalismanEffectId_MixedEffectIds_MatchesCatalogPattern(string effectId, bool expected)
        {
            Assert.AreEqual(expected, SettlementRules.IsTalismanEffectId(effectId));
        }

        [Test]
        public void SanitizeTalismans_InvalidDuplicateAndOverLimit_FixesToApiRules()
        {
            long maxQuantity = Constants.Session.SETTLEMENT_MAX_TALISMAN_QUANTITY;
            var  candidates  = new List<SettlementItemGrant>
            {
                new SettlementItemGrant(EFFECT_TWO, BINDING, 1),
                new SettlementItemGrant(EFFECT_ONE, BINDING, maxQuantity),
                new SettlementItemGrant(EFFECT_ONE, BINDING, 1),
                new SettlementItemGrant(ALLOWED_ITEM, BINDING, 1)
            };
            var  report      = new SettlementSanityReport();

            SettlementItemGrant[] talismans = SettlementRules.SanitizeTalismans(candidates, ref report);

            Assert.AreEqual(2, talismans.Length);
            Assert.AreEqual(EFFECT_ONE, talismans[0].DefinitionId, "효과 ID 순서로 정렬한다");
            Assert.AreEqual(maxQuantity, talismans[0].Quantity, "합친 수량도 상한으로 자른다");
            Assert.AreEqual(BINDING, talismans[0].BindingKind);
            Assert.AreEqual(EFFECT_TWO, talismans[1].DefinitionId);
            Assert.AreEqual(1, report.DroppedInvalidTalisman, "아이템 ID는 부적 줄로 보내지 않는다");
            Assert.AreEqual(1, report.ClampedTalisman);
            Assert.AreEqual(0, report.DroppedForbidden);
        }

        [Test]
        public void CreateGrants_WalletOverLimit_ClampsAndCurrencyOnlyOnSuccess()
        {
            long       maxShards  = Constants.Session.SETTLEMENT_MAX_SESSION_SHARDS;
            long       maxTickets = Constants.Session.SETTLEMENT_MAX_SESSION_TICKETS;
            const long CURRENCY   = 120;
            var        ledger     = new SettlementLedger(2);
            for (int i = 0; i < 2; ++i)
            {
                ledger.AddWaveBase(i, 0, maxShards + 1);
                ledger.AddLoot(i, new LootRollResult(0, (int)maxTickets + 1, 0, null));
                ledger.SetBossReward(i, 0, 0, CURRENCY);
            }

            var participants = new[]
            {
                new SettlementParticipant("win", ESessionEndReason.Success),
                new SettlementParticipant("left", ESessionEndReason.Abandon)
            };

            SettlementGrant[] grants = SettlementRules.CreateGrants(participants, participants.Length, ledger, null,
                out SettlementSanityReport report);

            Assert.AreEqual(maxShards, grants[0].RefineShards);
            Assert.AreEqual(maxTickets, grants[0].Tickets);
            Assert.AreEqual(CURRENCY, grants[0].Currency);
            Assert.AreEqual(maxShards, grants[1].RefineShards, "정련편은 실패해도 보존한다");
            Assert.AreEqual(maxTickets, grants[1].Tickets, "티켓은 실패해도 보존한다");
            Assert.AreEqual(0L, grants[1].Currency, "엽전은 처치 성공만 받는다");
            Assert.AreEqual(4, report.ClampedWallet);
        }

        [Test]
        public void CreateGrants_SameLedger_ProducesSameGrants()
        {
            SettlementGrant[] first  = CreatePureGrants();
            SettlementGrant[] second = CreatePureGrants();

            AssertSameGrants(first, second);
        }

        /****************************************
        *          Dungeon session tests
        ****************************************/

        [Test]
        public void Success_AllParticipantsComplete_PaysFullAndAccruedEqualsSettlementExtra()
        {
            StartNormal();
            ClearWave(0, "source_one");
            ClearWave(1, "source_two");
            Drain();

            Assert.AreEqual(ESessionEndReason.Success, _session.EndReason);
            long accrued  = ExpectedAccrued(2);
            long expected = (2 * _rewards.BaseEssencePerWave) + accrued;
            AssertGrant(PLAYER_ONE_KEY, expected);
            AssertGrant(PLAYER_TWO_KEY, expected);
            AssertAccrualInvariant(ESessionEndReason.Success, ESessionEndReason.Success);
        }

        [Test]
        public void Timeout_FailsAfterOneWave_KeepsBaseAndHalfExtra()
        {
            StartNormal();
            ClearWave(0, "source_one");
            _time = LIMIT;
            _session.BeginTick(_time);
            _session.CompleteTick();
            Drain();

            Assert.AreEqual(ESessionEndReason.Timeout, _session.EndReason);
            long accrued  = ExpectedAccrued(1);
            long expected = _rewards.BaseEssencePerWave + (accrued / 2);
            AssertGrant(PLAYER_ONE_KEY, expected);
            AssertGrant(PLAYER_TWO_KEY, expected);
            AssertAccrualInvariant(ESessionEndReason.Timeout, ESessionEndReason.Timeout);
        }

        [Test]
        public void Abandon_OneLeavesAfterFirstWave_LeaverHalfAndStayerFull()
        {
            StartNormal();
            ClearWave(0, "source_one");
            Assert.IsTrue(_session.TryAbandon(PLAYER_ONE, _time));
            ClearWave(1, "source_two");
            Drain();

            Assert.AreEqual(ESessionEndReason.Success, _session.EndReason);
            long firstWave = ExpectedAccrued(1);
            AssertGrant(PLAYER_ONE_KEY, _rewards.BaseEssencePerWave + (firstWave / 2));
            AssertGrant(PLAYER_TWO_KEY, (2 * _rewards.BaseEssencePerWave) + ExpectedAccrued(2));
            AssertAccrualInvariant(ESessionEndReason.Abandon, ESessionEndReason.Success);
        }

        [Test]
        public void DisconnectExpired_GraceRunsOut_SettlesAsPersonalFailure()
        {
            StartNormal();
            ClearWave(0, "source_one");
            Assert.IsTrue(_session.TryDisconnect(PLAYER_TWO, _time));
            _time += _config.ReconnectGraceSeconds;
            ClearWave(1, "source_two");
            Drain();

            Assert.AreEqual(ESessionEndReason.Success, _session.EndReason);
            AssertGrant(PLAYER_ONE_KEY, (2 * _rewards.BaseEssencePerWave) + ExpectedAccrued(2));
            AssertGrant(PLAYER_TWO_KEY, _rewards.BaseEssencePerWave + (ExpectedAccrued(1) / 2));
            AssertAccrualInvariant(ESessionEndReason.Success, ESessionEndReason.DisconnectExpired);
        }

        [Test]
        public void Dead_ParticipantDeadAtSuccess_StillPaidFull()
        {
            StartNormal();
            ClearWave(0, "source_one");
            _roster.TrySetLifeState(PLAYER_TWO, EParticipantLifeState.Dead);
            ClearWave(1, "source_two");
            Drain();

            long expected = (2 * _rewards.BaseEssencePerWave) + ExpectedAccrued(2);
            AssertGrant(PLAYER_TWO_KEY, expected);
            AssertAccrualInvariant(ESessionEndReason.Success, ESessionEndReason.Success);
        }

        [Test]
        public void Success_ShardsTicketsTalismans_GoToWalletAndTalismanFieldsWithoutWarning()
        {
            StartNormal();
            ClearWave(0, "source_one");
            ClearWave(1, "source_two");
            Drain();

            // 정련편과 티켓은 지갑 칸, 부적은 계정 귀속 부적 칸으로 간다. 고칠 것이 없으니 경고도 없다
            LogAssert.NoUnexpectedReceived();
            SettlementGrant one = FindGrant(PLAYER_ONE_KEY);
            SettlementGrant two = FindGrant(PLAYER_TWO_KEY);
            Assert.AreEqual(0, one.Items.Length);
            Assert.AreEqual(2 * _rewards.RefineShardsPerWave, one.RefineShards);
            Assert.AreEqual(_session.Ledger.GetTickets(0), one.Tickets);
            Assert.AreEqual(2L, one.Tickets);
            Assert.AreEqual(0L, one.Currency, "일반전은 엽전을 발행하지 않는다");
            AssertTalisman(one, EFFECT_ONE, 2);
            AssertTalisman(two, EFFECT_TWO, 2);
            AssertWalletInvariant(0, one);
            AssertWalletInvariant(1, two);
        }

        [Test]
        public void Timeout_ShardsTicketsTalismans_PreservedOnFailure()
        {
            StartNormal();
            ClearWave(0, "source_one");
            _time = LIMIT;
            _session.BeginTick(_time);
            _session.CompleteTick();
            Drain();

            Assert.AreEqual(ESessionEndReason.Timeout, _session.EndReason);
            SettlementGrant one = FindGrant(PLAYER_ONE_KEY);
            Assert.AreEqual(_rewards.RefineShardsPerWave, one.RefineShards);
            Assert.AreEqual(1L, one.Tickets);
            AssertTalisman(one, EFFECT_ONE, 1);
            AssertWalletInvariant(0, one);
        }

        [Test]
        public void WaveCleared_BaseAndExtraAccrual_SentAsSeparateEvents()
        {
            StartNormal();
            ClearWave(0, "source_one");
            ClearWave(1, "source_two");
            Drain();

            Assert.AreEqual(2 * _rewards.BaseEssencePerWave, _sink.SumAccrued(ESessionEventType.BaseEssenceAccrued,
                PLAYER_ONE_KEY), "기본 적립 합계 = 완료 웨이브 기본 정기");
            Assert.AreEqual(_session.Ledger.GetBaseEssence(0), _sink.SumAccrued(ESessionEventType.BaseEssenceAccrued,
                PLAYER_ONE_KEY));
            Assert.AreEqual(ExpectedAccrued(2), _sink.SumAccrued(ESessionEventType.EssenceAccrued, PLAYER_ONE_KEY),
                "추가 적립에는 기본 정기가 섞이지 않는다");
            Assert.AreEqual(4, _sink.Count(ESessionEventType.BaseEssenceAccrued), "참가자 2명 x 웨이브 2개");
        }

        [Test]
        public void Success_AccrualOverSessionLimit_ClampsAccrualAndGrantWithoutTerminal()
        {
            const int HUGE_KILL = 60000;
            long      max       = Constants.Session.SETTLEMENT_MAX_SESSION_ESSENCE;
            StartNormal(HUGE_KILL);
            ClearWave(0, "source_one");
            ClearWave(1, "source_two");
            Drain();

            Assert.AreEqual(ESessionEndReason.Success, _session.EndReason);
            Assert.IsFalse(_session.HasTerminalStorage);
            Assert.AreEqual(max, SumAccrued(PLAYER_ONE_KEY));
            Assert.AreEqual(max, _session.Ledger.GetExtraEssence(0));
            AssertGrant(PLAYER_ONE_KEY, max);
        }

        [Test]
        public void Drain_SameInputsTwice_ProducesSameGrantsAndAccruals()
        {
            StartNormal();
            ClearWave(0, "source_one");
            ClearWave(1, "source_two");
            Drain();
            SettlementGrant[] first         = LastGrants();
            List<long>        firstAccruals = _sink.Amounts();
            _session.Dispose();

            StartNormal();
            ClearWave(0, "source_one");
            ClearWave(1, "source_two");
            Drain();

            AssertSameGrants(first, LastGrants());
            CollectionAssert.AreEqual(firstAccruals, _sink.Amounts());
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [Test]
        public void BossSuccess_RegisteredReward_PaysEssenceAndRolledMaterial()
        {
            Assert.IsTrue(_rewards.TryGetBossReward(BOSS_ID, EBossDifficulty.Easy, out RewardCatalogSO.BossRewardSpec spec));
            StartBoss(spec);
            Assert.IsTrue(_session.TryCompleteBossForDevelopment(1.0));
            _time = 1.0;
            Drain();

            var             rule      = new BossRewardRule(spec);
            long            materials = LootRoller.RollBossMaterial(CreateSalt(), SESSION_ID, PLAYER_ONE_KEY, in rule);
            SettlementGrant grant     = FindGrant(PLAYER_ONE_KEY);
            Assert.AreEqual((long)spec.Essence, grant.Essence);
            Assert.AreEqual((long)spec.TradeCurrency, grant.Currency, "보스 성공은 확정 엽전을 지갑 칸으로 받는다");
            Assert.AreEqual(materials > 0 ? 1 : 0, grant.Items.Length);
            if (materials > 0)
            {
                Assert.AreEqual(spec.MaterialItemId, grant.Items[0].DefinitionId);
                Assert.AreEqual(materials, grant.Items[0].Quantity);
            }

            Assert.AreEqual(0, _sink.Count(ESessionEventType.EssenceAccrued), "보스 기본 정기는 적립 대상이 아니다");
            Assert.AreEqual(0, _sink.Count(ESessionEventType.BaseEssenceAccrued));
        }
#endif

        [Test]
        public void BossAllDead_RegisteredReward_PaysNothing()
        {
            Assert.IsTrue(_rewards.TryGetBossReward(BOSS_ID, EBossDifficulty.Easy, out RewardCatalogSO.BossRewardSpec spec));
            StartBoss(spec);
            _roster.TrySetLifeState(PLAYER_ONE, EParticipantLifeState.Dead);
            _roster.TrySetLifeState(PLAYER_TWO, EParticipantLifeState.Dead);
            _time = 1.0;
            _session.BeginTick(_time);
            _session.CompleteTick();
            Drain();

            Assert.AreEqual(ESessionEndReason.AllDead, _session.EndReason);
            Assert.AreEqual(0L, FindGrant(PLAYER_ONE_KEY).Essence);
            Assert.AreEqual(0, FindGrant(PLAYER_ONE_KEY).Items.Length);
            Assert.AreEqual(0L, FindGrant(PLAYER_ONE_KEY).Currency);
        }

        [Test]
        public void Begin_NormalWithoutRewards_Rejects()
        {
            CreateRoster();
            var queue = new SessionEventQueue(SESSION_ID, _sink, QUEUE_CAPACITY, QUEUE_CAPACITY);
            _session = new DungeonSession(SESSION_ID, _roster, queue, _config, _waves, new FakeWave(), CreateSalt());
            RegisterSources(KILL_ESSENCE);

            Assert.IsFalse(_session.Begin(0.0));
        }

        [Test]
        public void HttpSink_EssenceAccrued_MapsToContractKindOnlyForApiParticipant()
        {
            const string API_SESSION = "6f1c2d3e-4a5b-4c6d-8e7f-001122334455";
            const string API_PLAYER  = "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbccdd";
            const long   AMOUNT      = 8;
            var          events      = new[]
            {
                new SessionEvent(1, new SessionEventData(ESessionEventType.EssenceAccrued, 1.0,
                    participantId: Constants.Network.PARTICIPANT_KEY_DUNGEON_PREFIX + API_PLAYER, amount: AMOUNT)),
                new SessionEvent(2, new SessionEventData(ESessionEventType.EssenceAccrued, 1.0,
                    participantId: PLAYER_ONE_KEY, amount: AMOUNT)),
                new SessionEvent(3, new SessionEventData(ESessionEventType.BaseEssenceAccrued, 1.0,
                    participantId: Constants.Network.PARTICIPANT_KEY_DUNGEON_PREFIX + API_PLAYER, amount: AMOUNT))
            };

            BackendRequest request = HttpBackendEventSink.CreateRequest(API_SESSION, events, events.Length);
            var            body    = BackendJson.Deserialize<SessionEventBatchRequest>(request.BodyJson);

            Assert.AreEqual(GameSessionCodes.EventKind.ESSENCE_ACCRUED, body.Events[0].EventKind);
            Assert.AreEqual(API_PLAYER, body.Events[0].ParticipantId);
            Assert.AreEqual(AMOUNT, body.Events[0].Amount);
            Assert.AreEqual(GameSessionCodes.EventKind.LOOT, body.Events[1].EventKind, "GUID가 아닌 개발 키는 진단으로만 남긴다");
            Assert.AreEqual(0L, body.Events[1].Amount);
            Assert.AreEqual(GameSessionCodes.EventKind.BASE_ESSENCE_ACCRUED, body.Events[2].EventKind,
                "기본 정기 적립은 별도 종류로 보낸다");
            Assert.AreEqual(API_PLAYER, body.Events[2].ParticipantId);
            Assert.AreEqual(AMOUNT, body.Events[2].Amount);
        }

        /****************************************
        *               Helpers
        ****************************************/

        private void StartNormal(int killEssence = KILL_ESSENCE)
        {
            CreateRoster();
            var queue = new SessionEventQueue(SESSION_ID, _sink, QUEUE_CAPACITY, QUEUE_CAPACITY);
            _session = new DungeonSession(SESSION_ID, _roster, queue, _config, _waves, new FakeWave(), CreateSalt());
            Assert.IsTrue(_session.RegisterRewards(_rewards));
            RegisterSources(killEssence);
            Assert.IsTrue(_session.RegisterParticipantLoot(PLAYER_ONE, new[] { EFFECT_ONE }));
            Assert.IsTrue(_session.RegisterParticipantLoot(PLAYER_TWO, new[] { EFFECT_TWO }));
            _time = 0.0;
            Assert.IsTrue(_session.Begin(_time));
        }

        private void StartBoss(RewardCatalogSO.BossRewardSpec spec)
        {
            CreateRoster();
            var queue = new SessionEventQueue(SESSION_ID, _sink, QUEUE_CAPACITY, QUEUE_CAPACITY);
            _session = new DungeonSession(SESSION_ID, _roster, queue, _config, null, null, CreateSalt());
            Assert.IsTrue(_session.RegisterBossReward(spec));
            _time = 0.0;
            Assert.IsTrue(_session.Begin(_time));
        }

        private void CreateRoster()
        {
            _roster = new ParticipantRoster(_config);
            _roster.TryAdd(PLAYER_ONE, PLAYER_ONE_KEY);
            _roster.TryAdd(PLAYER_TWO, PLAYER_TWO_KEY);
            _sink = new RecordingSink();
        }

        private void RegisterSources(int killEssence)
        {
            // 티켓과 부적이 항상 나오게 해 확보 목록과 아이템 정리를 함께 본다
            var drop = new RewardCatalogSO.EnemyDropSpec("enemy_melee", killEssence, 1f, 1, 1f, 1);
            Assert.IsTrue(_session.RegisterEnemySource(0, "source_one", drop));
            Assert.IsTrue(_session.RegisterEnemySource(1, "source_two", drop));
        }

        private void ClearWave(int waveIndex, string sourceId)
        {
            ++_time;
            Assert.IsTrue(_session.BeginTick(_time));
            _session.OnEnemyDefeated(waveIndex, sourceId, PLAYER_ONE_KEY);
            _session.OnWaveCleared(waveIndex);
            _session.CompleteTick();
        }

        private void Drain()
        {
            double end = _time + DRAIN_SECONDS;
            while (_time < end)
            {
                ++_time;
                _session.BeginTick(_time);
                _session.CompleteTick();
            }

            Assert.IsTrue(_session.CanReturn, "종료 이벤트까지 저장돼야 한다");
        }

        private long ExpectedAccrued(int waves)
        {
            return waves * (KILL_ESSENCE + _rewards.ExtraEssencePerWave);
        }

        // 적립 이벤트 합계가 원장 기본, 추가 정기와 같고, 정산 정기가 기본 적립 + 추가 적립 x 지급률과 같아야 한다
        private void AssertAccrualInvariant(ESessionEndReason firstReason, ESessionEndReason secondReason)
        {
            AssertAccrualInvariant(0, PLAYER_ONE_KEY, firstReason);
            AssertAccrualInvariant(1, PLAYER_TWO_KEY, secondReason);
        }

        private void AssertAccrualInvariant(int index, string key, ESessionEndReason reason)
        {
            long             accrued     = SumAccrued(key);
            long             baseAccrued = _sink.SumAccrued(ESessionEventType.BaseEssenceAccrued, key);
            SettlementLedger ledger      = _session.Ledger;
            Assert.AreEqual(ledger.GetExtraEssence(index), accrued, key + " 적립 합계 = 원장 추가 정기");
            Assert.AreEqual(ledger.GetBaseEssence(index), baseAccrued, key + " 기본 적립 합계 = 원장 기본 정기");
            long expected = baseAccrued + SettlementRules.CalculatePaidExtra(accrued, reason);
            Assert.AreEqual(expected, FindGrant(key).Essence, key + " 정산 정기 = 기본 적립 + 추가 적립 x 지급률");
        }

        private long SumAccrued(string key)
        {
            return _sink.SumAccrued(ESessionEventType.EssenceAccrued, key);
        }

        // 정산안 지갑, 부적 칸이 원장 확보분과 같다(상한 안의 정상 출정)
        private void AssertWalletInvariant(int index, SettlementGrant grant)
        {
            SettlementLedger ledger = _session.Ledger;
            Assert.AreEqual(ledger.GetRefineShards(index), grant.RefineShards, "정산 정련편 = 원장 정련편");
            Assert.AreEqual(ledger.GetTickets(index), grant.Tickets, "정산 티켓 = 원장 티켓");
            long talismans = 0;
            for (int i = 0; i < grant.Talismans.Length; ++i)
            {
                talismans += grant.Talismans[i].Quantity;
            }

            long ledgerTalismans = 0;
            for (int e = 0; e < ledger.GetEffectCount(index); ++e)
            {
                ledgerTalismans += ledger.GetTalismans(index, e);
            }

            Assert.AreEqual(ledgerTalismans, talismans, "정산 부적 합계 = 원장 부적 합계");
        }

        private static void AssertTalisman(SettlementGrant grant, string effectId, long quantity)
        {
            Assert.AreEqual(1, grant.Talismans.Length);
            Assert.AreEqual(effectId, grant.Talismans[0].DefinitionId);
            Assert.AreEqual(BINDING, grant.Talismans[0].BindingKind, "부적은 계정 귀속이다");
            Assert.AreEqual(quantity, grant.Talismans[0].Quantity);
        }

        private void AssertGrant(string key, long essence)
        {
            Assert.AreEqual(essence, FindGrant(key).Essence, key);
        }

        private SettlementGrant FindGrant(string key)
        {
            SettlementGrant[] grants = LastGrants();
            for (int i = 0; i < grants.Length; ++i)
            {
                if (grants[i].ParticipantId == key)
                {
                    return grants[i];
                }
            }

            Assert.Fail("정산안에 참가자가 없다: " + key);
            return null;
        }

        private SettlementGrant[] LastGrants()
        {
            Assert.AreEqual(1, _sink.Count(ESessionEventType.SessionEnded));
            return _sink.Last(ESessionEventType.SessionEnded).Data.Grants;
        }

        private static SettlementGrant[] CreatePureGrants()
        {
            const long BASE   = 300;
            var        ledger = new SettlementLedger(2);
            ledger.SetEffects(0, new[] { EFFECT_ONE, "T01-1B" });
            ledger.AddWaveBase(0, BASE, 60);
            ledger.AccrueExtra(0, 101);
            ledger.AddLoot(0, new LootRollResult(KILL_ESSENCE, 1, 2, "T01-1B"));
            ledger.SetBossReward(1, 600, 3, 120);
            var participants = new[]
            {
                new SettlementParticipant("a", ESessionEndReason.Abandon),
                new SettlementParticipant("b", ESessionEndReason.Success)
            };

            return SettlementRules.CreateGrants(participants, participants.Length, ledger, ALLOWED_ITEM,
                out SettlementSanityReport _);
        }

        private static void AssertSameGrants(SettlementGrant[] expected, SettlementGrant[] actual)
        {
            Assert.AreEqual(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; ++i)
            {
                Assert.AreEqual(expected[i].ParticipantId, actual[i].ParticipantId);
                Assert.AreEqual(expected[i].Essence, actual[i].Essence);
                Assert.AreEqual(expected[i].RefineShards, actual[i].RefineShards);
                Assert.AreEqual(expected[i].Tickets, actual[i].Tickets);
                Assert.AreEqual(expected[i].Currency, actual[i].Currency);
                Assert.AreEqual(expected[i].Talismans.Length, actual[i].Talismans.Length);
                for (int j = 0; j < expected[i].Talismans.Length; ++j)
                {
                    Assert.AreEqual(expected[i].Talismans[j].DefinitionId, actual[i].Talismans[j].DefinitionId);
                    Assert.AreEqual(expected[i].Talismans[j].Quantity, actual[i].Talismans[j].Quantity);
                }

                Assert.AreEqual(expected[i].Items.Length, actual[i].Items.Length);
                for (int j = 0; j < expected[i].Items.Length; ++j)
                {
                    Assert.AreEqual(expected[i].Items[j].DefinitionId, actual[i].Items[j].DefinitionId);
                    Assert.AreEqual(expected[i].Items[j].BindingKind, actual[i].Items[j].BindingKind);
                    Assert.AreEqual(expected[i].Items[j].Quantity, actual[i].Items[j].Quantity);
                }
            }
        }

        private static byte[] CreateSalt()
        {
            var salt = new byte[Constants.Session.LOOT_SALT_BYTES];
            for (int i = 0; i < salt.Length; ++i)
            {
                salt[i] = (byte)(i + SALT_SEED);
            }

            return salt;
        }

        private sealed class FakeWave : IWaveSession
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

                throw new InvalidOperationException("이벤트가 없다: " + type);
            }

            public long SumAccrued(ESessionEventType type, string key)
            {
                long sum = 0;
                for (int i = 0; i < _received.Count; ++i)
                {
                    SessionEventData data = _received[i].Data;
                    if ((data.Type == type) && (data.ParticipantId == key))
                    {
                        Assert.Greater(data.Amount, 0L, "적립 이벤트는 양수만 보낸다");
                        sum += data.Amount;
                    }
                }

                return sum;
            }

            public List<long> Amounts()
            {
                var amounts = new List<long>();
                for (int i = 0; i < _received.Count; ++i)
                {
                    ESessionEventType type = _received[i].Data.Type;
                    if ((type == ESessionEventType.EssenceAccrued) || (type == ESessionEventType.BaseEssenceAccrued))
                    {
                        amounts.Add(_received[i].Data.Amount);
                    }
                }

                return amounts;
            }
        }
    }
}
