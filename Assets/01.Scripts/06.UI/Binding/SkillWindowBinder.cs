// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Network;
using Shinshi.Systems;

namespace Shinshi.UI
{
    /// <summary> 스킬창과 TownGrowthClient를 잇는다. 열 때 성장 상태를 받고, 부적 등록, 강화, 분해, 조합 저장을 보내고 결과를 반영한다 </summary>
    public sealed class SkillWindowBinder : MonoBehaviour, ICloseGuard
    {
        // 확률, 비용 차감, 조합 유효성은 마을 서버와 API가 판정한다. 여기는 미리보기와 버튼 활성만 정한다.
        // 조합 요청은 전체 교체형이라 창 안의 임시 조합을 모았다가 저장할 때 한 번에 보낸다.
        // 결과를 받지 못한 요청은 비용이 이미 빠졌을 수 있어 같은 번호로 다시 확인하기 전에는 새 쓰기를 보내지 않는다.
        // 이 잠금은 TownGrowthClient가 모든 성장창에 함께 건다

        /****************************************
        *                Fields
        ****************************************/

        private const string TITLE_BASIC      = "기본 공격";
        private const string WAITING_TEXT     = "요청 결과를 기다리고 있습니다";
        private const string UNKNOWN_TEXT     = "결과를 받지 못했습니다. 다시 확인을 눌러 같은 요청의 결과를 확인합니다";
        private const string CHECKING_TEXT    = "결과를 확인하는 중입니다";
        private const string LOADING_TEXT     = "성장 상태를 불러오는 중입니다";
        private const string OFFLINE_TEXT     = "마을 서버에 연결하면 부적을 관리할 수 있습니다";
        private const string DUNGEON_TEXT     = "부적 조합은 신시에서 확인하고 바꿀 수 있습니다";
        private const string SORTIE_TEXT      = "출정 중에는 조합을 바꿀 수 없습니다";
        private const string BASIC_NOTE       = "기본 공격에는 부적을 장착하지 않습니다";
        private const string NO_EFFECT_TEXT   = "이 효과는 현재 기술에 적용되지 않습니다";
        private const string REGISTERED_TEXT  = "등록에 성공했습니다";
        private const string REGISTER_FAIL    = "등록에 실패했습니다. 다음 시도의 성공률이 올라갑니다";
        private const string UPGRADED_TEXT    = "효과 레벨을 올렸습니다";
        private const string DISASSEMBLED     = "부적을 분해했습니다";
        private const string SAVED_TEXT       = "조합을 저장했습니다";
        private const string STALE_TEXT       = "다른 곳에서 조합이 바뀌어 최신 상태를 다시 불러왔습니다";
        private const string FAILED_TEXT      = "요청을 처리하지 못했습니다";
        private const string UNSAVED_TEXT     = "저장하지 않은 조합 변경이 있습니다";
        private const string PREVIEW_TEXT     = "미리보기입니다. 요청을 보내지 않습니다";
        private const string MISMATCH_TEXT    = "서버 콘텐츠와 맞지 않아 부적을 관리할 수 없습니다";
        private const string EQUIP_TEXT       = "장착";
        private const string UNEQUIP_TEXT     = "해제";
        private const int    DEFAULT_QUANTITY = 1;

        [SerializeField] private SkillWindowView    _view   = null;
        [SerializeField] private HotkeyWindowToggle _toggle = null; // 창의 닫기 버튼도 단축키와 같은 닫기 경로를 쓴다
        [SerializeField] private UiIconLibrarySO    _icons  = null;

        private readonly List<TownLoadoutEntry> _draft = new List<TownLoadoutEntry>(Constants.Town.MAX_GROWTH_LOADOUT_SLOTS);

        private SkillCatalogSO        _skills;
        private TalismanCatalogSO     _talismans;
        private SkillDisplayCatalogSO _display;
        private GrowthConfigSO        _growth;
        private PlayerConfigSO        _player;

        private GrowthSnapshotMessage _snapshot;
        private bool                  _hasSnapshot;
        private bool                  _previewMode;
        private EPlayerClass          _previewClass;

