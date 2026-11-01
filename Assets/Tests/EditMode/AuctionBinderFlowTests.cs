// System
using System;
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;
using Shinshi.UI;

namespace Shinshi.Tests
{
    public sealed class AuctionBinderFlowTests
    {
        // 경매장 바인더의 실제 OnEnable, OnDisable, Update 흐름으로 확인한다.
        // 열린 채 꺼졌다 켜질 때의 복구, 이전 연결 확인의 읽기 발신 횟수와 실패 복구, 확인 창이 없을 때의 자동 재확인 금지를 본다

        /****************************************
        *                Fields
        ****************************************/

        private const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private const int          TAB_BUY      = 0;
        private const int          TAB_LISTINGS = 2;
        private const int          TAB_HISTORY  = 3;

        private readonly List<MarketRequestMessage> _sent = new List<MarketRequestMessage>();

        private GameObject  _viewObject;
        private GameObject  _binderObject;
        private AuctionView _view;
        private Component   _binder;

        /****************************************
        *               Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _sent.Clear();
            TownMarketClient.ResetForTest();
            TownMarketClient.TestSink = _sent.Add;
            TownGrowthClient.ResetForTest();
            TownGrowthClient.TestSink = _ => { };
            AuctionBinder.TestIsTown  = () => true;

            _viewObject = new GameObject("AuctionBinderFlowView");
            _viewObject.SetActive(false);
            _view = _viewObject.AddComponent<AuctionView>();

            _binderObject = new GameObject("AuctionBinderFlowBinder");
            _binderObject.SetActive(false);
            _binder = _binderObject.AddComponent<AuctionBinder>();
            FieldInfo viewField = typeof(AuctionBinder).GetField("_view", INSTANCE);
            Assert.That(viewField, Is.Not.Null);
            viewField.SetValue(_binder, _view);
        }

        [TearDown]
        public void TearDown()
        {
            if (_binder != null)
            {
                Invoke("OnDisable");
            }

            if (_binderObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_binderObject);
            }

            if (_viewObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_viewObject);
            }

            _binder       = null;
            _binderObject = null;
            _view         = null;
            _viewObject   = null;
            AuctionBinder.TestIsTown = null;
            TownMarketClient.ResetForTest();
            TownGrowthClient.ResetForTest();
        }

        /****************************************
        *        Tests: enable/disable sync
        ****************************************/

