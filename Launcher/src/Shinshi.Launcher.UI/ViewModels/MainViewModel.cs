// System
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

// Project
using Shinshi.Contracts;
using Shinshi.Launcher.Account;
using Shinshi.Launcher.Config;
using Shinshi.Launcher.Diagnostics;
using Shinshi.Launcher.Mvvm;
using Shinshi.Launcher.Presentation;
using Shinshi.Launcher.Services;
using Shinshi.Patch;
using Shinshi.Patch.Client;

namespace Shinshi.Launcher.ViewModels
{
    /// <summary> 런처 메인 화면 상태. 설정을 읽고 Patch.Client로 확인, 설치, 업데이트, 복구, 게임 실행을 한다 </summary>
    public sealed class MainViewModel : ObservableObject, IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string KEYART_FOLDER         = "Assets";
        private const string KEYART_NAME           = "keyart";
        private const int    VERSION_FIELD_COUNT   = 3;
        private const double PERCENT_SCALE         = 100.0;
        private const string PERCENT_FORMAT        = "0";
        private const string EMPTY_VALUE           = "-";
        private const string UNEXPECTED_ERROR_TEXT = "예기치 못한 오류가 발생했습니다. 로그 폴더의 런처 로그를 확인해 주세요.";
        private const string SAMPLE_NOTICE_TEXT    = "내부 체험 기간입니다. 문제가 생기면 설정에서 로그 폴더를 열어 전달해 주세요.";
        private const string SERVICE_DOWN_TEXT     = "서버에 연결할 수 없습니다. 지금은 게임을 시작할 수 없습니다.";
        private const string MAINTENANCE_TEXT      = "서버 점검 중입니다.";
        private const string LOGIN_REQUIRED_TEXT   = "게임을 시작하려면 로그인해 주세요.";
        private const string LOGOUT_BLOCKED_TEXT   = "게임 실행 중에는 로그아웃할 수 없습니다";
        private const string SIGNUP_PATH           = "signup";
        private const char   URL_SEPARATOR         = '/';
        private const int    ACCOUNT_SHORT_LENGTH  = 8;
        private const string ACCOUNT_ELLIPSIS      = "...";

        private static readonly string[] G_KeyArtExtensions = { ".jpg", ".png" };

        private readonly Dispatcher         _dispatcher;
        private readonly GameProcess        _game       = new GameProcess();
        private readonly TransferSpeedMeter _speedMeter = new TransferSpeedMeter();
        private readonly Version            _launcherVersion;

        // 런처를 닫을 때 진행 중인 로그인, 세션 전달을 끊는다. 전달 중이면 시작한 게임을 끝내고 세션 폐기를 시도한다
        private readonly CancellationTokenSource _lifetimeCts = new CancellationTokenSource();

        private LauncherSettings?        _settings;
        private PatchClient?             _client;
        private HttpClient?              _httpClient;
        private CancellationTokenSource? _operationCts;
        private AuthApiClient?           _api;
        private LauncherSession?         _session;
        private EPatchStage              _lastStage;
        private bool                     _hasInstall;
        private string                   _installedVersion = string.Empty;
        private string                   _targetVersion    = string.Empty;
        private long                     _bytesToDownload;

        private ELauncherState _state       = ELauncherState.Checking;
        private ELauncherView  _currentView = ELauncherView.Home;

        // 소식 상세에서 뒤로 갈 탭. 상세를 연 순간의 탭이다
        private ELauncherView _newsDetailOrigin = ELauncherView.News;

        private ENewsLoadState          _newsLoadState = ENewsLoadState.Idle;
        private IReadOnlyList<NewsItem> _homeNews      = Array.Empty<NewsItem>();
        private IReadOnlyList<NewsItem> _news          = Array.Empty<NewsItem>();
        private IReadOnlyList<NewsItem> _patchNotes    = Array.Empty<NewsItem>();
        private NewsItem?               _selectedNews;

        // 서비스 상태를 마지막으로 받은 시각(Stopwatch 타임스탬프). 받은 적이 없으면 null
        private long? _lastServiceStateTimestamp;
        private bool  _isServiceStateRefreshing;
        private string         _channel     = PatchFormat.CHANNEL_STABLE;

        private string _errorMessage        = string.Empty;
        private string _noticeText          = string.Empty;
        private bool   _isNoticeMaintenance;
        private string _progressStageText   = string.Empty;
        private double _progressPercent;
        private bool   _isProgressIndeterminate;
        private string _progressPercentText = string.Empty;
        private string _progressSizeText    = string.Empty;
        private string _progressSpeedText   = string.Empty;
        private string _installPathText     = EMPTY_VALUE;
        private string _installRootText     = EMPTY_VALUE;
        private string _patchSourceText     = EMPTY_VALUE;
        private bool   _isTeamBuild;

        private string _loginIdText    = string.Empty;
        private bool   _rememberLogin;
        private bool   _isLoginBusy;
        private string _loginErrorText = string.Empty;

        /****************************************
        *              Properties
        ****************************************/

        public ELauncherState State
        {
            get => _state;
            private set
            {
                if (!SetProperty(ref _state, value))
                {
                    return;
                }

                OnStateChanged();
            }
        }

        public ELauncherView CurrentView
        {
            get => _currentView;
            set
            {
                if (!SetProperty(ref _currentView, value))
                {
                    return;
                }

                OnPropertyChanged(nameof(ActiveTab));
                OnCurrentViewChanged(value);
            }
        }

