// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary> 1:1 거래 창이며 내 제안, 상대 제안, 엽전 입력, 확정, 수락, 취소, 처리 중 표시를 보인다. 네트워크는 모르고 이벤트로 의도만 알린다 </summary>
    public sealed class TradeView : MonoBehaviour
    {
        // 줄 안의 칸 순서: 품목, 수량. 제안 줄을 누르면 내 제안에서는 빼고, 보관함 줄을 누르면 하나 넣는다

        /****************************************
        *                Fields
        ****************************************/

        private const string LOCK_TEXT      = "확정";
        private const string RELEASE_TEXT   = "확정 풀기";
        private const string CANCEL_TEXT    = "취소";
        private const string CLOSE_TEXT     = "닫기";
        private const string CURRENCY_ZERO  = "0";

        [Header("창")]
        [SerializeField] private Button   _closeButton       = null;
        [SerializeField] private TMP_Text _partnerNameLabel  = null;
        [SerializeField] private TMP_Text _statusLabel       = null;
        [SerializeField] private TMP_Text _countdownLabel    = null;
        [SerializeField] private GameObject _busyMark        = null;

        [Header("내 제안")]
        [SerializeField] private ListRowView[]  _myRows        = null;
        [SerializeField] private TMP_InputField _myCurrencyField = null;
        [SerializeField] private GameObject     _myLockedMark  = null;
        [SerializeField] private GameObject     _myAcceptedMark = null;

        [Header("상대 제안")]
        [SerializeField] private ListRowView[] _partnerRows         = null;
        [SerializeField] private TMP_Text      _partnerCurrencyLabel = null;
        [SerializeField] private GameObject    _partnerLockedMark   = null;
        [SerializeField] private GameObject    _partnerAcceptedMark = null;

        [Header("내 보관함")]
        [SerializeField] private ListRowView[] _inventoryRows    = null;
        [SerializeField] private TMP_Text      _yeopjeonLabel    = null;

        [Header("버튼")]
        [SerializeField] private Button   _lockButton       = null;
        [SerializeField] private TMP_Text _lockButtonLabel  = null;
        [SerializeField] private Button   _acceptButton     = null;
        [SerializeField] private Button   _cancelButton     = null;
        [SerializeField] private TMP_Text _cancelButtonLabel = null;

        private bool _canEdit = false;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsOpen => gameObject.activeSelf;

        public event Action<int>  InventoryRowClicked;
        public event Action<int>  MyLineClicked;
        public event Action<long> CurrencyEdited;
        public event Action       LockToggleRequested;
        public event Action       AcceptRequested;
        public event Action       CancelRequested;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            Bind(_closeButton, OnCancelClicked);
            Bind(_lockButton, OnLockClicked);
            Bind(_acceptButton, OnAcceptClicked);
            Bind(_cancelButton, OnCancelClicked);
            BindRows(_inventoryRows, OnInventoryClicked);
            BindRows(_myRows, OnMyRowClicked);
            if (_myCurrencyField != null)
            {
                _myCurrencyField.onEndEdit.AddListener(OnCurrencyEndEdit);
            }
        }

        private void OnDestroy()
        {
            Unbind(_closeButton, OnCancelClicked);
            Unbind(_lockButton, OnLockClicked);
            Unbind(_acceptButton, OnAcceptClicked);
            Unbind(_cancelButton, OnCancelClicked);
            if (_myCurrencyField != null)
            {
                _myCurrencyField.onEndEdit.RemoveListener(OnCurrencyEndEdit);
            }
        }

        private void OnEnable()
        {
            UiSound.PlayWindowTransition(gameObject, true);
        }

        private void OnDisable()
        {
            UiSound.PlayWindowTransition(gameObject, false);
        }

        /****************************************
        *                Methods
        ****************************************/

        public void SetOpen(bool open)
        {
            gameObject.SetActive(open);
        }

        public void SetPartnerName(string name)
        {
            SetText(_partnerNameLabel, name);
        }

        public void SetStatus(string text)
        {
            SetText(_statusLabel, text);
        }

        public void SetCountdown(string text)
        {
            SetText(_countdownLabel, text);
        }

        public void SetMyOffer(IReadOnlyList<ListRowData> rows, long currency)
        {
            ListRowData.Fill(_myRows, rows, -1);
            if (_myCurrencyField != null)
            {
                _myCurrencyField.SetTextWithoutNotify(currency.ToString());
            }
        }

        public void SetPartnerOffer(IReadOnlyList<ListRowData> rows, long currency)
        {
            ListRowData.Fill(_partnerRows, rows, -1);
            SetText(_partnerCurrencyLabel, currency.ToString());
        }

        public void SetInventory(IReadOnlyList<ListRowData> rows, long yeopjeon)
        {
            ListRowData.Fill(_inventoryRows, rows, -1);
            SetText(_yeopjeonLabel, AuctionViewRules.FormatPrice(yeopjeon));
        }

        public void SetMarks(bool myLocked, bool partnerLocked, bool myAccepted, bool partnerAccepted)
        {
            SetActive(_myLockedMark, myLocked);
            SetActive(_partnerLockedMark, partnerLocked);
            SetActive(_myAcceptedMark, myAccepted);
            SetActive(_partnerAcceptedMark, partnerAccepted);
        }

        /// <summary> 단계별 버튼 상태를 반영한다. 거래가 끝났으면 취소 버튼이 닫기 버튼이 된다 </summary>
        public void SetButtons(TradeButtonStates states, bool finished)
        {
            _canEdit = states.CanEdit;
            SetInteractable(_lockButton, states.CanToggleLock);
            SetInteractable(_acceptButton, states.CanAccept);
            SetInteractable(_cancelButton, states.CanCancel || finished);
            SetInteractable(_closeButton, states.CanCancel || finished);
            SetInteractable(_myCurrencyField, states.CanEdit);
            SetText(_lockButtonLabel, states.LockIsRelease ? RELEASE_TEXT : LOCK_TEXT);
            SetText(_cancelButtonLabel, finished ? CLOSE_TEXT : CANCEL_TEXT);
            SetActive(_busyMark, states.IsBusy);
        }

        private static void BindRows(ListRowView[] rows, Action<ListRowView> handler)
        {
            if (rows == null)
            {
                return;
            }

            for (int i = 0; i < rows.Length; ++i)
            {
                if (rows[i] != null)
                {
                    rows[i].Index = i;
                    rows[i].Clicked += handler;
                }
            }
        }

        private void OnInventoryClicked(ListRowView row)
        {
            if (_canEdit)
            {
                InventoryRowClicked?.Invoke(row.Index);
            }
        }

        private void OnMyRowClicked(ListRowView row)
        {
            if (_canEdit)
            {
                MyLineClicked?.Invoke(row.Index);
            }
        }

        private void OnCurrencyEndEdit(string text)
        {
            if (!_canEdit)
            {
                return;
            }

            if (!TradeViewRules.TryParseCurrency(text, out long value))
            {
                _myCurrencyField.SetTextWithoutNotify(CURRENCY_ZERO);
                return;
            }

            CurrencyEdited?.Invoke(value);
        }

        private void OnLockClicked()   { LockToggleRequested?.Invoke(); }
        private void OnAcceptClicked() { AcceptRequested?.Invoke(); }
        private void OnCancelClicked() { CancelRequested?.Invoke(); }

        private static void Bind(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
            {
                button.onClick.AddListener(action);
            }
        }

        private static void Unbind(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
            {
                button.onClick.RemoveListener(action);
            }
        }

        private static void SetText(TMP_Text label, string text)
        {
            if (label != null)
            {
                label.text = text ?? string.Empty;
            }
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null)
            {
                target.SetActive(active);
            }
        }

        private static void SetInteractable(Selectable target, bool interactable)
        {
            if (target != null)
            {
                target.interactable = interactable;
            }
        }
    }
}