        private int           _tab;
        private int           _tier;
        private int           _choice;
        private int           _quantity = DEFAULT_QUANTITY;
        private bool          _wasOpen;
        private bool          _isDirty;
        private bool          _draftChanged;
        private Guid          _pendingOperation = Guid.Empty;
        private EGrowthAction _pendingAction;
        private bool          _isOutcomeUnknown;
        private float         _pendingDeadline;
        private string        _status = string.Empty;
        private Action        _confirmClose;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 저장하지 않은 조합 변경이 있으면 참 </summary>
        public bool HasUnsavedChanges => (_draftChanged) && (!SkillWindowViewRules.SameLoadout(_draft, ServerLoadout()));

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            TownGrowthClient.SnapshotChanged  += OnSnapshot;
            TownGrowthClient.ResultReceived   += OnResult;
            TownGrowthClient.WriteLockChanged += OnWriteLockChanged;
            if (_view == null)
            {
                return;
            }

            _view.TabSelected          += OnTabSelected;
            _view.ChoiceSelected       += OnChoiceSelected;
            _view.RegisterRequested    += OnRegisterRequested;
            _view.UpgradeRequested     += OnUpgradeRequested;
            _view.DisassembleRequested += OnDisassembleRequested;
            _view.QuantityChanged      += OnQuantityChanged;
            _view.EquipRequested       += OnEquipRequested;
            _view.SaveRequested        += OnSaveRequested;
            _view.RetryRequested       += OnRetryRequested;
            _view.CloseRequested       += OnCloseRequested;
            _view.CloseConfirmed       += OnCloseConfirmed;
            _view.CloseCancelled       += OnCloseCancelled;
        }

        private void OnDisable()
        {
            TownGrowthClient.SnapshotChanged  -= OnSnapshot;
            TownGrowthClient.ResultReceived   -= OnResult;
            TownGrowthClient.WriteLockChanged -= OnWriteLockChanged;
            if (_view == null)
            {
                return;
            }

            _view.TabSelected          -= OnTabSelected;
            _view.ChoiceSelected       -= OnChoiceSelected;
            _view.RegisterRequested    -= OnRegisterRequested;
            _view.UpgradeRequested     -= OnUpgradeRequested;
            _view.DisassembleRequested -= OnDisassembleRequested;
            _view.QuantityChanged      -= OnQuantityChanged;
            _view.EquipRequested       -= OnEquipRequested;
            _view.SaveRequested        -= OnSaveRequested;
            _view.RetryRequested       -= OnRetryRequested;
            _view.CloseRequested       -= OnCloseRequested;
            _view.CloseConfirmed       -= OnCloseConfirmed;
            _view.CloseCancelled       -= OnCloseCancelled;
        }

        // GameDB는 Awake 뒤에 읽는다. 같은 씬의 GameDBBootstrap보다 먼저 돌 수 있다
        private void Start()
        {
            if ((!_previewMode) && (GameDB.IsInitialized))
            {
                UseCatalogs(GameDB.Skills, GameDB.Talismans, GameDB.SkillDisplay, GameDB.Growth, GameDB.Player);
            }
        }