        // 상단 탭의 선택 표시. 소식 상세에서는 들어온 탭이 선택된 채로 남는다
        public ELauncherView ActiveTab
        {
            get => (_currentView == ELauncherView.NewsDetail) ? _newsDetailOrigin : _currentView;
            set => CurrentView = value;
        }

        // 설정 화면의 채널 선택. 팀 빌드가 아니면 바꿀 수 없다
        public string Channel
        {
            get => _channel;
            set
            {
                if (string.Equals(_channel, value, StringComparison.Ordinal))
                {
                    return;
                }

                RunGuarded(() => ChangeChannelAsync(value));
            }
        }

        public string PrimaryText           => LauncherStateRules.GetPrimaryText(_state);
        public bool   IsPrimaryEnabled      => LauncherStateRules.IsPrimaryEnabled(_state);
        public bool   IsSecondaryStyle      => LauncherStateRules.IsSecondaryStyle(_state);
        public bool   IsOperationRunning    => LauncherStateRules.IsOperationRunning(_state);
        public string GameStatusText        => LauncherStateRules.GetStatusText(_state, _installedVersion);
        public bool   CanChangeChannel      => _isTeamBuild && IsIdleForSettings();

        public string VersionText           => "버전 " + ((_installedVersion.Length == 0) ? EMPTY_VALUE : _installedVersion);
        public string ChannelText           => "채널 " + _channel;
        public string UpdateInfoText        => BuildUpdateInfoText();
        public string LauncherVersionText   => "런처 " + _launcherVersion.ToString(VERSION_FIELD_COUNT);

        public string ErrorMessage
        {
            get => _errorMessage;
            private set => SetProperty(ref _errorMessage, value);
        }

        // 서비스 상태 응답의 런처 공지로 채운다. 빈 문자열이면 띠가 숨는다
        public string NoticeText
        {
            get => _noticeText;
            private set => SetProperty(ref _noticeText, value);
        }

        public bool IsNoticeMaintenance
        {
            get => _isNoticeMaintenance;
            private set => SetProperty(ref _isNoticeMaintenance, value);
        }

        public string ProgressStageText
        {
            get => _progressStageText;
            private set => SetProperty(ref _progressStageText, value);
        }

        public double ProgressPercent
        {
            get => _progressPercent;
            private set => SetProperty(ref _progressPercent, value);
        }

        public bool IsProgressIndeterminate
        {
            get => _isProgressIndeterminate;
            private set => SetProperty(ref _isProgressIndeterminate, value);
        }

        public string ProgressPercentText
        {
            get => _progressPercentText;
            private set => SetProperty(ref _progressPercentText, value);
        }

        public string ProgressSizeText
        {
            get => _progressSizeText;
            private set => SetProperty(ref _progressSizeText, value);
        }

        public string ProgressSpeedText
        {
            get => _progressSpeedText;
            private set => SetProperty(ref _progressSpeedText, value);
        }

        public string InstallPathText
        {
            get => _installPathText;
            private set => SetProperty(ref _installPathText, value);
        }

        public string InstallRootText
        {
            get => _installRootText;
            private set => SetProperty(ref _installRootText, value);
        }

        public string PatchSourceText
        {
            get => _patchSourceText;
            private set => SetProperty(ref _patchSourceText, value);
        }

        public bool IsTeamBuild
        {
            get => _isTeamBuild;
            private set
            {
                if (SetProperty(ref _isTeamBuild, value))
                {
                    OnPropertyChanged(nameof(CanChangeChannel));
                }
            }
        }

        public string LoginIdText
        {
            get => _loginIdText;
            set => SetProperty(ref _loginIdText, value ?? string.Empty);
        }

        public bool RememberLogin
        {
            get => _rememberLogin;
            set => SetProperty(ref _rememberLogin, value);
        }

        public bool IsLoginBusy
        {
            get => _isLoginBusy;
            private set
            {
                if (SetProperty(ref _isLoginBusy, value))
                {
                    OnPropertyChanged(nameof(CanSubmitLogin));
                }
            }
        }

        public bool CanSubmitLogin => !_isLoginBusy;

        public string LoginErrorText
        {
            get => _loginErrorText;
            private set => SetProperty(ref _loginErrorText, value);
        }

        public bool   IsLoggedIn        => _session?.State == ELauncherSessionState.LoggedIn;
        public bool   IsLoggedOut       => !IsLoggedIn;
        public string AccountText       => ShortenAccountId(_session?.AccountId);
        public bool   IsSignupAvailable => _settings?.WebsiteUri != null;

        public ImageSource? KeyArt { get; }

        public IReadOnlyList<NewsItem> HomeNews
        {
            get => _homeNews;
            private set => SetProperty(ref _homeNews, value);
        }

        public IReadOnlyList<NewsItem> News
        {
            get => _news;
            private set => SetProperty(ref _news, value);
        }

        public IReadOnlyList<NewsItem> PatchNotes
        {
            get => _patchNotes;
            private set => SetProperty(ref _patchNotes, value);
        }

        public NewsItem? SelectedNews
        {
            get => _selectedNews;
            private set => SetProperty(ref _selectedNews, value);
        }

        // 카드 대신 목록 자리에 보일 안내(비어 있음, 불러오는 중, 실패). 빈 문자열이면 숨는다
        public string NewsStatusText         => NewsRules.GetStatusText(_newsLoadState, _news.Count);
        public string PatchNotesStatusText   => NewsRules.GetStatusText(_newsLoadState, _patchNotes.Count);
        public bool   IsNewsRetryVisible     => NewsRules.IsRetryVisible(_newsLoadState);

