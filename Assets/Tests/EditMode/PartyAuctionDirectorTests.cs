// System
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Network;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public class PartyAuctionDirectorTests
    {
        // 개발 출정(게이트웨이 없음)으로 마감 시계, 자격, 금액 규칙을 보고, 가짜 전송기로 API 경로와 거절 처리를 본다

        /****************************************
        *                Fields
        ****************************************/

        private const string SESSION_ID      = "11111111-1111-1111-1111-111111111111";
        private const string ITEM_ID         = "item_auction_material_epic";
        private const string API_ONE         = "aaaaaaaa-0000-0000-0000-000000000001";
        private const string API_TWO         = "aaaaaaaa-0000-0000-0000-000000000002";
        private const int    PLAYER_ONE      = 1;
        private const int    PLAYER_TWO      = 2;
        private const int    LATE_RETURNER   = 3;
        private const long   UNIX_MS         = 1790000000000;
        private const long   UNAFFORDABLE    = 200;
        private const int    STATUS_OK       = 200;
        private const int    STATUS_CONFLICT = 409;
        private const double TIME_EPSILON    = 0.0001;

        private SessionConfigSO _config;
        private FakeAudience    _audience;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _config   = ScriptableObject.CreateInstance<SessionConfigSO>();
            _audience = new FakeAudience();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_config);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Begin_NoItem_FinishesWithoutOpening()
        {
            PartyAuctionDirector director = Create(null, PLAYER_ONE, PLAYER_TWO);

            director.Begin(0.0, null);

            Assert.IsTrue(director.IsFinished);
            Assert.AreEqual(EAuctionPhase.None, director.Phase);
        }

        [Test]
        public void Begin_NobodyConnected_Finishes()
        {
            PartyAuctionDirector director = Create(null, PLAYER_ONE, PLAYER_TWO);
            _audience.Connected.Clear();

            director.Begin(0.0, ITEM_ID);

            Assert.IsTrue(director.IsFinished);
        }

        [Test]
        public void Bidding_RaiseAndDeadline_SellsAndFinishes()
        {
            PartyAuctionDirector director = Create(null, PLAYER_ONE, PLAYER_TWO);
            director.Begin(0.0, ITEM_ID);
            long raised = _config.AuctionMinBid + _config.AuctionMinIncrement;

            director.HandleBid(PLAYER_ONE, _config.AuctionMinBid);
            director.HandleBid(PLAYER_TWO, raised - 1);
            director.HandleBid(PLAYER_TWO, raised);

            Assert.AreEqual(EAuctionMode.Bidding, director.Mode);
            Assert.AreEqual(EAuctionPhase.Open, director.Phase);
            Assert.AreEqual(PLAYER_TWO, director.HighParticipantId);
            Assert.AreEqual(ERejectReason.BidTooLow, _audience.LastResult(PLAYER_TWO, 1));
            Assert.AreEqual(ERejectReason.None, _audience.LastResult(PLAYER_TWO, 0));

            director.Tick(_config.AuctionBaseSeconds);

            Assert.AreEqual(EAuctionPhase.Sold, director.Phase);
            Assert.IsTrue(director.IsFinished);
            Assert.AreEqual(raised, director.PayoutEach, "분배 대상은 낙찰자를 뺀 한 명이다");
            Assert.AreEqual(EAuctionPhase.Sold, _audience.States[PLAYER_ONE].Last().Phase);
        }

        [Test]
        public void Bid_LateInWindow_ExtendsButNeverPastCap()
        {
            PartyAuctionDirector director = Create(null, PLAYER_ONE, PLAYER_TWO);
            director.Begin(0.0, ITEM_ID);
            long amount = _config.AuctionMinBid;
            double late = _config.AuctionBaseSeconds - (_config.AuctionExtendThreshold * 0.5f);

            director.Tick(late);
            director.HandleBid(PLAYER_ONE, amount);

            Assert.AreEqual(late + _config.AuctionExtendTo, director.EndsAt, TIME_EPSILON);

            // 계속 막판에 입찰해도 품목 시작부터 상한을 넘지 않는다
            int bidder = PLAYER_TWO;
            while (director.EndsAt < _config.AuctionMaxSeconds - TIME_EPSILON)
            {
                director.Tick(director.EndsAt - TIME_EPSILON);
                amount += _config.AuctionMinIncrement;
                director.HandleBid(bidder, amount);
                bidder = (bidder == PLAYER_ONE) ? PLAYER_TWO : PLAYER_ONE;
            }

            Assert.AreEqual(_config.AuctionMaxSeconds, director.EndsAt, TIME_EPSILON);
            director.Tick(_config.AuctionMaxSeconds);
            Assert.IsTrue(director.IsFinished);
        }

        [Test]
        public void Solo_OnlyMinimumOnce_ClosesImmediately()
        {
            PartyAuctionDirector director = Create(null, PLAYER_ONE, PLAYER_TWO);
            _audience.Connected.Remove(PLAYER_TWO);
            director.Begin(0.0, ITEM_ID);

            director.HandleBid(PLAYER_ONE, _config.AuctionMinBid + 1);
            director.HandleBid(PLAYER_ONE, _config.AuctionMinBid);

            Assert.AreEqual(EAuctionMode.Solo, director.Mode);
            Assert.AreEqual(ERejectReason.BidTooLow, _audience.LastResult(PLAYER_ONE, 1));
            Assert.AreEqual(EAuctionPhase.Sold, director.Phase);
            Assert.AreEqual(0L, director.PayoutEach, "단독 구매 대금은 전액 소각이다");
            Assert.IsTrue(director.IsFinished);
        }

        [Test]
        public void Bid_ReturnedAfterOpen_IsNotEligible()
        {
            PartyAuctionDirector director = Create(null, PLAYER_ONE, PLAYER_TWO);
            _audience.Connected.Remove(PLAYER_TWO);
            _audience.Connected.Add(LATE_RETURNER);
            director.Begin(0.0, ITEM_ID);
            _audience.Connected.Add(PLAYER_TWO);

            director.HandleBid(PLAYER_TWO, _config.AuctionMinBid);

            Assert.AreEqual(ERejectReason.NotEligible, _audience.LastResult(PLAYER_TWO, 0));
            Assert.IsFalse(_audience.States[PLAYER_TWO].Last().IsEligible);
        }

        [Test]
        public void Bid_HighestBidCannotIncrease_RejectsWithoutOverflow()
        {
            PartyAuctionDirector director = Create(null, PLAYER_ONE, PLAYER_TWO);
            director.Begin(0.0, ITEM_ID);
            director.HandleBid(PLAYER_ONE, long.MaxValue);

            director.HandleBid(PLAYER_TWO, _config.AuctionMinBid);
            director.HandleBid(PLAYER_TWO, long.MaxValue);

            Assert.AreEqual(long.MaxValue, director.HighBid);
            Assert.AreEqual(PLAYER_ONE, director.HighParticipantId);
            Assert.AreEqual(1L, director.BidSeq);
            Assert.AreEqual(ERejectReason.BidTooLow, _audience.LastResult(PLAYER_TWO, 0));
            Assert.AreEqual(ERejectReason.BidTooLow, _audience.LastResult(PLAYER_TWO, 1));
            Assert.AreEqual(0L, _audience.States[PLAYER_ONE].Last().MinimumBid);
        }

        [Test]
        public void Bid_PendingRejectionAfterDeadline_RejectsNewBidBeforeNextTick()
        {
            var transport = new FakeBackendTransport((request, token) => Task.FromResult(
                request.Path.EndsWith("/bids", StringComparison.Ordinal)
                    ? Result(ContractCodes.OperationStatus.REJECTED, ContractCodes.RejectReason.INSUFFICIENT_CURRENCY,
                        STATUS_CONFLICT)
                    : Result(ContractCodes.OperationStatus.COMMITTED, ContractCodes.RejectReason.NONE, STATUS_OK)));
            var gateway = new BackendGateway(transport, ZeroClock,
                FakeBackendTransport.ImmediateDelay(new ConcurrentQueue<TimeSpan>()), null, null);
            try
            {
                PartyAuctionDirector director = Create(gateway, PLAYER_ONE, PLAYER_TWO);
                director.Begin(0.0, ITEM_ID);
                Complete(gateway);
                director.HandleBid(PLAYER_ONE, _config.AuctionMinBid);
                director.Tick(_config.AuctionBaseSeconds);
                Complete(gateway);
                int requestsBeforeLateBid = transport.Requests.Count;

                director.HandleBid(PLAYER_TWO, _config.AuctionMinBid);

                Assert.AreEqual(ERejectReason.Closed, _audience.LastResult(PLAYER_TWO, 0));
                Assert.AreEqual(requestsBeforeLateBid, transport.Requests.Count);
                Assert.AreEqual(0L, director.BidSeq);
                Assert.AreEqual(0L, director.HighBid);

                director.Tick(_config.AuctionBaseSeconds);
                // 마감 응답을 처리하는 Pump가 빠른 finish 응답까지 함께 전달할 수 있다
                while (!director.IsFinished)
                {
                    Complete(gateway);
                }
                Assert.IsTrue(director.IsFinished);
                Assert.AreEqual(EAuctionPhase.Unsold, director.Phase);
            }
            finally
            {
                gateway.Dispose();
            }
        }

        [Test]
        public void Backend_RejectedBidKeepsState_ThenClosesAndFinishesThroughApi()
        {
            var requests = new ConcurrentQueue<BackendRequest>();
            string unaffordable = "\"amount\":" + UNAFFORDABLE;
            Func<BackendRequest, BackendTransportResult> respond = request =>
            {
                requests.Enqueue(request);
                bool poor = request.Path.EndsWith("/bids", StringComparison.Ordinal) && request.BodyJson.Contains(unaffordable);
                return poor
                    ? Result(ContractCodes.OperationStatus.REJECTED, ContractCodes.RejectReason.INSUFFICIENT_CURRENCY,
                        STATUS_CONFLICT)
                    : Result(ContractCodes.OperationStatus.COMMITTED, 0, STATUS_OK);
            };
            var transport = new FakeBackendTransport((request, token) => Task.FromResult(respond(request)));
            var gateway   = new BackendGateway(transport, ZeroClock,
                FakeBackendTransport.ImmediateDelay(new ConcurrentQueue<TimeSpan>()), null, null);
            try
            {
                PartyAuctionDirector director = Create(gateway, PLAYER_ONE, PLAYER_TWO);
                director.Begin(0.0, ITEM_ID);
                Complete(gateway);
                director.HandleBid(PLAYER_ONE, _config.AuctionMinBid);
                Complete(gateway);

                // 로컬 금액 규칙은 통과했지만 API가 잔액 부족으로 거절한다. 이 서버의 최고가와 순번은 그대로다
                director.HandleBid(PLAYER_TWO, UNAFFORDABLE);
                director.HandleBid(PLAYER_ONE, UNAFFORDABLE + _config.AuctionMinIncrement);
                Complete(gateway);

                Assert.AreEqual(ERejectReason.PendingCommit, _audience.LastResult(PLAYER_ONE, 0),
                    "보내는 중인 입찰이 있으면 다음 입찰은 기다리게 한다");
                Assert.AreEqual(ERejectReason.InsufficientCurrency, _audience.LastResult(PLAYER_TWO, 0));
                Assert.AreEqual(PLAYER_ONE, director.HighParticipantId);
                Assert.AreEqual(1L, director.BidSeq);

                director.Tick(_config.AuctionBaseSeconds);
                Complete(gateway);
                Complete(gateway);

                string[] paths = requests.Select(request => request.Path).ToArray();
                Assert.IsTrue(director.IsFinished);
                Assert.AreEqual(EAuctionPhase.Sold, director.Phase);
                StringAssert.EndsWith("/auctions", paths[0]);
                StringAssert.EndsWith("/close", paths[paths.Length - 2]);
                StringAssert.EndsWith("/auctions/finish", paths[paths.Length - 1]);
            }
            finally
            {
                gateway.Dispose();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private PartyAuctionDirector Create(BackendGateway gateway, params int[] participants)
        {
            var director = new PartyAuctionDirector(SESSION_ID, _config, gateway, _audience, () => UNIX_MS, null);
            foreach (int participant in participants)
            {
                Assert.IsTrue(director.AddParticipant(participant, (participant == PLAYER_ONE) ? API_ONE : API_TWO));
                _audience.Connected.Add(participant);
            }

            return director;
        }

        private static void Complete(BackendGateway gateway)
        {
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();
        }

        private static BackendTransportResult Result(int status, int reasonCode, int httpStatus)
        {
            return new BackendTransportResult(httpStatus, BackendJson.Serialize(new OperationResult(SESSION_ID, status,
                reasonCode, 1L, 0L, string.Empty)));
        }

        private static double ZeroClock()
        {
            return 0.0;
        }

        private sealed class FakeAudience : IAuctionAudience
        {
            public HashSet<int> Connected { get; } = new HashSet<int>();
            public Dictionary<int, List<AuctionStateMessage>> States { get; } = new Dictionary<int, List<AuctionStateMessage>>();
            public Dictionary<int, List<AuctionBidResultMessage>> Results { get; } =
                new Dictionary<int, List<AuctionBidResultMessage>>();

            public bool IsConnected(int participantId)
            {
                return Connected.Contains(participantId);
            }

            public void SendState(int participantId, in AuctionStateMessage message)
            {
                if (!States.TryGetValue(participantId, out List<AuctionStateMessage> list))
                {
                    list = new List<AuctionStateMessage>();
                    States.Add(participantId, list);
                }
                list.Add(message);
            }

            public void SendBidResult(int participantId, in AuctionBidResultMessage message)
            {
                if (!Results.TryGetValue(participantId, out List<AuctionBidResultMessage> list))
                {
                    list = new List<AuctionBidResultMessage>();
                    Results.Add(participantId, list);
                }
                list.Add(message);
            }

            // 뒤에서 index번째 결과의 사유, 0이 가장 최근이다
            public ERejectReason LastResult(int participantId, int index)
            {
                List<AuctionBidResultMessage> list = Results[participantId];
                return list[list.Count - 1 - index].Reason;
            }
        }
    }
}
