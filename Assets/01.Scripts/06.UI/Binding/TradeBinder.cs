// System
using System;
using System.Collections.Generic;
using System.Globalization;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 거래 창과 TownTradeClient를 잇는다. 요청 초대 팝업, 제안 편집, 확정, 수락, 취소를 보내고 서버 상태로 창을 채운다 </summary>
    public sealed class TradeBinder : MonoBehaviour
    {
        // 서버 상태가 정본이다. 편집본(TradeOfferDraft)은 서버 상태가 올 때마다 그 값으로 맞춘다.
        // 수락은 확인 팝업을 거친다. 거래가 끝나면 창은 결과 글자를 보이고, 닫기 버튼으로 닫는다.
        // 확정된 거래이면 성장, 생활 스냅숏을 다시 받는다. 카운트다운은 상태를 받은 시각 기준으로 표시만 한다

        /****************************************
        *                Fields
        ****************************************/

        private const int    INITIAL_CAPACITY = 32;
        private const string REQUEST_SENT_TEXT = "거래를 요청했습니다";

        [SerializeField] private TradeView  _view     = null;
        [SerializeField] private UiPopupHub _popupHub = null;

        private readonly TradeOfferDraft     _draft      = new TradeOfferDraft();
        private readonly List<SellableItem>  _sellables  = new List<SellableItem>(INITIAL_CAPACITY);
        private readonly List<ListRowData>   _rows       = new List<ListRowData>(INITIAL_CAPACITY);

        private TradeStateMessage _state;
        private bool              _hasState;
        private double            _countdownEnd;
        private Guid              _inviteTradeId;
        private double            _inviteExpireAt;
        private bool              _inventoryDirty = true;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            TownTradeClient.StateChanged     += OnState;
            TownTradeClient.InviteReceived   += OnInvite;
            TownTradeClient.ResultReceived   += OnResult;
            TownGrowthClient.SnapshotChanged += OnGrowthSnapshot;
            TownLifeClient.SnapshotChanged   += OnLifeSnapshot;
            if (_view != null)
            {
                _view.InventoryRowClicked += OnInventoryRow;
                _view.MyLineClicked       += OnMyLine;
                _view.CurrencyEdited      += OnCurrencyEdited;
                _view.LockToggleRequested += OnLockToggle;
                _view.AcceptRequested     += OnAcceptRequested;
                _view.CancelRequested     += OnCancelRequested;
            }
        }

        private void OnDisable()
        {
            TownTradeClient.StateChanged     -= OnState;
            TownTradeClient.InviteReceived   -= OnInvite;
            TownTradeClient.ResultReceived   -= OnResult;
            TownGrowthClient.SnapshotChanged -= OnGrowthSnapshot;
            TownLifeClient.SnapshotChanged   -= OnLifeSnapshot;
            if (_view != null)
            {
                _view.InventoryRowClicked -= OnInventoryRow;
                _view.MyLineClicked       -= OnMyLine;
                _view.CurrencyEdited      -= OnCurrencyEdited;
                _view.LockToggleRequested -= OnLockToggle;
                _view.AcceptRequested     -= OnAcceptRequested;
                _view.CancelRequested     -= OnCancelRequested;
            }
        }

        private void Update()
        {
            if (_view == null)
            {
                return;
            }

            // 받은 요청이 시간이 지나면 팝업을 닫는다
            if ((_inviteTradeId != Guid.Empty) && (Time.unscaledTimeAsDouble >= _inviteExpireAt))
            {
                _inviteTradeId = Guid.Empty;
                if (_popupHub != null)
                {
                    _popupHub.CloseAll();
                }
            }

            if ((_hasState) && (_state.State == ETradeState.Countdown))
            {
                float remaining = TradeViewRules.RemainingAt(_countdownEnd, Time.unscaledTimeAsDouble);
                _view.SetCountdown(TradeViewRules.CeilSeconds(remaining).ToString(CultureInfo.InvariantCulture));
                _view.SetStatus(TradeViewRules.StatusText(ETradeState.Countdown, _state.Reason, _state.MyLocked, _state.MyAccepted,
                    _state.PartnerAccepted, remaining));
            }

            if ((_inventoryDirty) && (_view.IsOpen))
            {
                RebuildInventory();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnInvite(TradeInviteMessage invite)
        {
            _inviteTradeId  = invite.TradeId;
            _inviteExpireAt = Time.unscaledTimeAsDouble + invite.ExpiresIn;
            if (_popupHub == null)
            {
                return;
            }

            Guid tradeId = invite.TradeId;
            _popupHub.ShowConfirm(EPopupSize.Small, TradeViewRules.InviteText(invite.FromName), accepted =>
            {
                _inviteTradeId = Guid.Empty;
                TownTradeClient.Respond(tradeId, accepted);
            });
        }

        private void OnState(TradeStateMessage state)
        {
            // 받은 요청이 접혔다. 초대 팝업을 닫는다
            if ((state.State == ETradeState.Cancelled) && (state.TradeId == _inviteTradeId))
            {
                _inviteTradeId = Guid.Empty;
                if (_popupHub != null)
                {
                    _popupHub.CloseAll();
                }

                return;
            }

            _state    = state;
            _hasState = true;
            if (_view == null)
            {
                return;
            }

            if (state.State == ETradeState.Requested)
            {
                if (_popupHub != null)
                {
                    _popupHub.ShowToast(REQUEST_SENT_TEXT);
                }

                return;
            }

            if (!_view.IsOpen)
            {
                _view.SetOpen(true);
                _inventoryDirty = true;
                TownGrowthClient.RequestSnapshot();
                TownLifeClient.RequestSnapshot();
            }

            _countdownEnd = TradeViewRules.LocalEnd(Time.unscaledTimeAsDouble, state.CountdownRemaining);
            _draft.SetFrom(state.MyLines, state.MyCurrency);
            Render(in state);
            if (state.State == ETradeState.Done)
            {
                TownGrowthClient.RequestSnapshot();
                TownLifeClient.RequestSnapshot();
                _inventoryDirty = true;
            }
        }

        private void Render(in TradeStateMessage state)
        {
            _view.SetPartnerName(state.PartnerName);
            _view.SetMyOffer(ToRows(state.MyLines), state.MyCurrency);
            _view.SetPartnerOffer(ToRows(state.PartnerLines), state.PartnerCurrency);
            _view.SetMarks(state.MyLocked, state.PartnerLocked, state.MyAccepted, state.PartnerAccepted);
            _view.SetButtons(TradeViewRules.ButtonsFor(state.State, state.MyLocked, state.MyAccepted), state.IsFinished);
            _view.SetStatus(TradeViewRules.StatusText(state.State, state.Reason, state.MyLocked, state.MyAccepted, state.PartnerAccepted,
                state.CountdownRemaining));
            _view.SetCountdown(string.Empty);
        }

        private void OnResult(TradeResultMessage result)
        {
            if ((_view == null) || (result.IsAccepted))
            {
                return;
            }

            // 낡은 값으로 보낸 요청은 서버가 최신 상태를 다시 보낸다. 그 밖의 거절은 사유를 보인다
            string text = RejectText.Describe(result.Reason);
            if (!string.IsNullOrEmpty(text))
            {
                if (_popupHub != null)
                {
                    _popupHub.ShowToast(text);
                }

                if (_hasState)
                {
                    _view.SetStatus(text);
                }
            }

            if (_hasState)
            {
                _draft.SetFrom(_state.MyLines, _state.MyCurrency);
                _view.SetMyOffer(ToRows(_state.MyLines), _state.MyCurrency);
            }
        }

        private void OnInventoryRow(int index)
        {
            if ((index < 0) || (index >= _sellables.Count))
            {
                return;
            }

            SellableItem item = _sellables[index];
            if (_draft.TryAdd(in item, 1))
            {
                SendOffer();
            }
        }

        private void OnMyLine(int index)
        {
            if (_draft.RemoveAt(index))
            {
                SendOffer();
            }
        }

        private void OnCurrencyEdited(long value)
        {
            _draft.SetCurrency(value);
            SendOffer();
        }

        private void SendOffer()
        {
            TownTradeClient.SetOffer(_draft.ToArray(), _draft.Currency);
        }

        private void OnLockToggle()
        {
            if (!_hasState)
            {
                return;
            }

            if (_state.MyLocked)
            {
                TownTradeClient.Unlock();
            }
            else
            {
                TownTradeClient.Lock();
            }
        }

        private void OnAcceptRequested()
        {
            if ((!_hasState) || (_popupHub == null))
            {
                return;
            }

            _popupHub.ShowConfirm(EPopupSize.Small, TradeViewRules.AcceptConfirmText(_state.PartnerName), accepted =>
            {
                if (accepted)
                {
                    TownTradeClient.Accept();
                }
            });
        }

        private void OnCancelRequested()
        {
            if (TownTradeClient.IsActive)
            {
                TownTradeClient.Cancel();
                return;
            }

            _view.SetOpen(false);
        }

        private void OnGrowthSnapshot(GrowthSnapshotMessage snapshot)
        {
            _inventoryDirty = true;
        }

        private void OnLifeSnapshot(LifeSnapshotMessage snapshot)
        {
            _inventoryDirty = true;
        }

        private void RebuildInventory()
        {
            _inventoryDirty = false;
            GrowthSnapshotMessage growth = TownGrowthClient.Snapshot;
            LifeSnapshotMessage   life   = TownLifeClient.Snapshot;
            SellableItemRules.Build(TownGrowthClient.HasSnapshot, in growth, TownLifeClient.HasSnapshot, in life, _sellables);
            _rows.Clear();
            for (int i = 0; i < _sellables.Count; ++i)
            {
                SellableItem item = _sellables[i];
                _rows.Add(new ListRowData(item.Grade, item.DefinitionId, item.MaxQuantity.ToString(CultureInfo.InvariantCulture)));
            }

            long yeopjeon = 0;
            if ((TownGrowthClient.HasSnapshot) && (GameDB.IsInitialized) && (GameDB.Rewards != null))
            {
                TownItemEntry[] items = growth.AccountItems;
                for (int i = 0; i < items.Length; ++i)
                {
                    if (items[i].DefinitionId == GameDB.Rewards.TradeCurrencyItemId)
                    {
                        yeopjeon += Math.Max(0L, items[i].Quantity - items[i].Reserved);
                    }
                }
            }

            _view.SetInventory(_rows, yeopjeon);
        }

        private List<ListRowData> ToRows(TradeOfferLine[] lines)
        {
            var rows = new List<ListRowData>(lines.Length);
            for (int i = 0; i < lines.Length; ++i)
            {
                rows.Add(new ListRowData(0, lines[i].DefinitionId, lines[i].Quantity.ToString(CultureInfo.InvariantCulture)));
            }

            return rows;
        }
    }
}
