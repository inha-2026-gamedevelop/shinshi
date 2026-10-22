// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    // 경매장 화면(UI 담당)은 이 창구의 이벤트만 구독하고 요청 메서드로 보낸다. 가격, 수수료, 소유 판정은 마을 서버와 API가 한다.
    // 요청 메서드는 새 RequestId를 만들어 돌려준다(쓰기에서는 API operationId다). 연결되지 않았으면 Guid.Empty를 돌려주고 아무것도 보내지 않는다.
    // 쓰기는 미확정 요청이 하나도 없을 때만 보낸다(서버가 연결당 쓰기 하나만 받는다). 결과를 모르면(BackendUnavailable, 응답 시간 초과)
    // 같은 ID와 원문으로만 다시 확인하고, 창이 닫혀 있어도 이 창구가 상태를 들고 있어 다시 열 때 이어 받는다.
    // 연결이 바뀌면(다른 계정일 수 있다) 이전 원문을 다시 보내지 않고 결과 불명 표식만 남긴다
    /// <summary> 클라이언트의 유저 경매장 요청 송신과 결과 수신 창구 </summary>
    public static class TownMarketClient
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly MarketPendingWrite _pending = new MarketPendingWrite();

        private static MarketResultMessage _completed;
        private static bool                _hasCompleted;

        // 테스트가 Mirror 연결 없이 송신을 가로챈다. 지정하면 연결된 것으로 본다
        internal static Action<MarketRequestMessage> TestSink;

        /****************************************
        *              Properties
        ****************************************/

        public static EMarketWriteState WriteState => _pending.State;

        public static Guid PendingWriteId => _pending.RequestId;

        public static EMarketAction PendingWriteAction => _pending.Action;

        /// <summary> 결과 불명이고 자동 재확인이 끝났다, 사용자가 RetryWrite로 같은 ID 재확인을 요청해야 한다 </summary>
        public static bool NeedsWriteReconfirm => _pending.NeedsReconfirm;

        public static bool HasStaleWrite => _pending.HasStale;

        public static EMarketAction StaleWriteAction => _pending.StaleAction;

        /// <summary> 창이 닫혀 있는 동안 끝난 쓰기의 결과가 아직 화면에 전달되지 않았다 </summary>
        public static bool HasCompletedWrite => _hasCompleted;

        public static Guid CompletedWriteId => _hasCompleted ? _completed.RequestId : Guid.Empty;

        public static event Action<MarketResultMessage> ResultReceived;
        public static event Action                      WriteStateChanged;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 클라이언트가 시작할 때 한 번 부른다, 이전 연결의 미확정 쓰기는 재전송하지 않고 결과 불명 표식으로만 남긴다 </summary>
        public static void RegisterHandlers()
        {
            ConnectionReplaced();
            NetworkClient.ReplaceHandler<MarketResultMessage>(OnResult);
        }

        internal static void ConnectionReplaced()
        {
            _pending.ConnectionReplaced();
            _completed    = default;
            _hasCompleted = false;
            WriteStateChanged?.Invoke();
        }

        internal static void ResetForTest()
        {
            _pending.Clear();
            _pending.ResolveStale();
            _completed    = default;
            _hasCompleted = false;
            TestSink      = null;
        }

        /// <summary> category, classId, grade는 MarketRequestMessage.NONE이면 조건 없음, definitionIds는 이름 검색 결과(없으면 null) </summary>
        public static Guid Search(int category, int classId, int grade, string[] definitionIds, int sort, int order, int page)
        {
            return Send(new MarketRequestMessage(Guid.NewGuid(), EMarketAction.Search, category, classId, grade, definitionIds, sort,
                order, page, null, null, null, null, 0, 0, null));
        }

        public static Guid QueryListings(string definitionId, int page)
        {
            return Send(Build(EMarketAction.Listings, page, definitionId, null, null, null, 0, 0, null));
        }

        public static Guid RequestMine()
        {
            return Send(Build(EMarketAction.Mine, 0, null, null, null, null, 0, 0, null));
        }

        public static Guid RequestHistory(int page)
        {
            return Send(Build(EMarketAction.History, page, null, null, null, null, 0, 0, null));
        }

        /// <summary> assetKind는 MarketCodes.ASSET_ACCOUNT_ITEM 또는 ASSET_EQUIPMENT, itemId는 장비일 때만. 미확정 쓰기가 있으면 Guid.Empty </summary>
        public static Guid List(string assetKind, string definitionId, string itemId, int quantity, long unitPrice)
        {
            return Send(Build(EMarketAction.List, 0, definitionId, null, assetKind, itemId, quantity, unitPrice, null));
        }

        /// <summary> expectedUnitPrice는 화면에서 확인한 개당 가격, 달라졌으면 서버가 MarketPriceChanged로 거절한다 </summary>
        public static Guid Buy(string listingId, int quantity, long expectedUnitPrice)
        {
            return Send(Build(EMarketAction.Buy, 0, null, listingId, null, null, quantity, expectedUnitPrice, null));
        }

        public static Guid Cancel(string listingId)
        {
            return Send(Build(EMarketAction.Cancel, 0, null, listingId, null, null, 0, 0, null));
        }

        /// <summary> 판매 결과를 확인했다고 표시한다 </summary>
        public static Guid Acknowledge(string[] listingIds)
        {
            return Send(Build(EMarketAction.Ack, 0, null, null, null, null, 0, 0, listingIds));
        }

        /// <summary> 결과 불명인 쓰기를 같은 RequestId와 같은 원문으로 다시 확인한다, 서버와 API가 처음 결과를 돌려준다. 다른 ID면 거절한다 </summary>
        public static bool RetryWrite(Guid requestId)
        {
            if ((!IsConnected) || (!_pending.TryReconfirm(requestId, Time.unscaledTime, out MarketRequestMessage request)))
            {
                return false;
            }

            Transmit(request);
            WriteStateChanged?.Invoke();
            return true;
        }

        /// <summary> 매 프레임 부른다, 응답 기한 초과를 결과 불명으로 바꾸고 제한된 자동 재확인 시각이 되면 같은 원문을 다시 보낸다 </summary>
        public static void Tick(float now)
        {
            bool changed = _pending.Tick(now);
            if ((IsConnected) && (_pending.TryTakeAutoRetry(now, out MarketRequestMessage request)))
            {
                Transmit(request);
                changed = true;
            }

            if (changed)
            {
                WriteStateChanged?.Invoke();
            }
        }

        /// <summary> 창이 닫힌 사이 끝난 쓰기 결과를 한 번 꺼낸다 </summary>
        public static bool TryTakeCompletedWrite(out MarketResultMessage result)
        {
            result        = _completed;
            bool has      = _hasCompleted;
            _completed    = default;
            _hasCompleted = false;
            return has;
        }

        /// <summary> 내 등록과 내역을 서버에서 다시 받아 확인할 수 있게 되었을 때 이전 연결의 결과 불명 표식을 내린다 </summary>
        public static void ResolveStaleWrite()
        {
            if (_pending.HasStale)
            {
                _pending.ResolveStale();
                WriteStateChanged?.Invoke();
            }
        }

        private static MarketRequestMessage Build(EMarketAction action, int page, string definitionId, string listingId, string assetKind,
            string itemId, int quantity, long unitPrice, string[] listingIds)
        {
            return new MarketRequestMessage(Guid.NewGuid(), action, MarketRequestMessage.NONE, MarketRequestMessage.NONE,
                MarketRequestMessage.NONE, null, 0, 0, page, definitionId, listingId, assetKind, itemId, quantity, unitPrice, listingIds);
        }

        private static bool IsConnected => (TestSink != null) || (NetworkClient.isConnected);

        private static void Transmit(MarketRequestMessage message)
        {
            if (TestSink != null)
            {
                TestSink(message);
                return;
            }

            NetworkClient.Send(message);
        }

        private static Guid Send(MarketRequestMessage message)
        {
            if (!IsConnected)
            {
                return Guid.Empty;
            }

            if (message.IsWrite)
            {
                if (!_pending.TryBegin(in message, Time.unscaledTime))
                {
                    return Guid.Empty;
                }

                Transmit(message);
                WriteStateChanged?.Invoke();
                return message.RequestId;
            }

            Transmit(message);
            return message.RequestId;
        }

        internal static void OnResult(MarketResultMessage message)
        {
            switch (_pending.Resolve(message.RequestId, message.Reason, Time.unscaledTime))
            {
                case EMarketWriteResolve.Completed:
                    _completed    = message;
                    _hasCompleted = true;
                    WriteStateChanged?.Invoke();
                    break;
                case EMarketWriteResolve.Unknown:
                    WriteStateChanged?.Invoke();
                    break;
            }

            ResultReceived?.Invoke(message);
        }
    }
}
