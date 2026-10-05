// System
using System;
using System.Globalization;
using System.Text;

// 서드파티
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 런처 세션 수신의 순수 판정, 실행 인자와 파이프 이름과 받은 세션을 검사한다 </summary>
    public static class LauncherSessionRules
    {
        /****************************************
        *                Fields
        ****************************************/

        // 런처가 접두사 뒤에 붙이는 무작위 hex 길이. 길이까지 고정해야 경로 문자나 다른 파이프 이름이 끼어들 수 없다
        public const int PIPE_NAME_HEX_LENGTH = 32;

        private const long MS_PER_SECOND = 1000;
        private const int  MAX_JSON_DEPTH = 8;

        // 게임 세션 수명은 15분이다. 남은 시간이 이 이하가 되면 갱신한다. 전송 재시도(1+2+4초)와 응답 지연을 넉넉히 덮는다
        public const long REFRESH_LEAD_MS = 5 * 60 * MS_PER_SECOND;

        // 연결 실패나 시간 초과일 때만 다시 보낸다. 횟수를 다 쓰면 이 세션으로는 더 갱신하지 않는다
        private static readonly float[] G_RefreshRetryDelaysSeconds = { 1f, 2f, 4f };

        // 출시 빌드는 런처 없이 로그인할 수 없다. 안내는 모두 이 문구를 기준으로 쓴다
        public const string TEXT_LAUNCH_REQUIRED    = "런처로 실행하세요.";
        public const string TEXT_RECEIVING          = "런처에서 로그인 정보를 받는 중이다.";
        public const string TEXT_SESSION_ENDED      = "로그인이 만료됐다. 런처로 다시 실행하세요.";
        public const string TEXT_RECEIVE_FAILED     = "런처에서 로그인 정보를 받지 못했다. 런처로 다시 실행하세요.";
        public const string TEXT_DEV_LOGIN          = "개발 로그인: 아이디와 비밀번호를 넣는다.";
        public const string TEXT_DEV_SESSION_ENDED  = "로그인이 만료됐다. 개발 로그인을 다시 한다.";
        public const string TEXT_DEV_RECEIVE_FAILED = "런처 로그인 정보를 받지 못했다. 개발 로그인을 쓴다.";

        private const string SCHEME_HTTP  = "http";
        private const string SCHEME_HTTPS = "https";

        // 잘못된 UTF-8을 대체 문자로 바꾸지 않고 거절한다
        private static readonly UTF8Encoding _strictUtf8 = new UTF8Encoding(false, true);

        // DTO는 DataContract와 private 필드라 기본 해석기가 DataMember 이름으로 읽는다. 백엔드 JSON 설정과 맞춘다.
        // 서버 어셈블리의 BackendJson은 클라이언트 빌드에 없어 여기서 같은 설정을 따로 둔다.
        // 타입 이름 처리는 끈다. 파이프 본문이 만들 타입을 고르게 두면 원격 코드 실행 경로가 된다
        private static readonly JsonSerializerSettings _jsonSettings = CreateJsonSettings();

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 실행 인자에서 파이프 이름과 런처 PID를 읽는다, 둘 다 없으면 Absent </summary>
        public static ELauncherArgsStatus ParseLaunchArgs(string[] args, out string pipeName, out int launcherPid)
        {
            pipeName    = null;
            launcherPid = 0;
            if (args == null)
            {
                return ELauncherArgsStatus.Absent;
            }

            int pipeIndex = -1;
            int pidIndex  = -1;
            for (int i = 0; i < args.Length; ++i)
            {
                if (string.Equals(args[i], LauncherPipeProtocol.ARG_PIPE, StringComparison.Ordinal))
                {
                    // 같은 인자가 두 번 오면 어느 쪽이 런처가 넣은 값인지 알 수 없다
                    if (pipeIndex >= 0)
                    {
                        return ELauncherArgsStatus.Invalid;
                    }

                    pipeIndex = i;
                }
                else if (string.Equals(args[i], LauncherPipeProtocol.ARG_PID, StringComparison.Ordinal))
                {
                    if (pidIndex >= 0)
                    {
                        return ELauncherArgsStatus.Invalid;
                    }

                    pidIndex = i;
                }
            }

            if ((pipeIndex < 0) && (pidIndex < 0))
            {
                return ELauncherArgsStatus.Absent;
            }

            if ((pipeIndex < 0) || (pidIndex < 0))
            {
                return ELauncherArgsStatus.Invalid;
            }

            string pipeValue = ReadValue(args, pipeIndex);
            string pidValue  = ReadValue(args, pidIndex);
            if (!IsValidPipeName(pipeValue))
            {
                return ELauncherArgsStatus.Invalid;
            }

            if (!int.TryParse(pidValue, NumberStyles.None, CultureInfo.InvariantCulture, out int pid))
            {
                return ELauncherArgsStatus.Invalid;
            }

            if (pid <= 0)
            {
                return ELauncherArgsStatus.Invalid;
            }

            pipeName    = pipeValue;
            launcherPid = pid;
            return ELauncherArgsStatus.Valid;
        }

        /// <summary> 접두사 뒤에 정해진 길이의 hex만 있는 이름인지 본다 </summary>
        public static bool IsValidPipeName(string pipeName)
        {
            if (string.IsNullOrEmpty(pipeName))
            {
                return false;
            }

            string prefix = LauncherPipeProtocol.PIPE_NAME_PREFIX;
            if (pipeName.Length != (prefix.Length + PIPE_NAME_HEX_LENGTH))
            {
                return false;
            }

            if (!pipeName.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            for (int i = prefix.Length; i < pipeName.Length; ++i)
            {
                if (!IsHexDigit(pipeName[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary> 파이프로 받은 한 프레임 본문을 세션으로 읽는다, 형식이 맞지 않으면 false </summary>
        public static bool TryDeserializeHandoff(byte[] body, out LauncherHandoff handoff)
        {
            handoff = null;
            if ((body == null) || (body.Length == 0))
            {
                return false;
            }

            string json;
            try
            {
                json = _strictUtf8.GetString(body);
            }
            catch (ArgumentException)
            {
                return false;
            }

            try
            {
                handoff = JsonConvert.DeserializeObject<LauncherHandoff>(json, _jsonSettings);
            }
            catch (JsonException)
            {
                handoff = null;
                return false;
            }

            return handoff != null;
        }

        /// <summary> 받은 세션을 쓸 수 있는지 본다, 쓸 수 있으면 None </summary>
        public static ELauncherSessionFailure ValidateHandoff(LauncherHandoff handoff)
        {
            if (handoff == null)
            {
                return ELauncherSessionFailure.InvalidPayload;
            }

            if (handoff.ProtocolVersion != LauncherPipeProtocol.PROTOCOL_VERSION)
            {
                return ELauncherSessionFailure.ProtocolMismatch;
            }

            if (string.IsNullOrWhiteSpace(handoff.AccountId))
            {
                return ELauncherSessionFailure.EmptyAccount;
            }

            if (string.IsNullOrWhiteSpace(handoff.GameSessionToken))
            {
                return ELauncherSessionFailure.EmptyToken;
            }

            if (ComputeRemainingMs(handoff) <= 0)
            {
                return ELauncherSessionFailure.Expired;
            }

            if (!IsValidApiBaseUrl(handoff.ApiBaseUrl))
            {
                return ELauncherSessionFailure.InvalidApiBaseUrl;
            }

            return ELauncherSessionFailure.None;
        }

        /// <summary> 서버 시각 기준 만료까지 남은 시간, PC 시계와 무관하다 </summary>
        public static long ComputeRemainingMs(LauncherHandoff handoff)
        {
            if (handoff == null)
            {
                return 0;
            }

            return handoff.ExpiresAtUnixMs - handoff.ServerTimeUnixMs;
        }

        /// <summary> 받은 순간의 단조 시계 값에 남은 시간을 더해 만료 시점을 단조 시계 값으로 바꾼다 </summary>
        public static long ComputeExpiryTimestamp(long receivedTimestamp, long remainingMs, long timestampFrequency)
        {
            if ((remainingMs <= 0) || (timestampFrequency <= 0))
            {
                return receivedTimestamp;
            }

            // 초와 나머지로 나눠 곱해야 큰 만료값에서 넘치지 않는다. 넘칠 만큼 크면 만료 없음과 같게 둔다
            long seconds  = remainingMs / MS_PER_SECOND;
            long leftover = remainingMs % MS_PER_SECOND;
            if (seconds >= ((long.MaxValue - receivedTimestamp) / timestampFrequency) - 1)
            {
                return long.MaxValue;
            }

            return receivedTimestamp + (seconds * timestampFrequency) + ((leftover * timestampFrequency) / MS_PER_SECOND);
        }

        /// <summary> 단조 시계 기준 만료까지 남은 시간, 지났으면 0 </summary>
        public static long ComputeRemainingMs(long expiryTimestamp, long nowTimestamp, long timestampFrequency)
        {
            if ((timestampFrequency <= 0) || (nowTimestamp >= expiryTimestamp))
            {
                return 0;
            }

            long remaining = expiryTimestamp - nowTimestamp;
            long seconds   = remaining / timestampFrequency;
            long leftover  = remaining % timestampFrequency;
            if (seconds >= (long.MaxValue / MS_PER_SECOND) - 1)
            {
                return long.MaxValue;
            }

            return (seconds * MS_PER_SECOND) + ((leftover * MS_PER_SECOND) / timestampFrequency);
        }

        /// <summary> 남은 시간이 갱신 시점 안으로 들어왔는지 본다, 이미 만료됐으면 갱신하지 않는다 </summary>
        public static bool ShouldRefresh(long remainingMs)
        {
            return (remainingMs > 0) && (remainingMs <= REFRESH_LEAD_MS);
        }

        /// <summary> 갱신 응답 하나의 처리 방식. statusCode 0은 서버에 닿지 못한 것이다 </summary>
        public static EGameSessionRefreshOutcome ClassifyRefreshReply(long statusCode, int reasonCode, GameSessionIssueResponse value)
        {
            if (value != null)
            {
                bool usable = (!string.IsNullOrWhiteSpace(value.GameSessionToken)) && (value.ExpiresAtUnixMs > value.ServerTimeUnixMs);
                return usable ? EGameSessionRefreshOutcome.Succeeded : EGameSessionRefreshOutcome.Stopped;
            }

            if ((reasonCode == ContractCodes.RejectReason.TOKEN_REVOKED) || (reasonCode == ContractCodes.RejectReason.TOKEN_EXPIRED))
            {
                return EGameSessionRefreshOutcome.SessionEnded;
            }

            return (statusCode == 0) ? EGameSessionRefreshOutcome.RetryTransport : EGameSessionRefreshOutcome.Stopped;
        }

        /// <summary> retryIndex번째(0부터) 전송 재시도 전 대기, 횟수를 다 썼으면 false </summary>
        public static bool TryGetRefreshRetryDelay(int retryIndex, out float delaySeconds)
        {
            delaySeconds = 0f;
            if ((retryIndex < 0) || (retryIndex >= G_RefreshRetryDelaysSeconds.Length))
            {
                return false;
            }

            delaySeconds = G_RefreshRetryDelaysSeconds[retryIndex];
            return true;
        }

        /// <summary> 유효한 세션이 없을 때 로비에 띄울 안내. Received는 받은 세션이 만료된 경우다 </summary>
        public static string DescribeLaunchStatus(bool launchRequired, ELauncherSessionState state, ELauncherSessionFailure failure)
        {
            if (state == ELauncherSessionState.Receiving)
            {
                return TEXT_RECEIVING;
            }

            bool ended = (state == ELauncherSessionState.Received) || (failure == ELauncherSessionFailure.Expired)
                || (failure == ELauncherSessionFailure.Revoked);
            if (launchRequired)
            {
                if (ended)
                {
                    return TEXT_SESSION_ENDED;
                }

                return (state == ELauncherSessionState.Failed) ? TEXT_RECEIVE_FAILED : TEXT_LAUNCH_REQUIRED;
            }

            if (ended)
            {
                return TEXT_DEV_SESSION_ENDED;
            }

            return (state == ELauncherSessionState.Failed) ? TEXT_DEV_RECEIVE_FAILED : TEXT_DEV_LOGIN;
        }

        /// <summary> 수신 확인을 파이프로 보낼 본문으로 만든다 </summary>
        public static byte[] SerializeAck(LauncherHandoffAck ack)
        {
            if (ack == null)
            {
                throw new ArgumentNullException(nameof(ack));
            }

            return _strictUtf8.GetBytes(JsonConvert.SerializeObject(ack, _jsonSettings));
        }

        private static string ReadValue(string[] args, int flagIndex)
        {
            int valueIndex = flagIndex + 1;
            if (valueIndex >= args.Length)
            {
                return null;
            }

            return args[valueIndex];
        }

        private static bool IsHexDigit(char c)
        {
            return ((c >= '0') && (c <= '9')) || ((c >= 'a') && (c <= 'f')) || ((c >= 'A') && (c <= 'F'));
        }

        private static bool IsValidApiBaseUrl(string apiBaseUrl)
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                return false;
            }

            if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            return (string.Equals(uri.Scheme, SCHEME_HTTPS, StringComparison.OrdinalIgnoreCase))
                || (string.Equals(uri.Scheme, SCHEME_HTTP, StringComparison.OrdinalIgnoreCase));
        }

        private static JsonSerializerSettings CreateJsonSettings()
        {
            var settings = new JsonSerializerSettings();
            settings.ContractResolver    = new DefaultContractResolver();
            settings.ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor;
            settings.TypeNameHandling    = TypeNameHandling.None;
            settings.DateParseHandling   = DateParseHandling.None;
            settings.Culture             = CultureInfo.InvariantCulture;
            settings.NullValueHandling   = NullValueHandling.Include;
            settings.Formatting          = Formatting.None;
            settings.MaxDepth            = MAX_JSON_DEPTH;
            return settings;
        }
    }
}
