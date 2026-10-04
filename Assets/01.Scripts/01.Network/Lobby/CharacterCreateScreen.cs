// System
using System;
using System.Collections;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 캐릭터 생성 화면. 직업, 얼굴/헤어/의상, 닉네임을 정해 API로 만들고, 만든 캐릭터를 고른 채 선택 화면으로 돌아간다 </summary>
    public sealed class CharacterCreateScreen : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        // 직업 버튼과 미리보기는 ContractCodes.PlayerClass 값(무사, 도술사, 신령사) 순서로 둔다
        [Header("직업")]
        [SerializeField] private Button[]     _classButtons     = null;
        [SerializeField] private Image[]      _classHighlights  = null;
        [SerializeField] private TMP_Text     _classNameText    = null;
        [SerializeField] private TMP_Text     _classDescription = null;
        [SerializeField] private Color        _selectedColor    = new Color(0.86f, 0.78f, 0.58f, 0.35f);
        [SerializeField] private Color        _normalColor      = new Color(1f, 1f, 1f, 0.06f);

        // 탭/옵션 패널은 ECharacterLookPart 값(얼굴, 헤어, 옷) 순서로 둔다
        [Header("커스터마이즈")]
        [SerializeField] private PlayerFemaleLookPreviewView    _preview          = null;
        [SerializeField] private CharacterLookOptionButton[]    _lookOptions      = null;
        [SerializeField] private Button[]                       _lookTabs         = null;
        [SerializeField] private GameObject[]                   _lookOptionPanels = null;

        [Header("닉네임")]
        [SerializeField] private TMP_InputField _nameInput   = null;
        [SerializeField] private Button         _checkButton = null;

        [Header("버튼")]
        [SerializeField] private Button _createButton = null;
        [SerializeField] private Button _cancelButton = null;

        [Header("안내")]
        [SerializeField] private TMP_Text _status = null;

        private readonly LobbyApiClient _api = new LobbyApiClient();

        private int                    _classId         = ContractCodes.PlayerClass.WARRIOR;
        private CharacterLookSelection _selection        = null;
        private bool                   _isBusy          = false;
        private CharacterCreateRequest _pendingCreation = null;
        private ECharacterLookPart     _activeLookTab   = ECharacterLookPart.Face;

        /****************************************
        *              Properties
        ****************************************/

        // DevCustomizationScenario와 자동화 검증용 공개 읽기 접근자
        public TMP_InputField NameInput      => _nameInput;
        public Button         CheckButton    => _checkButton;
        public Button         CreateButton   => _createButton;
        public bool           IsBusy         => _isBusy;
        public bool           IsPreviewReady => (_preview != null) && (_preview.IsReady);
        public TMP_Text       StatusText     => _status;

        public Button GetClassButton(int classId)
        {
            return ((_classButtons != null) && (classId >= 0) && (classId < _classButtons.Length)) ? _classButtons[classId] : null;
        }

        public Button GetLookOptionButton(ECharacterLookPart part, int id)
        {
            if (_lookOptions == null)
            {
                return null;
            }

            for (int i = 0; i < _lookOptions.Length; ++i)
            {
                if ((_lookOptions[i] != null) && (_lookOptions[i].Part == part) && (_lookOptions[i].Id == id))
                {
                    return _lookOptions[i].Button;
                }
            }

            return null;
        }

        // DevScenario는 옵션을 고르기 전에 실제 해당 탭을 먼저 눌러야 한다
        public Button GetLookTabButton(ECharacterLookPart part)
        {
            int index = (int)part;
            return ((_lookTabs != null) && (index >= 0) && (index < _lookTabs.Length)) ? _lookTabs[index] : null;
        }

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            if ((_classButtons == null) || (_classButtons.Length == 0) || (_classNameText == null) || (_classDescription == null)
                || (_preview == null) || (_lookOptions == null) || (_lookTabs == null) || (_lookTabs.Length == 0)
                || (_lookOptionPanels == null) || (_lookOptionPanels.Length != _lookTabs.Length) || (_nameInput == null)
                || (_checkButton == null) || (_createButton == null) || (_cancelButton == null) || (_status == null))
            {
                Debug.LogError("[CharacterCreate] 생성 화면 참조가 연결되지 않았다", this);
                enabled = false;
                return;
            }

            for (int i = 0; i < _classButtons.Length; ++i)
            {
                int classId = i;
                _classButtons[i].onClick.AddListener(() => SelectClass(classId));
            }

            for (int i = 0; i < _lookOptions.Length; ++i)
            {
                CharacterLookOptionButton option = _lookOptions[i];
                if ((option == null) || (option.Button == null))
                {
                    continue;
                }

                ECharacterLookPart part = option.Part;
                int id = option.Id;
                option.Button.onClick.AddListener(() => SelectLookOption(part, id));
            }

            for (int i = 0; i < _lookTabs.Length; ++i)
            {
                if (_lookTabs[i] == null)
                {
                    continue;
                }

                ECharacterLookPart part = (ECharacterLookPart)i;
                _lookTabs[i].onClick.AddListener(() => SelectLookTab(part));
            }

            _checkButton.onClick.AddListener(OnCheckClicked);
            _createButton.onClick.AddListener(OnCreateClicked);
            _cancelButton.onClick.AddListener(OnCancelClicked);
            _nameInput.characterLimit = CharacterNameRules.MAX_LENGTH;
            _createButton.interactable = false;
        }

        private void Start()
        {
            if (!LobbyFlow.EnsureBootstrapped())
            {
                return;
            }

            if (!LauncherSession.HasSession || LauncherSession.IsExpired)
            {
                LobbyFlow.GoToLobby();
                return;
            }

            _selection = new CharacterLookSelection(ContractCodes.PlayerClass.WARRIOR);
            SelectClass(ContractCodes.PlayerClass.WARRIOR);
            SelectLookTab(ECharacterLookPart.Face);
            _status.text = $"직업과 외형을 고르고 {CharacterNameRules.MIN_LENGTH}~{CharacterNameRules.MAX_LENGTH}자 닉네임을 정한다.";
        }

        private void OnDestroy()
        {
            _api.Abort();
            if (_lookTabs != null)
            {
                for (int i = 0; i < _lookTabs.Length; ++i)
                {
                    if (_lookTabs[i] != null)
                    {
                        _lookTabs[i].onClick.RemoveAllListeners();
                    }
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void SelectClass(int classId)
        {
            if (_isBusy)
            {
                return;
            }

            _classId = classId;
            _classNameText.text    = LobbyText.ClassName(classId);
            _classDescription.text = LobbyText.ClassDescription(classId);
            if (_classHighlights != null)
            {
                for (int i = 0; i < _classHighlights.Length; ++i)
                {
                    if (_classHighlights[i] != null)
                    {
                        _classHighlights[i].color = (i == classId) ? _selectedColor : _normalColor;
                    }
                }
            }

            // 사용자가 직접 고른 헤어/의상은 유지하고, 아직 고르지 않은 부위만 새 직업 기본값으로 바꾼다
            _selection.ApplyClassDefaults(classId);
            RefreshPreview();
        }

        private void SelectLookOption(ECharacterLookPart part, int id)
        {
            if (_isBusy)
            {
                return;
            }

            switch (part)
            {
                case ECharacterLookPart.Hair:
                    _selection.SelectHair(id);
                    break;
                case ECharacterLookPart.Outfit:
                    _selection.SelectOutfit(id);
                    break;
                default:
                    break;
            }

            RefreshPreview();
        }

        private void RefreshPreview()
        {
            _createButton.interactable = false;
            _preview.Show(_selection.ToLook());
        }

        // 고른 탭의 옵션 패널만 켜고, 나머지는 끈다. 현재 탭 버튼은 busy가 아닐 때도 누르지 못하게 해 선택 상태를 보인다
        private void SelectLookTab(ECharacterLookPart part)
        {
            if (_isBusy)
            {
                return;
            }

            _activeLookTab = part;
            RefreshTabVisuals();
        }

        private void RefreshTabVisuals()
        {
            int activeIndex = (int)_activeLookTab;
            for (int i = 0; i < _lookOptionPanels.Length; ++i)
            {
                if (_lookOptionPanels[i] != null)
                {
                    _lookOptionPanels[i].SetActive(i == activeIndex);
                }
            }

            for (int i = 0; i < _lookTabs.Length; ++i)
            {
                if (_lookTabs[i] != null)
                {
                    _lookTabs[i].interactable = (!_isBusy) && (i != activeIndex);
                }
            }
        }

        private void Update()
        {
            if ((!_isBusy) && (_createButton != null))
            {
                _createButton.interactable = IsPreviewReady;
            }
        }

        private void Run(IEnumerator operation)
        {
            if (_isBusy)
            {
                return;
            }

            // 작업이 바로 끝나면 StartCoroutine이 돌아오기 전에 CoRun이 끝나므로 표시를 먼저 세운다
            _isBusy = true;
            StartCoroutine(CoRun(operation));
            SetInteractable(false);
        }

        private IEnumerator CoRun(IEnumerator operation)
        {
            yield return operation;
            _isBusy = false;
            SetInteractable(true);
        }

        private void SetInteractable(bool value)
        {
            _checkButton.interactable  = value;
            _createButton.interactable = value && IsPreviewReady;
            _cancelButton.interactable = value;
            for (int i = 0; i < _classButtons.Length; ++i)
            {
                _classButtons[i].interactable = value;
            }

            for (int i = 0; i < _lookOptions.Length; ++i)
            {
                if ((_lookOptions[i] != null) && (_lookOptions[i].Button != null))
                {
                    _lookOptions[i].Button.interactable = value;
                }
            }

            RefreshTabVisuals();
        }

        private void OnCheckClicked()  { Run(CoCheckName()); }
        private void OnCreateClicked() { Run(CoCreate()); }
        private void OnCancelClicked() { LobbyFlow.GoToCharacterSelect(); }

        // 형식은 여기서 먼저 보고 중복은 API에 묻는다. 확정은 생성 요청이 한다
        private bool TryReadName(out string name)
        {
            if (!CharacterNameRules.TryNormalize(_nameInput.text, out name, out string _))
            {
                _status.text = LobbyText.Describe(ERejectReason.InvalidCharacterName);
                return false;
            }

            return true;
        }

        private IEnumerator CoCheckName()
        {
            if (!TryReadName(out string name))
            {
                yield break;
            }

            _status.text = "닉네임을 확인하는 중이다.";
            LobbyApiReply<AvailabilityResponse> reply = null;
            string path = ApiRoutes.CHARACTER_NAME_AVAILABILITY + "?name=" + Uri.EscapeDataString(name);
            yield return _api.SendGame<AvailabilityResponse>(path, null, value => reply = value);
            if ((reply == null) || (!reply.IsSuccess))
            {
                _status.text = LobbyText.Describe(reply);
                yield break;
            }

            _status.text = reply.Value.Available ? "쓸 수 있는 닉네임이다." : LobbyText.Describe((ERejectReason)reply.Value.ReasonCode);
        }

        private IEnumerator CoCreate()
        {
            if (!TryReadName(out string name))
            {
                yield break;
            }

            if (!IsPreviewReady)
            {
                _status.text = LobbyText.PreviewFailed();
                yield break;
            }

            CharacterLook look = _selection.ToLook();
            CharacterAppearance appearance = CharacterAppearanceCatalog.Create(look.FaceId, look.HairId, look.OutfitId);

            // 이름, 직업, 외형 전체 스냅샷이 같을 때만 같은 operationId로 다시 보낸다. 선택이 바뀌면 새 작업이다
            bool canReuse = (_pendingCreation != null)
                && CharacterCreateRetryRules.CanReuse(_pendingCreation.Name, _pendingCreation.ClassId,
                    PendingLook(_pendingCreation), name, _classId, look);
            if (!canReuse)
            {
                _pendingCreation = new CharacterCreateRequest(Guid.NewGuid().ToString("D"), name, _classId, appearance);
            }

            _status.text = "캐릭터를 만드는 중이다.";
            LobbyApiReply<CharacterCreateResponse> reply = null;
            yield return _api.SendGame<CharacterCreateResponse>(ApiRoutes.CHARACTERS, _pendingCreation, value => reply = value);
            if ((reply == null) || (!reply.IsSuccess) || (reply.Value.Character == null))
            {
                // 서버가 확정 거절한 작업은 다시 보내도 같은 거절이다. 연결 실패일 때만 같은 요청을 남긴다
                if ((reply != null) && (reply.StatusCode != 0))
                {
                    _pendingCreation = null;
                }

                _status.text = LobbyText.Describe(reply);
                yield break;
            }

            _pendingCreation = null;
            LobbyFlow.PreferredCharacterId = reply.Value.Character.CharacterId;
            LobbyFlow.GoToCharacterSelect();
        }

        private static CharacterLook PendingLook(CharacterCreateRequest pending)
        {
            CharacterLook.TryFrom(pending.Appearance, out CharacterLook look);
            return look;
        }
    }
}
