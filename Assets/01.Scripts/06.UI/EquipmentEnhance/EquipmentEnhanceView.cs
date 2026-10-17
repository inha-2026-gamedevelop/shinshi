// System
using System;
using System.Collections;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// 서드파티
using DG.Tweening;
using Mirror;
using TMPro;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 착용 장비 6부위의 강화 확률, 실패 보정, 능력치 전후와 비용을 표시하고 서버에 강화를 요청한다 </summary>
    [ExecuteAlways]
    public sealed class EquipmentEnhanceView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int   SLOT_COUNT     = Constants.Combat.EQUIPMENT_SLOT_COUNT;
        private const int   STAT_ROW_COUNT = 3;
        private const float PERCENT        = 100f;
        private const float FULL_RATE      = 1f;

        private static readonly string[] G_StatNames = { "공격력", "생명력", "방어력" };

        private const string READY_TEXT        = "강화를 눌러 한 단계 도전하세요";
        private const string WAITING_TEXT      = "서버의 장비 상태를 기다리고 있습니다";
        private const string OFFLINE_TEXT      = "마을 서버에 연결하면 강화할 수 있습니다";
        private const string SENDING_TEXT      = "강화 결과를 확인하고 있습니다";
        private const string MISMATCH_TEXT     = "서버 장비 상태와 설정이 맞지 않아 강화를 중단했습니다";
        private const string NO_ITEM_TEXT      = "이 부위에 착용한 장비가 없습니다";
        private const string MAX_TEXT          = "최대 단계에 도달한 장비입니다";
        private const string MAX_PITY_TEXT     = "더 강화할 단계가 없습니다";
        private const string IN_MARKET_TEXT    = "경매장에 맡긴 장비는 강화할 수 없습니다";
        private const string ESSENCE_LACK_TEXT = "강화에 필요한 정기가 부족합니다";
        private const string SHARD_LACK_TEXT   = "강화에 필요한 정련편이 부족합니다";
        private const string TIMEOUT_TEXT      = "응답을 확인하지 못했습니다. 서버 상태를 다시 확인합니다";
        private const string REJECT_TEXT       = "강화를 처리할 수 없습니다";
        private const string KEEP_NOTE_TEXT    = "실패해도 장비와 강화 단계는 유지됩니다";
        private const string PREVIEW_NOTE_TEXT = "미리보기입니다. 실제 재화와 저장에는 영향이 없습니다";

        [Header("장비 목록 (EEquipmentSlot 순서)")]
        [SerializeField] private Button[]     _slotButtons    = null;
        [SerializeField] private Image[]      _slotFrames     = null;
        [SerializeField] private Image[]      _slotIcons      = null;
        [SerializeField] private TMP_Text[]   _slotNameTexts  = null;
        [SerializeField] private TMP_Text[]   _slotLevelTexts = null;
        [SerializeField] private GameObject[] _slotSelections = null;

        [Header("선택 장비")]
        [SerializeField] private Image      _itemFrame     = null;
        [SerializeField] private Image      _itemIcon      = null;
        [SerializeField] private TMP_Text   _itemNameText  = null;
        [SerializeField] private TMP_Text   _itemGradeText = null;
        [SerializeField] private TMP_Text   _levelFromText = null;
        [SerializeField] private TMP_Text   _levelToText   = null;
        [SerializeField] private GameObject _levelArrow    = null;

        [Header("확률과 실패 보정")]
        [SerializeField] private TMP_Text _rateText    = null;
        [SerializeField] private TMP_Text _rateCaption = null;
        [SerializeField] private Image    _pityFill    = null;
        [SerializeField] private TMP_Text _pityText    = null;

        [Header("능력치 전후")]
        [SerializeField] private GameObject[] _statRows        = null;
        [SerializeField] private TMP_Text[]   _statNameTexts   = null;
        [SerializeField] private TMP_Text[]   _statBeforeTexts = null;
        [SerializeField] private GameObject[] _statArrows      = null;
        [SerializeField] private TMP_Text[]   _statAfterTexts  = null;

        [Header("비용과 보유")]
        [SerializeField] private TMP_Text _essenceCostText = null;
        [SerializeField] private TMP_Text _shardCostText   = null;
        [SerializeField] private TMP_Text _essenceText     = null;
        [SerializeField] private TMP_Text _shardText       = null;
        [SerializeField] private TMP_Text _noteText        = null;
        [SerializeField] private TMP_Text _statusText      = null;

        [Header("조작")]
        [SerializeField] private Button   _enhanceButton = null;
        [SerializeField] private TMP_Text _enhanceLabel  = null;
        [SerializeField] private Button   _closeButton   = null;

        [Header("결과 연출")]
        [SerializeField] private CanvasGroup   _resultGroup      = null;
        [SerializeField] private RectTransform _resultBadge      = null;
        [SerializeField] private TMP_Text      _resultTitleText  = null;
        [SerializeField] private TMP_Text      _resultDetailText = null;
        [SerializeField] private float         _resultFadeTime   = 0.18f;
        [SerializeField] private float         _resultHoldTime   = 1.1f;
        [SerializeField] private float         _resultPopScale   = 1.18f;

        [Header("표시 색")]
        [SerializeField] private Color _successColor = new Color(0.95f, 0.8f, 0.45f, 1f);
        [SerializeField] private Color _failColor    = new Color(0.66f, 0.74f, 0.8f, 1f);
        [SerializeField] private Color _lackColor    = new Color(0.86f, 0.42f, 0.38f, 1f);
        [SerializeField] private Color _normalColor  = new Color(0.9f, 0.92f, 0.92f, 1f);
        [SerializeField] private Color _mutedColor   = new Color(0.64f, 0.69f, 0.71f, 1f);

        [Header("그림 (EItemGrade, EEquipmentSlot 순서)")]
        [SerializeField] private Sprite[] _gradeFrames = null;
        [SerializeField] private Sprite[] _slotSprites = null;

        [Header("개발 미리보기 표시")]
        [SerializeField] private GameObject _previewTag = null;

        private readonly string[]            _itemIds       = new string[SLOT_COUNT];
        private readonly string[]            _definitionIds = new string[SLOT_COUNT];
        private readonly int[]               _levels        = new int[SLOT_COUNT];
        private readonly bool[]              _inMarket      = new bool[SLOT_COUNT];
        private readonly UnityAction[]       _selectActions = new UnityAction[SLOT_COUNT];
        private readonly List<TownPityEntry> _pity          = new List<TownPityEntry>();

        private EquipmentCatalogSO _catalog;
        private Button[]           _boundSlotButtons;
        private Button             _boundEnhanceButton;
        private Button             _boundCloseButton;
        private Sequence           _resultSequence;
        private Coroutine          _requestWatcher;
        private Guid               _pendingOperation;
        private long               _revision;
        private long               _essence;
        private long               _shards;
        private int                _selectedIndex;
        private int                _pendingFromLevel;
        private bool               _hasData;
        private bool               _hasServerSnapshot;
        private bool               _runtimeStarted;
        private bool               _requestInFlight;
        private bool               _selectActionsReady;
        private string             _status = READY_TEXT;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private bool _previewMode;
