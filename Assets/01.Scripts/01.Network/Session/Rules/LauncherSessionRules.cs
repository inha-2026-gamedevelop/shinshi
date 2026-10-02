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