        private void Update()
        {
            if (_view == null)
            {
                return;
            }

            bool isOpen = _view.IsOpen;
            if ((isOpen) && (!_wasOpen))
            {
                OnOpened();
            }

            _wasOpen = isOpen;
            if ((!_previewMode) && (_pendingOperation != Guid.Empty) && (!TownGrowthClient.HasUnresolvedWrite))
            {
                // 다시 접속하면 TownGrowthClient가 잠금을 비운다. 이전 연결의 요청은 다시 확인할 수 없으므로 놓고 상태를 새로 읽는다
                _pendingOperation = Guid.Empty;
                _isOutcomeUnknown = false;
                _status           = string.Empty;
                _isDirty          = true;
            }

            if ((_pendingOperation != Guid.Empty) && (!_isOutcomeUnknown) && (Time.unscaledTime >= _pendingDeadline))
            {
                // 응답이 오지 않으면 결과 불명으로 본다. 잠금은 남기고 같은 번호로 다시 확인하게 한다
                _isOutcomeUnknown = true;
                _status           = UNKNOWN_TEXT;
                _isDirty          = true;
            }

            if ((isOpen) && (_isDirty))
            {
                _isDirty = false;
                Apply();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public bool RequestClose(Action confirmClose)
        {
            if (!HasUnsavedChanges)
            {
                ResetDraft();
                return true;
            }

            // 확인을 띄운 채 다시 닫기를 누르면 확인을 접는다. 창은 그대로 둔다
            if (_confirmClose != null)
            {
                _confirmClose = null;
                _view.ShowConfirm(false);
                return false;
            }

            _confirmClose = confirmClose;
            _view.ShowConfirm(true);
            return false;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary> 서버 없이 가짜 성장 상태로 창을 그린다, 요청은 보내지 않는다 </summary>
        public void ApplyPreview(SkillCatalogSO skills, TalismanCatalogSO talismans, SkillDisplayCatalogSO display,
            GrowthConfigSO growth, PlayerConfigSO player, EPlayerClass playerClass, GrowthSnapshotMessage snapshot)
        {
            if ((TownGrowthClient.HasSnapshot) || ((Application.isPlaying) && (NetworkClient.isConnected)))
            {
                return;
            }

            UseCatalogs(skills, talismans, display, growth, player);
            _previewMode  = true;
            _previewClass = playerClass;
            _snapshot     = snapshot;
            _hasSnapshot  = true;
            _draftChanged = false;
            _status       = PREVIEW_TEXT;
            ResetDraft();
            Apply();
        }

        /// <summary> 미리보기에서 탭과 선택 칸을 바꾼다 </summary>
        public void SelectPreview(int tab, int tier, int choice)
        {
            _tab    = Mathf.Clamp(tab, 0, SkillWindowViewRules.TAB_COUNT - 1);
            _tier   = Mathf.Clamp(tier, 0, SkillWindowViewRules.TIERS_PER_SKILL - 1);
            _choice = Mathf.Clamp(choice, 0, SkillWindowViewRules.CHOICES_PER_TIER - 1);
            Apply();
        }
#endif

        private void UseCatalogs(SkillCatalogSO skills, TalismanCatalogSO talismans, SkillDisplayCatalogSO display,
            GrowthConfigSO growth, PlayerConfigSO player)
        {
            _skills    = skills;
            _talismans = talismans;
            _display   = display;
            _growth    = growth;
            _player    = player;
        }

        private void OnOpened()
        {
            _confirmClose = null;
            _view.ShowConfirm(false);
            if (_pendingOperation == Guid.Empty)
            {
                _status = string.Empty;
            }

            if ((!_previewMode) && (ClientRoles.IsTown()))
            {
                TownGrowthClient.RequestSnapshot();
            }

            ReadClientSnapshot();
            ResetDraft();
            _isDirty = true;
        }

        private void ReadClientSnapshot()
        {
            if (_previewMode)
            {
                return;
            }

            _hasSnapshot = TownGrowthClient.HasSnapshot;
            _snapshot    = TownGrowthClient.Snapshot;
        }

        private void OnSnapshot(GrowthSnapshotMessage snapshot)
        {
            if (_previewMode)
            {
                return;
            }

            _snapshot    = snapshot;
            _hasSnapshot = true;
            if (!_draftChanged)
            {
                ResetDraft();
            }

            _isDirty = true;
        }

        private void OnWriteLockChanged()
        {
            _isDirty = true;
        }

        private void OnTabSelected(int tab)
        {
            if ((tab < 0) || (tab >= SkillWindowViewRules.TAB_COUNT))
            {
                return;
            }

            _tab      = tab;
            _tier     = 0;
            _choice   = 0;
            _quantity = DEFAULT_QUANTITY;
            Refresh();
        }

        private void OnChoiceSelected(int tier, int choice)
        {
            _tier     = tier;
            _choice   = choice;
            _quantity = DEFAULT_QUANTITY;
            Refresh();
        }

        private void OnQuantityChanged(int delta)
        {
            _quantity += delta;
            Refresh();
        }

        private void OnRegisterRequested()
        {
            if ((!TryGetSelectedEffect(out TalismanCatalogSO.TalismanEffectDefinition effect)) || (!CanWriteNow()))
            {
                return;
            }

            ReadEffect(effect.DefinitionId, out int level, out _, out long cards);
            if (!SkillWindowViewRules.CanRegister(level, cards, _snapshot.Essence, _growth.RegisterCardCost, _growth.RegisterEssenceCost))
            {
                return;
            }

            Begin(TownGrowthClient.RegisterTalisman(effect.DefinitionId), EGrowthAction.TalismanRegister);
        }

        private void OnUpgradeRequested()
        {
            if ((!TryGetSelectedEffect(out TalismanCatalogSO.TalismanEffectDefinition effect)) || (!CanWriteNow()))
            {
                return;
            }

            ReadEffect(effect.DefinitionId, out int level, out _, out long cards);
            if ((!SkillWindowViewRules.IsRegistered(level)) || (level >= _growth.MaxEffectLevel))
            {
                return;
            }

            GrowthConfigSO.EffectUpgradeCost cost = _growth.GetEffectUpgradeCost(level + 1);
            if (!SkillWindowViewRules.CanUpgrade(level, _growth.MaxEffectLevel, cards, _snapshot.Essence, cost.CardCost,
                cost.EssenceCost))
            {
                return;
            }

            Begin(TownGrowthClient.UpgradeTalisman(effect.DefinitionId), EGrowthAction.TalismanUpgrade);
        }

        private void OnDisassembleRequested()
        {
            if ((!TryGetSelectedEffect(out TalismanCatalogSO.TalismanEffectDefinition effect)) || (!CanWriteNow()))
            {
                return;
            }

            ReadEffect(effect.DefinitionId, out _, out _, out long cards);
            int quantity = SkillWindowViewRules.ClampDisassembleQuantity(_quantity, cards);
            if (quantity <= 0)
            {
                return;
            }

            Begin(TownGrowthClient.DisassembleTalisman(effect.DefinitionId, quantity), EGrowthAction.TalismanDisassemble);
        }

        // 장착은 창 안의 임시 조합만 바꾼다. 서버에는 저장을 누를 때 보낸다
        private void OnEquipRequested()
        {
            if ((!TryGetSelectedEffect(out TalismanCatalogSO.TalismanEffectDefinition effect)) || (!CanEditLoadout()))
            {
                return;
            }

            ReadEffect(effect.DefinitionId, out int level, out _, out _);
            bool equipped = SkillWindowViewRules.IsEquipped(_draft, effect.SkillId, (int)effect.Tier, effect.DefinitionId);
            if ((!equipped) && (!SkillWindowViewRules.IsRegistered(level)))
            {
                _status = SkillWindowViewRules.LoadoutErrorText(SkillWindowViewRules.ELoadoutError.Unregistered);
                Refresh();
                return;
            }

            SkillWindowViewRules.ToggleEquip(_draft, effect.SkillId, (int)effect.Tier, effect.DefinitionId);
            _draftChanged = true;
            _status       = HasUnsavedChanges ? UNSAVED_TEXT : string.Empty;
            Refresh();
        }

        private void OnSaveRequested()
        {
            if ((!CanEditLoadout()) || (!HasUnsavedChanges) || (!CanWriteNow()))
            {
                return;
            }

            SkillWindowViewRules.ELoadoutError error = SkillWindowViewRules.ValidateLoadout(_draft, LevelOf);
            if (error != SkillWindowViewRules.ELoadoutError.None)
            {
                _status = SkillWindowViewRules.LoadoutErrorText(error);
                Refresh();
                return;
            }

            Begin(TownGrowthClient.ReplaceTalismanLoadout(_draft.ToArray()), EGrowthAction.TalismanLoadout);
        }

        private void OnRetryRequested()
        {
            if ((!_isOutcomeUnknown) || (!GrowthWriteGuard.CanRetry(TownGrowthClient.HasUnresolvedWrite,
                (TownGrowthClient.IsUnresolvedOutcomeUnknown) || (_isOutcomeUnknown), TownGrowthClient.UnresolvedAction,
                TownGrowthClient.UnresolvedOperationId, _pendingOperation, SkillWindowViewRules.G_OwnActions)))
            {
                return;
            }

            // 같은 요청을 같은 번호로 다시 보낸다. 서버와 API가 처음 결과를 돌려주고 다시 차감하지 않는다
            if (TownGrowthClient.RetryLast())
            {
                _isOutcomeUnknown = false;
                _pendingDeadline  = Time.unscaledTime + Constants.Network.API_TIMEOUT_SECONDS;
                _status           = CHECKING_TEXT;
                Refresh();
            }
        }

        private void OnCloseRequested()
        {
            if (_toggle != null)
            {
                _toggle.RequestClose();
                return;
            }

            if (RequestClose(CloseView))
            {
                CloseView();
            }
        }

        private void OnCloseConfirmed()
        {
            Action close = _confirmClose;
            _confirmClose = null;
            _view.ShowConfirm(false);
            _draftChanged = false;
            ResetDraft();
            if (close != null)
            {
                close();
                return;
            }

            CloseView();
        }

        private void OnCloseCancelled()
        {
            _confirmClose = null;
            _view.ShowConfirm(false);
        }

        private void CloseView()
        {
            _view.SetOpen(false);
        }

        private void Begin(Guid operationId, EGrowthAction action)
        {
            if (operationId == Guid.Empty)
            {
                _status = GrowthWriteGuard.IsBlockedByOther(Guid.Empty) ? GrowthWriteGuard.OTHER_WINDOW_TEXT : OFFLINE_TEXT;
                Refresh();
                return;
            }

            _pendingOperation = operationId;
            _pendingAction    = action;
            _isOutcomeUnknown = false;
            _pendingDeadline  = Time.unscaledTime + Constants.Network.API_TIMEOUT_SECONDS;
            _status           = WAITING_TEXT;
            Refresh();
        }

        // 서버는 결과보다 성장 상태를 먼저 보낸다. 결과가 오면 화면은 이미 새 상태라 안내 글자만 바꾼다
        private void OnResult(GrowthResultMessage result)
        {
            if ((_pendingOperation == Guid.Empty) || (result.OperationId != _pendingOperation))
            {
                return;
            }

            if (result.Reason == ERejectReason.BackendUnavailable)
            {
                _isOutcomeUnknown = true;
                _status           = UNKNOWN_TEXT;
                _isDirty          = true;
                return;
            }

            EGrowthAction action = _pendingAction;
            _pendingOperation = Guid.Empty;
            _isOutcomeUnknown = false;
            if (result.IsAccepted)
            {
                _status = AcceptedText(action, result.Outcome);
                if (action == EGrowthAction.TalismanLoadout)
                {
                    _draftChanged = false;
                    ResetDraft();
                }
            }
            else if ((action == EGrowthAction.TalismanLoadout) && (result.Reason == ERejectReason.StaleRevision))
            {
                // 다른 곳에서 조합이 바뀌었다. 임시 조합을 버리고 최신 상태를 다시 받는다
                _draftChanged = false;
                TownGrowthClient.RequestSnapshot();
                ResetDraft();
                _status = STALE_TEXT;
            }
            else
            {
                string text = RejectText.Describe(result.Reason);
                _status = string.IsNullOrEmpty(text) ? FAILED_TEXT : text;
            }

            _isDirty = true;
        }

        private static string AcceptedText(EGrowthAction action, int outcome)
        {
            switch (action)
            {
                case EGrowthAction.TalismanRegister:
                    return (outcome == GrowthCodes.Outcome.SUCCEEDED) ? REGISTERED_TEXT : REGISTER_FAIL;
                case EGrowthAction.TalismanUpgrade:
                    return UPGRADED_TEXT;
                case EGrowthAction.TalismanDisassemble:
                    return DISASSEMBLED;
                default:
                    return SAVED_TEXT;
            }
        }

        private void Refresh()
        {
            _isDirty = true;
            if (_previewMode)
            {
                Apply();
            }
        }

        private void ResetDraft()
        {
            _draft.Clear();
            TownLoadoutEntry[] loadout = ServerLoadout();
            for (int i = 0; i < loadout.Length; ++i)
            {
                _draft.Add(loadout[i]);
            }

            _draftChanged = false;
        }

        private TownLoadoutEntry[] ServerLoadout()
        {
            return _hasSnapshot ? _snapshot.Loadout : Array.Empty<TownLoadoutEntry>();
        }

        private SkillWindowViewRules.EMode Mode()
        {
            if (_previewMode)
            {
                return SkillWindowViewRules.EMode.SortieLocked;
            }

            bool transferring = (NetworkManager.singleton is ShinshiNetworkManager manager) && (manager.IsTransferringServer);
            PartyRoomStateMessage room = TownPartyClient.Room;
            return SkillWindowViewRules.ResolveMode(ClientRoles.IsTown(), ClientRoles.IsDungeon(), room.InRoom, room.Phase,
                transferring);
        }

        private bool HasContent()
        {
            return (_skills != null) && (_talismans != null) && (_display != null) && (_growth != null) && (_player != null);
        }

        private bool ContentMatches()
        {
            return (_previewMode) || (!GameDB.IsInitialized) || (!_hasSnapshot)
                || (string.Equals(_snapshot.ContentVersion, GameDB.ContentVersion, StringComparison.Ordinal));
        }

        private bool CanEditLoadout()
        {
            return (!_previewMode) && (_hasSnapshot) && (HasContent()) && (ContentMatches())
                && (Mode() == SkillWindowViewRules.EMode.Writable);
        }

        private bool CanWriteNow()
        {
            return (CanEditLoadout()) && (_pendingOperation == Guid.Empty) && (!GrowthWriteGuard.IsBlockedByOther(_pendingOperation));
        }

        private EPlayerClass PlayerClass()
        {
            if (_previewMode)
            {
                return _previewClass;
            }

            if (_hasSnapshot)
            {
                return (EPlayerClass)_snapshot.ClassId;
            }

            NetworkIdentity local = NetworkClient.localPlayer;
            if ((local != null) && (local.TryGetComponent(out PlayerCombatState combat)))
            {
                return combat.PlayerClass;
            }

            return EPlayerClass.Swordsman;
        }

        private bool TryGetSelectedSkill(out SkillCatalogSO.SkillDefinition skill)
        {
            skill = null;
            int slot = SkillWindowViewRules.SlotOfTab(_tab);
            return (slot >= 0) && (_skills != null) && (_skills.TryGetSkillBySlot(PlayerClass(), slot, out skill));
        }

        private bool TryGetSelectedEffect(out TalismanCatalogSO.TalismanEffectDefinition effect)
        {
            effect = null;
            return (TryGetSelectedSkill(out SkillCatalogSO.SkillDefinition skill))
                && (SkillWindowViewRules.TryFindEffect(_talismans, skill.DefinitionId, _tier, _choice, out effect));
        }

        private void ReadEffect(string effectId, out int level, out int failures, out long cards)
        {
            level    = 0;
            failures = 0;
            cards    = 0;
            if (!_hasSnapshot)
            {
                return;
            }

            if (SkillWindowViewRules.TryFindTalisman(_snapshot.Talismans, effectId, out TownTalismanEntry entry))
            {
                level    = entry.Level;
                failures = entry.RegisterFailures;
            }

            cards = SkillWindowViewRules.AvailableCards(_snapshot.AccountItems, effectId);
        }

        private int LevelOf(string effectId)
        {
            ReadEffect(effectId, out int level, out _, out _);
            return level;
        }

        private void Apply()
        {
            if (_view == null)
            {
                return;
            }

            if (!HasContent())
            {
                _view.SetHeader(string.Empty, string.Empty);
                _view.SetSkill(string.Empty, string.Empty, string.Empty);
                _view.SetTalismanArea(false, LOADING_TEXT);
                _view.SetStatus(_status);
                _view.SetRetry(false);
                _view.SetSave(false);
                return;
            }

            EPlayerClass playerClass = PlayerClass();
            string stackRule = string.Empty;
            string basicText = string.Empty;
            if (_display.TryGetClass(playerClass, out SkillDisplayCatalogSO.ClassDisplay classDisplay))
            {
                stackRule = SkillWindowViewRules.StackRuleText(classDisplay.StackRule, _skills.ClassStackMax);
                basicText = classDisplay.BasicAttackDescription;
            }

            _view.SetHeader(CharDetailRules.ClassName((int)playerClass), stackRule);
            ApplyTabs(playerClass);

            SkillWindowViewRules.EMode mode = Mode();
            bool showTalismans = (SkillWindowViewRules.ShowsTalismans(mode)) && (_hasSnapshot) && (ContentMatches());
            if (!TryGetSelectedSkill(out SkillCatalogSO.SkillDefinition skill))
            {
                _view.SetSkill(TITLE_BASIC, SkillWindowViewRules.BasicAttackText(_player.GetBasicAttack(playerClass)), basicText);
                _view.SetTalismanArea(false, BASIC_NOTE);
                ApplyFooter(mode);
                return;
            }

            _display.TryGetSkill(skill.DefinitionId, out SkillDisplayCatalogSO.DisplayEntry skillText);
            _view.SetSkill(_display.DisplayNameOf(skill.DefinitionId), SkillWindowViewRules.StatsText(skill),
                (skillText != null) ? skillText.Description : string.Empty);
            _view.SetTalismanArea(showTalismans, NoticeOf(mode));
            if (showTalismans)
            {
                ApplyChoices(skill);
                ApplySelectedEffect(mode);
            }

            ApplyFooter(mode);
        }

        private string NoticeOf(SkillWindowViewRules.EMode mode)
        {
            switch (mode)
            {
                case SkillWindowViewRules.EMode.Dungeon:
                    return DUNGEON_TEXT;
                case SkillWindowViewRules.EMode.Offline:
                    return OFFLINE_TEXT;
                default:
                    if (!ContentMatches())
                    {
                        return MISMATCH_TEXT;
                    }

                    return _hasSnapshot ? string.Empty : LOADING_TEXT;
            }
        }

        private void ApplyTabs(EPlayerClass playerClass)
        {
            for (int tab = 0; tab < SkillWindowViewRules.TAB_COUNT; ++tab)
            {
                Sprite icon = null;
                int    slot = SkillWindowViewRules.SlotOfTab(tab);
                if ((slot >= 0) && (_icons != null) && (_skills.TryGetSkillBySlot(playerClass, slot, out SkillCatalogSO.SkillDefinition skill))
                    && (SkillWindowViewRules.TryFindEffect(_talismans, skill.DefinitionId, 0, 0,
                        out TalismanCatalogSO.TalismanEffectDefinition first)))
                {
                    // 기술 정식 아이콘이 없어 그 기술의 첫 부적 아이콘을 임시로 쓴다
                    icon = _icons.FindIcon(first.DefinitionId);
                }

                _view.SetTab(tab, SkillWindowViewRules.TabLabel(tab), icon, tab == _tab);
            }
        }

        private void ApplyChoices(SkillCatalogSO.SkillDefinition skill)
        {
            for (int tier = 0; tier < SkillWindowViewRules.TIERS_PER_SKILL; ++tier)
            {
                for (int choice = 0; choice < SkillWindowViewRules.CHOICES_PER_TIER; ++choice)
                {
                    if (!SkillWindowViewRules.TryFindEffect(_talismans, skill.DefinitionId, tier, choice,
                        out TalismanCatalogSO.TalismanEffectDefinition effect))
                    {
                        _view.SetChoice(tier, choice, false, null, null, null, false, false, false);
                        continue;
                    }

                    ReadEffect(effect.DefinitionId, out int level, out _, out long cards);
                    SkillWindowViewRules.EOwnState state = SkillWindowViewRules.OwnState(level, cards);
                    bool noEffect = (_display.TryGetEffect(effect.DefinitionId, out SkillDisplayCatalogSO.DisplayEntry text))
                        && (text.NoEffect);
                    string stateText = SkillWindowViewRules.OwnStateText(state, level, cards) + (noEffect ? "  효과 없음" : string.Empty);
                    Sprite icon = (_icons != null) ? _icons.FindIcon(effect.DefinitionId) : null;
                    _view.SetChoice(tier, choice, true, _display.DisplayNameOf(effect.DefinitionId), stateText, icon,
                        (tier == _tier) && (choice == _choice),
                        SkillWindowViewRules.IsEquipped(_draft, skill.DefinitionId, tier, effect.DefinitionId), noEffect);
                }
            }
        }

        private void ApplySelectedEffect(SkillWindowViewRules.EMode mode)
        {
            if (!TryGetSelectedEffect(out TalismanCatalogSO.TalismanEffectDefinition effect))
            {
                _tier   = 0;
                _choice = 0;
                if (!TryGetSelectedEffect(out effect))
                {
                    _view.SetEffect(string.Empty, string.Empty, string.Empty, string.Empty);
                    DisableActions();
                    return;
                }
            }

            ReadEffect(effect.DefinitionId, out int level, out int failures, out long cards);
            _display.TryGetEffect(effect.DefinitionId, out SkillDisplayCatalogSO.DisplayEntry text);
            int maxLevel = _growth.MaxEffectLevel;
            string title = _display.DisplayNameOf(effect.DefinitionId) + "  " + SkillWindowViewRules.LevelText(level, maxLevel);
            _view.SetEffect(title, BonusLine(effect, level, maxLevel),
                (text != null) ? text.Description + "\n강화 항목: " + text.UpgradeDescription : string.Empty,
                ((text != null) && (text.NoEffect)) ? NO_EFFECT_TEXT : string.Empty);

            bool canWrite = CanWriteNow();
            long essence  = _snapshot.Essence;
            float rate    = _growth.GetRegisterSuccessRate(failures);
            _view.SetRegister(SkillWindowViewRules.RegisterLabel(_growth.RegisterCardCost, _growth.RegisterEssenceCost, rate),
                (canWrite) && (SkillWindowViewRules.CanRegister(level, cards, essence, _growth.RegisterCardCost,
                    _growth.RegisterEssenceCost)));

            if ((SkillWindowViewRules.IsRegistered(level)) && (level < maxLevel))
            {
                GrowthConfigSO.EffectUpgradeCost cost = _growth.GetEffectUpgradeCost(level + 1);
                _view.SetUpgrade(SkillWindowViewRules.UpgradeLabel(cost.CardCost, cost.EssenceCost),
                    (canWrite) && (SkillWindowViewRules.CanUpgrade(level, maxLevel, cards, essence, cost.CardCost, cost.EssenceCost)));
            }
            else
            {
                _view.SetUpgrade(SkillWindowViewRules.IsRegistered(level) ? "최대 레벨" : "강화", false);
            }

            _quantity = SkillWindowViewRules.ClampDisassembleQuantity(_quantity, cards);
            _view.SetDisassemble(SkillWindowViewRules.DisassembleLabel(_quantity,
                SkillWindowViewRules.DisassembleEssence(_quantity, _growth.CardDisassembleEssence)),
                (canWrite) && (_quantity > 0), (canWrite) && (_quantity > 1), (canWrite) && (_quantity < cards));

            bool equipped = SkillWindowViewRules.IsEquipped(_draft, effect.SkillId, (int)effect.Tier, effect.DefinitionId);
            _view.SetEquip(equipped ? UNEQUIP_TEXT : EQUIP_TEXT,
                (CanEditLoadout()) && (_pendingOperation == Guid.Empty) && ((equipped) || (SkillWindowViewRules.IsRegistered(level))));
        }

        private string BonusLine(TalismanCatalogSO.TalismanEffectDefinition effect, int level, int maxLevel)
        {
            int shown = Mathf.Max(SkillWindowViewRules.FIRST_LEVEL, level);
            string current = SkillWindowViewRules.BonusText(effect.UpgradeStat, _talismans.ScaleBonus(effect.LevelOneBonus, shown));
            if (!SkillWindowViewRules.IsRegistered(level))
            {
                return "등록하면 Lv 1  " + current;
            }

            if (level >= maxLevel)
            {
                return "현재  " + current;
            }

            string next = SkillWindowViewRules.BonusText(effect.UpgradeStat, _talismans.ScaleBonus(effect.LevelOneBonus, level + 1));
            return "현재  " + current + "    다음 레벨  " + next;
        }

        private void DisableActions()
        {
            _view.SetRegister("등록", false);
            _view.SetUpgrade("강화", false);
            _view.SetDisassemble("분해", false, false, false);
            _view.SetEquip(EQUIP_TEXT, false);
        }

        // 버튼 상태와 안내는 한곳에서 정한다. 결과 불명이면 다시 확인, 다른 창 요청이면 대기, 그 밖에는 상황을 본다
        private void ApplyFooter(SkillWindowViewRules.EMode mode)
        {
            long cards = 0;
            if ((_hasSnapshot) && (TryGetSelectedEffect(out TalismanCatalogSO.TalismanEffectDefinition effect)))
            {
                ReadEffect(effect.DefinitionId, out _, out _, out cards);
            }

            bool showTalismans = (SkillWindowViewRules.ShowsTalismans(mode)) && (_hasSnapshot);
            _view.SetFooter(showTalismans ? SkillWindowViewRules.FooterText(_snapshot.Essence, cards, _draft.Count) : string.Empty);

            bool ownUnknown = (_isOutcomeUnknown) && (GrowthWriteGuard.CanRetry(TownGrowthClient.HasUnresolvedWrite, true,
                TownGrowthClient.UnresolvedAction, TownGrowthClient.UnresolvedOperationId, _pendingOperation,
                SkillWindowViewRules.G_OwnActions));
            _view.SetRetry(ownUnknown);
            _view.SetSave((showTalismans) && (CanWriteNow()) && (HasUnsavedChanges));

            string status = _status;
            if (_pendingOperation == Guid.Empty)
            {
                if ((!_previewMode) && (GrowthWriteGuard.IsBlockedByOther(_pendingOperation)) && (showTalismans))
                {
                    status = GrowthWriteGuard.OTHER_WINDOW_TEXT;
                }
                else if ((mode == SkillWindowViewRules.EMode.SortieLocked) && (!_previewMode))
                {
                    status = SORTIE_TEXT;
                }
                else if ((string.IsNullOrEmpty(status)) && (HasUnsavedChanges))
                {
                    status = UNSAVED_TEXT;
                }
            }

            _view.SetStatus(status);
        }
    }
}