        public RelayCommand      PrimaryCommand           { get; }
        public AsyncRelayCommand RepairCommand            { get; }
        public RelayCommand      OpenInstallFolderCommand { get; }
        public RelayCommand      OpenLogFolderCommand     { get; }
        public RelayCommand      NavigateCommand          { get; }
        public RelayCommand      DismissErrorCommand      { get; }
        public AsyncRelayCommand LogoutCommand            { get; }
        public RelayCommand      OpenSignupCommand        { get; }
        public RelayCommand      OpenNewsCommand          { get; }
        public RelayCommand      CloseNewsDetailCommand   { get; }
        public RelayCommand      RetryNewsCommand         { get; }

        /****************************************
        *                Methods
        ****************************************/

        public MainViewModel()
        {
            _dispatcher      = Dispatcher.CurrentDispatcher;
            _launcherVersion = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

            PrimaryCommand           = new RelayCommand(_ => ExecutePrimary(), _ => IsPrimaryEnabled);
            RepairCommand            = new AsyncRelayCommand(_ => RunOperationAsync(true), OnUnexpectedError,
                _ => LauncherStateRules.CanRepair(_state, _hasInstall));
            OpenInstallFolderCommand = new RelayCommand(_ => OpenInstallFolder());
            OpenLogFolderCommand     = new RelayCommand(_ => OpenLogFolder());
            NavigateCommand          = new RelayCommand(Navigate);
            DismissErrorCommand      = new RelayCommand(_ => ErrorMessage = string.Empty);
            LogoutCommand            = new AsyncRelayCommand(_ => LogoutAsync(), OnUnexpectedError,
                _ => IsLoggedIn && (_state != ELauncherState.Launching));
            OpenSignupCommand        = new RelayCommand(_ => OpenSignup());
            OpenNewsCommand          = new RelayCommand(OpenNews);
            CloseNewsDetailCommand   = new RelayCommand(_ => CurrentView = _newsDetailOrigin);
            RetryNewsCommand         = new RelayCommand(_ => RunGuarded(RefreshServiceStateAsync));

            _game.Exited += OnGameExited;

            KeyArt = LoadKeyArt();

            NoticeText = SAMPLE_NOTICE_TEXT;
        }

        /// <summary> 설정을 읽고 첫 확인을 시작한다. 창을 띄운 뒤 한 번 부른다 </summary>
        public void Start()
        {
            LauncherLog.Info("Launcher " + _launcherVersion + " starting.");
            RunGuarded(StartAsync);
        }

        /// <summary> 로그인 화면의 로그인 단추. 비밀번호는 화면이 PasswordBox에서 읽어 넘기고 바로 지운다 </summary>
        public void Login(string password) { RunGuarded(() => LoginAsync(password)); }

        public void Dispose()
        {
            _lifetimeCts.Cancel();
            _operationCts?.Cancel();
            _game.Exited -= OnGameExited;
            _game.Dispose();
            if (_session != null)
            {
                _session.StateChanged -= OnSessionStateChanged;
                _session.Dispose();
                _session = null;
            }

            _httpClient?.Dispose();
            _httpClient = null;
        }

        private async Task StartAsync()
        {
            ErrorMessage = string.Empty;
            State        = ELauncherState.Checking;

            string path = LauncherConfigLoader.GetConfigPath();
            if (!LauncherConfigLoader.TryLoad(path, out LauncherSettings? settings, out string error))
            {
                _settings = null;
                _client   = null;
                LauncherLog.Warn("Config error: " + error);
                ErrorMessage = error;
                State        = ELauncherState.ConfigError;
                return;
            }

            _settings       = settings;
            _game.ConfigureInstallRoot(settings.InstallRoot);
            _channel        = settings.Channel;
            IsTeamBuild     = settings.TeamBuild;
            InstallRootText = settings.InstallRoot;
            PatchSourceText = settings.PatchSource;
            OnPropertyChanged(nameof(Channel));
            OnPropertyChanged(nameof(ChannelText));
            LauncherLog.Info("Config loaded. channel=" + _channel + " source=" + settings.PatchSource + " installRoot="
                + settings.InstallRoot + " teamBuild=" + settings.TeamBuild);

            OnPropertyChanged(nameof(IsSignupAvailable));

            CreateClient();
            CreateAccount();

            // 계정과 패치는 서로 기다리지 않는다. 로그인 전이든 자동 로그인이 실패하든 업데이트는 진행되고,
            // 패치 원본이 죽어도 이미 설치한 게임으로 로그인할 수 있다. 둘 다 UI 스레드에서 이어지므로 상태를 함께 써도 된다
            Task account = StartAccountAsync();
            await CheckAsync();
            await AutoUpdateAsync();
            await account;
        }

        private async Task StartAccountAsync()
        {
            // 여기서 난 예외를 RunGuarded로 올리면 진행 중인 업데이트 상태를 확인 실패로 바꾼다. 계정 쪽에서 끝낸다
            try
            {
                await RefreshServiceStateAsync();
                await TryAutoLoginAsync();
            }
            catch (OperationCanceledException)
            {
                LauncherLog.Info("Account start cancelled.");
            }
            catch (Exception exception)
            {
                LauncherLog.Error("Unexpected account start error.", exception);
                LoginErrorText = UNEXPECTED_ERROR_TEXT;
            }
        }

        // 시작할 때 받을 업데이트가 있으면 사용자가 누르지 않아도 바로 받는다. 새 설치는 크기와 위치를 사용자가 보고 고르게 자동으로 하지 않는다
        private async Task AutoUpdateAsync()
        {
            if (_state != ELauncherState.UpdateAvailable)
            {
                return;
            }

            LauncherLog.Info("Auto update started. loggedIn=" + IsLoggedIn);
            await RunOperationAsync(false);
        }

