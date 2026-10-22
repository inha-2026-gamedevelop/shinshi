// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public sealed class AuctionWriteLifecycleTests
    {
        // 경매장 쓰기 수명: 미확정 쓰기는 하나만 들고, 결과 불명을 확정 실패로 바꾸지 않으며, 같은 ID로만 다시 확인한다

        /****************************************
        *                Fields
        ****************************************/

        private const float NOW = 10f;

        private MarketPendingWrite _pending;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _pending = new MarketPendingWrite();
            TownMarketClient.ResetForTest();
        }

        [TearDown]
        public void TearDown()
        {
            TownMarketClient.ResetForTest();
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TryBegin_WhileUnresolved_RefusesAndKeepsOriginal()
        {
            MarketRequestMessage first  = Write(EMarketAction.Buy, "L1");
            MarketRequestMessage second = Write(EMarketAction.Cancel, "L2");

            Assert.That(_pending.TryBegin(in first, NOW), Is.True);
            Assert.That(_pending.TryBegin(in second, NOW + 1f), Is.False);

            Assert.That(_pending.RequestId, Is.EqualTo(first.RequestId));
            Assert.That(_pending.Action, Is.EqualTo(EMarketAction.Buy));
            Assert.That(_pending.Request.ListingId, Is.EqualTo("L1"));
        }

        [Test]
        public void TryBegin_ReadOrEmptyId_Refuses()
        {
            MarketRequestMessage read  = new MarketRequestMessage(Guid.NewGuid(), EMarketAction.Mine, MarketRequestMessage.NONE,
                MarketRequestMessage.NONE, MarketRequestMessage.NONE, null, 0, 0, 0, null, null, null, null, 0, 0, null);
            MarketRequestMessage empty = new MarketRequestMessage(Guid.Empty, EMarketAction.Buy, MarketRequestMessage.NONE,
                MarketRequestMessage.NONE, MarketRequestMessage.NONE, null, 0, 0, 0, null, "L1", null, null, 1, 100, null);

            Assert.That(_pending.TryBegin(in read, NOW), Is.False);
            Assert.That(_pending.TryBegin(in empty, NOW), Is.False);
            Assert.That(_pending.State, Is.EqualTo(EMarketWriteState.None));
        }

        [Test]
        public void Resolve_ForeignOrLateResult_IsIgnored()
        {
            MarketRequestMessage request = Write(EMarketAction.Buy, "L1");

            Assert.That(_pending.Resolve(request.RequestId, ERejectReason.None, NOW), Is.EqualTo(EMarketWriteResolve.Ignored));

            _pending.TryBegin(in request, NOW);
            Assert.That(_pending.Resolve(Guid.NewGuid(), ERejectReason.None, NOW + 1f), Is.EqualTo(EMarketWriteResolve.Ignored));
            Assert.That(_pending.State, Is.EqualTo(EMarketWriteState.Pending));

            Assert.That(_pending.Resolve(request.RequestId, ERejectReason.None, NOW + 2f), Is.EqualTo(EMarketWriteResolve.Completed));
            Assert.That(_pending.Resolve(request.RequestId, ERejectReason.None, NOW + 3f), Is.EqualTo(EMarketWriteResolve.Ignored));
        }

        [Test]
        public void Resolve_Confirmed_ClearsAndAllowsNextWrite()
        {
            MarketRequestMessage first = Write(EMarketAction.List, "L1");
            _pending.TryBegin(in first, NOW);

            Assert.That(_pending.Resolve(first.RequestId, ERejectReason.None, NOW + 1f), Is.EqualTo(EMarketWriteResolve.Completed));
            Assert.That(_pending.State, Is.EqualTo(EMarketWriteState.None));

            MarketRequestMessage next = Write(EMarketAction.Buy, "L2");
            Assert.That(_pending.TryBegin(in next, NOW + 2f), Is.True);
        }

        [Test]
        public void Resolve_BackendUnavailable_IsUnknownNotCompleted()
        {
            MarketRequestMessage request = Write(EMarketAction.Buy, "L1");
            _pending.TryBegin(in request, NOW);

            EMarketWriteResolve resolve = _pending.Resolve(request.RequestId, ERejectReason.BackendUnavailable, NOW + 1f);

            Assert.That(resolve, Is.EqualTo(EMarketWriteResolve.Unknown));
            Assert.That(_pending.State, Is.EqualTo(EMarketWriteState.Unknown));
            Assert.That(_pending.IsHeld, Is.True);
            Assert.That(_pending.RequestId, Is.EqualTo(request.RequestId));
        }

        [Test]
        public void Tick_AfterDeadline_BecomesUnknownAndKeepsOriginal()
        {
            MarketRequestMessage request = Write(EMarketAction.Cancel, "L1");
            _pending.TryBegin(in request, NOW);

            Assert.That(_pending.Tick(NOW + MarketPendingWrite.RESPONSE_TIMEOUT_SECONDS - 0.1f), Is.False);
            Assert.That(_pending.State, Is.EqualTo(EMarketWriteState.Pending));

            Assert.That(_pending.Tick(NOW + MarketPendingWrite.RESPONSE_TIMEOUT_SECONDS), Is.True);
            Assert.That(_pending.State, Is.EqualTo(EMarketWriteState.Unknown));
            Assert.That(_pending.Request.RequestId, Is.EqualTo(request.RequestId));
            Assert.That(_pending.Request.ListingId, Is.EqualTo("L1"));
        }

        [Test]
        public void AutoRetry_IsBoundedThenWaitsForExplicitReconfirm()
        {
            MarketRequestMessage request = Write(EMarketAction.Buy, "L1");
            _pending.TryBegin(in request, NOW);
            float now = NOW;

            for (int attempt = 0; attempt < MarketPendingWrite.AUTO_RETRIES; ++attempt)
            {
                now += 1f;
                _pending.Resolve(request.RequestId, ERejectReason.BackendUnavailable, now);
                Assert.That(_pending.NeedsReconfirm, Is.False, "자동 재확인이 남아 있으면 사용자 확인을 요구하지 않는다");
                Assert.That(_pending.TryTakeAutoRetry(now + MarketPendingWrite.AUTO_RETRY_SECONDS - 0.1f, out _), Is.False);

                now += MarketPendingWrite.AUTO_RETRY_SECONDS;
                Assert.That(_pending.TryTakeAutoRetry(now, out MarketRequestMessage resend), Is.True);
                Assert.That(resend.RequestId, Is.EqualTo(request.RequestId));
                Assert.That(resend.ListingId, Is.EqualTo("L1"));
                Assert.That(_pending.State, Is.EqualTo(EMarketWriteState.Pending));
            }

            now += 1f;
            _pending.Resolve(request.RequestId, ERejectReason.BackendUnavailable, now);

            Assert.That(_pending.State, Is.EqualTo(EMarketWriteState.Unknown));
            Assert.That(_pending.NeedsReconfirm, Is.True);
            Assert.That(_pending.TryTakeAutoRetry(now + 1000f, out _), Is.False, "예산을 다 쓴 뒤에는 자동으로 다시 보내지 않는다");
            Assert.That(_pending.RequestId, Is.EqualTo(request.RequestId));
        }

        [Test]
        public void TryReconfirm_OnlyWithSameIdAndRestartsBudget()
        {
            MarketRequestMessage request = Write(EMarketAction.Buy, "L1");
            _pending.TryBegin(in request, NOW);
            _pending.Resolve(request.RequestId, ERejectReason.BackendUnavailable, NOW + 1f);

            Assert.That(_pending.TryReconfirm(Guid.NewGuid(), NOW + 2f, out _), Is.False);
            Assert.That(_pending.State, Is.EqualTo(EMarketWriteState.Unknown));

            Assert.That(_pending.TryReconfirm(request.RequestId, NOW + 2f, out MarketRequestMessage resend), Is.True);
            Assert.That(resend.RequestId, Is.EqualTo(request.RequestId));
            Assert.That(resend.Action, Is.EqualTo(EMarketAction.Buy));
            Assert.That(_pending.State, Is.EqualTo(EMarketWriteState.Pending));

            Assert.That(_pending.TryReconfirm(request.RequestId, NOW + 3f, out _), Is.False, "진행 중에는 다시 보내지 않는다");
        }

        [Test]
        public void TryReconfirm_AfterExhaustedBudget_ResumesBoundedAutoRetry()
        {
            MarketRequestMessage request = Write(EMarketAction.Ack, "L1");
            _pending.TryBegin(in request, NOW);
            float now = NOW;
            for (int attempt = 0; attempt < MarketPendingWrite.AUTO_RETRIES; ++attempt)
            {
                now += 1f;
                _pending.Resolve(request.RequestId, ERejectReason.BackendUnavailable, now);
                now += MarketPendingWrite.AUTO_RETRY_SECONDS;
                _pending.TryTakeAutoRetry(now, out _);
            }

            now += 1f;
            _pending.Resolve(request.RequestId, ERejectReason.BackendUnavailable, now);
            Assert.That(_pending.NeedsReconfirm, Is.True);

            Assert.That(_pending.TryReconfirm(request.RequestId, now, out _), Is.True);
            _pending.Resolve(request.RequestId, ERejectReason.BackendUnavailable, now + 1f);

            Assert.That(_pending.NeedsReconfirm, Is.False);
            Assert.That(_pending.State, Is.EqualTo(EMarketWriteState.Unknown));
        }

        [Test]
        public void ConnectionReplaced_LeavesStaleMarkerAndNeverResends()
        {
            MarketRequestMessage request = Write(EMarketAction.Buy, "L1");
            _pending.TryBegin(in request, NOW);
            _pending.Resolve(request.RequestId, ERejectReason.BackendUnavailable, NOW + 1f);

            _pending.ConnectionReplaced();

            Assert.That(_pending.IsHeld, Is.False);
            Assert.That(_pending.State, Is.EqualTo(EMarketWriteState.None));
            Assert.That(_pending.HasStale, Is.True);
            Assert.That(_pending.StaleAction, Is.EqualTo(EMarketAction.Buy));
            Assert.That(_pending.TryTakeAutoRetry(NOW + 1000f, out _), Is.False);
            Assert.That(_pending.TryReconfirm(request.RequestId, NOW + 1000f, out _), Is.False);
            Assert.That(_pending.Resolve(request.RequestId, ERejectReason.None, NOW + 1001f), Is.EqualTo(EMarketWriteResolve.Ignored));
        }

        [Test]
        public void ConnectionReplaced_StaleMarkerDoesNotBlockWritesAndResolveClearsIt()
        {
            MarketRequestMessage request = Write(EMarketAction.Buy, "L1");
            _pending.TryBegin(in request, NOW);
            _pending.ConnectionReplaced();

            MarketRequestMessage next = Write(EMarketAction.Cancel, "L2");
            Assert.That(_pending.TryBegin(in next, NOW + 1f), Is.True, "이전 연결의 결과 불명이 새 연결의 쓰기를 영구히 막지 않는다");
            Assert.That(_pending.RequestId, Is.EqualTo(next.RequestId));
            Assert.That(_pending.HasStale, Is.True);

            _pending.ResolveStale();

            Assert.That(_pending.HasStale, Is.False);
            Assert.That(_pending.StaleAction, Is.EqualTo(EMarketAction.None));
            Assert.That(_pending.RequestId, Is.EqualTo(next.RequestId));
        }

        [Test]
        public void ConnectionReplaced_WithNothingHeld_LeavesNoMarker()
        {
            _pending.ConnectionReplaced();

            Assert.That(_pending.HasStale, Is.False);
        }

        [TestCase(ERejectReason.RateLimited)]
        [TestCase(ERejectReason.PendingCommit)]
        [TestCase(ERejectReason.SessionMismatch)]
        public void Resolve_PreflightRejectOnFirstSend_IsDefiniteRejection(ERejectReason reason)
        {
            MarketRequestMessage request = Write(EMarketAction.Buy, "L1");
            _pending.TryBegin(in request, NOW);

            Assert.That(_pending.SendCount, Is.EqualTo(1));
            Assert.That(_pending.Resolve(request.RequestId, reason, NOW + 1f), Is.EqualTo(EMarketWriteResolve.Completed),
                "처음 보낸 요청이 서버에서 실행 전에 거절되면 확정 거절이다");
            Assert.That(_pending.IsHeld, Is.False);
        }

        [TestCase(ERejectReason.RateLimited)]
        [TestCase(ERejectReason.PendingCommit)]
        [TestCase(ERejectReason.SessionMismatch)]
        public void Resolve_PreflightRejectOnReconfirmSend_KeepsOriginalHeldAndUnknown(ERejectReason reason)
        {
            MarketRequestMessage request = Write(EMarketAction.Buy, "L1");
            _pending.TryBegin(in request, NOW);
            _pending.Tick(NOW + MarketPendingWrite.RESPONSE_TIMEOUT_SECONDS);
            Assert.That(_pending.TryReconfirm(request.RequestId, NOW + 9f, out _), Is.True);
            Assert.That(_pending.SendCount, Is.EqualTo(2));

            EMarketWriteResolve resolve = _pending.Resolve(request.RequestId, reason, NOW + 10f);

            Assert.That(resolve, Is.EqualTo(EMarketWriteResolve.Unknown), "재확인 전송의 거절은 앞선 전송의 결과를 확정하지 않는다");
            Assert.That(_pending.IsHeld, Is.True);
            Assert.That(_pending.RequestId, Is.EqualTo(request.RequestId));
            Assert.That(_pending.State, Is.EqualTo(EMarketWriteState.Unknown));

            Assert.That(_pending.Resolve(request.RequestId, ERejectReason.None, NOW + 11f), Is.EqualTo(EMarketWriteResolve.Completed),
                "그 뒤 도착한 원래 요청의 성공은 여전히 이 요청의 결과로 반영된다");
        }

        [Test]
        public void Resolve_RepeatedPreflightRejects_StayBoundedAndEndWaitingForExplicitReconfirm()
        {
            MarketRequestMessage request = Write(EMarketAction.Buy, "L1");
            _pending.TryBegin(in request, NOW);
            _pending.Tick(NOW + MarketPendingWrite.RESPONSE_TIMEOUT_SECONDS);
            float now = NOW + MarketPendingWrite.RESPONSE_TIMEOUT_SECONDS;

            for (int attempt = 0; attempt < MarketPendingWrite.AUTO_RETRIES; ++attempt)
            {
                now += MarketPendingWrite.AUTO_RETRY_SECONDS;
                Assert.That(_pending.TryTakeAutoRetry(now, out _), Is.True);
                now += 0.5f;
                _pending.Resolve(request.RequestId, ERejectReason.RateLimited, now);
            }

            Assert.That(_pending.State, Is.EqualTo(EMarketWriteState.Unknown));
            Assert.That(_pending.NeedsReconfirm, Is.True, "재확인 거절이 반복돼도 예산이 끝나면 사용자 재확인을 기다린다");
            Assert.That(_pending.TryTakeAutoRetry(now + 1000f, out _), Is.False);
            Assert.That(_pending.RequestId, Is.EqualTo(request.RequestId));
        }

        [Test]
        public void Resolve_PreflightRejectWhileAlreadyUnknown_DoesNotExtendRetrySchedule()
        {
            MarketRequestMessage request = Write(EMarketAction.Buy, "L1");
            _pending.TryBegin(in request, NOW);
            _pending.Tick(NOW + MarketPendingWrite.RESPONSE_TIMEOUT_SECONDS);
            _pending.TryReconfirm(request.RequestId, NOW + 9f, out _);
            _pending.Resolve(request.RequestId, ERejectReason.RateLimited, NOW + 10f);
            Assert.That(_pending.State, Is.EqualTo(EMarketWriteState.Unknown));

            _pending.Resolve(request.RequestId, ERejectReason.RateLimited, NOW + 10.5f);

            Assert.That(_pending.TryTakeAutoRetry(NOW + 10f + MarketPendingWrite.AUTO_RETRY_SECONDS, out _), Is.True,
                "이미 결과 불명인 요청의 중복 거절이 다음 자동 재확인 시각을 밀지 않는다");
        }

        [Test]
        public void SendCount_CountsEverySendOfSameIdAndResetsOnClear()
        {
            MarketRequestMessage request = Write(EMarketAction.Cancel, "L1");
            Assert.That(_pending.SendCount, Is.EqualTo(0));

            _pending.TryBegin(in request, NOW);
            Assert.That(_pending.SendCount, Is.EqualTo(1));

            _pending.Resolve(request.RequestId, ERejectReason.BackendUnavailable, NOW + 1f);
            _pending.TryReconfirm(request.RequestId, NOW + 2f, out _);
            Assert.That(_pending.SendCount, Is.EqualTo(2));

            _pending.Clear();
            Assert.That(_pending.SendCount, Is.EqualTo(0));
        }

        // 서버는 같은 ID가 처리 중인지 보기 전에 요청 한도부터 검사한다.
        // 처리 중인 A가 시간 초과로 결과 불명이 되고, 읽기 요청이 한도를 써 버려 A의 재확인이 RateLimited로 돌아와도
        // 원래 A의 성공은 여전히 이 요청의 결과로 도착해야 한다
        [Test]
        public void Client_ReconfirmRateLimited_ThenOriginalSuccessArrives_IsStillCompleted()
        {
            List<MarketRequestMessage> sent = new List<MarketRequestMessage>();
            TownMarketClient.TestSink = sent.Add;
            Guid id = TownMarketClient.Buy("L1", 1, 100L);
            Assert.That(id, Is.Not.EqualTo(Guid.Empty));
            float now = Time.unscaledTime;

            TownMarketClient.Tick(now + MarketPendingWrite.RESPONSE_TIMEOUT_SECONDS);
            Assert.That(TownMarketClient.WriteState, Is.EqualTo(EMarketWriteState.Unknown));

            TownMarketClient.Tick(now + MarketPendingWrite.RESPONSE_TIMEOUT_SECONDS + MarketPendingWrite.AUTO_RETRY_SECONDS);
            Assert.That(sent.Count, Is.EqualTo(2), "같은 원문이 다시 전송됐다");
            Assert.That(sent[1].RequestId, Is.EqualTo(id));

            TownMarketClient.OnResult(Result(id, EMarketAction.Buy, ERejectReason.RateLimited));

            Assert.That(TownMarketClient.PendingWriteId, Is.EqualTo(id), "재확인 거절로 추적을 풀지 않는다");
            Assert.That(TownMarketClient.HasCompletedWrite, Is.False);

            TownMarketClient.OnResult(Result(id, EMarketAction.Buy, ERejectReason.None));

            Assert.That(TownMarketClient.WriteState, Is.EqualTo(EMarketWriteState.None));
            Assert.That(TownMarketClient.TryTakeCompletedWrite(out MarketResultMessage completed), Is.True);
            Assert.That(completed.RequestId, Is.EqualTo(id));
            Assert.That(completed.IsAccepted, Is.True, "원래 성공을 무시하지 않는다");
        }

        [Test]
        public void Client_FirstSendRateLimited_IsDefiniteRejection()
        {
            TownMarketClient.TestSink = _ => { };
            Guid id = TownMarketClient.Buy("L1", 1, 100L);

            TownMarketClient.OnResult(Result(id, EMarketAction.Buy, ERejectReason.RateLimited));

            Assert.That(TownMarketClient.WriteState, Is.EqualTo(EMarketWriteState.None));
            Assert.That(TownMarketClient.TryTakeCompletedWrite(out MarketResultMessage completed), Is.True);
            Assert.That(completed.Reason, Is.EqualTo(ERejectReason.RateLimited));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static MarketResultMessage Result(Guid requestId, EMarketAction action, ERejectReason reason)
        {
            return new MarketResultMessage(requestId, action, reason, null, null, null, 0, 0, false, 0L, 0L, 0, 0L, null, null, 0L, 0L, 0L);
        }

        private static MarketRequestMessage Write(EMarketAction action, string listingId)
        {
            return new MarketRequestMessage(Guid.NewGuid(), action, MarketRequestMessage.NONE, MarketRequestMessage.NONE,
                MarketRequestMessage.NONE, null, 0, 0, 0, null, listingId, null, null, 1, 100, null);
        }
    }
}
