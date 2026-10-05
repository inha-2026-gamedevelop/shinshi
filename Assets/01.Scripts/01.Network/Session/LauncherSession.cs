// System
using System;
using System.Diagnostics;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 런처가 넘긴 게임 세션을 메모리에만 들고 있는다, 파일과 로그에 남기지 않는다 </summary>
    public static class LauncherSession
    {
        // 상태 변경은 모두 메인 스레드에서만 한다. 수신 스레드는 결과를 메인 스레드로 넘기고 여기를 직접 부르지 않는다.
        // 그래서 StateChanged도 항상 메인 스레드에서 불린다.
        // 만료 전 갱신은 GameSessionRefresher가 하고, 런처 필수 빌드의 안내는 LauncherSessionRules.DescribeLaunchStatus가 정한다

        /****************************************
        *                Fields
        ****************************************/

        private static ELauncherSessionState   _state            = ELauncherSessionState.NotLaunched;
        private static ELauncherSessionFailure _failureReason    = ELauncherSessionFailure.None;
        private static string                  _accountId        = null;
        private static string                  _gameSessionToken = null;
        private static string                  _apiBaseUrl       = null;
        private static long                    _expiryTimestamp  = 0;

        // 수신 시도 번호. 시간 초과나 리셋 뒤에 늦게 도착한 결과를 버린다. 토큰을 갱신할 때도 올려 늦은 갱신 응답을 버린다
        private static int _attempt = 0;

        /****************************************
        *              Properties
        ****************************************/

        public static ELauncherSessionState   State         => _state;
        public static ELauncherSessionFailure FailureReason => _failureReason;
        public static string                  AccountId     => _accountId;
        public static string                  ApiBaseUrl    => _apiBaseUrl;
        public static bool                    HasSession    => _state == ELauncherSessionState.Received;

        // Release 빌드는 런처로만 정식 로그인한다. 에디터와 개발 빌드는 개발용 로그인을 쓴다
        public static bool IsLaunchRequired => (!Application.isEditor) && (!UnityEngine.Debug.isDebugBuild);

        public static bool IsExpired => GetRemainingMs() <= 0;

        // 지금 들고 있는 세션의 번호. 갱신 요청을 보낼 때 잡아 두고 응답을 넣을 때 ReplaceToken에 돌려준다
        public static int SessionAttempt => _attempt;

        public static event Action<ELauncherSessionState> StateChanged;

        /****************************************
        *                Methods
        ****************************************/

        // 도메인 리로드를 끈 에디터에서 이전 플레이의 세션과 구독자가 남지 않게 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            ClearSession();
            _state         = ELauncherSessionState.NotLaunched;
            _failureReason = ELauncherSessionFailure.None;
            StateChanged   = null;
            ++_attempt;
        }

        /// <summary> 받은 게임 세션 토큰, 세션이 없거나 만료됐으면 null. 로그와 파일에 쓰지 않는다 </summary>
        public static string GetGameSessionToken()
        {
            if (!HasSession)
            {
                return null;
            }

            if (IsExpired)
            {
                return null;
            }

            return _gameSessionToken;
        }

        /// <summary> 만료까지 남은 시간, 받은 순간의 단조 시계 기준이라 PC 시계를 바꿔도 변하지 않는다 </summary>
        public static long GetRemainingMs()
        {
            if (!HasSession)
            {
                return 0;
            }

            return LauncherSessionRules.ComputeRemainingMs(_expiryTimestamp, Stopwatch.GetTimestamp(), Stopwatch.Frequency);
        }

        internal static int BeginReceiving()
        {
            ++_attempt;
            ClearSession();
            _failureReason = ELauncherSessionFailure.None;
            SetState(ELauncherSessionState.Receiving);
            return _attempt;
        }

        // 이 시도가 아직 유효해 세션을 받아들였으면 true
        internal static bool Complete(int attempt, LauncherHandoff handoff, long receivedTimestamp)
        {
            if (!IsCurrentAttempt(attempt))
            {
                return false;
            }

            if (handoff == null)
            {
                Fail(attempt, ELauncherSessionFailure.InvalidPayload);
                return false;
            }

            _accountId        = handoff.AccountId;
            _gameSessionToken = handoff.GameSessionToken;
            _apiBaseUrl       = handoff.ApiBaseUrl;
            _expiryTimestamp  = LauncherSessionRules.ComputeExpiryTimestamp(receivedTimestamp,
                LauncherSessionRules.ComputeRemainingMs(handoff), Stopwatch.Frequency);
            SetState(ELauncherSessionState.Received);
            return true;
        }

        // 에디터와 개발 빌드는 런처 없이 로비의 개발 로그인으로 받은 게임 세션을 같은 자리에 넣는다.
        // 런처 세션을 받는 중이거나 아직 유효한 세션이 있으면 덮어쓰지 않는다
        internal static bool AcceptDevelopmentLogin(LauncherHandoff handoff)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if ((IsLaunchRequired) || (_state == ELauncherSessionState.Receiving) || (HasSession && !IsExpired))
            {
                return false;
            }

            int attempt = BeginReceiving();
            return Complete(attempt, handoff, Stopwatch.GetTimestamp());