        private void CreateAccount()
        {
            // 다시 시도로 설정을 다시 읽어도 로그인은 유지한다. API 주소를 바꾸려면 런처를 다시 켠다
            if (_session != null)
            {
                return;
            }

            Uri? apiBaseUri = _settings?.ApiBaseUri;
            if (apiBaseUri == null)
            {
                LauncherLog.Warn("apiBaseUrl is not configured. Login and game start are disabled.");
                return;
            }

            _httpClient ??= new HttpClient();
            _api     = new AuthApiClient(_httpClient, apiBaseUri);
            _session = new LauncherSession(_api, new RefreshTokenStore())
            {
                IsRefreshSuspended = _state == ELauncherState.GameRunning
            };
            _session.StateChanged += OnSessionStateChanged;
            LauncherLog.Info("Account API configured. api=" + apiBaseUri.GetLeftPart(UriPartial.Authority));
        }

        private void CreateClient()
        {
            if (_settings == null)
            {
                return;
            }

            IPatchSource source;
            if (_settings.IsHttpSource)
            {
                _httpClient ??= new HttpClient();
                source = new HttpPatchSource(_httpClient, new Uri(_settings.PatchSource, UriKind.Absolute));
            }
            else
            {
                source = new FileSystemPatchSource(_settings.PatchSource);
            }

            PatchClientOptions options = new PatchClientOptions(_settings.InstallRoot, PatchFormat.PRODUCT_GAME, _channel)
            {
                LauncherVersion = _launcherVersion
            };
            _client = new PatchClient(options, source, _settings.Keys);
        }

        private async Task CheckAsync()
        {
            if (_client == null)
            {
                return;
            }

            State = ELauncherState.Checking;
            PatchCheckResult result = await _client.CheckAsync();
            RefreshInstallInfo();

            LauncherLog.Info("Check result. status=" + result.Status + " error=" + result.Error + " patchError=" + result.PatchError
                + " active=" + (result.ActiveReleaseId ?? EMPTY_VALUE) + " target=" + (result.TargetReleaseId ?? EMPTY_VALUE)
                + " bytesToDownload=" + result.BytesToDownload);

            if (result.Status == EPatchCheckStatus.Failed)
            {
                ErrorMessage = PatchErrorText.ToMessageWithCode(result.Error);
                State        = ELauncherState.CheckFailed;
                return;
            }

            _targetVersion   = result.DisplayVersion ?? string.Empty;
            _bytesToDownload = result.BytesToDownload;

            if (result.Error == EPatchClientError.LauncherOutdated)
            {
                ErrorMessage = PatchErrorText.ToMessageWithCode(result.Error);
                State        = ELauncherState.LauncherOutdated;
                return;
            }

            if (_game.IsRunning)
            {
                State = ELauncherState.GameRunning;
                return;
            }

            switch (result.Status)
            {
                case EPatchCheckStatus.NotInstalled:
                    State = ELauncherState.NotInstalled;
                    break;
                case EPatchCheckStatus.UpdateAvailable:
                    State = ELauncherState.UpdateAvailable;
                    break;
                default:
                    State = ELauncherState.Ready;
                    break;
            }

            OnPropertyChanged(nameof(UpdateInfoText));
            LauncherLog.Info("Launcher state: " + _state);
        }

        private async Task RunOperationAsync(bool isRepair)
        {
            if (_client == null)
            {
                return;
            }

            if (_game.IsRunning)
            {
                ErrorMessage = PatchErrorText.ToMessageWithCode(EPatchClientError.GameRunning);
                return;
            }

            ErrorMessage  = string.Empty;
            _operationCts = new CancellationTokenSource();
            ResetProgress();
            State = isRepair ? ELauncherState.Repairing : ELauncherState.Updating;
            LauncherLog.Info((isRepair ? "Repair" : "Update") + " started. channel=" + _channel);

            Progress<PatchProgress> progress = new Progress<PatchProgress>(OnProgress);
            CancellationToken       token    = _operationCts.Token;
            PatchOperationResult    result;
            try
            {
                result = isRepair
                    ? await _client.VerifyAndRepairAsync(progress, () => _game.IsRunning, token)
                    : await _client.UpdateAsync(progress, () => _game.IsRunning, token);
            }
            finally
            {
                _operationCts.Dispose();
                _operationCts = null;
                ResetProgress();
            }

            LauncherLog.Info((isRepair ? "Repair" : "Update") + " finished. error=" + result.Error + " patchError=" + result.PatchError
                + " release=" + (result.ReleaseId ?? EMPTY_VALUE) + " downloadedFiles=" + result.DownloadedFiles + " downloadedBytes="
                + result.DownloadedBytes + " copiedFiles=" + result.CopiedFiles + " repairedFiles=" + result.RepairedFiles
                + " removedFiles=" + result.RemovedFiles + " removedVersions=" + result.RemovedVersions);

            if (!result.IsSuccess && (result.Error != EPatchClientError.Cancelled))
            {
                ErrorMessage = PatchErrorText.ToMessageWithCode(result.Error);
            }

            await CheckAsync();
        }

        private async Task ChangeChannelAsync(string channel)
        {
            if (!CanChangeChannel || !PatchFormat.IsKnownChannel(channel))
            {
                OnPropertyChanged(nameof(Channel));
                return;
            }

            _channel = channel;
            OnPropertyChanged(nameof(Channel));
            OnPropertyChanged(nameof(ChannelText));
            LauncherLog.Info("Channel changed to " + channel);

            // TODO(민성): 지금은 이번 실행에만 적용한다. 계속 쓰려면 launcher.json에 저장할지 정해야 한다
            CreateClient();
            await CheckAsync();
        }

