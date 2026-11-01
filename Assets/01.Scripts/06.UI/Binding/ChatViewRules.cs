// System
using System;
using System.Text;

// 프로젝트 내부
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 채팅 탭 거르기와 한 줄 서식, 입력 채널 선택, 귓속말 명령 파싱 </summary>
    public static class ChatViewRules
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string PREFIX_ALL      = "[전체] ";
        private const string PREFIX_PARTY    = "[파티] ";
        private const string PREFIX_SYSTEM   = "[시스템] ";
        private const string PREFIX_GUILD    = "[길드] ";
        private const string PREFIX_WHISPER  = "[귓속말] ";
        private const string SENT_SUFFIX     = "에게";
        private const string NAME_SEPARATOR  = " : ";
        private const string COLOR_OPEN      = "<color=#";
        private const string COLOR_CLOSE     = "</color>";
        private const char   TAG_END         = '>';
        private const char   LINE_BREAK      = '\n';
        private const char   COMMAND_MARK    = '/';
        private const char   SPACE           = ' ';

        // 귓속말 명령. 영문은 대소문자를 가리지 않는다
        private const string COMMAND_WHISPER        = "w";
        private const string COMMAND_WHISPER_KOREAN = "귓";
        private const string COMMAND_REPLY          = "r";
        private const string WHISPER_PREFIX_FORMAT  = "/w {0} ";

        // 슬래시와 명령 글자 하나가 있어야 명령 줄이다
        private const int MIN_COMMAND_LENGTH = 2;

        // 입력 칸 채널 목록의 순서
        private const int DROPDOWN_ALL   = 0;
        private const int DROPDOWN_PARTY = 1;
        private const int DROPDOWN_GUILD = 2;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 탭에 보일 줄인지. 전체 탭은 모든 채널을, 귓속말 탭은 받은 것과 보낸 것을 보여 준다 </summary>
        public static bool Matches(EChatTab tab, EChatChannel channel)
        {
            switch (tab)
            {
                case EChatTab.Party:
                    return channel == EChatChannel.Party;
                case EChatTab.System:
                    return channel == EChatChannel.System;
                case EChatTab.Guild:
                    return channel == EChatChannel.Guild;
                case EChatTab.Whisper:
                    return (channel == EChatChannel.Whisper) || (channel == EChatChannel.WhisperSent);
                default:
                    return true;
            }
        }

        /// <summary> 길드, 귓속말 색 없이 한 줄을 붙인다 </summary>
        public static void AppendLine(StringBuilder into, in ChatEntry entry, string allColor, string partyColor, string systemColor)
        {
            AppendLine(into, in entry, allColor, partyColor, systemColor, null, null);
        }

        /// <summary> 한 줄을 붙인다. 색 값이 비어 있으면 기본 글자색을 쓴다. 본문과 이름은 서버가 태그를 막은 평문이다 </summary>
        public static void AppendLine(StringBuilder into, in ChatEntry entry, string allColor, string partyColor, string systemColor,
            string guildColor, string whisperColor)
        {
            if (into.Length > 0)
            {
                into.Append(LINE_BREAK);
            }

            string color    = ColorOf(entry.Channel, allColor, partyColor, systemColor, guildColor, whisperColor);
            bool   hasColor = !string.IsNullOrEmpty(color);
            if (hasColor)
            {
                into.Append(COLOR_OPEN).Append(color).Append(TAG_END);
            }

            switch (entry.Channel)
            {
                case EChatChannel.System:
                    into.Append(PREFIX_SYSTEM).Append(entry.Text);
                    break;
                case EChatChannel.WhisperSent:
                    into.Append(PREFIX_WHISPER).Append(entry.Sender).Append(SENT_SUFFIX).Append(NAME_SEPARATOR).Append(entry.Text);
                    break;
                default:
                    into.Append(PrefixOf(entry.Channel)).Append(entry.Sender).Append(NAME_SEPARATOR).Append(entry.Text);
                    break;
            }

            if (hasColor)
            {
                into.Append(COLOR_CLOSE);
            }
        }

        /// <summary> 입력 칸 채널 목록의 선택 번호를 보낼 채널로 바꾼다 </summary>
        public static EChatChannel ToSendChannel(int dropdownIndex)
        {
            switch (dropdownIndex)
            {
                case DROPDOWN_PARTY:
                    return EChatChannel.Party;
                case DROPDOWN_GUILD:
                    return EChatChannel.Guild;
                default:
                    return EChatChannel.All;
            }
        }

        /// <summary> 탭을 고르면 입력 채널도 맞춘다. 파티, 길드 탭은 그 채널로, 나머지는 전체로 둔다 </summary>
        public static int ToDropdownIndex(EChatTab tab)
        {
            switch (tab)
            {
                case EChatTab.Party:
                    return DROPDOWN_PARTY;
                case EChatTab.Guild:
                    return DROPDOWN_GUILD;
                default:
                    return DROPDOWN_ALL;
            }
        }

        /// <summary> 입력 중 Tab으로 넘길 다음 채널의 탭. 전체, 파티, 길드 순으로 돌고 길드 다음은 다시 전체다 </summary>
        public static EChatTab NextInputTab(int dropdownIndex)
        {
            switch (dropdownIndex)
            {
                case DROPDOWN_ALL:
                    return EChatTab.Party;
                case DROPDOWN_PARTY:
                    return EChatTab.Guild;
                default:
                    return EChatTab.All;
            }
        }

        /// <summary> 탭을 골랐을 때 입력 채널을 바꿀지. 시스템과 귓속말 탭은 보낼 채널이 아니라 바꾸지 않는다 </summary>
        public static bool ChangesInputChannel(EChatTab tab)
        {
            return (tab != EChatTab.System) && (tab != EChatTab.Whisper);
        }

        // 슬래시로 시작하지 않거나 모르는 명령은 일반 채팅으로 보낸다. 귓속말 대상 이름에는 공백이 없으므로 첫 공백까지가 이름이다
        /// <summary> 입력 줄을 귓속말 명령으로 읽는다. 명령이 아니면 None이고 body는 입력 그대로다 </summary>
        public static EChatCommand TryParseCommand(string input, out string target, out string body)
        {
            target = string.Empty;
            body   = input ?? string.Empty;
            if (string.IsNullOrEmpty(input))
            {
                return EChatCommand.None;
            }

            string line = input.TrimStart();
            if ((line.Length < MIN_COMMAND_LENGTH) || (line[0] != COMMAND_MARK))
            {
                return EChatCommand.None;
            }

            SplitFirst(line.Substring(1), out string command, out string rest);
            if (string.Equals(command, COMMAND_REPLY, StringComparison.OrdinalIgnoreCase))
            {
                body = rest;
                return EChatCommand.Reply;
            }

            if ((!string.Equals(command, COMMAND_WHISPER, StringComparison.OrdinalIgnoreCase))
                && (!string.Equals(command, COMMAND_WHISPER_KOREAN, StringComparison.Ordinal)))
            {
                return EChatCommand.None;
            }

            SplitFirst(rest, out string name, out string text);
            if (name.Length == 0)
            {
                body = string.Empty;
                return EChatCommand.Invalid;
            }

            target = name;
            body   = text;
            return EChatCommand.Whisper;
        }

        /// <summary> 친구 창, 상호작용 패널의 귓속말 버튼이 입력 칸에 채우는 글자 </summary>
        public static string WhisperPrefix(string characterName)
        {
            return string.Format(WHISPER_PREFIX_FORMAT, characterName ?? string.Empty);
        }

        private static void SplitFirst(string value, out string head, out string tail)
        {
            string trimmed = (value ?? string.Empty).TrimStart();
            int    space   = trimmed.IndexOf(SPACE);
            if (space < 0)
            {
                head = trimmed;
                tail = string.Empty;
                return;
            }

            head = trimmed.Substring(0, space);
            tail = trimmed.Substring(space + 1).Trim();
        }

        private static string PrefixOf(EChatChannel channel)
        {
            switch (channel)
            {
                case EChatChannel.Party:
                    return PREFIX_PARTY;
                case EChatChannel.Guild:
                    return PREFIX_GUILD;
                case EChatChannel.Whisper:
                    return PREFIX_WHISPER;
                default:
                    return PREFIX_ALL;
            }
        }

        private static string ColorOf(EChatChannel channel, string allColor, string partyColor, string systemColor,
            string guildColor, string whisperColor)
        {
            switch (channel)
            {
                case EChatChannel.System:
                    return systemColor;
                case EChatChannel.Party:
                    return partyColor;
                case EChatChannel.Guild:
                    return guildColor;
                case EChatChannel.Whisper:
                case EChatChannel.WhisperSent:
                    return whisperColor;
                default:
                    return allColor;
            }
        }
    }
}