#else
            return false;
#endif
        }

        // 갱신으로 받은 새 토큰을 넣는다. 그사이 세션이 바뀌었거나 다른 계정의 응답이면 버리고 false.
        // 넣으면 번호를 올린다. 이전 토큰으로 보낸 요청의 응답이 늦게 와도 새 토큰을 덮지 못한다
        internal static bool ReplaceToken(int attempt, GameSessionIssueResponse refreshed, long receivedTimestamp)
        {
            if ((attempt != _attempt) || (_state != ELauncherSessionState.Received))
            {
                return false;
            }

            if ((refreshed == null) || string.IsNullOrWhiteSpace(refreshed.GameSessionToken)
                || (!string.Equals(refreshed.AccountId, _accountId, StringComparison.Ordinal)))
            {
                return false;
            }

            long remainingMs = refreshed.ExpiresAtUnixMs - refreshed.ServerTimeUnixMs;
            if (remainingMs <= 0)
            {
                return false;
            }

            ++_attempt;
            _gameSessionToken = refreshed.GameSessionToken;
            _expiryTimestamp  = LauncherSessionRules.ComputeExpiryTimestamp(receivedTimestamp, remainingMs, Stopwatch.Frequency);
            return true;
        }

        // 받은 세션이 API에서 폐기, 만료로 거절됐다. 이 세션 번호가 아직 유효해 실패로 바꿨으면 true
        internal static bool EndSession(int attempt, ELauncherSessionFailure reason)
        {
            if ((attempt != _attempt) || (_state != ELauncherSessionState.Received))
            {
                return false;
            }

            ++_attempt;
            ClearSession();
            _failureReason = reason;
            SetState(ELauncherSessionState.Failed);
            return true;
        }

        // 이 시도가 아직 유효해 실패로 바꿨으면 true
        internal static bool Fail(int attempt, ELauncherSessionFailure reason)
        {
            if (!IsCurrentAttempt(attempt))
            {
                return false;
            }

            ClearSession();
            _failureReason = reason;
            SetState(ELauncherSessionState.Failed);
            return true;
        }

        // 수신을 시작하기 전에 인자나 실행 환경 문제로 실패한 경우
        internal static void FailBeforeReceiving(ELauncherSessionFailure reason)
        {
            ++_attempt;
            ClearSession();
            _failureReason = reason;
            SetState(ELauncherSessionState.Failed);
        }

        private static bool IsCurrentAttempt(int attempt)
        {
            return (attempt == _attempt) && (_state == ELauncherSessionState.Receiving);
        }

        private static void ClearSession()
        {
            _accountId        = null;
            _gameSessionToken = null;
            _apiBaseUrl       = null;
            _expiryTimestamp  = 0;
        }

        private static void SetState(ELauncherSessionState state)
        {
            _state = state;
            StateChanged?.Invoke(state);
        }
    }
}