        private void ExecutePrimary()
        {
            switch (LauncherStateRules.GetPrimaryAction(_state))
            {
                case EPrimaryAction.Install:
                case EPrimaryAction.Update:
                    RunGuarded(() => RunOperationAsync(false));
                    break;
                case EPrimaryAction.Launch:
                    RunGuarded(LaunchGameAsync);
                    break;
                case EPrimaryAction.Cancel:
                    _operationCts?.Cancel();
                    LauncherLog.Info("Cancel requested.");
                    break;
                case EPrimaryAction.Retry:
                    RunGuarded(StartAsync);
                    break;
                default:
                    break;
            }
        }

        // 로그인 확인 -> 설치 빌드 신원 -> 서비스 상태(점검, 허용 빌드) -> 게임 세션 발급 -> 게임 실행과 파이프 전달.
        // API가 응답하지 않으면 실행하지 않는다(오프라인 플레이 없음)
        private async Task LaunchGameAsync()
        {
            if ((_session == null) || (_api == null) || (_settings?.ApiBaseUri == null))
            {
                ErrorMessage = AccountErrorText.API_NOT_CONFIGURED;
                return;
            }

            if (_session.State != ELauncherSessionState.LoggedIn)
            {
                // 시작할 때 서버 응답을 못 받아 자동 로그인이 미뤄졌으면 저장된 로그인으로 한 번 더 시도한다
                State          = ELauncherState.Launching;
                LoginErrorText = string.Empty;
                try
                {
                    await TryAutoLoginAsync();
                }
                finally
                {
                    State = ELauncherState.Ready;
                }

                if (!IsLoggedIn)
                {
                    if (LoginErrorText.Length == 0)
                    {
                        LoginErrorText = LOGIN_REQUIRED_TEXT;
                    }

                    CurrentView = ELauncherView.Login;
                    return;
                }
            }

            if (!TryReadInstalledBuild(out InstalledRelease? install, out ClientBuildInfo? build, out string buildError))
            {
                ErrorMessage = buildError;
                return;
            }

            ErrorMessage = string.Empty;
            State        = ELauncherState.Launching;
            LauncherLog.Info("Launch requested. buildId=" + build.BuildId);

            string blockReason = await CheckServiceStateForLaunchAsync(build);
            if (blockReason.Length > 0)
            {
                ErrorMessage = blockReason;
                await CheckAsync();
                await AutoUpdateAsync();
                return;
            }

            SessionResult<GameSessionIssueResponse> issued = await _session.IssueGameSessionAsync(build, _lifetimeCts.Token);
            if (!issued.IsSuccess)
            {
                LauncherLog.Warn("Game session issue failed. error=" + issued.Error + " apiError=" + issued.ApiError + " reason="
                    + issued.ReasonCode);

                // 로그인은 빌드와 무관하게 유지된다. 서버가 설치 빌드를 거절하면 업데이트 안내를 띄우고 패치 확인을 다시 한다
                bool isVersionMismatch = (issued.Error == ESessionError.ApiFailed) && (issued.ApiError == EAuthApiError.VersionMismatch);
                ErrorMessage = isVersionMismatch ? AccountErrorText.UPDATE_REQUIRED : AccountErrorText.ToLoginMessage(issued);

                // 응답을 잃어 메모리 로그인만 버렸으면 저장된 로그인이 남아 있다. 다시 시작을 누르면 그것으로 이어 간다
                if (!IsLoggedIn && !_session.HasStoredLogin)
                {
                    CurrentView = ELauncherView.Login;
                }

                await CheckAsync();
                await AutoUpdateAsync();
                return;
            }

            string startError = string.Empty;
            GameSessionHandoff handoff = new GameSessionHandoff(_api, issued.Value!, _settings.ApiBaseUri.OriginalString,
                arguments => StartGameForHandoff(install.VersionPath, arguments, out startError));
            GameHandoffResult result = await handoff.RunAsync(_lifetimeCts.Token);
            LauncherLog.Info("Game handoff finished. error=" + result.Error + " revoked=" + result.IsRevoked);

            if (!result.IsSuccess)
            {
                ErrorMessage = ((result.Error == EGameHandoffError.StartFailed) && (startError.Length > 0))
                    ? startError
                    : AccountErrorText.ToHandoffMessage(result.Error);
                await CheckAsync();
                return;
            }

            State = ELauncherState.GameRunning;

            // 전달 직후 끝난 게임은 Launching 중이라 OnGameExited가 무시했다
            if (!_game.IsRunning)
            {
                await CheckAsync();
            }
        }

        private IStartedGame? StartGameForHandoff(string versionPath, string arguments, out string error)
        {
            if (!_game.TryStart(versionPath, arguments, out int processId, out error))
            {
                return null;
            }

            return new StartedGame(_game, processId);
        }

        // 막아야 하면 화면에 보일 이유, 통과하면 빈 문자열
        private async Task<string> CheckServiceStateForLaunchAsync(ClientBuildInfo build)
        {
            ServiceStateResponse? state = await RefreshServiceStateAsync();
            if (state == null)
            {
                return AccountErrorText.SERVICE_UNREACHABLE;
            }

            if (state.Maintenance)
            {
                return AccountErrorText.MAINTENANCE;
            }

            IReadOnlyList<string>? allowed = state.AllowedBuildIds;
            if ((allowed == null) || !Contains(allowed, build.BuildId))
            {
                LauncherLog.Warn("Installed build is not allowed by the service. buildId=" + build.BuildId);
                return AccountErrorText.UPDATE_REQUIRED;
            }

            return string.Empty;
        }

