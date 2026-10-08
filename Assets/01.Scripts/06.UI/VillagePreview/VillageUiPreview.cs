// System
using System;
using System.Collections;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Systems;

namespace Shinshi.UI
{
    /// <summary>마을 개발 씬의 창과 입력을 연결한다. 예시 조작은 서버 요청이나 재화 변경을 보내지 않는다</summary>
    public sealed class VillageUiPreview : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SELF_NAME       = "나 (미리보기)";
        private const string OTHER_NAME      = "시험용 상대";
        private const string PREVIEW_TEXT    = "UI 미리보기 - 실제 아이템과 재화는 변경되지 않습니다";
        private const int INVENTORY_CAPACITY = 96;
        private const int TRADE_CAPACITY     = 8;
        private const int MAIL_COUNT         = 14;
        private const int COUNTDOWN_SECONDS  = 5;
        private const long PREVIEW_CURRENCY  = 12000;

        [SerializeField] private HotkeyRouter _router = null;
        [SerializeField] private PlayerInteractionPanelOpener _interaction = null;
        [SerializeField] private InventoryView _inventory = null;
        [SerializeField] private AuctionView _auction = null;
        [SerializeField] private VillageAuctionPreview _auctionPreview = null;
        [SerializeField] private MailBoxView _mail = null;
        [SerializeField] private TradeView _trade = null;
        [SerializeField] private GameObject _tradeInventory = null;
        [SerializeField] private CharDetailView _character = null;
        [SerializeField] private SettingsView _settings = null;
        [SerializeField] private SortiePrepView _sortie = null;
        [SerializeField] private GameObject _sortieReadyControl = null;
        [SerializeField] private UiPopupHub _popup = null;

        private readonly bool[] _claimed = new bool[MAIL_COUNT];
        private readonly List<ListRowData> _offer = new List<ListRowData>();
        private GameObject _opened = null;
        private Func<bool> _onClose = null;
        private Coroutine _countdown = null;
        private int _mailPage;
        private int _mailSelected = -1;
        private long _tradeCurrency;
        private bool _locked;
        private bool _accepted;
        private bool _finished;
        private bool _ready;
        private bool _inventoryPrepared;

