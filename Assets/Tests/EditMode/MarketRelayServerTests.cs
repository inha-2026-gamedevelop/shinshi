// System
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class MarketRelayServerTests
    {
        // 경매장 중계: 쓰기는 연결당 하나만 진행, 같은 requestId 재전송 무시, 결과 불명은 BackendUnavailable 뒤 같은 requestId 재전송,
        // 신원은 연결에서만, 연결이 끊기면 응답 버림, 형식 검사

        /****************************************
        *                Fields
        ****************************************/

        private const string CHARACTER_ID = "11111111-0000-4000-8000-000000000001";
        private const string LISTING_ID   = "22222222-0000-4000-8000-000000000001";
        private const int    STATUS_OK    = 200;
        private const int    STATUS_DOWN  = 503;

        private double                  _now;
        private volatile bool           _down;
        private ConcurrentQueue<string> _operationIds;
        private FakeBackendTransport    _transport;
        private BackendGateway          _gateway;
        private FakeTownAudience        _audience;
        private MarketRelayServer       _relay;
        private TownMember              _member;
        private int                     _walletChanged;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _now           = 0.0;
            _down          = false;
            _walletChanged = 0;
            _operationIds  = new ConcurrentQueue<string>();
            _transport     = new FakeBackendTransport((request, token) => Task.FromResult(Respond(request)));
            _gateway       = new BackendGateway(_transport, () => 0.0, FakeBackendTransport.ImmediateDelay(new ConcurrentQueue<TimeSpan>()),
                null, null);
            _audience      = new FakeTownAudience();
            _relay         = new MarketRelayServer(_gateway, _audience, () => _now, id => ++_walletChanged);
            _member        = new TownMember(1, "acc-a", CHARACTER_ID, "lease-a", "Alpha", EPlayerClass.Warrior);
        }

        [TearDown]
        public void TearDown()
        {
            _gateway.Dispose();
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Handle_WriteInFlight_SameIdIgnoredOtherIdPendingCommit()
        {
            MarketRequestMessage first = ListHerb();

            _relay.Handle(_member, first);
            _relay.Handle(_member, first);
            _relay.Handle(_member, ListHerb());

            Assert.AreEqual(1, _audience.Count<MarketResultMessage>(1));
            Assert.IsTrue(_audience.TryGetLast(1, out MarketResultMessage busy));
            Assert.AreEqual(ERejectReason.PendingCommit, busy.Reason);
            Pump();
            Assert.IsTrue(_audience.TryGetLast(1, out MarketResultMessage done));
            Assert.AreEqual(ERejectReason.None, done.Reason);
            Assert.AreEqual(first.RequestId, done.RequestId);
            Assert.AreEqual(LISTING_ID, done.ListingId);
            Assert.AreEqual(1, _walletChanged);
            Assert.AreEqual(1, _transport.Calls);
        }

        [Test]
        public void Handle_UnknownResult_ReportsUnavailableAndResendUsesSameOperationId()
        {
            _down = true;
            MarketRequestMessage message = ListHerb();
            _relay.Handle(_member, message);
            Pump();

            Assert.IsTrue(_audience.TryGetLast(1, out MarketResultMessage unknown));
            Assert.AreEqual(ERejectReason.BackendUnavailable, unknown.Reason);
            Assert.AreEqual(0, _walletChanged);

            _down = false;
            _relay.Handle(_member, message);
            Pump();

            Assert.IsTrue(_audience.TryGetLast(1, out MarketResultMessage done));
            Assert.AreEqual(ERejectReason.None, done.Reason);
            string expected = message.RequestId.ToString("D");
            foreach (string id in _operationIds)
            {
                Assert.AreEqual(expected, id);
            }
        }

        [Test]
        public void Handle_ReadSameActionInFlight_RejectsPendingCommit()
        {
            _relay.Handle(_member, Read(EMarketAction.Mine));
            _relay.Handle(_member, Read(EMarketAction.Mine));

            Assert.IsTrue(_audience.TryGetLast(1, out MarketResultMessage busy));
            Assert.AreEqual(ERejectReason.PendingCommit, busy.Reason);
            Pump();
            Assert.IsTrue(_audience.TryGetLast(1, out MarketResultMessage mine));
            Assert.AreEqual(EMarketAction.Mine, mine.Action);
            Assert.AreEqual(ERejectReason.None, mine.Reason);
            Assert.AreEqual(777, mine.AvailableYeopjeon);
        }

        [Test]
        public void Handle_WithoutBackendIdentity_RejectsSessionMismatch()
        {
            var dev = new TownMember(2, null, null, null, "Dev", EPlayerClass.Warrior);

            _relay.Handle(dev, ListHerb());

            Assert.IsTrue(_audience.TryGetLast(2, out MarketResultMessage result));
            Assert.AreEqual(ERejectReason.SessionMismatch, result.Reason);
            Assert.AreEqual(0, _transport.Calls);
        }

        [Test]
        public void RemoveMember_WhileWriteInFlight_DropsTheLateResponse()
        {
            _relay.Handle(_member, ListHerb());

            _relay.RemoveMember(1);
            Pump();

            Assert.AreEqual(0, _audience.Count<MarketResultMessage>(1));
            Assert.AreEqual(0, _relay.PendingCount);
        }

        [Test]
        public void Handle_MoreThanLimitInOneSecond_RejectsRateLimited()
        {
            for (int i = 0; i < Constants.Town.MARKET_READS_PER_SECOND; ++i)
            {
                _relay.Handle(_member, Read(EMarketAction.History));
            }

            _relay.Handle(_member, Read(EMarketAction.History));

            Assert.IsTrue(_audience.TryGetLast(1, out MarketResultMessage result));
            Assert.AreEqual(ERejectReason.RateLimited, result.Reason);
        }

        // 창을 열 때 나가는 내 등록과 검색, 곧바로 이어지는 검색과 상세는 쓰기 한도와 따로 세어 막히지 않아야 한다
        [Test]
        public void Handle_ReadsAfterWritesInSameSecond_UseSeparateBudget()
        {
            for (int i = 0; i < Constants.Town.REQUESTS_PER_SECOND; ++i)
            {
                _relay.Handle(_member, ListHerb());
            }

            int before = _audience.Count<MarketResultMessage>(1);
            _relay.Handle(_member, Read(EMarketAction.Listings, MarketTradeRules.HERB_ID));

            Assert.AreEqual(before, _audience.Count<MarketResultMessage>(1), "쓰기가 한도를 채워도 조회는 거절 없이 API로 보낸다");
            Assert.AreEqual(2, _relay.PendingCount, "쓰기 하나와 상세 하나가 진행 중이다");
        }

        [Test]
        public void Handle_ReadsUpToReadLimitInOneSecond_AreNotRateLimited()
        {
            Assert.Greater(Constants.Town.MARKET_READS_PER_SECOND, Constants.Town.REQUESTS_PER_SECOND,
                "열기, 검색, 상세가 이어지는 정상 흐름보다 조회 한도가 넉넉해야 한다");

            // 첫 요청은 API로 가고 나머지는 같은 종류 진행 중(PendingCommit)으로 답한다. 한도 안에서는 RateLimited가 없어야 한다
            for (int i = 0; i < Constants.Town.MARKET_READS_PER_SECOND; ++i)
            {
                _relay.Handle(_member, Read(EMarketAction.History));
                if (_audience.TryGetLast(1, out MarketResultMessage reply))
                {
                    Assert.AreNotEqual(ERejectReason.RateLimited, reply.Reason, "조회 " + i + "번째");
                }
            }

            _relay.Handle(_member, Read(EMarketAction.History));
            Assert.IsTrue(_audience.TryGetLast(1, out MarketResultMessage limited));
            Assert.AreEqual(ERejectReason.RateLimited, limited.Reason, "조회 남용은 여전히 막는다");

            _now += Constants.Town.REQUEST_WINDOW_SECONDS;
            _relay.Handle(_member, Read(EMarketAction.History));
            Assert.IsTrue(_audience.TryGetLast(1, out MarketResultMessage next));
            Assert.AreNotEqual(ERejectReason.RateLimited, next.Reason, "다음 창에서는 다시 받는다");
        }

        [Test]
        public void Handle_WritesOverWriteLimit_StillRateLimited()
        {
            for (int i = 0; i < Constants.Town.REQUESTS_PER_SECOND; ++i)
            {
                _relay.Handle(_member, ListHerb());
            }

            _relay.Handle(_member, ListHerb());

            Assert.IsTrue(_audience.TryGetLast(1, out MarketResultMessage result));
            Assert.AreEqual(ERejectReason.RateLimited, result.Reason, "쓰기 남용 방지는 그대로다");
        }

        [Test]
        public void Rules_ListPotion_RejectsNotTradable()
        {
            var message = new MarketRequestMessage(Guid.NewGuid(), EMarketAction.List, -1, -1, -1, null, 0, 0, 0, "item_potion_small", null,
                MarketCodes.ASSET_ACCOUNT_ITEM, null, 1, 100, null);

            ERejectReason reason = MarketRequestRules.TryBuild(in message, CHARACTER_ID, out BackendRequest request);

            Assert.AreEqual(ERejectReason.MarketNotTradable, reason);
            Assert.IsNull(request);
        }

        [Test]
        public void Rules_PriceOutOfRange_RejectsBuyAndList()
        {
            var list = new MarketRequestMessage(Guid.NewGuid(), EMarketAction.List, -1, -1, -1, null, 0, 0, 0, MarketTradeRules.HERB_ID, null,
                MarketCodes.ASSET_ACCOUNT_ITEM, null, 1, MarketLimits.MAX_UNIT_PRICE + 1, null);
            var buy = new MarketRequestMessage(Guid.NewGuid(), EMarketAction.Buy, -1, -1, -1, null, 0, 0, 0, null, LISTING_ID, null, null, 0, 5,
                null);

            Assert.AreEqual(ERejectReason.MarketPriceOutOfRange, MarketRequestRules.TryBuild(in list, CHARACTER_ID, out _));
            Assert.AreEqual(ERejectReason.MarketPriceOutOfRange, MarketRequestRules.TryBuild(in buy, CHARACTER_ID, out _));
        }

        [Test]
        public void Rules_Buy_UsesCharacterPathAndRequestIdAsOperationId()
        {
            var buy = new MarketRequestMessage(Guid.NewGuid(), EMarketAction.Buy, -1, -1, -1, null, 0, 0, 0, null, LISTING_ID, null, null, 2, 50,
                null);

            ERejectReason reason = MarketRequestRules.TryBuild(in buy, CHARACTER_ID, out BackendRequest request);

            Assert.AreEqual(ERejectReason.None, reason);
            StringAssert.Contains(CHARACTER_ID, request.Path);
            Assert.AreEqual(buy.RequestId.ToString("D"), request.OperationId);
            var body = BackendJson.Deserialize<MarketBuyRequest>(request.BodyJson);
            Assert.AreEqual(2, body.Quantity);
            Assert.AreEqual(50, body.ExpectedUnitPrice);
        }

        [Test]
        public void Serializer_RoundTripsRequestAndResult()
        {
            var request = new MarketRequestMessage(Guid.NewGuid(), EMarketAction.Search, 1, -1, 2, new[] { "a", "b" }, 1, 0, 3, null, null, null,
                null, 0, 0, null);
            var writer = new Mirror.NetworkWriter();
            MarketMessageSerializer.WriteMarketRequestMessage(writer, request);
            var reader = new Mirror.NetworkReader(writer.ToArraySegment());
            MarketRequestMessage read = MarketMessageSerializer.ReadMarketRequestMessage(reader);

            Assert.AreEqual(request.RequestId, read.RequestId);
            Assert.AreEqual(2, read.DefinitionIds.Length);
            Assert.AreEqual(3, read.Page);

            var listing = new MarketListingEntry("l", MarketCodes.ASSET_EQUIPMENT, "equip_x", "item", 1, 99, 1, 1, MarketCodes.ListingState.ACTIVE,
                0, 0, 0, 10, 20, 0, false, true);
            var result = new MarketResultMessage(request.RequestId, EMarketAction.Mine, ERejectReason.None, null, new[] { listing }, null, 1, 0,
                false, 0, 55, 2, 1234, null, null, 0, 0, 0);
            var resultWriter = new Mirror.NetworkWriter();
            MarketMessageSerializer.WriteMarketResultMessage(resultWriter, result);
            MarketResultMessage readResult = MarketMessageSerializer.ReadMarketResultMessage(new Mirror.NetworkReader(resultWriter.ToArraySegment()));

            Assert.AreEqual(1, readResult.Listings.Length);
            Assert.AreEqual(99, readResult.Listings[0].UnitPrice);
            Assert.IsTrue(readResult.Listings[0].Mine);
            Assert.AreEqual(55, readResult.AvailableYeopjeon);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private void Pump()
        {
            FakeBackendTransport.WaitForCompleted(_gateway, 1);
            _gateway.Pump();
        }

        private static MarketRequestMessage ListHerb()
        {
            return new MarketRequestMessage(Guid.NewGuid(), EMarketAction.List, -1, -1, -1, null, 0, 0, 0, MarketTradeRules.HERB_ID, null,
                MarketCodes.ASSET_ACCOUNT_ITEM, null, 5, 100, null);
        }

        private static MarketRequestMessage Read(EMarketAction action)
        {
            return Read(action, null);
        }

        private static MarketRequestMessage Read(EMarketAction action, string definitionId)
        {
            return new MarketRequestMessage(Guid.NewGuid(), action, -1, -1, -1, null, 0, 0, 0, definitionId, null, null, null, 0, 0, null);
        }

        private BackendTransportResult Respond(BackendRequest request)
        {
            if (!string.IsNullOrEmpty(request.OperationId))
            {
                _operationIds.Enqueue(request.OperationId);
            }

            if (_down)
            {
                return new BackendTransportResult(STATUS_DOWN, null);
            }

            if (request.Method == EBackendMethod.Get)
            {
                return new BackendTransportResult(STATUS_OK, BackendJson.Serialize(new MarketMineResponse(
                    new MarketListingView[0], 777, 0, 1000)));
            }

            var result = new OperationResult(request.OperationId, ContractCodes.OperationStatus.COMMITTED, 0, 1, 0, string.Empty);
            return new BackendTransportResult(STATUS_OK, BackendJson.Serialize(new MarketOperationResponse(result, LISTING_ID, null, 0, 0, 0)));
        }
    }
}