        // 실패하면 null이고 공지 띠에 서버 연결 실패를, 소식 목록에 불러오기 실패를 보인다
        private async Task<ServiceStateResponse?> RefreshServiceStateAsync()
        {
            if (_api == null)
            {
                return null;
            }

            _isServiceStateRefreshing = true;
            SetNewsLoadState(ENewsLoadState.Loading);
            AuthApiResult<ServiceStateResponse> result;
            try
            {
                result = await _api.GetServiceStateAsync(_lifetimeCts.Token);
            }
            finally
            {
                _isServiceStateRefreshing = false;
            }

            if (!result.IsSuccess || (result.Value == null))
            {
                LauncherLog.Warn("Service state failed. error=" + result.Error + " status=" + result.StatusCode + " reason="
                    + result.ReasonCode);
                NoticeText          = SERVICE_DOWN_TEXT;
                IsNoticeMaintenance = true;
                SetNewsLoadState(ENewsLoadState.Failed);
                return null;
            }

            ServiceStateResponse state  = result.Value;
            _lastServiceStateTimestamp = Stopwatch.GetTimestamp();
            ApplyNews(NewsRules.ToItems(state.Notices));
            SetNewsLoadState(ENewsLoadState.Loaded);
            string               notice = PickLauncherNotice(state);
            IsNoticeMaintenance = state.Maintenance;
            if (state.Maintenance)
            {
                NoticeText = (notice.Length > 0) ? notice : MAINTENANCE_TEXT;
            }
            else
            {
                NoticeText = (notice.Length > 0) ? notice : SAMPLE_NOTICE_TEXT;
            }

            LauncherLog.Info("Service state. maintenance=" + state.Maintenance + " allowedBuilds=" + (state.AllowedBuildIds?.Count ?? 0));
            return state;
        }

        // notice는 서버가 고른 런처 대상 첫 공지의 제목이다. 이전 서버처럼 비어 있으면 목록에서 직접 고른다
        private static string PickLauncherNotice(ServiceStateResponse state)
        {
            if (!string.IsNullOrWhiteSpace(state.Notice))
            {
                return state.Notice.Trim();
            }

            foreach (ServiceNotice notice in state.Notices)
            {
                if ((notice == null) || (string.IsNullOrWhiteSpace(notice.Title)))
                {
                    continue;
                }

                if ((notice.Target == ServiceNotice.TARGET_LAUNCHER) || (notice.Target == ServiceNotice.TARGET_BOTH))
                {
                    return notice.Title.Trim();
                }
            }

            return string.Empty;
        }