        private static readonly ListRowData[] G_TradeItems =
        {
            new ListRowData(1, "희귀 검 (예시)", "1"),
            new ListRowData(0, "약초 (예시)", "20"),
            new ListRowData(0, "철광석 (예시)", "10"),
        };

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _onClose = CloseTop;
            _interaction.TradeRequested += OpenTrade;
            _interaction.PartyInviteRequested += InviteParty;
            _interaction.InfoRequested += OpenOtherCharacter;
            _interaction.WhisperRequested += Whisper;
            _mail.RowSelected += SelectMail;
            _mail.PageDeltaRequested += ChangeMailPage;
            _mail.ClaimRequested += ClaimMail;
            _trade.InventoryRowClicked += AddTradeItem;
            _trade.MyLineClicked += RemoveTradeItem;
            _trade.CurrencyEdited += EditTradeCurrency;
            _trade.LockToggleRequested += ToggleTradeLock;
            _trade.AcceptRequested += AcceptTrade;
            _trade.CancelRequested += CloseCurrent;
            _sortie.ReadyClicked += ToggleReady;
            _sortie.StartClicked += PreviewSortie;
            _sortie.LeaveClicked += CloseCurrent;
            _sortie.DifficultyChanged += ChangeDifficulty;
        }

        private void OnEnable()
        {
            _router.Register(EHotkeyAction.Inventory, ToggleInventory);
            _router.Register(EHotkeyAction.AuctionHouse, ToggleAuction);
            _router.Register(EHotkeyAction.MailBox, ToggleMail);
            _router.Register(EHotkeyAction.CharacterInfo, ToggleCharacter);
            _router.Register(EHotkeyAction.SortiePrep, ToggleSortie);
        }

        private void OnDisable()
        {
            StopCountdown();
            _router.Unregister(EHotkeyAction.Inventory, ToggleInventory);
            _router.Unregister(EHotkeyAction.AuctionHouse, ToggleAuction);
            _router.Unregister(EHotkeyAction.MailBox, ToggleMail);
            _router.Unregister(EHotkeyAction.CharacterInfo, ToggleCharacter);
            _router.Unregister(EHotkeyAction.SortiePrep, ToggleSortie);
            _router.Unregister(EHotkeyAction.CloseWindow, _onClose);
        }

        private void OnDestroy()
        {
            if (_interaction != null)
            {
                _interaction.TradeRequested -= OpenTrade;
                _interaction.PartyInviteRequested -= InviteParty;
                _interaction.InfoRequested -= OpenOtherCharacter;
                _interaction.WhisperRequested -= Whisper;
            }
            if (_mail != null)
            {
                _mail.RowSelected -= SelectMail;
                _mail.PageDeltaRequested -= ChangeMailPage;
                _mail.ClaimRequested -= ClaimMail;
            }
            if (_trade != null)
            {
                _trade.InventoryRowClicked -= AddTradeItem;
                _trade.MyLineClicked -= RemoveTradeItem;
                _trade.CurrencyEdited -= EditTradeCurrency;
                _trade.LockToggleRequested -= ToggleTradeLock;
                _trade.AcceptRequested -= AcceptTrade;
                _trade.CancelRequested -= CloseCurrent;
            }
            if (_sortie != null)
            {
                _sortie.ReadyClicked -= ToggleReady;
                _sortie.StartClicked -= PreviewSortie;
                _sortie.LeaveClicked -= CloseCurrent;
                _sortie.DifficultyChanged -= ChangeDifficulty;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void Open(GameObject window)
        {
            CloseCurrent();
            _interaction.ClosePanel();
            _opened = window;
            window.SetActive(true);
            _router.Register(EHotkeyAction.CloseWindow, _onClose);
        }

        public void CloseCurrent()
        {
            StopCountdown();
            _popup.CloseAll();
            _router.Unregister(EHotkeyAction.CloseWindow, _onClose);
            _tradeInventory.SetActive(false);
            if (_character.IsOpen)
            {
                _character.SetOpen(false);
            }
            if (_opened != null)
            {
                _opened.SetActive(false);
                _opened = null;
            }
        }

        private bool CloseTop()
        {
            foreach (ConfirmPopupView view in transform.GetComponentsInChildren<ConfirmPopupView>(true))
            {
                if (view.gameObject.activeSelf)
                {
                    _popup.CloseAll();
                    return true;
                }
            }
            foreach (AlertPopupView view in transform.GetComponentsInChildren<AlertPopupView>(true))
            {
                if (view.gameObject.activeSelf)
                {
                    _popup.CloseAll();
                    return true;
                }
            }
            bool hadWindow = (_opened != null) && (_opened.activeSelf);
            CloseCurrent();
            _router.Unregister(EHotkeyAction.CloseWindow, _onClose);
            return hadWindow;
        }

        private bool ToggleInventory() { Toggle(_inventory.gameObject, OpenInventory); return true; }
        private bool ToggleAuction() { Toggle(_auction.gameObject, OpenAuction); return true; }
        private bool ToggleMail() { Toggle(_mail.gameObject, OpenMail); return true; }
        private bool ToggleCharacter() { Toggle(_character.gameObject, OpenCharacter); return true; }
        private bool ToggleSortie() { Toggle(_sortie.gameObject, OpenSortie); return true; }

        private void Toggle(GameObject window, Action open)
        {
            if (window.activeSelf)
            {
                CloseCurrent();
            }
            else
            {
                open();
            }
        }

        public void OpenInventory()
        {
            Open(_inventory.gameObject);
            if (_inventoryPrepared)
            {
                return;
            }
            _inventoryPrepared = true;
            var items = new[]
            {
                new InventoryItemData(new ItemSlotData("equip_weapon", EItemGrade.Rare, 1, "희귀 검", PREVIEW_TEXT), EInventoryCategory.Equipment, 1),
                new InventoryItemData(new ItemSlotData("item_potion_small", EItemGrade.Common, 12, "회복 물약", PREVIEW_TEXT), EInventoryCategory.Consumable, 2),
                new InventoryItemData(new ItemSlotData("item_herb", EItemGrade.Common, 20, "약초", PREVIEW_TEXT), EInventoryCategory.Material, 3),
            };
            _inventory.SetItems(items, INVENTORY_CAPACITY);
            _inventory.SetCurrency(PREVIEW_CURRENCY, 5000);
        }

        public void OpenAuction() { Open(_auction.gameObject); _auctionPreview.Prepare(); }

        public void OpenSettings() { Open(_settings.gameObject); }

        public void OpenCharacter() { OpenOtherCharacter(SELF_NAME); }

        private void OpenOtherCharacter(string name)
        {
            Open(_character.gameObject);
            _character.SetOpen(true);
            _character.SetIdentity(name, "무사 (미리보기)");
            _character.SetStats("100 (예시)", "1,000", "50", "5%", "100", "5");
            _character.SetSlot(0, "희귀 검 (예시)", "+0");
            for (int i = 0; i < 3; ++i)
            {
                _character.SetTraining(i, "0 (예시)");
            }
            _character.SetTalismanSummary("장착 부적 없음 (예시)");
        }

        public void OpenMail()
        {
            Open(_mail.gameObject);
            _mailPage = 0;
            ShowMailPage();
        }

        private void ShowMailPage()
        {
            int capacity = Mathf.Max(1, _mail.RowCapacity);
            var rows = new List<ListRowData>();
            int first = _mailPage * capacity;
            for (int i = first; i < Mathf.Min(MAIL_COUNT, first + capacity); ++i)
            {
                rows.Add(new ListRowData(0, "운영팀", "마을 안내 " + (i + 1), _claimed[i] ? "수령" : "물약 2", "예시", "-"));
            }
            _mailSelected = -1;
            _mail.ShowDetail(null, null, null, null);
            _mail.SetList(rows, _mailPage, Mathf.CeilToInt((float)MAIL_COUNT / capacity));
            _mail.SetStatus(PREVIEW_TEXT);
        }

        private void ChangeMailPage(int delta)
        {
            int last = (MAIL_COUNT - 1) / Mathf.Max(1, _mail.RowCapacity);
            _mailPage = Mathf.Clamp(_mailPage + delta, 0, last);
            ShowMailPage();
        }

        private void SelectMail(int index)
        {
            _mailSelected = (_mailPage * _mail.RowCapacity) + index;
            if ((_mailSelected < 0) || (_mailSelected >= MAIL_COUNT))
            {
                return;
            }
            _mail.SelectRow(index);
            _mail.ShowDetail("마을 안내 우편 " + (_mailSelected + 1), "운영팀 (예시)", "UI 미리보기",
                "신시에 오신 것을 환영합니다.\n목록 선택, 페이지 이동과 보상 받기를 시험할 수 있습니다.\n\n" + PREVIEW_TEXT);
            _mail.SetClaimInteractable(!_claimed[_mailSelected]);
        }

        private void ClaimMail()
        {
            if ((_mailSelected < 0) || (_mailSelected >= MAIL_COUNT) || (_claimed[_mailSelected]))
            {
                return;
            }
            _claimed[_mailSelected] = true;
            int selected = _mailSelected;
            ShowMailPage();
            SelectMail(selected % _mail.RowCapacity);
            _mail.SetStatus("미리보기 보상을 받았습니다. 실제 지급은 서버 연결 후 가능합니다");
        }

        public void OpenTradeMenu() { OpenTrade(OTHER_NAME); }

        private void OpenTrade(string name)
        {
            Open(_trade.gameObject);
            _tradeInventory.SetActive(true);
            _offer.Clear();
            _tradeCurrency = 0;
            _locked = false;
            _accepted = false;
            _finished = false;
            _trade.SetPartnerName(name + " (미리보기)");
            _trade.SetInventory(G_TradeItems, PREVIEW_CURRENCY);
            _trade.SetPartnerOffer(new[] { new ListRowData(0, "철광석 (예시)", "3") }, 500);
            RenderTrade();
        }

        private void AddTradeItem(int index)
        {
            if ((_locked) || (_finished) || (index < 0) || (index >= G_TradeItems.Length) || (_offer.Count >= TRADE_CAPACITY))
            {
                return;
            }
            _offer.Add(G_TradeItems[index]);
            RenderTrade();
        }

        private void RemoveTradeItem(int index)
        {
            if ((_locked) || (_finished) || (index < 0) || (index >= _offer.Count))
            {
                return;
            }
            _offer.RemoveAt(index);
            RenderTrade();
        }

        private void EditTradeCurrency(long value)
        {
            if ((_locked) || (_finished))
            {
                return;
            }
            _tradeCurrency = Math.Max(0L, Math.Min(PREVIEW_CURRENCY, value));
            RenderTrade();
        }

        private void ToggleTradeLock()
        {
            StopCountdown();
            _locked = !_locked;
            _accepted = false;
            RenderTrade();
            if (_locked)
            {
                _countdown = StartCoroutine(TradeCountdown());
            }
        }

        private IEnumerator TradeCountdown()
        {
            for (int seconds = COUNTDOWN_SECONDS; seconds > 0; --seconds)
            {
                _trade.SetCountdown(seconds.ToString());
                yield return new WaitForSecondsRealtime(1f);
            }
            _countdown = null;
            _accepted = true;
            RenderTrade();
        }

        private void StopCountdown()
        {
            if (_countdown != null)
            {
                StopCoroutine(_countdown);
                _countdown = null;
            }
        }

        private void RenderTrade()
        {
            _trade.SetMyOffer(_offer, _tradeCurrency);
            _trade.SetMarks(_locked, _locked, _finished, _finished);
            _trade.SetButtons(new TradeButtonStates((!_locked) && (!_finished), !_finished, _locked,
                (_accepted) && (!_finished), !_finished, false), _finished);
            _trade.SetCountdown(string.Empty);
            _trade.SetStatus(_finished ? "미리보기 완료 (실제 거래 없음)" : "UI 미리보기 (실제 거래 없음)");
        }

        private void AcceptTrade()
        {
            if ((!_accepted) || (_finished))
            {
                return;
            }
            _popup.ShowConfirm(EPopupSize.Medium, "예시 거래의 수락 화면을 확인할까요?\n" + PREVIEW_TEXT, accepted =>
            {
                if ((accepted) && (_trade.IsOpen))
                {
                    _finished = true;
                    RenderTrade();
                }
            });
        }

        public void OpenSortie()
        {
            Open(_sortie.gameObject);
            _ready = false;
            _sortie.SetDungeon("출정 조작 미리보기", "예시 파티의 난이도와 준비 상태를 확인합니다", null);
            _sortie.SetLocalState(true, _ready);
            // 개발 씬에서는 한 창에서 방장과 파티원의 버튼을 함께 시험한다
            _sortieReadyControl.SetActive(true);
            RenderSortieMembers();
            _sortie.SetStartEnabled(false);
            _sortie.SetNotice(PREVIEW_TEXT);
        }

        private void ToggleReady()
        {
            _ready = !_ready;
            _sortie.SetLocalState(true, _ready);
            _sortieReadyControl.SetActive(true);
            RenderSortieMembers();
            _sortie.SetStartEnabled(_ready);
        }

        private void RenderSortieMembers()
        {
            _sortie.SetMembers(new[]
            {
                new SortieMemberData(SELF_NAME, EPlayerClass.Warrior, true, true, true, true, 1, 100),
                new SortieMemberData(OTHER_NAME, EPlayerClass.Sorcerer, false, _ready, true, true, 1, 100),
            });
        }

        private void ChangeDifficulty(EBossDifficulty difficulty) { _sortie.SetNotice(PREVIEW_TEXT); }
        private void PreviewSortie() { _popup.ShowAlert(EPopupSize.Medium, "출정 버튼 조작을 확인했습니다. 실제 출정은 서버 연결 후 가능합니다"); }

        private void InviteParty(string name)
        {
            _popup.ShowConfirm(EPopupSize.Medium, name + "에게 파티 초대를 보내는 화면을 확인할까요?", accepted =>
            {
                if (accepted)
                {
                    OpenSortie();
                }
            });
            _router.Register(EHotkeyAction.CloseWindow, _onClose);
        }

        private void Whisper(string name)
        {
            _popup.ShowAlert(EPopupSize.Medium, name + " 귓속말은 서버 채팅 연결 후 사용할 수 있습니다");
            _router.Register(EHotkeyAction.CloseWindow, _onClose);
        }

        public void OpenCharacterSelect()
        {
            _popup.ShowAlert(EPopupSize.Medium, "이 씬은 UI 조작 미리보기입니다.\n캐릭터 선택 화면은 Dev_CharSelect 씬에서 확인할 수 있습니다");
            _router.Register(EHotkeyAction.CloseWindow, _onClose);
        }

        public void OpenExitConfirm()
        {
            _popup.ShowConfirm(EPopupSize.Medium, "UI 조작 미리보기를 종료할까요?", accepted =>
            {
                if (!accepted)
                {
                    return;
                }
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                CloseCurrent();
#endif
            });
            _router.Register(EHotkeyAction.CloseWindow, _onClose);
        }
    }
}
