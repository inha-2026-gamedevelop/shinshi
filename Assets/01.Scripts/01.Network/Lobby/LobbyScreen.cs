// System
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
    /// <summary> 로비 화면. 런처 세션을 기다리거나 개발 로그인을 받은 뒤 캐릭터 선택으로 넘어간다 </summary>
    public sealed class LobbyScreen : ClientView
    {
        // 출시 빌드는 런처로만 로그인한다. 개발 로그인 패널은 에디터와 개발 빌드에서 런처 없이 실행했을 때만 보인다

        /****************************************
        *                Fields
        ****************************************/

        [Header("상태")]
        [SerializeField] private TMP_Text   _status       = null;
        [SerializeField] private Button     _startButton  = null;
        [SerializeField] private Button     _quitButton   = null;

        [Header("개발 로그인")]
        [SerializeField] private GameObject     _loginPanel    = null;
        [SerializeField] private TMP_InputField _apiUrlInput   = null;
        [SerializeField] private TMP_InputField _loginIdInput  = null;
        [SerializeField] private TMP_InputField _passwordInput = null;
        [SerializeField] private TMP_InputField _emailInput    = null;
        [SerializeField] private Button         _loginButton   = null;
        [SerializeField] private Button         _signupButton  = null;

        private readonly LobbyApiClient _api = new LobbyApiClient();

        private bool _isBusy = false;

        /****************************************
        *              Properties
        ****************************************/

        private static bool HasValidSession => LauncherSession.HasSession && !LauncherSession.IsExpired;

        private static bool CanDevelopmentLogin => (!LauncherSession.IsLaunchRequired)
            && (LauncherSession.State != ELauncherSessionState.Receiving) && (!HasValidSession);

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            if ((_status == null) || (_startButton == null) || (_quitButton == null) || (_loginPanel == null)
                || (_apiUrlInput == null) || (_loginIdInput == null) || (_passwordInput == null) || (_emailInput == null)
                || (_loginButton == null) || (_signupButton == null))
            {
                Debug.LogError("[Lobby] 로비 화면 참조가 연결되지 않았다", this);
                enabled = false;
                return;
            }

            _startButton.onClick.AddListener(OnStartClicked);
            _quitButton.onClick.AddListener(OnQuitClicked);
            _loginButton.onClick.AddListener(OnLoginClicked);
            _signupButton.onClick.AddListener(OnSignupClicked);
            _passwordInput.contentType = TMP_InputField.ContentType.Password;
        }

        private void Start()
        {
            if (!LobbyFlow.EnsureBootstrapped())
            {
                return;
            }

            // 꺼진 입력란에 넣은 글자는 켜질 때 지워질 수 있어 패널을 먼저 켠 뒤 채운다
            LauncherSession.StateChanged += OnSessionStateChanged;
            RefreshControls();
            _apiUrlInput.text = LobbyFlow.ReadDevApiBaseUrl();
            ShowSessionStatus();
        }

        private void OnDestroy()
        {
            LauncherSession.StateChanged -= OnSessionStateChanged;
            _api.Abort();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnSessionStateChanged(ELauncherSessionState state)
        {
            RefreshControls();
            ShowSessionStatus();
        }

        // 버튼과 패널만 바꾼다. 안내 문구는 로그인 실패 사유를 남겨야 하므로 따로 쓴다
        private void RefreshControls()
        {
            bool busy = _isBusy;
            _startButton.interactable  = HasValidSession && !busy;
            _loginPanel.SetActive(CanDevelopmentLogin);
            _loginButton.interactable  = !busy;
            _signupButton.interactable = !busy;
        }

        private void ShowSessionStatus()
        {
            if (HasValidSession)
            {
                _status.text = "로그인했다. 게임 시작을 누른다.";
            }
            else if (LauncherSession.State == ELauncherSessionState.Receiving)
            {
                _status.text = "런처에서 로그인 정보를 받는 중이다.";
            }
            else if (CanDevelopmentLogin)
            {
                _status.text = "개발 로그인: 아이디와 비밀번호를 넣는다.";
            }
            else
            {
                _status.text = "런처로 다시 실행한다.";
            }
        }

        private void OnStartClicked()
        {
            if (LauncherSession.HasSession && !LauncherSession.IsExpired)
            {
                LobbyFlow.GoToCharacterSelect();
            }
        }

        private void OnQuitClicked()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnLoginClicked()  { Run(CoLogin(false)); }
        private void OnSignupClicked() { Run(CoLogin(true)); }

        private void Run(IEnumerator operation)
        {
            if (_isBusy)
            {
                return;
            }

            // 작업이 바로 끝나면 StartCoroutine이 돌아오기 전에 CoRun이 끝나므로 표시를 먼저 세운다
            _isBusy = true;
            StartCoroutine(CoRun(operation));
            RefreshControls();
        }

        private IEnumerator CoRun(IEnumerator operation)
        {
            yield return operation;
            _isBusy = false;
            RefreshControls();
        }

        // 가입이면 계정을 먼저 만든다. 그 뒤 아이디, 비밀번호로 접근 토큰을 받고 게임 하위 세션으로 바꾼다
        private IEnumerator CoLogin(bool signup)
        {
            string apiBaseUrl = _apiUrlInput.text.Trim();
            string loginId    = _loginIdInput.text.Trim();
            string password   = _passwordInput.text;
            if ((apiBaseUrl.Length == 0) || (loginId.Length == 0) || (password.Length == 0))
            {
                _status.text = "API 주소, 아이디, 비밀번호를 모두 넣는다.";
                yield break;
            }

            ClientBuildInfo build = LobbyFlow.CreateBuildInfo();
            if (build == null)
            {
                _status.text = "게임 데이터를 읽지 못해 로그인할 수 없다.";
                yield break;
            }

            if (signup)
            {
                _status.text = "계정을 만드는 중이다.";
                LobbyApiReply<AccountSignupResponse> created = null;
                yield return _api.Send<AccountSignupResponse>(apiBaseUrl, null, ApiRoutes.ACCOUNTS,
                    new AccountSignupRequest(loginId, password, _emailInput.text.Trim()), reply => created = reply);
                if ((created == null) || (!created.IsSuccess))
                {
                    _status.text = LobbyText.Describe(created);
                    yield break;
                }
            }

            _status.text = "로그인하는 중이다.";
            LobbyApiReply<AuthExchangeResponse> exchanged = null;
            yield return _api.Send<AuthExchangeResponse>(apiBaseUrl, null, ApiRoutes.AUTH_EXCHANGE,
                AuthExchangeRequest.ForCredentials(loginId, password, null), reply => exchanged = reply);
            if ((exchanged == null) || (!exchanged.IsSuccess) || string.IsNullOrEmpty(exchanged.Value.AccessToken))
            {
                _status.text = LobbyText.Describe(exchanged);
                yield break;
            }

            LobbyApiReply<GameSessionIssueResponse> issued = null;
            yield return _api.Send<GameSessionIssueResponse>(apiBaseUrl, exchanged.Value.AccessToken, ApiRoutes.AUTH_GAME_SESSION,
                new GameSessionIssueRequest(build), reply => issued = reply);
            if ((issued == null) || (!issued.IsSuccess) || string.IsNullOrEmpty(issued.Value.GameSessionToken))
            {
                _status.text = LobbyText.Describe(issued);
                yield break;
            }

            GameSessionIssueResponse session = issued.Value;
            var handoff = new LauncherHandoff(LauncherPipeProtocol.PROTOCOL_VERSION, session.AccountId, session.GameSessionToken,
                session.ExpiresAtUnixMs, session.ServerTimeUnixMs, apiBaseUrl);
            if (!LauncherSession.AcceptDevelopmentLogin(handoff))
            {
                _status.text = "로그인 정보를 받지 못했다. 게임을 다시 실행한다.";
                yield break;
            }

            _passwordInput.text = string.Empty;
            LobbyFlow.GoToCharacterSelect();
        }
    }
}
