// System
using System;
using System.Globalization;
using System.Text;

namespace Shinshi.Network
{
    /// <summary> 배포 로그 규약의 JSON 한 줄을 만든다, 공통 키를 항상 모두 쓰고 없는 값은 null이다 </summary>
    public sealed class ServerLogLine
    {
        // 수집기가 키 순서로 읽지는 않지만 사람이 줄을 비교하기 쉽게 규약 표 순서를 고정한다.
        // 상관 ID는 서버가 확정한 값만 받는다. 빈 문자열을 null로 바꾸는 이유는 규약이 빈 값 대신 null을 요구해서다.
        // 같은 인스턴스를 여러 스레드가 함께 쓰지 않는다. ServerJsonLogHandler가 잠근 채로만 부른다

        /****************************************
        *                Fields
        ****************************************/

        public const string LEVEL_TRACE    = "trace";
        public const string LEVEL_DEBUG    = "debug";
        public const string LEVEL_INFO     = "info";
        public const string LEVEL_WARN     = "warn";
        public const string LEVEL_ERROR    = "error";
        public const string LEVEL_CRITICAL = "critical";

        // 이벤트 이름은 고정 문자열만 쓴다. 사용자 입력, 예외 메시지, 토큰을 이름에 넣지 않는다
        public const string EVENT_SERVER_STARTED         = "server.started";
        public const string EVENT_SERVER_METRICS         = "server.metrics";
        public const string EVENT_DRAIN_REQUESTED        = "server.drain.requested";
        public const string EVENT_SHUTDOWN_FORCED        = "server.shutdown.forced";
        public const string EVENT_UNITY_LOG              = "unity.log";
        public const string EVENT_UNITY_EXCEPTION        = "unity.exception";

        // 0은 ContractCodes.RejectReason.NONE이다
        public const int NO_REASON = 0;

        private const string TIMESTAMP_FORMAT = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
        private const string NULL_LITERAL     = "null";
        private const string BEARER_PREFIX    = "Bearer ";
        private const string REDACTED         = "[REDACTED]";
        private const string HEX_DIGITS       = "0123456789abcdef";
        private const int    INITIAL_CAPACITY = 512;
        private const char   FIRST_PRINTABLE  = ' ';
        private const char   LINE_SEPARATOR   = '\u2028';
        private const char   PARA_SEPARATOR   = '\u2029';
        private const int    NIBBLE_BITS      = 4;
        private const int    NIBBLE_MASK      = 15;
        private const int    NIBBLE2_SHIFT    = 8;
        private const int    NIBBLE3_SHIFT    = 12;

        private readonly StringBuilder _builder = new StringBuilder(INITIAL_CAPACITY);

        /****************************************
        *              Properties
        ****************************************/

        public string Service        { get; }
        public string InstanceId     { get; }
        public string ContentVersion { get; }

        // 빌드 신원을 읽기 전에는 null이다
        public string BuildId        { get; set; }

        /****************************************
        *                Methods
        ****************************************/

        public ServerLogLine(string service, string instanceId, string contentVersion)
        {
            Service        = NullIfEmpty(service);
            InstanceId     = NullIfEmpty(instanceId);
            ContentVersion = NullIfEmpty(contentVersion);
        }

        /// <summary> 한 줄을 만든다. dataJson은 이 어셈블리가 만든 JSON 객체이거나 null이다 </summary>
        public string Format(DateTime utcTime, string level, string eventName, string sessionId, string participantId,
            string operationId, string eventId, int reasonCode, string dataJson)
        {
            StringBuilder b = _builder;
            b.Clear();
            b.Append("{\"ts\":");
            AppendJsonString(b, utcTime.ToUniversalTime().ToString(TIMESTAMP_FORMAT, CultureInfo.InvariantCulture));
            b.Append(",\"level\":");
            AppendJsonString(b, level);
            b.Append(",\"service\":");
            AppendJsonString(b, Service);
            b.Append(",\"instanceId\":");
            AppendJsonString(b, InstanceId);
            b.Append(",\"sessionId\":");
            AppendJsonString(b, NullIfEmpty(sessionId));
            b.Append(",\"participantId\":");
            AppendJsonString(b, NullIfEmpty(participantId));
            b.Append(",\"operationId\":");
            AppendJsonString(b, NullIfEmpty(operationId));
            b.Append(",\"event\":");
            AppendJsonString(b, eventName);
            b.Append(",\"buildId\":");
            AppendJsonString(b, NullIfEmpty(BuildId));
            b.Append(",\"contentVersion\":");
            AppendJsonString(b, ContentVersion);
            b.Append(",\"eventId\":");
            AppendJsonString(b, NullIfEmpty(eventId));
            b.Append(",\"reasonCode\":");
            if (reasonCode == NO_REASON)
            {
                b.Append(NULL_LITERAL);
            }
            else
            {
                b.Append(reasonCode.ToString(CultureInfo.InvariantCulture));
            }

            b.Append(",\"data\":");
            b.Append(string.IsNullOrEmpty(dataJson) ? NULL_LITERAL : dataJson);
            b.Append('}');
            return b.ToString();
        }

