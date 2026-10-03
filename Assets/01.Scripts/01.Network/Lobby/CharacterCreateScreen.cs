// System
using System;
using System.Collections;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 캐릭터 생성 화면. 직업과 이름을 정해 API로 만들고, 만든 캐릭터를 고른 채 선택 화면으로 돌아간다 </summary>
    public sealed class CharacterCreateScreen : ClientView
    {
        // 외형 슬라이더 화면은 아직 없다. 외형은 계약의 중립값으로 보내고, 슬라이더 화면이 생기면 그 값으로 바꾼다

        /****************************************
        *                Fields
        ****************************************/

        // 직업 버튼과 미리보기는 ContractCodes.PlayerClass 값(무사, 도술사, 신령사) 순서로 둔다
        [Header("직업")]
        [SerializeField] private Button[]     _classButtons     = null;
        [SerializeField] private Image[]      _classHighlights  = null;
        [SerializeField] private GameObject[] _classPreviews    = null;
        [SerializeField] private TMP_Text     _classNameText    = null;
        [SerializeField] private TMP_Text     _classDescription = null;
        [SerializeField] private Color        _selectedColor    = new Color(0.86f, 0.78f, 0.58f, 0.35f);
        [SerializeField] private Color        _normalColor      = new Color(1f, 1f, 1f, 0.06f);

        [Header("이름")]
        [SerializeField] private TMP_InputField _nameInput   = null;
        [SerializeField] private Button         _checkButton = null;

        [Header("버튼")]
        [SerializeField] private Button _createButton = null;
        [SerializeField] private Button _cancelButton = null;

        [Header("안내")]
        [SerializeField] private TMP_Text _status = null;

        private readonly LobbyApiClient _api = new LobbyApiClient();

        private int                    _classId         = ContractCodes.PlayerClass.WARRIOR;
        private bool                   _isBusy          = false;
        private CharacterCreateRequest _pendingCreation = null;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            if ((_classButtons == null) || (_classButtons.Length == 0) || (_classNameText == null) || (_classDescription == null)
                || (_nameInput == null) || (_checkButton == null) || (_createButton == null) || (_cancelButton == null)
                || (_status == null))
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

            _checkButton.onClick.AddListener(OnCheckClicked);
            _createButton.onClick.AddListener(OnCreateClicked);
            _cancelButton.onClick.AddListener(OnCancelClicked);
            _nameInput.characterLimit = CharacterNameRules.MAX_LENGTH;
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

            SelectClass(ContractCodes.PlayerClass.WARRIOR);
            _status.text = $"직업을 고르고 {CharacterNameRules.MIN_LENGTH}~{CharacterNameRules.MAX_LENGTH}자 이름을 정한다.";
        }

        private void OnDestroy() { _api.Abort(); }

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

            if (_classPreviews != null)
            {
                for (int i = 0; i < _classPreviews.Length; ++i)
                {
                    if (_classPreviews[i] != null)
                    {
                        _classPreviews[i].SetActive(i == classId);
                    }
                }
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
            _createButton.interactable = value;
            _cancelButton.interactable = value;
            for (int i = 0; i < _classButtons.Length; ++i)
            {
                _classButtons[i].interactable = value;
            }
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

            _status.text = "이름을 확인하는 중이다.";
            LobbyApiReply<AvailabilityResponse> reply = null;
            string path = ApiRoutes.CHARACTER_NAME_AVAILABILITY + "?name=" + Uri.EscapeDataString(name);
            yield return _api.SendGame<AvailabilityResponse>(path, null, value => reply = value);
            if ((reply == null) || (!reply.IsSuccess))
            {
                _status.text = LobbyText.Describe(reply);
                yield break;
            }

            _status.text = reply.Value.Available ? "쓸 수 있는 이름이다." : LobbyText.Describe((ERejectReason)reply.Value.ReasonCode);
        }

        private IEnumerator CoCreate()
        {
            if (!TryReadName(out string name))
            {
                yield break;
            }

            // 응답을 잃었을 때만 같은 작업 ID와 본문을 다시 보내 중복 생성을 막는다. 직업이나 이름을 바꿨으면 새 작업이다
            if ((_pendingCreation == null) || (_pendingCreation.ClassId != _classId)
                || !string.Equals(_pendingCreation.Name, name, StringComparison.Ordinal))
            {
                _pendingCreation = new CharacterCreateRequest(Guid.NewGuid().ToString("D"), name, _classId, CreateDefaultAppearance());
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

        private static CharacterAppearance CreateDefaultAppearance()
        {
            int[] face = new int[CharacterAppearance.FACE_SLIDER_COUNT];
            int[] body = new int[CharacterAppearance.BODY_SLIDER_COUNT];
            Array.Fill(face, CharacterAppearance.SLIDER_NEUTRAL);
            Array.Fill(body, CharacterAppearance.SLIDER_NEUTRAL);
            return new CharacterAppearance(CharacterAppearance.CURRENT_VERSION, CharacterAppearance.BODY_TYPE_FEMALE,
                face, body, CharacterAppearance.SLIDER_NEUTRAL, CharacterAppearance.VALUE_MIN, CharacterAppearance.VALUE_MIN,
                CharacterAppearance.VALUE_MIN, CharacterAppearance.VALUE_MIN, CharacterAppearance.VALUE_MIN);
        }
    }
}
