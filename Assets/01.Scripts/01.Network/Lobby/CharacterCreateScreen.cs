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
    /// <summary> 캐릭터 생성 화면. 직업, P09 외형, 닉네임을 정해 API로 만들고, 만든 캐릭터를 고른 채 선택 화면으로 돌아간다 </summary>
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

        [Header("커스터마이즈")]
        [SerializeField] private PlayerFemaleLookPreviewView _preview        = null;
        [SerializeField] private CharacterCustomizePanel     _customizePanel = null;

        [Header("닉네임")]
        [SerializeField] private TMP_InputField _nameInput   = null;
        [SerializeField] private Button         _checkButton = null;

        [Header("버튼")]
        [SerializeField] private Button _createButton = null;
        [SerializeField] private Button _cancelButton = null;

        [Header("안내")]
        [SerializeField] private TMP_Text _status = null;

        private readonly LobbyApiClient _api = new LobbyApiClient();

        private int                    _classId         = ContractCodes.PlayerClass.SWORDSMAN;
        private CharacterP09Selection  _selection       = null;
        private bool                   _isBusy          = false;
        private CharacterCreateRequest _pendingCreation = null;

        /****************************************
        *              Properties
        ****************************************/

        // DevCustomizationScenario와 자동화 검증용 공개 읽기 접근자
        public TMP_InputField          NameInput      => _nameInput;
        public Button                  CheckButton    => _checkButton;
        public Button                  CreateButton   => _createButton;
        public bool                    IsBusy         => _isBusy;
        public bool                    IsPreviewReady => (_preview != null) && (_preview.IsReady);
        public TMP_Text                StatusText     => _status;
        public CharacterCustomizePanel CustomizePanel => _customizePanel;

        public Button GetClassButton(int classId)
        {
            int index = classId - ContractCodes.PlayerClass.SWORDSMAN;
            return ((_classButtons != null) && (index >= 0) && (index < _classButtons.Length)) ? _classButtons[index] : null;
        }

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            if ((_classButtons == null) || (_classButtons.Length == 0) || (_classNameText == null) || (_classDescription == null)
                || (_preview == null) || (_customizePanel == null) || (!_customizePanel.IsConfigured) || (_nameInput == null)
                || (_checkButton == null) || (_createButton == null) || (_cancelButton == null) || (_status == null))
            {
                Debug.LogError("[CharacterCreate] 생성 화면 참조가 연결되지 않았다", this);
                enabled = false;
                return;
            }

            for (int i = 0; i < _classButtons.Length; ++i)
            {
                int classId = i + ContractCodes.PlayerClass.SWORDSMAN;
                _classButtons[i].onClick.AddListener(() => SelectClass(classId));
            }

            _customizePanel.Changed        += OnCustomizeChanged;
            _customizePanel.EmotionChanged += OnEmotionChanged;
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

            _selection = new CharacterP09Selection(ContractCodes.PlayerClass.SWORDSMAN);
            _customizePanel.Bind(_selection);
            SelectClass(ContractCodes.PlayerClass.SWORDSMAN);
            _status.text = $"직업과 외형을 고르고 {CharacterNameRules.MIN_LENGTH}~{CharacterNameRules.MAX_LENGTH}자 닉네임을 정한다.";
        }

        private void OnDestroy()
        {
            _api.Abort();
            if (_customizePanel != null)
            {
                _customizePanel.Changed        -= OnCustomizeChanged;
                _customizePanel.EmotionChanged -= OnEmotionChanged;
            }
        }

        private void Update()
        {
            if ((!_isBusy) && (_createButton != null))
            {
                _createButton.interactable = IsPreviewReady;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void SelectClass(int classId)
        {
            if ((_isBusy) || (_selection == null))
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
                        _classHighlights[i].color = (i + ContractCodes.PlayerClass.SWORDSMAN == classId) ? _selectedColor : _normalColor;
                    }
                }
            }

            // 사용자가 직접 고른 항목은 유지하고, 아직 고르지 않은 항목만 새 직업 기본값으로 바꾼다
            _selection.ApplyClassDefaults(classId);
            _customizePanel.Bind(_selection);
            RefreshPreview();
        }

        private void OnCustomizeChanged()
        {
            RefreshPreview();
        }

        private void OnEmotionChanged(int emotion)
        {
            _preview.SetFaceEmotion(emotion);
        }

        private void RefreshPreview()
        {
            _createButton.interactable = false;
            _preview.Show(_selection.ToLook());
            if (!_preview.IsReady)
            {
                _status.text = LobbyText.PreviewFailed();
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

            _customizePanel.SetInteractable(value);
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

            CharacterP09Look    look       = _selection.ToLook();
            CharacterAppearance appearance = look.ToAppearance();

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
            LobbyFlow.PreferredCharacterId   = reply.Value.Character.CharacterId;
            LobbyFlow.JustCreatedCharacterId = reply.Value.Character.CharacterId;
            LobbyFlow.GoToCharacterSelect();
        }

        private static CharacterP09Look PendingLook(CharacterCreateRequest pending)
        {
            CharacterP09Look.TryFrom(pending.Appearance, out CharacterP09Look look);
            return look;
        }
    }
}
