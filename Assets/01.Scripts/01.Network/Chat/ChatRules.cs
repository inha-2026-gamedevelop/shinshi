// System
using System.Globalization;
using System.Text;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    // 클라이언트는 보내기 전에 같은 검사로 바로 안내하고, 서버는 받은 본문을 다시 검사한다. 클라이언트 검사만 믿지 않는다
    /// <summary> 채팅 본문과 이름을 한 줄 평문으로 정리하고 거절 이유를 안내 문구로 바꾼다 </summary>
    public static class ChatRules
    {
        /****************************************
        *                Fields
        ****************************************/

        // 꺾쇠를 전각으로 바꿔 TMP 서식 태그로 해석되지 않게 한다
        private const char TAG_OPEN       = '<';
        private const char TAG_CLOSE      = '>';
        private const char SAFE_TAG_OPEN  = '＜';
        private const char SAFE_TAG_CLOSE = '＞';
        private const char SPACE          = ' ';

        private const string FALLBACK_SENDER_FORMAT = "모험가 {0}";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 길이와 UTF-16 경계를 검사하고 줄바꿈, 연속 공백, 태그를 정리한다. 통과하면 None </summary>
        public static EChatReject TryNormalize(string input, out string text)
        {
            text = null;
            if (string.IsNullOrWhiteSpace(input))
            {
                return EChatReject.Empty;
            }

            if (input.Length > Constants.Chat.MAX_MESSAGE_LENGTH)
            {
                return EChatReject.TooLong;
            }

            var  buffer       = new StringBuilder(input.Length);
            bool pendingSpace = false;
            for (int i = 0; i < input.Length; ++i)
            {
                char c = input[i];
                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = buffer.Length > 0;
                    continue;
                }

                if (IsForbidden(input, i))
                {
                    return EChatReject.InvalidCharacter;
                }

                if (pendingSpace)
                {
                    buffer.Append(SPACE);
                    pendingSpace = false;
                }

                buffer.Append(ToSafe(c));
                if (char.IsHighSurrogate(c))
                {
                    ++i;
                    buffer.Append(input[i]);
                }
            }

            if (buffer.Length == 0)
            {
                return EChatReject.Empty;
            }

            text = buffer.ToString();
            return EChatReject.None;
        }

        /// <summary> 보낸 사람 이름을 평문으로 정리한다. 쓸 수 없으면 참가자 번호로 만든 이름 </summary>
        public static string NormalizeSender(string name, int participantId)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                string trimmed = (name.Length > Constants.Chat.MAX_SENDER_LENGTH)
                    ? name.Substring(0, Constants.Chat.MAX_SENDER_LENGTH)
                    : name;

                // 자른 끝에 짝 잃은 대리 문자가 남으면 버린다
                if ((trimmed.Length > 1) && (char.IsHighSurrogate(trimmed[trimmed.Length - 1])))
                {
                    trimmed = trimmed.Substring(0, trimmed.Length - 1);
                }

                if (TryNormalize(trimmed, out string normalized) == EChatReject.None)
                {
                    return normalized;
                }
            }

            return string.Format(CultureInfo.InvariantCulture, FALLBACK_SENDER_FORMAT, participantId);
        }

        // 귓속말은 대상 이름이 필요해 ChatWhisperRequestMessage로만 보낸다. 그래서 보낼 수 있는 채널과 받아 표시할 채널이 다르다
        /// <summary> 클라이언트가 ChatRequestMessage로 보낼 수 있는 채널인지 </summary>
        public static bool IsSendable(EChatChannel channel)
        {
            return (channel == EChatChannel.All) || (channel == EChatChannel.Party) || (channel == EChatChannel.Guild);
        }

        // System은 서버 방송이 아니라 ChatLog.AddSystem으로만 넣는다
        /// <summary> 서버가 보낸 채팅 줄로 표시할 채널인지 </summary>
        public static bool IsDisplayable(EChatChannel channel)
        {
            switch (channel)
            {
                case EChatChannel.All:
                case EChatChannel.Party:
                case EChatChannel.Guild:
                case EChatChannel.Whisper:
                case EChatChannel.WhisperSent:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary> 거절 이유의 시스템 안내 문구 </summary>
        public static string Describe(EChatReject reason)
        {
            switch (reason)
            {
                case EChatReject.Empty:
                    return "채팅 내용을 입력해 주세요.";
                case EChatReject.TooLong:
                    return $"채팅은 {Constants.Chat.MAX_MESSAGE_LENGTH}자까지 보낼 수 있습니다.";
                case EChatReject.InvalidCharacter:
                    return "채팅에 사용할 수 없는 문자가 포함되어 있습니다.";
                case EChatReject.RateLimited:
                    return "채팅을 너무 빠르게 보내고 있습니다. 잠시 후 다시 보내 주세요.";
                case EChatReject.NoParty:
                    return "파티에 들어간 뒤 파티 채팅을 보낼 수 있습니다.";
                case EChatReject.InvalidChannel:
                    return "이 채널로는 보낼 수 없습니다.";
                case EChatReject.NotConnected:
                    return "서버에 연결된 뒤 채팅을 보낼 수 있습니다.";
                case EChatReject.NoGuild:
                    return "길드에 가입한 뒤 길드 채팅을 보낼 수 있습니다.";
                case EChatReject.WhisperTargetNotFound:
                    return "해당 이름의 캐릭터가 없습니다.";
                case EChatReject.WhisperTargetOffline:
                    return "상대가 접속 중이 아닙니다.";
                case EChatReject.WhisperSelf:
                    return "자기 자신에게는 귓속말을 보낼 수 없습니다.";
                case EChatReject.BackendUnavailable:
                    return "잠시 후 다시 보내 주세요. 서버가 메시지를 전달하지 못했습니다.";
                case EChatReject.FeatureDisabled:
                    return "현재 사용할 수 없는 기능입니다.";
                default:
                    return string.Empty;
            }
        }

        // 제어, 서식(보이지 않는 방향 제어 등) 문자와 짝이 없는 대리 문자는 받지 않는다
        private static bool IsForbidden(string input, int index)
        {
            char c = input[index];
            if ((char.IsControl(c)) || (char.GetUnicodeCategory(c) == UnicodeCategory.Format) || (char.IsLowSurrogate(c)))
            {
                return true;
            }

            return (char.IsHighSurrogate(c)) && (((index + 1) >= input.Length) || (!char.IsLowSurrogate(input[index + 1])));
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