#endif

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 개발 예시 데이터만 바꾸는 화면이면 참이며 일반 출시 빌드에서는 항상 거짓이다 </summary>
        public bool PreviewMode
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return _previewMode;
#else
                return false;
#endif
            }
        }

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake() { BindButtons(); }

        private void OnEnable()
        {
            BindButtons();
            HideResultImmediate();
            if (!Application.isPlaying)
            {
                RefreshAll();
                return;
            }

            TownGrowthClient.SnapshotChanged  += OnSnapshotChanged;
            TownGrowthClient.ResultReceived   += OnResultReceived;
            TownGrowthClient.WriteLockChanged += OnWriteLockChanged;
            if (_runtimeStarted)
            {
                ReadServerState();
            }
        }

        private void Start()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            _runtimeStarted = true;
            if (!GameDB.IsInitialized)
            {
                if (!PreviewMode)
                {
                    _catalog = null;
                    _hasData = false;
                    SetStatus("성장 데이터를 불러오지 못했습니다");
                    Debug.LogError("EquipmentEnhanceView: GameDB가 초기화되지 않았다", this);
                }

                return;
            }

            _catalog = GameDB.Equipment;
            ReadServerState();
        }

        private void OnDisable()
        {
            UnbindButtons();
            TownGrowthClient.SnapshotChanged  -= OnSnapshotChanged;
            TownGrowthClient.ResultReceived   -= OnResultReceived;
            TownGrowthClient.WriteLockChanged -= OnWriteLockChanged;
            if (_requestInFlight)
            {
                // 결과를 못 본 채 닫혔다. 다시 열면 서버 상태부터 다시 읽는다
                ClearRequest();
                _hasServerSnapshot = false;
                _hasData           = false;
            }

            if (_resultSequence != null)
            {
                _resultSequence.Pause();
            }
        }

        private void OnDestroy()
        {
            if (_resultSequence != null)
            {
                _resultSequence.Kill();
                _resultSequence = null;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> EEquipmentSlot 순서의 부위를 선택한다 </summary>
        public void SelectSlot(int index)
        {
            if ((index < 0) || (index >= SLOT_COUNT) || (_requestInFlight))
            {
                return;
            }

            _selectedIndex = index;
            SetStatus(ReadyStatus());
        }

        /// <summary> 선택한 장비를 한 단계 강화 시도한다. 서버에서는 결과와 스냅샷을 받을 때까지 다음 시도를 막는다 </summary>
        public void Enhance()
        {
            if (!TryValidate(out string reason))
            {
                SetStatus(reason);
                return;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_previewMode)
            {
                if ((TownGrowthClient.HasSnapshot) || (NetworkClient.isConnected))
                {
                    _previewMode = false;
                    ReadServerState();
                    SetStatus("서버 상태를 확인한 뒤 다시 강화하세요");
                    return;
                }

                SimulatePreviewAttempt();
                return;
            }
#endif

            if ((!Application.isPlaying) || (!_hasServerSnapshot) || (!ClientRoles.IsTown()))
            {
                SetStatus(OFFLINE_TEXT);
                return;
            }

            _pendingFromLevel = _levels[_selectedIndex];
            _pendingOperation = TownGrowthClient.EnhanceEquipment(_itemIds[_selectedIndex]);
            if (_pendingOperation == Guid.Empty)
            {
                SetStatus("서버에 연결되지 않아 강화를 보낼 수 없습니다");
                return;
            }

            _requestInFlight = true;
            _requestWatcher  = StartCoroutine(CoWatchRequest());
            SetStatus(SENDING_TEXT);
        }

        /// <summary> 처리 중에는 창을 유지하며 그 외에는 닫는다 </summary>
        public void Close()
        {
            if (_requestInFlight)
            {
                SetStatus(SENDING_TEXT);
                return;
            }

            gameObject.SetActive(false);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary> 실제 스냅샷이 없는 개발 화면에 카탈로그와 저장하지 않는 예시 장비를 주입한다 </summary>
        public void ApplyPreviewSnapshot(EquipmentCatalogSO catalog, long essence, long shards,
            TownEquipmentEntry[] equipment, TownPityEntry[] pity)
        {
            if ((catalog == null) || (equipment == null) || (essence < 0) || (shards < 0))
            {
                SetStatus("미리보기 데이터와 장비 설정을 확인하세요");
                return;
            }

            if ((_hasServerSnapshot) || (_requestInFlight) || (TownGrowthClient.HasSnapshot)
                || ((Application.isPlaying) && (NetworkClient.isConnected)))
            {
                return;
            }

            _catalog     = catalog;
            _essence     = essence;
            _shards      = shards;
            _previewMode = true;
            if (!ReadEquipment(equipment, pity))
            {
                _hasData = false;
                SetStatus("미리보기 장비가 카탈로그와 맞지 않습니다");
                return;
            }

            _hasData = true;
            if (isActiveAndEnabled)
            {
                // 에디터에서 AddComponent가 직렬화 참조 주입보다 먼저 Awake를 부를 수 있다
                BindButtons();
            }

            SetStatus(ReadyStatus());
        }

        // 서버 roll 대신 로컬 난수로 한 번 판정한다. 서버와 같은 규칙(현재 단계 보정 사용, 성공하면 그 단계 보정 초기화)을 따른다
        private void SimulatePreviewAttempt()
        {
            int   from     = _levels[_selectedIndex];
            int   failures = EquipmentEnhanceRules.FindFailures(_pity, (EEquipmentSlot)_selectedIndex, from);
            float rate     = EquipmentEnhanceRules.SuccessRate(_catalog, from, failures);
            EquipmentCatalogSO.EnhancementStep step = _catalog.GetEnhancementStep(from + 1);
            _essence -= step.EssenceCost;
            _shards  -= step.ShardCost;

            bool succeeded = UnityEngine.Random.value < rate;
            SetPity((EEquipmentSlot)_selectedIndex, from, succeeded ? 0 : failures + 1);
            if (succeeded)
            {
                _levels[_selectedIndex] = from + 1;
            }

            SetStatus(ReadyStatus());
            PlayResult(succeeded, from, step.FailBonusRate);
        }
#endif

        private bool TryValidate(out string reason)
        {
            reason = string.Empty;
            if ((_catalog == null) || (!_hasData))
            {
                reason = Application.isPlaying ? WAITING_TEXT : OFFLINE_TEXT;
                return false;
            }

            if (_requestInFlight)
            {
                reason = SENDING_TEXT;
                return false;
            }

            return TryValidateSelected(out reason);
        }

        private bool TryValidateSelected(out string reason)
        {
            reason = string.Empty;
            if ((Application.isPlaying) && (GrowthWriteGuard.IsBlockedByOther(_pendingOperation)))
            {
                reason = GrowthWriteGuard.OTHER_WINDOW_TEXT;
                return false;
            }

            if (string.IsNullOrEmpty(_itemIds[_selectedIndex]))
            {
                reason = NO_ITEM_TEXT;
                return false;
            }

            if (_inMarket[_selectedIndex])
            {
                reason = IN_MARKET_TEXT;
                return false;
            }

            int level = _levels[_selectedIndex];
            if (EquipmentEnhanceRules.IsMaxLevel(_catalog, level))
            {
                reason = MAX_TEXT;
                return false;
            }

            EquipmentCatalogSO.EnhancementStep step = _catalog.GetEnhancementStep(level + 1);
            if (step.EssenceCost > _essence)
            {
                reason = ESSENCE_LACK_TEXT;
                return false;
            }

            if (step.ShardCost > _shards)
            {
                reason = SHARD_LACK_TEXT;
                return false;
            }

            return true;
        }

        private string ReadyStatus()
        {
            if ((_catalog == null) || (!_hasData))
            {
                return _status;
            }

            return TryValidateSelected(out string reason) ? READY_TEXT : reason;
        }

        private void ReadServerState()
        {
            if (_catalog == null)
            {
                RefreshAll();
                return;
            }

            if (TownGrowthClient.HasSnapshot)
            {
                OnSnapshotChanged(TownGrowthClient.Snapshot);
                return;
            }

            if (NetworkClient.isConnected)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                _previewMode = false;
#endif
                _hasData = false;
                TownGrowthClient.RequestSnapshot();
                SetStatus(WAITING_TEXT);
                return;
            }

            SetStatus(PreviewMode ? ReadyStatus() : OFFLINE_TEXT);
        }

        private IEnumerator CoWatchRequest()
        {
            float deadline = Time.realtimeSinceStartup + Constants.Network.API_TIMEOUT_SECONDS;
            while (_requestInFlight)
            {
                if ((!NetworkClient.isConnected) || (Time.realtimeSinceStartup >= deadline))
                {
                    _requestWatcher = null;
                    StopAndRefresh(TIMEOUT_TEXT);
                    yield break;
                }

                yield return null;
            }

            _requestWatcher = null;
        }

        private void OnSnapshotChanged(GrowthSnapshotMessage message)
        {
            if ((!Application.isPlaying) || (_catalog == null))
            {
                return;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _previewMode = false;
#endif
            if ((message.Essence < 0) || (message.Shards < 0) || (message.LoadoutRevision < 0)
                || ((GameDB.IsInitialized) && (!string.Equals(message.ContentVersion, GameDB.ContentVersion, StringComparison.Ordinal))))
            {
                _hasData = false;
                SetStatus(MISMATCH_TEXT);
                return;
            }

            if ((_hasServerSnapshot) && (message.LoadoutRevision < _revision))
            {
                return;
            }

            _essence  = message.Essence;
            _shards   = message.Shards;
            _revision = message.LoadoutRevision;
            if (!ReadEquipment(message.Equipment, message.Pity))
            {
                _hasData = false;
                SetStatus(MISMATCH_TEXT);
                return;
            }

            _hasData           = true;
            _hasServerSnapshot = true;
            SetStatus(_requestInFlight ? SENDING_TEXT : ReadyStatus());
        }

        // 서버는 결과보다 스냅샷을 먼저 보낸다. 결과가 오면 화면은 이미 새 단계를 보여 주고 있으므로 연출만 띄운다
        private void OnResultReceived(GrowthResultMessage message)
        {
            if ((!_requestInFlight) || (message.OperationId != _pendingOperation) || (message.Action != EGrowthAction.EquipmentEnhance))
            {
                return;
            }

            int from = _pendingFromLevel;
            ClearRequest();
            if (!message.IsAccepted)
            {
                string reason = RejectText.Describe(message.Reason);
                StopAndRefresh(string.IsNullOrEmpty(reason) ? REJECT_TEXT : reason);
                return;
            }

            bool succeeded = message.Outcome == GrowthCodes.Outcome.SUCCEEDED;
            float bonus    = ((_catalog != null) && (!EquipmentEnhanceRules.IsMaxLevel(_catalog, from)))
                ? _catalog.GetEnhancementStep(from + 1).FailBonusRate
                : 0f;
            SetStatus(ReadyStatus());
            PlayResult(succeeded, from, bonus);
        }

        private void StopAndRefresh(string status)
        {
            ClearRequest();
            _hasData           = false;
            _hasServerSnapshot = false;
            if (NetworkClient.isConnected)
            {
                TownGrowthClient.RequestSnapshot();
            }

            SetStatus(status);
        }

        // 시간 초과나 백엔드 장애로 끝내면 이 창은 같은 번호로 다시 묻지 않고 서버 상태를 다시 읽는다.
        // 그래서 공용 잠금도 함께 푼다. 이미 결과가 온 요청이면 잠금이 먼저 풀려 아무 일도 없다
        private void ClearRequest()
        {
            if (_requestWatcher != null)
            {
                StopCoroutine(_requestWatcher);
                _requestWatcher = null;
            }

            TownGrowthClient.ReleaseWrite(_pendingOperation);
            _requestInFlight  = false;
            _pendingOperation = Guid.Empty;
        }

        private void OnWriteLockChanged()
        {
            if ((!Application.isPlaying) || (_requestInFlight))
            {
                return;
            }

            SetStatus(ReadyStatus());
        }

        // 부위마다 착용한 장비 하나만 읽는다. 카탈로그에 없는 정의나 범위를 벗어난 단계가 있으면 거절한다
        private bool ReadEquipment(TownEquipmentEntry[] equipment, TownPityEntry[] pity)
        {
            Array.Clear(_itemIds, 0, SLOT_COUNT);
            Array.Clear(_definitionIds, 0, SLOT_COUNT);
            Array.Clear(_levels, 0, SLOT_COUNT);
            Array.Clear(_inMarket, 0, SLOT_COUNT);
            for (int i = 0; i < equipment.Length; ++i)
            {
                TownEquipmentEntry entry = equipment[i];
                if ((!entry.Equipped) || (entry.Slot >= SLOT_COUNT))
                {
                    continue;
                }

                if ((!_catalog.TryGetEquipment(entry.DefinitionId, out EquipmentCatalogSO.EquipmentDefinition definition))
                    || ((int)definition.Slot != entry.Slot) || (entry.Enhancement < 0)
                    || (entry.Enhancement > _catalog.MaxEnhancement))
                {
                    return false;
                }

                _itemIds[entry.Slot]       = entry.ItemId;
                _definitionIds[entry.Slot] = entry.DefinitionId;
                _levels[entry.Slot]        = entry.Enhancement;
                _inMarket[entry.Slot]      = entry.InMarket;
            }

            _pity.Clear();
            if (pity != null)
            {
                _pity.AddRange(pity);
            }

            return true;
        }

        private void SetPity(EEquipmentSlot slot, int stage, int failures)
        {
            for (int i = 0; i < _pity.Count; ++i)
            {
                if ((_pity[i].Slot == (byte)slot) && (_pity[i].Stage == stage))
                {
                    _pity[i] = new TownPityEntry((byte)slot, stage, failures);
                    return;
                }
            }

            _pity.Add(new TownPityEntry((byte)slot, stage, failures));
        }

        private int SelectedFailures()
        {
            return EquipmentEnhanceRules.FindFailures(_pity, (EEquipmentSlot)_selectedIndex, _levels[_selectedIndex]);
        }

        private void SetStatus(string status)
        {
            _status = status;
            RefreshAll();
        }

        private void RefreshAll()
        {
            bool ready = (_catalog != null) && (_hasData);
            RefreshSlots(ready);
            RefreshSelected(ready);

            SetText(_essenceText, ready ? $"{_essence:N0}" : "-");
            SetText(_shardText, ready ? $"{_shards:N0}" : "-");
            SetText(_statusText, _status);
            SetText(_noteText, PreviewMode ? PREVIEW_NOTE_TEXT : KEEP_NOTE_TEXT);
            SetInteractable(_enhanceButton, (ready) && (!_requestInFlight) && (TryValidateSelected(out string _)));
            SetInteractable(_closeButton, !_requestInFlight);
            if (_previewTag != null)
            {
                _previewTag.SetActive(PreviewMode);
            }
        }

        private void RefreshSlots(bool ready)
        {
            for (int i = 0; i < SLOT_COUNT; ++i)
            {
                bool       hasItem = (ready) && (!string.IsNullOrEmpty(_itemIds[i]));
                EItemGrade grade   = hasItem ? GradeOf(_definitionIds[i]) : EItemGrade.Common;

                SetText(ElementAt(_slotNameTexts, i), hasItem
                    ? EquipmentEnhanceRules.DisplayName(_catalog, _definitionIds[i])
                    : EquipmentEnhanceRules.SlotName((EEquipmentSlot)i) + " 비어 있음");
                SetText(ElementAt(_slotLevelTexts, i), hasItem ? $"+{_levels[i]}" : string.Empty);
                SetColor(ElementAt(_slotNameTexts, i), hasItem ? _normalColor : _mutedColor);
                SetSprite(ElementAt(_slotFrames, i), ElementAt(_gradeFrames, (int)grade));
                SetSprite(ElementAt(_slotIcons, i), ElementAt(_slotSprites, i));
                SetColor(ElementAt(_slotIcons, i), hasItem ? Color.white : _mutedColor);

                GameObject selection = ElementAt(_slotSelections, i);
                if (selection != null)
                {
                    selection.SetActive(i == _selectedIndex);
                }

                SetInteractable(ElementAt(_slotButtons, i), !_requestInFlight);
            }
        }

        private void RefreshSelected(bool ready)
        {
            int  index   = _selectedIndex;
            bool hasItem = (ready) && (!string.IsNullOrEmpty(_itemIds[index]));
            EquipmentCatalogSO.EquipmentDefinition definition = null;
            if (hasItem)
            {
                _catalog.TryGetEquipment(_definitionIds[index], out definition);
            }

            EItemGrade grade = (definition != null) ? definition.Grade : EItemGrade.Common;
            SetSprite(_itemFrame, ElementAt(_gradeFrames, (int)grade));
            SetSprite(_itemIcon, ElementAt(_slotSprites, index));
            SetColor(_itemIcon, hasItem ? Color.white : _mutedColor);
            SetText(_itemNameText, hasItem
                ? EquipmentEnhanceRules.DisplayName(_catalog, _definitionIds[index])
                : EquipmentEnhanceRules.SlotName((EEquipmentSlot)index));
            SetText(_itemGradeText, (definition != null)
                ? (definition.IsTraining ? "수련용 장비" : EquipmentEnhanceRules.GradeName(grade) + " 등급")
                : "착용한 장비 없음");

            if ((definition == null) || (EquipmentEnhanceRules.IsMaxLevel(_catalog, _levels[index])))
            {
                bool isMax = definition != null;
                SetText(_levelFromText, string.Empty);
                SetText(_levelToText, isMax ? $"+{_levels[index]}" : "-");
                SetActive(_levelArrow, false);
                SetText(_rateText, isMax ? "완료" : "-");
                SetText(_rateCaption, isMax ? "최대 강화 단계" : "성공 확률");
                SetText(_pityText, isMax ? MAX_PITY_TEXT : string.Empty);
                SetFill(_pityFill, isMax ? FULL_RATE : 0f);
                SetText(_essenceCostText, "-");
                SetText(_shardCostText, "-");
                SetColor(_essenceCostText, _normalColor);
                SetColor(_shardCostText, _normalColor);
                RefreshStats(definition, isMax ? _levels[index] : 0, isMax ? _levels[index] : 0);
                SetText(_enhanceLabel, isMax ? "최대 단계" : "강화");
                return;
            }

            int   level    = _levels[index];
            int   failures = SelectedFailures();
            float rate     = EquipmentEnhanceRules.SuccessRate(_catalog, level, failures);
            int   attempts = EquipmentEnhanceRules.AttemptsUntilGuaranteed(_catalog, level, failures);
            EquipmentCatalogSO.EnhancementStep step = _catalog.GetEnhancementStep(level + 1);

            SetActive(_levelArrow, true);
            SetText(_levelFromText, $"+{level}");
            SetText(_levelToText, $"+{level + 1}");
            SetText(_rateText, rate >= FULL_RATE ? "확정" : $"{rate * PERCENT:0.#}%");
            SetColor(_rateText, rate >= FULL_RATE ? _successColor : _normalColor);
            SetText(_rateCaption, rate >= FULL_RATE ? "이번 시도는 반드시 성공합니다" : "성공 확률");
            SetFill(_pityFill, EquipmentEnhanceRules.PityProgress(_catalog, level, failures));
            SetText(_pityText, PityLine(step, failures, attempts));

            SetText(_essenceCostText, $"{step.EssenceCost:N0}");
            SetText(_shardCostText, $"{step.ShardCost:N0}");
            SetColor(_essenceCostText, step.EssenceCost > _essence ? _lackColor : _normalColor);
            SetColor(_shardCostText, step.ShardCost > _shards ? _lackColor : _normalColor);
            RefreshStats(definition, level, level + 1);
            SetText(_enhanceLabel, "강화");
        }

        private string PityLine(EquipmentCatalogSO.EnhancementStep step, int failures, int attempts)
        {
            if (step.FailBonusRate <= 0f)
            {
                return (step.BaseSuccessRate >= FULL_RATE) ? "이 단계는 항상 성공합니다" : "이 단계는 실패 보정이 없습니다";
            }

            string bonus = $"실패 시 +{step.FailBonusRate * PERCENT:0.#}%p";
            if (attempts == 1)
            {
                return $"실패 {failures}회 누적, 이번 시도 확정 성공";
            }

            return $"실패 {failures}회 누적, {bonus}, {attempts}회째 확정";
        }

        private void RefreshStats(EquipmentCatalogSO.EquipmentDefinition definition, int fromLevel, int toLevel)
        {
            int row = 0;
            if (definition != null)
            {
                EquipmentCatalogSO.SlotBaseStats slotBase = _catalog.GetSlotBase(definition.Slot);
                row = WriteStat(row, 0, definition, slotBase.Attack, fromLevel, toLevel);
                row = WriteStat(row, 1, definition, slotBase.Health, fromLevel, toLevel);
                row = WriteStat(row, 2, definition, slotBase.Defense, fromLevel, toLevel);
            }

            for (int i = row; i < STAT_ROW_COUNT; ++i)
            {
                GameObject target = ElementAt(_statRows, i);
                if (target != null)
                {
                    target.SetActive(false);
                }
            }
        }

        private int WriteStat(int row, int statIndex, EquipmentCatalogSO.EquipmentDefinition definition, float baseValue,
            int fromLevel, int toLevel)
        {
            if ((baseValue <= 0f) || (row >= STAT_ROW_COUNT))
            {
                return row;
            }

            float before = EquipmentEnhanceRules.ScaledStat(_catalog, definition, baseValue, fromLevel);
            float after  = EquipmentEnhanceRules.ScaledStat(_catalog, definition, baseValue, toLevel);
            GameObject target = ElementAt(_statRows, row);
            if (target != null)
            {
                target.SetActive(true);
            }

            // 최대 단계처럼 오를 단계가 없으면 현재 값 하나만 보여 준다
            bool changes = fromLevel != toLevel;
            GameObject arrow = ElementAt(_statArrows, row);
            if (arrow != null)
            {
                arrow.SetActive(changes);
            }

            SetText(ElementAt(_statNameTexts, row), G_StatNames[statIndex]);
            SetText(ElementAt(_statBeforeTexts, row), changes ? $"{before:N0}" : string.Empty);
            SetText(ElementAt(_statAfterTexts, row), changes ? $"{after:N0}  (+{after - before:N0})" : $"{after:N0}");
            return row + 1;
        }

        private void PlayResult(bool succeeded, int fromLevel, float failBonus)
        {
            if ((_resultGroup == null) || (_resultBadge == null))
            {
                return;
            }

            SetText(_resultTitleText, succeeded ? $"+{fromLevel + 1} 강화 성공" : "강화 실패");
            SetColor(_resultTitleText, succeeded ? _successColor : _failColor);
            SetText(_resultDetailText, succeeded
                ? "장비 능력치가 올랐습니다"
                : $"단계 +{fromLevel} 유지, 다음 성공률 +{failBonus * PERCENT:0.#}%p");

            if (!Application.isPlaying)
            {
                return;
            }

            EnsureResultSequence();
            _resultSequence.Restart();
        }

        // 결과 연출은 매번 같은 모양이라 한 번 만들어 Restart로 재사용한다.
        // CanvasGroup.DOFade는 Shinshi.Runtime이 참조하지 못하는 DOTween 모듈에 있어 코어 DOTween.To를 쓴다
        private void EnsureResultSequence()
        {
            if (_resultSequence != null)
            {
                return;
            }

            _resultSequence = DOTween.Sequence()
                .Append(DOTween.To(GetResultAlpha, SetResultAlpha, 1f, _resultFadeTime))
                .Join(DOTween.To(GetBadgeScale, SetBadgeScale, 1f, _resultFadeTime).From(_resultPopScale))
                .AppendInterval(_resultHoldTime)
                .Append(DOTween.To(GetResultAlpha, SetResultAlpha, 0f, _resultFadeTime))
                .SetAutoKill(false)
                .Pause()
                .SetLink(gameObject);
        }

        private float GetResultAlpha() { return _resultGroup.alpha; }

        private void SetResultAlpha(float alpha) { _resultGroup.alpha = alpha; }

        private float GetBadgeScale() { return _resultBadge.localScale.x; }

        private void SetBadgeScale(float scale) { _resultBadge.localScale = Vector3.one * scale; }

        private void HideResultImmediate()
        {
            if (_resultGroup != null)
            {
                _resultGroup.alpha          = 0f;
                _resultGroup.blocksRaycasts = false;
                _resultGroup.interactable   = false;
            }
        }

        private EItemGrade GradeOf(string definitionId)
        {
            return _catalog.TryGetEquipment(definitionId, out EquipmentCatalogSO.EquipmentDefinition definition)
                ? definition.Grade
                : EItemGrade.Common;
        }

        private void BindButtons()
        {
            UnbindButtons();
            if (!_selectActionsReady)
            {
                for (int i = 0; i < SLOT_COUNT; ++i)
                {
                    int index = i;
                    _selectActions[i] = () => SelectSlot(index);
                }

                _selectActionsReady = true;
            }

            if (_slotButtons != null)
            {
                _boundSlotButtons = (Button[])_slotButtons.Clone();
                for (int i = 0; i < Math.Min(_boundSlotButtons.Length, SLOT_COUNT); ++i)
                {
                    AddListener(_boundSlotButtons[i], _selectActions[i]);
                }
            }

            _boundEnhanceButton = _enhanceButton;
            _boundCloseButton   = _closeButton;
            AddListener(_boundEnhanceButton, Enhance);
            AddListener(_boundCloseButton, Close);
        }

        private void UnbindButtons()
        {
            if (_boundSlotButtons != null)
            {
                for (int i = 0; i < Math.Min(_boundSlotButtons.Length, SLOT_COUNT); ++i)
                {
                    RemoveListener(_boundSlotButtons[i], _selectActions[i]);
                }

                _boundSlotButtons = null;
            }

            RemoveListener(_boundEnhanceButton, Enhance);
            RemoveListener(_boundCloseButton, Close);
        }

        private static T ElementAt<T>(T[] values, int index) where T : class
        {
            return ((values != null) && (index >= 0) && (index < values.Length)) ? values[index] : null;
        }

        private static void SetText(TMP_Text label, string text)
        {
            if (label != null)
            {
                label.text = text;
            }
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null)
            {
                target.SetActive(active);
            }
        }

        private static void SetColor(Graphic graphic, Color color)
        {
            if (graphic != null)
            {
                graphic.color = color;
            }
        }

        private static void SetSprite(Image image, Sprite sprite)
        {
            if ((image != null) && (sprite != null))
            {
                image.sprite = sprite;
            }
        }

        private static void SetFill(Image image, float amount)
        {
            if (image != null)
            {
                image.fillAmount = amount;
            }
        }

        private static void SetInteractable(Button button, bool interactable)
        {
            if (button != null)
            {
                button.interactable = interactable;
            }
        }

        private static void AddListener(Button button, UnityAction action)
        {
            if ((button != null) && (action != null))
            {
                button.onClick.AddListener(action);
            }
        }

        private static void RemoveListener(Button button, UnityAction action)
        {
            if ((button != null) && (action != null))
            {
                button.onClick.RemoveListener(action);
            }
        }
    }
}
