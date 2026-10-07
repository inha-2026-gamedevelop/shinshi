// System
using System.Globalization;
using System.Text;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    // 공지는 서버가 보낸 문자열이라 채팅 본문과 같은 위험(TMP 태그, 제어 문자)을 가진다. ChatRules.TryNormalize는 길이를 넘기거나
    // 금지 문자가 있으면 줄 전체를 거절하지만, 공지는 운영자가 쓴 글이라 거절하지 않고 문자만 걸러 최대한 보여 준다
    /// <summary> 서비스 상태의 공지를 게임 채팅 시스템 줄로 보여 줄지 고르고 한 줄로 정리하는 순수 규칙 </summary>
    public static class ServiceNoticeRules
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string LINE_PREFIX      = "[공지] ";
        private const string TITLE_BODY_JOIN  = " - ";
        private const string ELLIPSIS         = "...";
        private const char   TAG_OPEN         = '<';
        private const char   TAG_CLOSE        = '>';
        private const char   SAFE_TAG_OPEN    = '＜';
        private const char   SAFE_TAG_CLOSE   = '＞';
        private const char   SPACE            = ' ';

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 게임 클라이언트가 볼 공지인지, 런처 전용 공지는 거른다 </summary>
        public static bool IsForGame(ServiceNotice notice)
        {
            if (notice == null)
            {
                return false;
            }

            return (notice.Target == ServiceNotice.TARGET_GAME) || (notice.Target == ServiceNotice.TARGET_BOTH);
        }

        // 서버가 노출 중인 공지만 보내지만 클라이언트도 한 번 더 확인한다. 끝 시각이 0 이하면 끝이 없는 공지로 본다
        /// <summary> 서버 시각 기준으로 노출 기간 안인지, 시작 시각 포함 끝 시각 제외 </summary>
        public static bool IsActive(ServiceNotice notice, long nowUnixMs)
        {
            if (notice == null)
            {
                return false;
            }

            if (nowUnixMs < notice.StartsAtUnixMs)
            {
                return false;
            }

            return (notice.EndsAtUnixMs <= 0) || (nowUnixMs < notice.EndsAtUnixMs);
        }

        /// <summary> 아직 보인 적이 없으면 보이고, 가장 높은 중요도는 노출 중에 일정 간격으로 다시 보인다 </summary>
        public static bool ShouldShow(bool alreadyShown, double lastShownSeconds, double nowSeconds, int severity)
        {
            if (!alreadyShown)
            {
                return true;
            }

            return (severity >= ServiceNotice.SEVERITY_CRITICAL)
                && ((nowSeconds - lastShownSeconds) >= Constants.Chat.NOTICE_CRITICAL_REPEAT_SECONDS);
        }

        // 응답이 오는 사이 접속이 끝났거나 다시 맺어졌거나, 채팅 로그가 비워졌거나, 주소가 바뀌었으면 그 응답은 지난 접속의 것이다.
        // 이를 넣으면 비운 로그가 다시 채워지고 새 접속 로그에 옛 공지가 섞인다
        /// <summary> 요청을 보낼 때의 상태와 응답을 받은 지금의 상태가 달라 응답을 버려야 하는지 </summary>
        public static bool IsStaleResponse(int requestConnection, int currentConnection, bool isConnected, int requestClear,
            int currentClear, string requestUrl, string currentUrl)
        {
            return (!isConnected) || (requestConnection != currentConnection) || (requestClear != currentClear)
                || (!string.Equals(requestUrl, currentUrl, System.StringComparison.Ordinal));
        }

        /// <summary> 채팅 시스템 줄 한 줄, 제목과 본문이 모두 비면 null </summary>
        public static string FormatLine(ServiceNotice notice)
        {
            if (notice == null)
            {
                return null;
            }

            string title = Clean(notice.Title);
            string body  = Clean(notice.Body);
            if ((title.Length == 0) && (body.Length == 0))
            {
                return null;
            }

            string line;
            if (body.Length == 0)
            {
                line = LINE_PREFIX + title;
            }
            else if (title.Length == 0)
            {
                line = LINE_PREFIX + body;
            }
            else
            {
                line = LINE_PREFIX + title + TITLE_BODY_JOIN + body;
            }

            return Truncate(line);
        }

        /// <summary> 한 줄 평문으로 정리한다. 공백류와 제어 문자는 공백 하나로 합치고, 서식 문자와 짝 없는 대리 문자는 버리고, 꺾쇠는 전각으로 바꾼다 </summary>
        public static string Clean(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            var  buffer       = new StringBuilder(input.Length);
            bool pendingSpace = false;
            for (int i = 0; i < input.Length; ++i)
            {
                char c = input[i];
                if ((char.IsWhiteSpace(c)) || (char.IsControl(c)))
                {
                    pendingSpace = buffer.Length > 0;
                    continue;
                }

                if (char.GetUnicodeCategory(c) == UnicodeCategory.Format)
                {
                    continue;
                }

                bool isPair = (char.IsHighSurrogate(c)) && ((i + 1) < input.Length) && (char.IsLowSurrogate(input[i + 1]));
                if (((char.IsHighSurrogate(c)) && (!isPair)) || (char.IsLowSurrogate(c)))
                {
                    continue;
                }

                if (pendingSpace)
                {
                    buffer.Append(SPACE);
                    pendingSpace = false;
                }

                buffer.Append(ToSafe(c));
                if (isPair)
                {
                    ++i;
                    buffer.Append(input[i]);
                }
            }

            return buffer.ToString();
        }

        private static string Truncate(string line)
        {
            if (line.Length <= Constants.Chat.NOTICE_MAX_LENGTH)
            {
                return line;
            }

            int keep = Constants.Chat.NOTICE_MAX_LENGTH - ELLIPSIS.Length;

            // 대리 쌍의 가운데에서 자르지 않는다
            if ((keep > 0) && (char.IsHighSurrogate(line[keep - 1])))
            {
                --keep;
            }

            return line.Substring(0, keep) + ELLIPSIS;
        }

        private static char ToSafe(char c)
        {
            if (c == TAG_OPEN)
            {
                return SAFE_TAG_OPEN;
            }

            return (c == TAG_CLOSE) ? SAFE_TAG_CLOSE : c;
        }
    }
}