        private static bool Contains(IReadOnlyList<string> values, string value)
        {
            foreach (string item in values)
            {
                if (string.Equals(item, value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private async Task TryAutoLoginAsync()
        {
            if ((_session == null) || !_session.HasStoredLogin || (_session.State != ELauncherSessionState.LoggedOut))
            {
                return;
            }

            // 런처를 다시 켰을 때 이미 도는 게임은 저장된 갱신 토큰 아래 세션을 쓰고 있다. 자동 로그인(회전)이 그 세션을 끊는다
            if (_game.IsRunning)
            {
                LauncherLog.Info("Auto-login skipped: game is running.");
                return;
            }

            // 설치가 없거나 빌드가 서버와 달라도 자동 로그인한다. 빌드는 게임 세션 발급에서만 검사한다
            SessionResult result = await _session.TryAutoLoginAsync(_lifetimeCts.Token);
            LauncherLog.Info("Auto-login finished. error=" + result.Error + " apiError=" + result.ApiError + " reason=" + result.ReasonCode
                + " storeFailed=" + result.IsStoreFailed);
            if (!result.IsSuccess)
            {
                LoginErrorText = AccountErrorText.ToLoginMessage(result);
            }
        }

        private async Task LoginAsync(string password)
        {
            if (_session == null)
            {
                LoginErrorText = AccountErrorText.API_NOT_CONFIGURED;
                return;
            }

            if (_isLoginBusy)
            {
                return;
            }

            LoginErrorText = string.Empty;
            IsLoginBusy    = true;
            SessionResult result;
            try
            {
                result = await _session.LoginWithCredentialsAsync(LoginIdText, password, RememberLogin, _lifetimeCts.Token);
            }
            finally
            {
                IsLoginBusy = false;
            }

            // 아이디와 비밀번호는 남기지 않는다
            LauncherLog.Info("Login finished. error=" + result.Error + " apiError=" + result.ApiError + " reason=" + result.ReasonCode
                + " remember=" + RememberLogin + " storeFailed=" + result.IsStoreFailed);
            if (!result.IsSuccess)
            {
                LoginErrorText = AccountErrorText.ToLoginMessage(result);
                return;
            }

            CurrentView = ELauncherView.Home;
        }

        private async Task LogoutAsync()
        {
            if (_session == null)
            {
                return;
            }

            // 서버에서 로그인 묶음을 폐기하면 실행 중인 게임의 세션이 끊긴다
            if ((_state == ELauncherState.GameRunning) || _game.IsRunning)
            {
                ErrorMessage = LOGOUT_BLOCKED_TEXT;
                return;
            }

            SessionResult result = await _session.LogoutAsync(_lifetimeCts.Token);
            if (result.Error == ESessionError.RefreshSuspended)
            {
                ErrorMessage = LOGOUT_BLOCKED_TEXT;
                return;
            }

            // 서버 폐기에 실패해도 로컬 로그인은 지웠다. 사용자에게는 로그아웃된 것으로 보이고 실패는 로그에만 남긴다
            if (!result.IsSuccess)
            {
                LauncherLog.Warn("Server logout failed. apiError=" + result.ApiError + " reason=" + result.ReasonCode);
            }

            LoginErrorText = string.Empty;
            LauncherLog.Info("Logged out. serverRevoked=" + result.IsSuccess);
        }

        private void OnSessionStateChanged()
        {
            _dispatcher.InvokeAsync(() =>
            {
                OnPropertyChanged(nameof(IsLoggedIn));
                OnPropertyChanged(nameof(IsLoggedOut));
                OnPropertyChanged(nameof(AccountText));
                LogoutCommand.RaiseCanExecuteChanged();
            });
        }

        private bool TryReadInstalledBuild([NotNullWhen(true)] out InstalledRelease? install,
            [NotNullWhen(true)] out ClientBuildInfo? build, out string error)
        {
            build = null;
            error = string.Empty;
            if ((_client == null) || !_client.TryGetActiveInstall(out install))
            {
                install = null;
                error   = AccountErrorText.NOT_INSTALLED;
                return false;
            }

            if (!BuildIdentityReader.TryRead(install.VersionPath, out BuildIdentity? identity, out EPatchError identityError))
            {
                LauncherLog.Warn("Build identity read failed. error=" + identityError + " path=" + install.VersionPath);
                error = AccountErrorText.BUILD_UNREADABLE;
                return false;
            }

            build = identity.ToClientBuildInfo();
            return true;
        }

        private void OpenSignup()
        {
            Uri? website = _settings?.WebsiteUri;
            if (website == null)
            {
                return;
            }

            // "http://host/site"에 상대 경로를 붙이면 마지막 세그먼트가 사라지므로 끝에 "/"를 보장한다
            string text    = website.AbsoluteUri;
            Uri    baseUri = text.EndsWith(URL_SEPARATOR) ? website : new Uri(text + URL_SEPARATOR);
            OpenShell(new Uri(baseUri, SIGNUP_PATH).AbsoluteUri, "회원가입 페이지를 열지 못했습니다.");
        }

        private static string ShortenAccountId(string? accountId)
        {
            if (string.IsNullOrEmpty(accountId))
            {
                return string.Empty;
            }

            return (accountId.Length <= ACCOUNT_SHORT_LENGTH) ? accountId : accountId.Substring(0, ACCOUNT_SHORT_LENGTH) + ACCOUNT_ELLIPSIS;
        }

        private void OnGameExited(int exitCode)
        {
            _dispatcher.InvokeAsync(() =>
            {
                if (_state != ELauncherState.GameRunning)
                {
                    return;
                }

                RunGuarded(CheckAsync);
            });
        }

        private void OnProgress(PatchProgress value)
        {
            if (!IsOperationRunning)
            {
                return;
            }

            if (value.Stage != _lastStage)
            {
                _lastStage = value.Stage;
                _speedMeter.Reset();
            }

            ProgressStageText = PatchErrorText.ToStageText(value.Stage);
            if (value.TotalBytes <= 0)
            {
                IsProgressIndeterminate = value.Stage != EPatchStage.Completed;
                ProgressPercent         = 0.0;
                ProgressPercentText     = string.Empty;
                ProgressSizeText        = string.Empty;
                ProgressSpeedText       = string.Empty;
                return;
            }

            double percent = Math.Clamp(value.CompletedBytes * PERCENT_SCALE / value.TotalBytes, 0.0, PERCENT_SCALE);
            IsProgressIndeterminate = false;
            ProgressPercent         = percent;
            ProgressPercentText     = percent.ToString(PERCENT_FORMAT, CultureInfo.InvariantCulture) + "%";
            ProgressSizeText        = ByteFormat.FormatProgress(value.CompletedBytes, value.TotalBytes);

            if (value.Stage != EPatchStage.Downloading)
            {
                ProgressSpeedText = string.Empty;
                return;
            }

            _speedMeter.Add(value.CompletedBytes);
            ProgressSpeedText = ByteFormat.FormatSpeed(_speedMeter.BytesPerSecond);
        }

        private void ResetProgress()
        {
            _lastStage = EPatchStage.Checking;
            _speedMeter.Reset();
            ProgressStageText       = PatchErrorText.ToStageText(EPatchStage.Checking);
            ProgressPercent         = 0.0;
            IsProgressIndeterminate = true;
            ProgressPercentText     = string.Empty;
            ProgressSizeText        = string.Empty;
            ProgressSpeedText       = string.Empty;
        }

        private void RefreshInstallInfo()
        {
            if ((_client != null) && _client.TryGetActiveInstall(out InstalledRelease? install))
            {
                _hasInstall       = true;
                _installedVersion = install.Manifest.DisplayVersion;
                InstallPathText   = install.VersionPath;
            }
            else
            {
                _hasInstall       = false;
                _installedVersion = string.Empty;
                InstallPathText   = _settings?.InstallRoot ?? EMPTY_VALUE;
            }

            OnPropertyChanged(nameof(VersionText));
            OnPropertyChanged(nameof(GameStatusText));
            RepairCommand.RaiseCanExecuteChanged();
        }

        private void OnStateChanged()
        {
            // 갱신 토큰을 회전하면 서버가 그 아래 게임 세션을 폐기하므로 게임이 도는 동안에는 갱신을 막는다
            if (_session != null)
            {
                _session.IsRefreshSuspended = _state == ELauncherState.GameRunning;
            }

            OnPropertyChanged(nameof(PrimaryText));
            OnPropertyChanged(nameof(IsPrimaryEnabled));
            OnPropertyChanged(nameof(IsSecondaryStyle));
            OnPropertyChanged(nameof(IsOperationRunning));
            OnPropertyChanged(nameof(GameStatusText));
            OnPropertyChanged(nameof(CanChangeChannel));
            OnPropertyChanged(nameof(UpdateInfoText));
            PrimaryCommand.RaiseCanExecuteChanged();
            RepairCommand.RaiseCanExecuteChanged();
            LogoutCommand.RaiseCanExecuteChanged();
        }

        private bool IsIdleForSettings()
        {
            return (_state != ELauncherState.Checking) && !IsOperationRunning && (_state != ELauncherState.GameRunning)
                && (_state != ELauncherState.Launching);
        }

        private string BuildUpdateInfoText()
        {
            if ((_state != ELauncherState.UpdateAvailable) && (_state != ELauncherState.NotInstalled))
            {
                return string.Empty;
            }

            string version = (_targetVersion.Length == 0) ? string.Empty : "새 버전 " + _targetVersion + ", ";
            return version + "받을 크기 " + ByteFormat.Format(_bytesToDownload);
        }

        private void OnCurrentViewChanged(ELauncherView view)
        {
            if ((view != ELauncherView.News) && (view != ELauncherView.PatchNotes))
            {
                return;
            }

            // 받지 못한 상태(Failed)이면 오래된 목록을 믿지 않고 다시 받는다
            TimeSpan? sinceLastFetch = null;
            if (_lastServiceStateTimestamp.HasValue && (_newsLoadState == ENewsLoadState.Loaded))
            {
                sinceLastFetch = Stopwatch.GetElapsedTime(_lastServiceStateTimestamp.Value);
            }

            if (_isServiceStateRefreshing || !NewsRules.ShouldRefresh(sinceLastFetch))
            {
                return;
            }

            RunGuarded(RefreshServiceStateAsync);
        }

        private void ApplyNews(IReadOnlyList<NewsItem> items)
        {
            News       = items;
            HomeNews   = NewsRules.TakeHome(items);
            PatchNotes = NewsRules.FilterPatchNotes(items);
            RaiseNewsStatusChanged();
        }

        private void SetNewsLoadState(ENewsLoadState state)
        {
            _newsLoadState = state;
            RaiseNewsStatusChanged();
        }

        private void RaiseNewsStatusChanged()
        {
            OnPropertyChanged(nameof(NewsStatusText));
            OnPropertyChanged(nameof(PatchNotesStatusText));
            OnPropertyChanged(nameof(IsNewsRetryVisible));
        }

        private void OpenNews(object? parameter)
        {
            if (parameter is not NewsItem item)
            {
                return;
            }

            bool isTab = (_currentView == ELauncherView.Home) || (_currentView == ELauncherView.News)
                || (_currentView == ELauncherView.PatchNotes);
            _newsDetailOrigin = isTab ? _currentView : ELauncherView.News;
            SelectedNews      = item;
            CurrentView       = ELauncherView.NewsDetail;
        }

        private void Navigate(object? parameter)
        {
            if (parameter is ELauncherView view)
            {
                CurrentView = view;
                return;
            }

            if (Enum.TryParse(parameter as string, out ELauncherView parsed))
            {
                CurrentView = parsed;
            }
        }

        private void OpenInstallFolder()
        {
            string path = _settings?.InstallRoot ?? string.Empty;
            if ((_client != null) && _client.TryGetActiveInstall(out InstalledRelease? install))
            {
                path = install.VersionPath;
            }

            if ((path.Length == 0) || !Directory.Exists(path))
            {
                ErrorMessage = "설치 폴더가 아직 없습니다.";
                return;
            }

            OpenFolder(path);
        }

        private void OpenLogFolder()
        {
            try
            {
                Directory.CreateDirectory(LauncherLog.LogFolder);
            }
            catch (IOException exception)
            {
                LauncherLog.Warn("Log folder create failed: " + exception.Message);
            }

            OpenFolder(LauncherLog.LogFolder);
        }

        private void OpenFolder(string path) { OpenShell(path, "폴더를 열지 못했습니다."); }

        private void OpenShell(string target, string failureText)
        {
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
            }
            catch (Win32Exception exception)
            {
                LauncherLog.Warn("Shell open failed: " + exception.Message);
                ErrorMessage = failureText;
            }
        }

        // 화면 이벤트에서 시작하는 비동기 작업의 경계. 여기서 잡지 않은 예외는 디스패처까지 올라가 앱을 죽인다
        private async void RunGuarded(Func<Task> operation)
        {
            try
            {
                await operation();
            }
            catch (Exception exception)
            {
                OnUnexpectedError(exception);
            }
        }

        private void OnUnexpectedError(Exception exception)
        {
            LauncherLog.Error("Unexpected launcher error.", exception);
            ErrorMessage = UNEXPECTED_ERROR_TEXT;
            if ((_state == ELauncherState.Checking) || IsOperationRunning)
            {
                State = ELauncherState.CheckFailed;
            }
        }

        private static ImageSource? LoadKeyArt()
        {
            foreach (string extension in G_KeyArtExtensions)
            {
                string path = Path.Combine(AppContext.BaseDirectory, KEYART_FOLDER, KEYART_NAME + extension);
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    BitmapImage image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.UriSource   = new Uri(path, UriKind.Absolute);
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
                catch (Exception exception) when ((exception is IOException) || (exception is NotSupportedException)
                    || (exception is UnauthorizedAccessException))
                {
                    LauncherLog.Warn("Key art load failed: " + exception.Message);
                }
            }

            return null;
        }
    }
}
