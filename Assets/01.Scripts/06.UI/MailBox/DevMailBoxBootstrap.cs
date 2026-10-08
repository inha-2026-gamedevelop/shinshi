// System
using System;
using System.Collections;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.UI
{
    // Dev_MailBox를 로비 없이 단독으로 실행할 때 쓰는 개발용 로그인 부품이다.
    // 로비의 개발 로그인(LobbyScreen.CoLogin)과 같은 순서로 세션을 받아 LauncherSession에 넣는다.
    // 비밀번호는 씬에 저장하지 않고 환경 변수에서만 읽는다. 세션을 받는 LauncherSession.AcceptDevelopmentLogin은
    // 에디터와 개발 빌드에서만 동작하므로 릴리즈 빌드에서는 로그인이 거절된다
    /// <summary> 우편함 개발 씬의 자동 개발 로그인과 샘플 우편 표시 </summary>
    public sealed class DevMailBoxBootstrap : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string PASSWORD_ENV = "SHINSHI_DEV_PASSWORD";

        private const int  SAMPLE_PAGE_SIZE = 12;
        private const long MS_PER_DAY       = 86400000L;

        // 샘플 우편의 경과 일수, 남은 일수, 보상 수량. 줄 요약과 상세 본문의 여러 모양을 보기 위한 값이다
        private const long SAMPLE_AGE_ADMIN   = 1;
        private const long SAMPLE_AGE_SESSION = 2;
        private const long SAMPLE_AGE_AUCTION = 3;
        private const long SAMPLE_AGE_RETURN  = 5;
        private const long SAMPLE_LEFT_FULL   = 30;
        private const long SAMPLE_LEFT_SHORT  = 2;

        private const long SAMPLE_CURRENCY_SMALL = 5000;
        private const long SAMPLE_CURRENCY_LARGE = 120000;
        private const long SAMPLE_ESSENCE        = 120;
        private const long SAMPLE_SHARDS         = 8;
        private const long SAMPLE_TICKETS        = 2;
        private const long SAMPLE_POTIONS        = 5;

        [Header("접속")]
        [SerializeField] private string _apiBaseUrl = "http://127.0.0.1:5080";
        [SerializeField] private string _loginId    = string.Empty;

        [Header("수령 테스트용 캐릭터(목록 조회만 볼 때는 비워 둔다)")]
        [SerializeField] private string _characterId = string.Empty;

        // 로그인이 끝나기 전에 우편함이 목록을 요청하면 토큰이 없어 실패한다.
        // MailBoxBinder는 창이 열린 순간 목록을 요청하므로, 이 오브젝트를 로그인 뒤에 켠다
        [Header("로그인 성공 뒤에 켤 오브젝트(씬에서는 꺼 둔다)")]
        [SerializeField] private GameObject _enableAfterLogin = null;

        // 서버 없이 화면만 볼 때 켠다. 가짜 우편을 직접 채우므로 로그인도 API 호출도 하지 않는다
        [Header("샘플 우편으로 화면만 보기(서버 연결 안 함)")]
        [SerializeField] private bool _useSampleMail = false;

        private readonly LobbyApiClient    _api  = new LobbyApiClient();
        private readonly List<ListRowData> _rows = new List<ListRowData>(SAMPLE_PAGE_SIZE);

        private InboxEntry[] _sampleEntries = null;
        private long         _sampleNowMs   = 0;
        private MailBoxView  _sampleView    = null;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Start()
        {
            if (_useSampleMail)
            {
                ShowSampleMail();
                return;
            }

            StartCoroutine(CoLogin());
        }

        private void OnDestroy()
        {
            if (_sampleView != null)
            {
                _sampleView.RowSelected -= OnSampleRowSelected;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        // MailBoxBinder가 하는 일(목록 채우기, 줄 선택 시 상세 표시)을 서버 응답 없이 직접 한다.
        // 바인더가 켜져 있으면 창이 열리는 순간 서버에 목록을 요청하므로 먼저 끈다
        private void ShowSampleMail()
        {
            if (_enableAfterLogin == null)
            {
                return;
            }

            if (_enableAfterLogin.TryGetComponent(out MailBoxBinder binder))
            {
                binder.enabled = false;
            }

            _enableAfterLogin.SetActive(true);
            _sampleView = _enableAfterLogin.GetComponentInChildren<MailBoxView>(true);
            if (_sampleView == null)
            {
                return;
            }

            // 프리팹의 창은 꺼진 채로 저장돼 있다. 마을에서는 단축키가 여는데 이 씬에는 없으므로 직접 연다.
            // 목록을 채우기 전에 열어야 Awake에서 줄 클릭 연결이 끝난다
            _sampleView.SetOpen(true);
            _sampleNowMs   = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _sampleEntries = BuildSampleEntries(_sampleNowMs);

            int count = Math.Min(_sampleEntries.Length, Math.Max(1, _sampleView.RowCapacity));
            MailBoxViewRules.BuildRows(_sampleEntries, _sampleNowMs, 0, count, _rows);
            _sampleView.SetList(_rows, 0, 1);
            _sampleView.RowSelected += OnSampleRowSelected;
        }

        private void OnSampleRowSelected(int rowIndex)
        {
            if ((rowIndex < 0) || (rowIndex >= _sampleEntries.Length))
            {
                return;
            }

            InboxEntry entry = _sampleEntries[rowIndex];
            _sampleView.ShowDetail(MailBoxViewRules.TitleOf(entry.SourceKind), MailBoxViewRules.SenderOf(entry.SourceKind),
                MailBoxViewRules.DateText(entry.CreatedAtUnixMs), MailBoxViewRules.Body(entry));
        }

        // 출처와 보상 개수를 달리해 줄 요약과 상세 본문의 여러 모양을 한 번에 본다
        private static InboxEntry[] BuildSampleEntries(long nowMs)
        {
            return new[]
            {
                SampleEntry("sample-1", "AdminGrant", nowMs, SAMPLE_AGE_ADMIN, SAMPLE_LEFT_FULL - SAMPLE_AGE_ADMIN, false,
                    new InboxReward("Currency", null, "None", SAMPLE_CURRENCY_SMALL)),
                SampleEntry("sample-2", "SessionReward", nowMs, SAMPLE_AGE_SESSION, SAMPLE_LEFT_FULL - SAMPLE_AGE_SESSION, true,
                    new InboxReward("Essence", null, "None", SAMPLE_ESSENCE),
                    new InboxReward("Shards", null, "None", SAMPLE_SHARDS),
                    new InboxReward("Tickets", null, "None", SAMPLE_TICKETS)),
                SampleEntry("sample-3", "AuctionReward", nowMs, SAMPLE_AGE_AUCTION, SAMPLE_LEFT_FULL - SAMPLE_AGE_AUCTION, false,
                    new InboxReward("AccountItem", "item_potion_small", "None", SAMPLE_POTIONS)),
                SampleEntry("sample-4", "MarketReturn", nowMs, SAMPLE_AGE_RETURN, SAMPLE_LEFT_SHORT, false,
                    new InboxReward("Currency", null, "None", SAMPLE_CURRENCY_LARGE)),
            };
        }

        private static InboxEntry SampleEntry(string id, string sourceKind, long nowMs, long ageDays, long leftDays,
            bool requiresCharacter, params InboxReward[] rewards)
        {
            return new InboxEntry(id, sourceKind, nowMs - (ageDays * MS_PER_DAY), nowMs + (leftDays * MS_PER_DAY),
                rewards, requiresCharacter);
        }

        private IEnumerator CoLogin()
        {
            string password = Environment.GetEnvironmentVariable(PASSWORD_ENV);
            if ((string.IsNullOrEmpty(_loginId)) || (string.IsNullOrEmpty(password)))
            {
                Debug.LogWarning($"[DevMailBox] 로그인 아이디를 넣고 환경 변수 {PASSWORD_ENV}를 설정한다");
                yield break;
            }

            // 이미 유효한 세션이 있으면(로비를 거쳐 온 경우) 다시 로그인하지 않는다
            if (LauncherSession.HasSession && !LauncherSession.IsExpired)
            {
                Finish();
                yield break;
            }

            ClientBuildInfo build = LobbyFlow.CreateBuildInfo();
            if (build == null)
            {
                Debug.LogWarning("[DevMailBox] 게임 데이터를 읽지 못해 로그인할 수 없다");
                yield break;
            }

            // 1. 아이디, 비밀번호로 접근 토큰을 받는다
            LobbyApiReply<AuthExchangeResponse> exchanged = null;
            yield return _api.Send<AuthExchangeResponse>(_apiBaseUrl, null, ApiRoutes.AUTH_EXCHANGE,
                AuthExchangeRequest.ForCredentials(_loginId, password, null), reply => exchanged = reply);
            if ((exchanged == null) || (!exchanged.IsSuccess) || (string.IsNullOrEmpty(exchanged.Value.AccessToken)))
            {
                Debug.LogWarning($"[DevMailBox] 로그인 실패: {LobbyText.Describe(exchanged)}");
                yield break;
            }

            // 2. 접근 토큰을 게임 하위 세션으로 바꾼다
            LobbyApiReply<GameSessionIssueResponse> issued = null;
            yield return _api.Send<GameSessionIssueResponse>(_apiBaseUrl, exchanged.Value.AccessToken, ApiRoutes.AUTH_GAME_SESSION,
                new GameSessionIssueRequest(build), reply => issued = reply);
            if ((issued == null) || (!issued.IsSuccess) || (string.IsNullOrEmpty(issued.Value.GameSessionToken)))
            {
                Debug.LogWarning($"[DevMailBox] 게임 세션 발급 실패: {LobbyText.Describe(issued)}");
                yield break;
            }

            // 3. 우편함이 쓰는 LauncherSession에 넣는다
            GameSessionIssueResponse session = issued.Value;
            var handoff = new LauncherHandoff(LauncherPipeProtocol.PROTOCOL_VERSION, session.AccountId, session.GameSessionToken,
                session.ExpiresAtUnixMs, session.ServerTimeUnixMs, _apiBaseUrl);
            if (!LauncherSession.AcceptDevelopmentLogin(handoff))
            {
                Debug.LogWarning("[DevMailBox] 세션을 받아들이지 못했다");
                yield break;
            }

            Finish();
        }

        private void Finish()
        {
            if (!string.IsNullOrEmpty(_characterId))
            {
                LobbyFlow.ActiveCharacterId = _characterId;
            }

            if (_enableAfterLogin != null)
            {
                _enableAfterLogin.SetActive(true);

                // 창이 열려야 MailBoxBinder가 목록을 요청한다
                MailBoxView view = _enableAfterLogin.GetComponentInChildren<MailBoxView>(true);
                if (view != null)
                {
                    view.SetOpen(true);
                }
            }

            Debug.Log("[DevMailBox] 개발 로그인 완료");
        }
    }
}
#if UNITY_EDITOR || DEVELOPMENT_BUILD
#endif