        /// <summary> 로그 문장 하나를 data 객체로 만든다, Bearer 뒤 값은 가린다 </summary>
        public static string FormatMessageData(string message)
        {
            var b = new StringBuilder(INITIAL_CAPACITY);
            b.Append("{\"message\":");
            AppendJsonString(b, RedactBearer(message));
            b.Append('}');
            return b.ToString();
        }

        /// <summary> 예외는 메시지에 비밀값이 섞일 수 있어 타입 이름만 남긴다 </summary>
        public static string FormatExceptionData(Exception exception)
        {
            var b = new StringBuilder(INITIAL_CAPACITY);
            b.Append("{\"exceptionType\":");
            AppendJsonString(b, exception?.GetType().FullName);
            b.Append('}');
            return b.ToString();
        }

        public static string FormatPortData(int port)
        {
            return "{\"gamePort\":" + port.ToString(CultureInfo.InvariantCulture) + "}";
        }

        /// <summary> JSON 문자열 값으로 붙인다, null이면 null 리터럴이다 </summary>
        public static void AppendJsonString(StringBuilder b, string value)
        {
            if (value == null)
            {
                b.Append(NULL_LITERAL);
                return;
            }

            b.Append('"');
            for (int i = 0; i < value.Length; ++i)
            {
                char c = value[i];
                switch (c)
                {
                    case '"':
                        b.Append("\\\"");
                        continue;
                    case '\\':
                        b.Append("\\\\");
                        continue;
                    case '\n':
                        b.Append("\\n");
                        continue;
                    case '\r':
                        b.Append("\\r");
                        continue;
                    case '\t':
                        b.Append("\\t");
                        continue;
                    default:
                        break;
                }

                // 줄 구분 문자는 JSON에서 허용되지만 일부 수집기가 줄바꿈으로 읽어 한 줄 계약이 깨진다.
                // 짝 없는 대리 문자는 UTF-8로 쓸 수 없어 출력에서 깨지므로 이스케이프로 남긴다
                if ((c < FIRST_PRINTABLE) || (c == LINE_SEPARATOR) || (c == PARA_SEPARATOR) || IsLoneSurrogate(value, i))
                {
                    AppendUnicodeEscape(b, c);
                    continue;
                }

                if (char.IsHighSurrogate(c))
                {
                    b.Append(c);
                    b.Append(value[i + 1]);
                    ++i;
                    continue;
                }

                b.Append(c);
            }

            b.Append('"');
        }

        // "Bearer " 뒤 공백 전까지를 가린다. 로그 문장에 인증 헤더가 실수로 들어가도 토큰이 남지 않게 하는 마지막 방어다
        public static string RedactBearer(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return message;
            }

            int index = message.IndexOf(BEARER_PREFIX, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return message;
            }

            var b = new StringBuilder(message.Length);
            int cursor = 0;
            while (index >= 0)
            {
                int valueStart = index + BEARER_PREFIX.Length;
                b.Append(message, cursor, valueStart - cursor);
                int valueEnd = valueStart;
                while ((valueEnd < message.Length) && (!char.IsWhiteSpace(message[valueEnd])))
                {
                    ++valueEnd;
                }

                if (valueEnd > valueStart)
                {
                    b.Append(REDACTED);
                }

                cursor = valueEnd;
                index  = (cursor < message.Length) ? message.IndexOf(BEARER_PREFIX, cursor, StringComparison.OrdinalIgnoreCase) : -1;
            }

            b.Append(message, cursor, message.Length - cursor);
            return b.ToString();
        }

        private static bool IsLoneSurrogate(string value, int index)
        {
            char c = value[index];
            if (char.IsHighSurrogate(c))
            {
                return ((index + 1) >= value.Length) || (!char.IsLowSurrogate(value[index + 1]));
            }

            if (char.IsLowSurrogate(c))
            {
                return (index == 0) || (!char.IsHighSurrogate(value[index - 1]));
            }

            return false;
        }

        private static void AppendUnicodeEscape(StringBuilder b, char c)
        {
            b.Append("\\u");
            b.Append(HEX_DIGITS[(c >> NIBBLE3_SHIFT) & NIBBLE_MASK]);
            b.Append(HEX_DIGITS[(c >> NIBBLE2_SHIFT) & NIBBLE_MASK]);
            b.Append(HEX_DIGITS[(c >> NIBBLE_BITS) & NIBBLE_MASK]);
            b.Append(HEX_DIGITS[c & NIBBLE_MASK]);
        }

        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