        // 창이 열린 채 바인더만 꺼진 사이 쓰기가 확정되면, 다시 켜질 때 완료 응답을 받고 잠금 표시를 풀어야 한다
        [Test]
        public void Enable_OpenWindowDisabledWhileWriteCompleted_RecoversResultAndBusy()
        {
            Invoke("OnEnable");
            OpenWindow(TAB_BUY);
            Guid id = TownMarketClient.Buy("L1", 1, 100L);
            Assert.That(id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(_view.WriteBusy, Is.True, "진행 중에는 버튼이 잠긴다");

            Invoke("OnDisable");
            TownMarketClient.OnResult(Result(id, EMarketAction.Buy, ERejectReason.None));
            Assert.That(TownMarketClient.HasCompletedWrite, Is.True, "꺼진 사이에도 클라이언트가 결과를 들고 있다");
            Assert.That(_view.WriteBusy, Is.True, "꺼진 사이에는 화면이 바뀌지 않는다");

            _sent.Clear();
            Invoke("OnEnable");
            Invoke("Update");

            Assert.That(TownMarketClient.HasCompletedWrite, Is.False, "완료 응답을 소비한다");
            Assert.That(_view.WriteBusy, Is.False, "잠금 표시가 남지 않는다");
            Assert.That(_view.LastStatus, Is.EqualTo("처리되었습니다"));
            Assert.That(Count(EMarketAction.Mine), Is.EqualTo(1), "내 등록은 한 번만 다시 받는다");
        }

        [Test]
        public void Enable_OpenWindowDisabledWhileWriteUnknownThenReconnected_RecoversStaleRefresh()
        {
            Invoke("OnEnable");
            OpenWindow(TAB_LISTINGS);
            TownMarketClient.Buy("L1", 1, 100L);
            Invoke("OnDisable");

            TownMarketClient.ConnectionReplaced();
            Assert.That(TownMarketClient.HasStaleWrite, Is.True);

            _sent.Clear();
            Invoke("OnEnable");
            Invoke("Update");

            Assert.That(_view.WriteBusy, Is.False);
            Assert.That(Count(EMarketAction.Mine), Is.EqualTo(1));
            Assert.That(Count(EMarketAction.History), Is.EqualTo(1));
        }

        /****************************************
        *      Tests: single read per action
        ****************************************/

        [TestCase(TAB_BUY, 1, 1, 1)]
        [TestCase(TAB_LISTINGS, 0, 1, 1)]
        [TestCase(TAB_HISTORY, 0, 1, 1)]
        public void Open_StaleWrite_SendsEachReadOnce(int tab, int searches, int mines, int histories)
        {
            Guid buy = TownMarketClient.Buy("L1", 1, 100L);
            Assert.That(buy, Is.Not.EqualTo(Guid.Empty));
            TownMarketClient.ConnectionReplaced();
            _sent.Clear();

            Invoke("OnEnable");
            OpenWindow(tab);

            Assert.That(Count(EMarketAction.Search), Is.EqualTo(searches));
            Assert.That(Count(EMarketAction.Mine), Is.EqualTo(mines), "내 등록 읽기는 한 번");
            Assert.That(Count(EMarketAction.History), Is.EqualTo(histories), "거래 내역 읽기는 한 번");
        }

        [TestCase(TAB_BUY, 1, 1, 0)]
        [TestCase(TAB_LISTINGS, 0, 1, 0)]
        [TestCase(TAB_HISTORY, 0, 1, 1)]
        public void Open_NoStaleWrite_SendsEachReadOnce(int tab, int searches, int mines, int histories)
        {
            Invoke("OnEnable");
            OpenWindow(tab);

            Assert.That(Count(EMarketAction.Search), Is.EqualTo(searches));
            Assert.That(Count(EMarketAction.Mine), Is.EqualTo(mines));
            Assert.That(Count(EMarketAction.History), Is.EqualTo(histories));
        }

        [Test]
        public void Stale_HistoryRejectedByPendingCommit_RetriesOnlyThatReadAndResolves()
        {
            BeginStaleOnTab(TAB_HISTORY);
            MarketRequestMessage mineA    = Last(EMarketAction.Mine);
            MarketRequestMessage historyA = Last(EMarketAction.History);

            TownMarketClient.OnResult(Result(historyA.RequestId, EMarketAction.History, ERejectReason.PendingCommit));
            Assert.That(TownMarketClient.HasStaleWrite, Is.True, "거절로 확인을 끝내지 않는다");
            Assert.That(Count(EMarketAction.History), Is.EqualTo(1), "다시 받기는 간격을 둔다");

            SetField("_staleRetryAt", 0f);
            Invoke("Update");

            Assert.That(Count(EMarketAction.History), Is.EqualTo(2), "거절된 읽기만 다시 보낸다");
            Assert.That(Count(EMarketAction.Mine), Is.EqualTo(1), "응답을 기다리는 읽기는 다시 보내지 않는다");
            MarketRequestMessage historyB = Last(EMarketAction.History);
            Assert.That(historyB.RequestId, Is.Not.EqualTo(historyA.RequestId));

            TownMarketClient.OnResult(Result(mineA.RequestId, EMarketAction.Mine, ERejectReason.None));
            Assert.That(TownMarketClient.HasStaleWrite, Is.True);
            TownMarketClient.OnResult(Result(historyB.RequestId, EMarketAction.History, ERejectReason.None));
            Assert.That(TownMarketClient.HasStaleWrite, Is.False, "두 읽기가 정상으로 오면 표식을 내린다");
        }

        [Test]
        public void Stale_MineRejected_ThenRecoversAfterRetry()
        {
            BeginStaleOnTab(TAB_LISTINGS);
            MarketRequestMessage mineA    = Last(EMarketAction.Mine);
            MarketRequestMessage historyA = Last(EMarketAction.History);

            TownMarketClient.OnResult(Result(mineA.RequestId, EMarketAction.Mine, ERejectReason.BackendUnavailable));
            TownMarketClient.OnResult(Result(historyA.RequestId, EMarketAction.History, ERejectReason.None));
            Assert.That(TownMarketClient.HasStaleWrite, Is.True, "내 등록을 못 받았으면 끝내지 않는다");

            SetField("_staleRetryAt", 0f);
            Invoke("Update");
            Assert.That(Count(EMarketAction.Mine), Is.EqualTo(2));
            Assert.That(Count(EMarketAction.History), Is.EqualTo(1), "이미 받은 내역은 다시 받지 않는다");

            TownMarketClient.OnResult(Result(Last(EMarketAction.Mine).RequestId, EMarketAction.Mine, ERejectReason.None));
            Assert.That(TownMarketClient.HasStaleWrite, Is.False);
        }

        [Test]
        public void Stale_NoResponse_AbandonsAndRetriesWithoutAcceptingLateOldResponse()
        {
            BeginStaleOnTab(TAB_HISTORY);
            MarketRequestMessage mineA    = Last(EMarketAction.Mine);
            MarketRequestMessage historyA = Last(EMarketAction.History);

            SetField("_staleDeadline", -1f);
            Invoke("Update");
            SetField("_staleRetryAt", 0f);
            Invoke("Update");

            Assert.That(Count(EMarketAction.Mine), Is.EqualTo(2));
            Assert.That(Count(EMarketAction.History), Is.EqualTo(2));

            TownMarketClient.OnResult(Result(mineA.RequestId, EMarketAction.Mine, ERejectReason.None));
            TownMarketClient.OnResult(Result(historyA.RequestId, EMarketAction.History, ERejectReason.None));
            Assert.That(TownMarketClient.HasStaleWrite, Is.True, "버린 요청의 늦은 응답으로 확인을 끝내지 않는다");

            TownMarketClient.OnResult(Result(Last(EMarketAction.Mine).RequestId, EMarketAction.Mine, ERejectReason.None));
            TownMarketClient.OnResult(Result(Last(EMarketAction.History).RequestId, EMarketAction.History, ERejectReason.None));
            Assert.That(TownMarketClient.HasStaleWrite, Is.False);
        }

        [Test]
        public void Stale_OtherTabOpenedMeanwhile_DoesNotOverwriteOutstandingReads()
        {
            BeginStaleOnTab(TAB_BUY);
            MarketRequestMessage mine    = Last(EMarketAction.Mine);
            MarketRequestMessage history = Last(EMarketAction.History);

            _view.ShowTab(TAB_LISTINGS);
            Invoke("OnTabChanged", TAB_LISTINGS);
            _view.ShowTab(TAB_HISTORY);
            Invoke("OnTabChanged", TAB_HISTORY);

            Assert.That(Count(EMarketAction.Mine), Is.EqualTo(1), "확인용 읽기가 나가 있으면 같은 읽기를 겹쳐 보내지 않는다");
            Assert.That(Count(EMarketAction.History), Is.EqualTo(1));

            TownMarketClient.OnResult(Result(mine.RequestId, EMarketAction.Mine, ERejectReason.None));
            TownMarketClient.OnResult(Result(history.RequestId, EMarketAction.History, ERejectReason.None));
            Assert.That(TownMarketClient.HasStaleWrite, Is.False, "먼저 보낸 응답이 그대로 받아들여진다");

            Invoke("OnTabChanged", TAB_LISTINGS);
            Assert.That(Count(EMarketAction.Mine), Is.EqualTo(2), "확인이 끝난 뒤에는 탭을 열 때마다 다시 받는다");
        }

        [Test]
        public void Open_WriteCompletedWhileDisabled_DoesNotSendMineTwice()
        {
            Invoke("OnEnable");
            OpenWindow(TAB_LISTINGS);
            Invoke("OnDisable");
            Guid id = TownMarketClient.Buy("L1", 1, 100L);
            TownMarketClient.OnResult(Result(id, EMarketAction.Buy, ERejectReason.None));
            _sent.Clear();

            Invoke("OnEnable");
            Invoke("Update");

            Assert.That(Count(EMarketAction.Mine), Is.EqualTo(1), "끝난 쓰기 뒤 다시 받기와 열림 읽기가 겹치지 않는다");
        }

        /****************************************
        *       Tests: reconfirm without popup
        ****************************************/

        [Test]
        public void Reconfirm_WithoutPopupHub_NeverRetriesOnItsOwn()
        {
            Guid id = DriveToReconfirm();
            int before = CountWrites(id);

            Invoke("OnEnable");
            OpenWindow(TAB_BUY);
            for (int i = 0; i < 20; ++i)
            {
                Invoke("Update");
            }

            Assert.That(CountWrites(id), Is.EqualTo(before), "승인 없이 같은 요청을 다시 보내지 않는다");
            Assert.That(TownMarketClient.WriteState, Is.EqualTo(EMarketWriteState.Unknown));
            Assert.That(TownMarketClient.NeedsWriteReconfirm, Is.True);
            Assert.That(_view.LastStatus, Does.Contain("자동으로 다시 보내지 않습니다"), "결과 불명임을 안내한다");

            object blocked = InvokeResult("CanBeginWrite");
            Assert.That(blocked, Is.EqualTo(false), "새 쓰기는 막는다");
            Assert.That(CountWrites(id), Is.EqualTo(before), "쓰기 시도도 이전 요청을 대신 보내지 않는다");
        }

        [Test]
        public void Reconfirm_ApprovedWhileStillTracked_ResendsSameIdOnce()
        {
            Guid id = DriveToReconfirm();
            Invoke("OnEnable");
            int before = CountWrites(id);

            Invoke("OnReconfirmApproved", id);

            Assert.That(CountWrites(id), Is.EqualTo(before + 1), "같은 ID로 한 번 다시 확인한다");
            Assert.That(TownMarketClient.PendingWriteId, Is.EqualTo(id));
            Assert.That(TownMarketClient.WriteState, Is.EqualTo(EMarketWriteState.Pending));
        }

        [Test]
        public void Reconfirm_ApprovedAfterConnectionChanged_SendsNothingAndGuides()
        {
            Guid id = DriveToReconfirm();
            Invoke("OnEnable");
            TownMarketClient.ConnectionReplaced();
            int before = _sent.Count;

            Invoke("OnReconfirmApproved", id);

            Assert.That(_sent.Count, Is.EqualTo(before), "이전 연결의 원문을 새 연결로 보내지 않는다");
            Assert.That(TownMarketClient.WriteState, Is.EqualTo(EMarketWriteState.None));
            Assert.That(_view.LastStatus, Does.Contain("더 이상 다시 확인할 수 없습니다"));
        }

        [Test]
        public void Reconfirm_NotApproved_KeepsUnknownAndStaysReachable()
        {
            Guid id = DriveToReconfirm();
            Invoke("OnEnable");
            OpenWindow(TAB_BUY);
            int before = _sent.Count;

            Invoke("Update");
            Invoke("Update");

            Assert.That(_sent.Count, Is.EqualTo(before));
            Assert.That(TownMarketClient.NeedsWriteReconfirm, Is.True, "취소해도 결과 불명은 남아 다시 고를 수 있다");
            Assert.That(TownMarketClient.PendingWriteId, Is.EqualTo(id));
            Assert.That(TownMarketClient.RetryWrite(id), Is.True, "나중에 같은 ID로 다시 확인할 수 있다");
        }

        /****************************************
        *       Tests: listings detail ordering
        ****************************************/

        // 상세 응답을 기다리는 사이 다른 묶음을 눌러도 요청을 덮어쓰지 않는다. 먼저 온 응답은 쓰지 않고 마지막에 고른 품목을 이어서 받는다
        [Test]
        public void Listings_SwitchGroupWhilePending_DefersAndFollowsLatestSelection()
        {
            OpenWithGroups("A", "B");

            Invoke("OnGroupSelected", 0);
            MarketRequestMessage requestA = Last(EMarketAction.Listings);
            Invoke("OnGroupSelected", 1);

            Assert.That(Count(EMarketAction.Listings), Is.EqualTo(1), "진행 중에는 새 상세 읽기를 겹쳐 보내지 않는다");

            TownMarketClient.OnResult(ListingsResult(requestA.RequestId, "A"));
            Assert.That(Listings().Length, Is.EqualTo(0), "고르지 않은 품목의 응답은 화면에 쓰지 않는다");
            Assert.That(Count(EMarketAction.Listings), Is.EqualTo(2), "마지막에 고른 품목을 이어서 받는다");
            MarketRequestMessage requestB = Last(EMarketAction.Listings);
            Assert.That(requestB.DefinitionId, Is.EqualTo("B"));

            TownMarketClient.OnResult(ListingsResult(requestB.RequestId, "B"));
            Assert.That(Listings().Length, Is.EqualTo(1));
            Assert.That(Listings()[0].DefinitionId, Is.EqualTo("B"));
        }

        [Test]
        public void Listings_ClickWithoutPending_SendsImmediately()
        {
            OpenWithGroups("A", "B");

            Invoke("OnGroupSelected", 1);

            Assert.That(Count(EMarketAction.Listings), Is.EqualTo(1));
            Assert.That(Last(EMarketAction.Listings).DefinitionId, Is.EqualTo("B"));
        }

        // 새 검색 응답을 기다리는 동안 보이는 묶음은 곧 바뀌므로 눌러도 상세를 열지 않는다
        [Test]
        public void Listings_GroupClickWhileSearchPending_IsIgnoredUntilSearchResult()
        {
            OpenWithGroups("A");
            Invoke("OnPageDelta", 1);
            MarketRequestMessage search = Last(EMarketAction.Search);

            Invoke("OnGroupSelected", 0);
            Assert.That(Count(EMarketAction.Listings), Is.EqualTo(0));

            TownMarketClient.OnResult(GroupsResult(search.RequestId, "C"));
            Invoke("OnGroupSelected", 0);
            Assert.That(Count(EMarketAction.Listings), Is.EqualTo(1));
            Assert.That(Last(EMarketAction.Listings).DefinitionId, Is.EqualTo("C"));
        }

        // 상세를 기다리는 사이 검색 결과로 목록이 바뀌면 늦게 온 상세 응답은 새 목록 위에 쓰지 않고 이어 받지도 않는다
        [Test]
        public void Listings_LateResponseAfterNewSearch_IsDropped()
        {
            OpenWithGroups("A");
            Invoke("OnGroupSelected", 0);
            MarketRequestMessage requestA = Last(EMarketAction.Listings);

            Invoke("OnPageDelta", 1);
            TownMarketClient.OnResult(GroupsResult(Last(EMarketAction.Search).RequestId, "C"));
            TownMarketClient.OnResult(ListingsResult(requestA.RequestId, "A"));

            Assert.That(Listings().Length, Is.EqualTo(0), "새 목록 위에 이전 상세를 쓰지 않는다");
            Assert.That(Count(EMarketAction.Listings), Is.EqualTo(1), "고른 품목이 없으니 이어 받지 않는다");
        }

        // 응답이 영영 오지 않아도 일정 시간 뒤에는 다시 누를 수 있다
        [Test]
        public void Listings_NoResponse_AllowsNewRequestAfterTimeout()
        {
            OpenWithGroups("A", "B");
            Invoke("OnGroupSelected", 0);
            SetField("_listingsSentAt", -1000f);

            Invoke("OnGroupSelected", 1);

            Assert.That(Count(EMarketAction.Listings), Is.EqualTo(2));
            Assert.That(Last(EMarketAction.Listings).DefinitionId, Is.EqualTo("B"));
        }

        // 묶음을 누르자마자 상세 요청이 요청 한도로 거절되어도 상세를 포기하지 않고 잠시 뒤 다시 받아 채운다
        [Test]
        public void Listings_RateLimited_RetriesAfterDelayAndFillsDetail()
        {
            OpenWithGroups("item_herb");

            Invoke("OnGroupSelected", 0);
            MarketRequestMessage first = Last(EMarketAction.Listings);
            Assert.That(_view.LastStatus, Is.EqualTo("불러오는 중입니다"));

            TownMarketClient.OnResult(Result(first.RequestId, EMarketAction.Listings, ERejectReason.RateLimited));
            Assert.That(Count(EMarketAction.Listings), Is.EqualTo(1), "거절 직후 바로 다시 보내지 않는다(같은 1초 창이라 또 막힌다)");
            Assert.That(_view.LastStatus, Is.EqualTo("불러오는 중입니다"), "요청 한도 안내로 상세를 포기하지 않는다");

            Invoke("TickReadRetries", Time.unscaledTime + 10f);
            Assert.That(Count(EMarketAction.Listings), Is.EqualTo(2));
            MarketRequestMessage retry = Last(EMarketAction.Listings);
            Assert.That(retry.DefinitionId, Is.EqualTo("item_herb"));

            TownMarketClient.OnResult(ListingsResult(retry.RequestId, "item_herb"));
            Assert.That(Listings().Length, Is.EqualTo(1), "다시 받은 상세를 채운다");
            Assert.That(_view.LastStatus, Is.Empty, "읽기 대기 안내를 지운다");
        }

        [Test]
        public void Listings_RateLimitedBeyondRetries_ShowsReason()
        {
            OpenWithGroups("A");
            Invoke("OnGroupSelected", 0);

            for (int i = 0; i <= 2; ++i)
            {
                TownMarketClient.OnResult(Result(Last(EMarketAction.Listings).RequestId, EMarketAction.Listings, ERejectReason.RateLimited));
                Invoke("TickReadRetries", Time.unscaledTime + 10f);
            }

            Assert.That(Count(EMarketAction.Listings), Is.EqualTo(3), "처음 한 번과 다시 받기 두 번");
            Assert.That(_view.LastStatus, Is.EqualTo(RejectText.Describe(ERejectReason.RateLimited)));
        }

        // 이전 읽기 실패 안내가 남아 있어도 상세를 받으면 지운다
        [Test]
        public void Listings_Accepted_ClearsEarlierReadError()
        {
            OpenWithGroups("A");
            Guid mine = (Guid)GetField("_mineId");
            TownMarketClient.OnResult(Result(mine, EMarketAction.Mine, ERejectReason.RateLimited));
            Assert.That(_view.LastStatus, Is.EqualTo(RejectText.Describe(ERejectReason.RateLimited)));

            Invoke("OnGroupSelected", 0);
            TownMarketClient.OnResult(ListingsResult(Last(EMarketAction.Listings).RequestId, "A"));

            Assert.That(_view.LastStatus, Is.Empty);
        }

        // 같은 품목을 거듭 눌러도 진행 중이거나 방금 받은 상세가 있으면 다시 요청하지 않는다
        [Test]
        public void Listings_SameGroupClickedRepeatedly_SendsOnce()
        {
            OpenWithGroups("A");

            Invoke("OnGroupSelected", 0);
            Invoke("OnGroupSelected", 0);
            TownMarketClient.OnResult(ListingsResult(Last(EMarketAction.Listings).RequestId, "A"));
            Invoke("OnGroupSelected", 0);

            Assert.That(Count(EMarketAction.Listings), Is.EqualTo(1));
            Assert.That(Listings().Length, Is.EqualTo(1));
        }

        [Test]
        public void Listings_SameGroupAfterFreshWindow_RequestsAgain()
        {
            OpenWithGroups("A");
            Invoke("OnGroupSelected", 0);
            TownMarketClient.OnResult(ListingsResult(Last(EMarketAction.Listings).RequestId, "A"));
            SetField("_shownAt", -1000f);

            Invoke("OnGroupSelected", 0);

            Assert.That(Count(EMarketAction.Listings), Is.EqualTo(2));
        }

        // 검색 응답을 기다리는 사이 조건을 바꾸면 겹쳐 보내지 않고(서버가 PendingCommit으로 거절한다) 응답 뒤 새 조건으로 받는다
        [Test]
        public void Search_ChangedWhilePending_SendsLatestAfterResponse()
        {
            OpenWithGroups("A");
            Invoke("OnPageDelta", 1);
            MarketRequestMessage first = Last(EMarketAction.Search);
            Invoke("OnPageDelta", 1);

            Assert.That(Count(EMarketAction.Search), Is.EqualTo(1), "진행 중에는 검색을 겹쳐 보내지 않는다");

            TownMarketClient.OnResult(GroupsResult(first.RequestId, "B"));
            Assert.That(Count(EMarketAction.Search), Is.EqualTo(2));
            Assert.That(Last(EMarketAction.Search).Page, Is.EqualTo(2), "마지막 조건으로 받는다");
            Assert.That(Groups()[0].DefinitionId, Is.EqualTo("A"), "낡은 조건의 응답은 쓰지 않는다");
        }

        // 상세 응답은 조회자를 몰라 Mine이 거짓이다. 받아 둔 내 등록과 같은 등록은 사지 못하게 한다
        [Test]
        public void Listings_OwnListingKnownFromMine_BlocksBuyTarget()
        {
            OpenWithGroups("A");
            Guid mineId = (Guid)GetField("_mineId");
            var own = new[]
            {
                new MarketListingEntry("L-A", "AccountItem", "A", string.Empty, 0, 100L, 1, 1, "Active", 0L, 0L, 0L, 0L, 0L, 0L, false, true)
            };
            TownMarketClient.OnResult(new MarketResultMessage(mineId, EMarketAction.Mine, ERejectReason.None, null, own, null, 1, 0, false,
                0L, 0L, 0, 0L, null, null, 0L, 0L, 0L));

            Invoke("OnGroupSelected", 0);
            TownMarketClient.OnResult(ListingsResult(Last(EMarketAction.Listings).RequestId, "A"));
            Invoke("OnListingSelected", 0);

            Assert.That(_view.LastStatus, Is.EqualTo("내 등록은 살 수 없습니다"));
        }

        /****************************************
        *                Helpers
        ****************************************/

        // 구매 탭을 열어 검색 응답으로 묶음을 채운다
        private void OpenWithGroups(params string[] definitionIds)
        {
            Invoke("OnEnable");
            OpenWindow(TAB_BUY);
            TownMarketClient.OnResult(GroupsResult(Last(EMarketAction.Search).RequestId, definitionIds));
            _sent.Clear();
        }

        private MarketListingEntry[] Listings()
        {
            FieldInfo field = typeof(AuctionBinder).GetField("_listings", INSTANCE);
            Assert.That(field, Is.Not.Null, "AuctionBinder._listings");
            return (MarketListingEntry[])field.GetValue(_binder);
        }

        private static MarketResultMessage GroupsResult(Guid requestId, params string[] definitionIds)
        {
            var groups = new MarketGroupEntry[definitionIds.Length];
            for (int i = 0; i < groups.Length; ++i)
            {
                groups[i] = new MarketGroupEntry(definitionIds[i], 0, 100L, 1L, 1, 0L);
            }

            return new MarketResultMessage(requestId, EMarketAction.Search, ERejectReason.None, groups, null, null, groups.Length, 0,
                false, 0L, 0L, 0, 0L, null, null, 0L, 0L, 0L);
        }

        private static MarketResultMessage ListingsResult(Guid requestId, string definitionId)
        {
            var listings = new[]
            {
                new MarketListingEntry("L-" + definitionId, "AccountItem", definitionId, string.Empty, 0, 100L, 1, 1, "Active", 0L, 0L, 0L,
                    0L, 0L, 0L, false, false)
            };

            return new MarketResultMessage(requestId, EMarketAction.Listings, ERejectReason.None, null, listings, null, 1, 0, false, 0L,
                0L, 0, 0L, null, null, 0L, 0L, 0L);
        }

        // 이전 연결에서 보낸 쓰기를 남긴 채 지정한 탭으로 창을 연다
        private void BeginStaleOnTab(int tab)
        {
            Guid buy = TownMarketClient.Buy("L1", 1, 100L);
            Assert.That(buy, Is.Not.EqualTo(Guid.Empty));
            TownMarketClient.ConnectionReplaced();
            Assert.That(TownMarketClient.HasStaleWrite, Is.True);
            _sent.Clear();

            Invoke("OnEnable");
            OpenWindow(tab);
            Assert.That(Count(EMarketAction.Mine), Is.EqualTo(1));
            Assert.That(Count(EMarketAction.History), Is.EqualTo(1));
        }

        // 자동 재확인을 모두 쓰고 사용자 재확인만 남은 쓰기를 만든다
        private Guid DriveToReconfirm()
        {
            Guid id = TownMarketClient.Buy("L1", 1, 100L);
            Assert.That(id, Is.Not.EqualTo(Guid.Empty));
            float t = Time.unscaledTime;
            for (int i = 0; i < MarketPendingWrite.AUTO_RETRIES + 1; ++i)
            {
                t += MarketPendingWrite.RESPONSE_TIMEOUT_SECONDS;
                TownMarketClient.Tick(t);
                t += MarketPendingWrite.AUTO_RETRY_SECONDS;
                TownMarketClient.Tick(t);
            }

            Assert.That(TownMarketClient.NeedsWriteReconfirm, Is.True);
            return id;
        }

        private void OpenWindow(int tab)
        {
            _view.ShowTab(tab);
            _viewObject.SetActive(true);
            Invoke("Update");
        }

        private int Count(EMarketAction action)
        {
            int count = 0;
            for (int i = 0; i < _sent.Count; ++i)
            {
                if (_sent[i].Action == action)
                {
                    ++count;
                }
            }

            return count;
        }

        private MarketRequestMessage Last(EMarketAction action)
        {
            for (int i = _sent.Count - 1; i >= 0; --i)
            {
                if (_sent[i].Action == action)
                {
                    return _sent[i];
                }
            }

            Assert.Fail("보낸 " + action + " 요청이 없다");
            return default;
        }

        private int CountWrites(Guid id)
        {
            int count = 0;
            for (int i = 0; i < _sent.Count; ++i)
            {
                if (_sent[i].RequestId == id)
                {
                    ++count;
                }
            }

            return count;
        }

        private static MarketResultMessage Result(Guid requestId, EMarketAction action, ERejectReason reason)
        {
            return new MarketResultMessage(requestId, action, reason, null, null, null, 0, 0, false, 0L, 0L, 0, 0L, null, null, 0L, 0L, 0L);
        }

        private MarketGroupEntry[] Groups()
        {
            return (MarketGroupEntry[])GetField("_groups");
        }

        private object GetField(string name)
        {
            FieldInfo field = typeof(AuctionBinder).GetField(name, INSTANCE);
            Assert.That(field, Is.Not.Null, "AuctionBinder." + name);
            return field.GetValue(_binder);
        }

        private void SetField(string name, object value)
        {
            FieldInfo field = typeof(AuctionBinder).GetField(name, INSTANCE);
            Assert.That(field, Is.Not.Null, "AuctionBinder." + name);
            field.SetValue(_binder, value);
        }

        private void Invoke(string method, params object[] args)
        {
            InvokeResult(method, args);
        }

        private object InvokeResult(string method, params object[] args)
        {
            MethodInfo info = typeof(AuctionBinder).GetMethod(method, INSTANCE);
            Assert.That(info, Is.Not.Null, "AuctionBinder." + method);
            return info.Invoke(_binder, args);
        }
    }
}
