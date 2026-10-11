// System
using System;
using System.Collections.Generic;

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
    public class SortieFlowTests
    {
        // 출정 계약 고정, 보스와 일반전 분기, 포기, 경매 중 재입장을 개발 출정(백엔드 없음) 호스트로 본다

        /****************************************
        *                Fields
        ****************************************/

        private const int    REGISTRY_CAPACITY = 16;
        private const string SORTIE_ID         = "6f1c2d3e-4a5b-4c6d-8e7f-001122334455";
        private const string API_ONE           = "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string API_TWO           = "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc02";
        private const string API_THREE         = "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc03";
        private const string API_FOUR          = "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc04";
        private const string BOSS_CHEOLGAK     = "boss_cheolgak";
        private const string BOSS_WOLBAEK      = "boss_wolbaek";
        private const string UNKNOWN_BOSS      = "boss_unknown";
        private const string MAP_ID            = "region_forest";
        private const string BATTLE_ID         = "battle_forest";
        private const string AUCTION_ITEM      = "item_auction_test";

        private readonly List<Object> _created = new List<Object>();
        private SessionConfigSO         _config;
        private WaveConfigSO            _waves;
        private RewardCatalogSO         _rewards;
        private TalismanCatalogSO       _talismans;
        private ServerSessionRegistry   _registry;
        private RecordingSink           _sink;
        private RecordingSortieAudience _audience;
        private DungeonSessionHost      _host;
        private List<string>            _warnings;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            GameDB.ResetForTests();
            GameDB.Initialize(GameDBTestFixtures.CreateValidRoot(_created));
            _config    = ScriptableObject.CreateInstance<SessionConfigSO>();
            _waves     = ScriptableObject.CreateInstance<WaveConfigSO>();
            _rewards   = ScriptableObject.CreateInstance<RewardCatalogSO>();
            _talismans = ScriptableObject.CreateInstance<TalismanCatalogSO>();
            _registry  = new ServerSessionRegistry(REGISTRY_CAPACITY);
            _sink      = new RecordingSink();
            _audience  = new RecordingSortieAudience();
            _warnings  = new List<string>();
            _host      = new DungeonSessionHost(_config, _registry, null, true, _warnings.Add, _sink);
            _host.SortieAudience = _audience;
        }

        [TearDown]
        public void TearDown()
        {
            _host.Dispose();
            GameDB.ResetForTests();
            foreach (Object value in _created) Object.DestroyImmediate(value);
            _created.Clear();
            Object.DestroyImmediate(_config);
            Object.DestroyImmediate(_waves);
            Object.DestroyImmediate(_rewards);
            Object.DestroyImmediate(_talismans);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Admit_FirstTokenFixesSortie_DifferentContractRejected()
        {
            SortieDescriptor boss = Boss(BOSS_CHEOLGAK, GameSessionCodes.Difficulty.EASY);
            Assert.AreEqual(ERejectReason.None, AdmitReason(API_ONE, boss, out _));

            Assert.AreEqual(ERejectReason.SessionMismatch, AdmitReason(API_TWO, Boss(BOSS_CHEOLGAK, GameSessionCodes.Difficulty.HARD), out _),
                "같은 출정 ID라도 계약이 다르면 받지 않는다");
            Assert.AreEqual(ERejectReason.SessionMismatch, AdmitReason(API_TWO, default, out _), "계약 없는 토큰도 다르다");
            Assert.AreEqual(ERejectReason.None, AdmitReason(API_TWO, boss, out _));
            Assert.IsTrue(_host.Sortie.Matches(in boss));
            Assert.AreEqual(2, _host.Roster.Count);
        }

        [Test]
        public void TryReadSortie_ContractShapes_ValidatesModeBossAndDifficulty()
        {
            Assert.AreEqual(ERejectReason.None, JoinTokenClaimsRules.TryReadSortie(Response(GameSessionCodes.Mode.NORMAL, null,
                GameSessionCodes.Difficulty.NONE), true, out SortieDescriptor normal));
            Assert.IsTrue(normal.IsNormal);
            Assert.AreEqual(MAP_ID, normal.MapId);

            Assert.AreEqual(ERejectReason.None, JoinTokenClaimsRules.TryReadSortie(Response(GameSessionCodes.Mode.BOSS,
                BOSS_WOLBAEK, GameSessionCodes.Difficulty.HARD), true, out SortieDescriptor boss));
            Assert.AreEqual(EBossDifficulty.Hard, boss.BossDifficulty);

            Assert.AreEqual(ERejectReason.SessionMismatch, JoinTokenClaimsRules.TryReadSortie(Response(GameSessionCodes.Mode.NORMAL,
                BOSS_WOLBAEK, GameSessionCodes.Difficulty.NONE), false, out _), "일반전에 보스가 있으면 거절한다");
            Assert.AreEqual(ERejectReason.SessionMismatch, JoinTokenClaimsRules.TryReadSortie(Response(GameSessionCodes.Mode.BOSS,
                BOSS_WOLBAEK, GameSessionCodes.Difficulty.NONE), false, out _), "보스전에 난이도가 없으면 거절한다");
            Assert.AreEqual(ERejectReason.SessionMismatch, JoinTokenClaimsRules.TryReadSortie(Response(JoinTokenExchangeResponse.NO_MODE,
                null, GameSessionCodes.Difficulty.NONE), true, out _), "출시 서버는 계약 없는 응답을 거절한다");
            Assert.AreEqual(ERejectReason.None, JoinTokenClaimsRules.TryReadSortie(Response(JoinTokenExchangeResponse.NO_MODE,
                null, GameSessionCodes.Difficulty.NONE), false, out SortieDescriptor none));
            Assert.IsFalse(none.HasContract);
        }

        [Test]
        public void BossDescriptor_SelectsContractBossDifficultyAndRewardTable()
        {
            _host.ConfigureContent(_waves, _rewards, _talismans, BOSS_CHEOLGAK, IsKnownBoss);
            Admit(API_ONE, Boss(BOSS_WOLBAEK, GameSessionCodes.Difficulty.HARD));

            Assert.AreEqual(BOSS_WOLBAEK, _host.BossId);
            Assert.AreEqual(EBossDifficulty.Hard, _host.BossDifficulty);
            Assert.IsTrue(SortieContentRules.TryFindBossReward(_rewards.BossRewards, BOSS_WOLBAEK, EBossDifficulty.Hard,
                out RewardCatalogSO.BossRewardSpec spec));

            StartSession();
            Assert.IsTrue(_host.Session.IsBossSession);
            Assert.IsTrue(_host.Session.TryCompleteBossForDevelopment(_config.ReadyCountdownSeconds));
            Tick(_config.ReadyCountdownSeconds + 1.0);
            Tick(_config.ReadyCountdownSeconds + 2.0);

            SettlementGrant[] grants = _sink.LastGrants;
            Assert.IsNotNull(grants);
            Assert.AreEqual(1, grants.Length);
            Assert.AreEqual(spec.TradeCurrency, grants[0].Currency, "계약 보스와 난이도의 보상 표로 정산한다");
        }

        [Test]
        public void Admit_NoContract_UsesDefaultBoss_UnknownContractBossRejected()
        {
            _host.ConfigureContent(_waves, _rewards, _talismans, BOSS_CHEOLGAK, IsKnownBoss);
            Assert.AreEqual(ERejectReason.SessionMismatch, AdmitReason(API_ONE, Boss(UNKNOWN_BOSS, GameSessionCodes.Difficulty.EASY),
                out _), "서버 콘텐츠에 없는 보스로는 출정을 열지 않는다");
            Assert.IsNull(_host.SessionId);

            Admit(API_ONE, default);
            Assert.AreEqual(BOSS_CHEOLGAK, _host.BossId);
            Assert.AreEqual(EBossDifficulty.Easy, _host.BossDifficulty);
        }

        [Test]
        public void BeginSession_NormalWithFactory_OpensWaveSessionAndTicksOnlyInWaveStep()
        {
            var factory = new FakeWaveFactory();
            _host.WaveFactory = factory;
            _host.ConfigureContent(_waves, _rewards, _talismans, BOSS_CHEOLGAK, IsKnownBoss);
            Admit(API_ONE, Normal(), EPlayerClass.Warrior);
            Admit(API_TWO, Normal(), EPlayerClass.Summoner);

            StartSession();

            Assert.IsTrue(_host.IsSessionRunning, "배치 계획 전체와 참가자별 직업 드롭이 등록돼야 일반전이 열린다");
            Assert.IsFalse(_host.Session.IsBossSession);
            Assert.AreEqual(1, factory.Calls);
            Assert.AreEqual(2, factory.Last.StartingCount);
            Assert.AreEqual(_config.GetEnemyHealthScale(2), factory.Last.EnemyHealthScale);
            Assert.AreEqual(_config.GetDamageScale(2), factory.Last.DamageScale);
            Assert.AreEqual(MAP_ID, factory.Last.MapId);
            Assert.AreSame(_waves, factory.Last.Waves);
            Assert.IsTrue(factory.Wave.Began);

            double time = _config.ReadyCountdownSeconds + 1.0;
            Assert.IsTrue(_host.BeginTick(time));
            _host.CompleteTick();
            Assert.AreEqual(0, factory.Wave.TickCount, "세션 종료 판정은 웨이브를 진행하지 않는다");

            Assert.IsTrue(_host.BeginTick(time + 1.0));
            _host.TickWave();
            _host.CompleteTick();
            Assert.AreEqual(1, factory.Wave.TickCount);
            _host.TickWave();
            Assert.AreEqual(1, factory.Wave.TickCount, "틱 밖에서는 진행하지 않는다");
        }

        [Test]
        public void RegisterWaveSources_FullPlan_RegistersCountTotalEnemies()
        {
            var roster = new ParticipantRoster(_config);
            Assert.IsTrue(roster.TryAdd(1, Key(API_ONE)));
            var queue   = new SessionEventQueue(SORTIE_ID, _sink);
            var session = DungeonSession.CreateNormal(SORTIE_ID, roster, queue, _config, _waves, _rewards, new FakeWave(), null);

            int registered = SortieContentRules.RegisterWaveSources(session, _waves, _rewards);

            Assert.AreEqual(_waves.CountTotalEnemies(), registered);
            Assert.AreEqual(0, SortieContentRules.RegisterWaveSources(session, _waves, _rewards), "같은 source ID는 두 번 등록하지 않는다");
            session.Dispose();
        }

        [Test]
        public void TryGetEnemyId_SpawnIndex_WalksEntriesInOrder()
        {
            WaveConfigSO.WaveDefinition wave = _waves.Waves[1];
            int first = wave.Enemies[0].Count;

            Assert.IsTrue(SortieContentRules.TryGetEnemyId(wave, 0, out string head));
            Assert.IsTrue(SortieContentRules.TryGetEnemyId(wave, first, out string next));
            Assert.AreEqual(wave.Enemies[0].EnemyId, head);
            Assert.AreEqual(wave.Enemies[1].EnemyId, next);
            Assert.IsFalse(SortieContentRules.TryGetEnemyId(wave, wave.CountEnemies(), out _));
        }

        [Test]
        public void BeginSession_NormalWithoutFactory_WarnsAndOpensDevelopmentSession()
        {
            _host.ConfigureContent(_waves, _rewards, _talismans, BOSS_CHEOLGAK, IsKnownBoss);
            Admit(API_ONE, Normal());

            StartSession();

            Assert.IsTrue(_host.IsSessionRunning);
            Assert.IsTrue(_host.Session.IsBossSession, "웨이브 구현이 없으면 웨이브 없는 개발 세션이다");
            Assert.IsTrue(_warnings.Exists(IsWaveMissingWarning));
        }

        [Test]
        public void Abandon_ClosesRosterOnceAndResendReturnsStoredResult()
        {
            int one = Admit(API_ONE, default);
            int two = Admit(API_TWO, default);
            StartSession();
            double time = _config.ReadyCountdownSeconds;
            Guid   op   = Guid.NewGuid();

            _host.RequestAbandon(one, op);
            Assert.AreEqual(0, _audience.ResultCount(one), "포기는 다음 틱 BeginTick 뒤에 처리한다");
            Assert.IsFalse(_host.Roster.IsClosed(one));

            Tick(time + 1.0);
            Assert.IsTrue(_host.Roster.IsClosed(one));
            Assert.IsTrue(_registry.IsClosed(one), "포기한 참가자는 다시 들어오지 못한다");
            Assert.AreEqual(1, _audience.ResultCount(one));
            Assert.AreEqual(ERejectReason.None, _audience.LastResult(one).Reason);

            _host.RequestAbandon(one, op);
            Tick(time + 1.0 + Constants.Session.EVENT_BATCH_INTERVAL_SECONDS);
            Assert.AreEqual(2, _audience.ResultCount(one), "같은 작업 ID 재전송에는 처음 결과를 다시 보낸다");
            Assert.AreEqual(ERejectReason.None, _audience.LastResult(one).Reason);
            Assert.AreEqual(1, _sink.CountSettled(Key(API_ONE)), "개인 이탈은 한 번만 저장한다");
            Assert.IsTrue(_host.IsSessionRunning, "남은 참가자가 있으면 출정은 계속된다");

            SortieStatusMessage status = _audience.LastStatus(one);
            Assert.IsTrue(status.HasPersonalClose);
            Assert.AreEqual(ESessionEndReason.Abandon, status.PersonalCloseReason);
            Assert.IsTrue(status.CanReturn, "이탈 기록이 저장되면 마을로 갈 수 있다");
            Assert.IsFalse(_audience.LastStatus(two).HasPersonalClose);

            Assert.AreEqual(1, CountDeparted(Key(API_ONE)), "PARTICIPANT_DEPARTED는 참가자당 한 번이다");
        }

        [Test]
        public void Abandon_AllParticipants_FinishesSortieWithAbandon()
        {
            int one = Admit(API_ONE, default);
            int two = Admit(API_TWO, default);
            StartSession();
            double time = _config.ReadyCountdownSeconds;

            _host.RequestAbandon(one, Guid.NewGuid());
            _host.RequestAbandon(two, Guid.NewGuid());
            Tick(time + 1.0);

            Assert.IsTrue(_host.Session.IsEnded);
            Assert.AreEqual(ESessionEndReason.Abandon, _host.Session.EndReason);
            Tick(time + 1.0 + Constants.Session.EVENT_BATCH_INTERVAL_SECONDS);
            Assert.AreEqual(1, CountDeparted(Key(API_ONE)));
            Assert.AreEqual(1, CountDeparted(Key(API_TWO)));
            Assert.AreEqual(2, _sink.LastGrants.Length, "떠난 참가자도 SESSION_ENDED 정산안에 한 번 들어간다");
        }

        [Test]
        public void Abandon_BeforeStart_RepliesWrongPhase()
        {
            int one = Admit(API_ONE, default);
            _host.RequestAbandon(one, Guid.NewGuid());

            Assert.IsFalse(_host.BeginTick(0.0));

            Assert.AreEqual(ERejectReason.WrongPhase, _audience.LastResult(one).Reason);
            Assert.IsFalse(_host.Roster.IsClosed(one));
        }

        [Test]
        public void AuctionReentry_EligibleReturnsSnapshotAndPayout_NewcomerNotEligible_AbandonerRejected()
        {
            var auction = new FakeAuctionAudience();
            _host.AuctionAudience = auction;
            _host.ConfigureBossAuction(AUCTION_ITEM, 1f);
            int one   = Admit(API_ONE, default, out SessionBinding bindingOne);
            int two   = Admit(API_TWO, default, out SessionBinding bindingTwo);
            int three = Admit(API_THREE, default, out SessionBinding bindingThree);
            int four  = Admit(API_FOUR, default, out SessionBinding bindingFour);
            StartSession();
            double time = _config.ReadyCountdownSeconds;

            // 네 번째는 출정 중에 포기한다. 세 번째는 경매 개시 순간 접속하지 않았다
            _host.RequestAbandon(four, Guid.NewGuid());
            Tick(++time);
            _registry.Release(bindingThree);
            auction.Connected.Add(one);
            auction.Connected.Add(two);

            Assert.IsTrue(_host.Session.TryCompleteBossForDevelopment(time));
            Tick(++time);
            Tick(++time);
            Assert.IsNotNull(_host.Auction, "보스 성공 정산 뒤 경매가 열린다");
            Assert.AreEqual(EAuctionStep.Open, _host.Auction.Step);
            Assert.IsTrue(_registry.AllowsAuctionReadmission(one));
            Assert.IsFalse(_registry.AllowsAuctionReadmission(four), "포기자는 경매 대상이 아니다");

            // 개시 때 자격자였던 두 번째가 끊겼다가 돌아온다
            _registry.Release(bindingTwo);
            auction.Connected.Remove(two);
            Assert.AreEqual(ERejectReason.None, AdmitReason(API_TWO, default, out SessionBinding returning));
            Assert.IsTrue(returning.IsAuctionOnly);
            auction.Connected.Add(two);
            int statesBefore = auction.StateCount(two);
            _host.OnAuctionOnlyJoined(two);
            Assert.AreEqual(statesBefore + 1, auction.StateCount(two), "재입장 직후 경매 상태를 한 번 보낸다");
            Assert.IsTrue(auction.LastState(two).IsEligible);

            // 개시 뒤 처음 들어온 세 번째는 그 품목 자격이 없다
            Assert.AreEqual(ERejectReason.None, AdmitReason(API_THREE, default, out SessionBinding newcomer));
            Assert.IsTrue(newcomer.IsAuctionOnly);
            auction.Connected.Add(three);
            _host.HandleAuctionBid(three, _config.AuctionMinBid);
            Assert.AreEqual(ERejectReason.NotEligible, auction.LastBid(three).Reason);

            // 포기자는 다시 들어오지 못한다
            Assert.AreEqual(ERejectReason.AlreadySettled, AdmitReason(API_FOUR, default, out _));

            _host.HandleAuctionBid(one, _config.AuctionMinBid);
            Assert.AreEqual(ERejectReason.None, auction.LastBid(one).Reason);
            Tick(time + _config.AuctionMaxSeconds + 1.0);

            Assert.AreEqual(EAuctionPhase.Sold, _host.Auction.Phase);
            Assert.AreEqual((long)_config.AuctionMinBid, _host.Auction.PayoutEach, "돌아온 자격자는 분배에 들어간다(낙찰자와 신규 입장자 제외)");
            Assert.IsTrue(_host.Auction.IsFinished);
            Assert.IsFalse(_registry.AllowsAuctionReadmission(two), "경매가 끝나면 재입장을 다시 막는다");
            Assert.IsNotNull(bindingOne);
            Assert.IsNotNull(bindingFour);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static bool IsKnownBoss(string bossId)
        {
            return (bossId == BOSS_CHEOLGAK) || (bossId == BOSS_WOLBAEK);
        }

        private static bool IsWaveMissingWarning(string warning)
        {
            return warning.Contains("웨이브 구현이 없어");
        }

        private static string Key(string apiId)
        {
            return Constants.Network.PARTICIPANT_KEY_DUNGEON_PREFIX + apiId;
        }

        private static SortieDescriptor Boss(string bossId, int difficulty)
        {
            return new SortieDescriptor(GameSessionCodes.Mode.BOSS, MAP_ID, BATTLE_ID, bossId, difficulty);
        }

        private static SortieDescriptor Normal()
        {
            return new SortieDescriptor(GameSessionCodes.Mode.NORMAL, MAP_ID, BATTLE_ID, null, GameSessionCodes.Difficulty.NONE);
        }

        private static JoinTokenExchangeResponse Response(int mode, string bossId, int difficulty)
        {
            return new JoinTokenExchangeResponse("char-1", "dungeon-01", ContractCodes.ServerRole.DUNGEON, API_ONE, SORTIE_ID, 1L,
                null, (int)EPlayerClass.Warrior, null, mode, MAP_ID, BATTLE_ID, bossId, difficulty, null);
        }

        private int Admit(string apiId, SortieDescriptor sortie, EPlayerClass playerClass = EPlayerClass.Warrior)
        {
            Assert.AreEqual(ERejectReason.None, AdmitReason(apiId, sortie, out SessionBinding binding, playerClass));
            return binding.ParticipantId;
        }

        private int Admit(string apiId, SortieDescriptor sortie, out SessionBinding binding)
        {
            Assert.AreEqual(ERejectReason.None, AdmitReason(apiId, sortie, out binding));
            return binding.ParticipantId;
        }

        private ERejectReason AdmitReason(string apiId, SortieDescriptor sortie, out SessionBinding binding,
            EPlayerClass playerClass = EPlayerClass.Warrior)
        {
            JoinTokenClaims claims = new JoinTokenClaims(Key(apiId), null, null, SORTIE_ID, null, playerClass).WithSortie(in sortie);
            ERejectReason   reason = _registry.TryAdmit(in claims, out binding);
            if (reason != ERejectReason.None)
            {
                return reason;
            }

            reason = _host.Admit(in claims, binding);
            if (reason != ERejectReason.None)
            {
                _registry.Release(binding);
            }

            return reason;
        }

        private void StartSession()
        {
            Assert.IsFalse(_host.BeginTick(0.0));
            Assert.IsTrue(_host.BeginTick(_config.ReadyCountdownSeconds));
            _host.CompleteTick();
        }

        private void Tick(double serverTime)
        {
            if (_host.BeginTick(serverTime))
            {
                _host.CompleteTick();
            }
        }

        // 받은 이벤트를 백엔드 형식으로 바꿔 PARTICIPANT_DEPARTED 수를 센다
        private int CountDeparted(string participantKey)
        {
            SessionEvent[] events = _sink.Received.ToArray();
            BackendRequest request = HttpBackendEventSink.CreateRequest(SORTIE_ID, events, events.Length);
            var            body    = BackendJson.Deserialize<SessionEventBatchRequest>(request.BodyJson);
            string         apiId   = participantKey.Substring(Constants.Network.PARTICIPANT_KEY_DUNGEON_PREFIX.Length);
            int            count   = 0;
            for (int i = 0; i < body.Events.Length; ++i)
            {
                if ((body.Events[i].EventKind == GameSessionCodes.EventKind.PARTICIPANT_DEPARTED)
                    && (body.Events[i].ParticipantId == apiId))
                {
                    Assert.AreEqual(GameSessionCodes.EndReason.ABANDON, body.Events[i].EndReason);
                    ++count;
                }
            }

            return count;
        }

        private sealed class RecordingSink : IBackendEventSink
        {
            public List<SessionEvent> Received   { get; } = new List<SessionEvent>();
            public SettlementGrant[]  LastGrants { get; private set; }

            public bool TrySend(string sessionId, long attemptId, SessionEvent[] events, int count,
                IBackendEventSinkListener listener)
            {
                for (int i = 0; i < count; ++i)
                {
                    Received.Add(events[i]);
                    if (events[i].Data.Type == ESessionEventType.SessionEnded)
                    {
                        LastGrants = events[i].Data.Grants;
                    }
                }

                listener.OnBatchCompleted(attemptId, EEventBatchResult.Committed);
                return true;
            }

            public int CountSettled(string participantKey)
            {
                int count = 0;
                for (int i = 0; i < Received.Count; ++i)
                {
                    if ((Received[i].Data.Type == ESessionEventType.ParticipantSettled)
                        && (Received[i].Data.ParticipantId == participantKey))
                    {
                        ++count;
                    }
                }

                return count;
            }
        }

        private sealed class RecordingSortieAudience : ISortieAudience
        {
            private readonly List<KeyValuePair<int, SortieStatusMessage>>        _statuses =
                new List<KeyValuePair<int, SortieStatusMessage>>();
            private readonly List<KeyValuePair<int, SortieAbandonResultMessage>> _results  =
                new List<KeyValuePair<int, SortieAbandonResultMessage>>();

            public void SendStatus(int participantId, in SortieStatusMessage message)
            {
                _statuses.Add(new KeyValuePair<int, SortieStatusMessage>(participantId, message));
            }

            public void SendAbandonResult(int participantId, in SortieAbandonResultMessage message)
            {
                _results.Add(new KeyValuePair<int, SortieAbandonResultMessage>(participantId, message));
            }

            public int ResultCount(int participantId)
            {
                int count = 0;
                for (int i = 0; i < _results.Count; ++i)
                {
                    if (_results[i].Key == participantId)
                    {
                        ++count;
                    }
                }

                return count;
            }

            public SortieAbandonResultMessage LastResult(int participantId)
            {
                for (int i = _results.Count - 1; i >= 0; --i)
                {
                    if (_results[i].Key == participantId)
                    {
                        return _results[i].Value;
                    }
                }

                Assert.Fail($"포기 결과가 없다, participantId={participantId}");
                return default;
            }

            public SortieStatusMessage LastStatus(int participantId)
            {
                for (int i = _statuses.Count - 1; i >= 0; --i)
                {
                    if (_statuses[i].Key == participantId)
                    {
                        return _statuses[i].Value;
                    }
                }

                Assert.Fail($"출정 상태가 없다, participantId={participantId}");
                return default;
            }
        }

        private sealed class FakeAuctionAudience : IAuctionAudience
        {
            private readonly List<KeyValuePair<int, AuctionStateMessage>>     _states = new List<KeyValuePair<int, AuctionStateMessage>>();
            private readonly List<KeyValuePair<int, AuctionBidResultMessage>> _bids   = new List<KeyValuePair<int, AuctionBidResultMessage>>();

            public HashSet<int> Connected { get; } = new HashSet<int>();

            public bool IsConnected(int participantId)
            {
                return Connected.Contains(participantId);
            }

            public void SendState(int participantId, in AuctionStateMessage message)
            {
                if (Connected.Contains(participantId))
                {
                    _states.Add(new KeyValuePair<int, AuctionStateMessage>(participantId, message));
                }
            }

            public void SendBidResult(int participantId, in AuctionBidResultMessage message)
            {
                _bids.Add(new KeyValuePair<int, AuctionBidResultMessage>(participantId, message));
            }

            public int StateCount(int participantId)
            {
                int count = 0;
                for (int i = 0; i < _states.Count; ++i)
                {
                    if (_states[i].Key == participantId)
                    {
                        ++count;
                    }
                }

                return count;
            }

            public AuctionStateMessage LastState(int participantId)
            {
                for (int i = _states.Count - 1; i >= 0; --i)
                {
                    if (_states[i].Key == participantId)
                    {
                        return _states[i].Value;
                    }
                }

                Assert.Fail($"경매 상태가 없다, participantId={participantId}");
                return default;
            }

            public AuctionBidResultMessage LastBid(int participantId)
            {
                for (int i = _bids.Count - 1; i >= 0; --i)
                {
                    if (_bids[i].Key == participantId)
                    {
                        return _bids[i].Value;
                    }
                }

                Assert.Fail($"입찰 결과가 없다, participantId={participantId}");
                return default;
            }
        }

        private sealed class FakeWave : IWaveSession
        {
            public bool                 IsRunning  => Began;
            public bool                 IsFinished => false;
            public WaveProgressSnapshot Progress   => default;
            public bool                 Began      { get; private set; }
            public int                  TickCount  { get; private set; }

            public void SetListener(IWaveSessionListener listener) { }
            public void Begin(double serverTime) { Began = true; }
            public void Tick(double serverTime) { ++TickCount; }
            public void Abort() { }
        }

        private sealed class FakeWaveFactory : IWaveSessionFactory
        {
            public FakeWave           Wave  { get; } = new FakeWave();
            public int                Calls { get; private set; }
            public WaveSessionContext Last  { get; private set; }

            public IWaveSession Create(in WaveSessionContext context)
            {
                ++Calls;
                Last = context;
                return Wave;
            }
        }
    }
}
