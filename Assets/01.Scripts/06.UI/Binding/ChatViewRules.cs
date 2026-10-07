// System
using System.Text;

// 프로젝트 내부
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 채팅 탭 거르기와 한 줄 서식, 입력 채널 선택 </summary>
    public static class ChatViewRules
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string PREFIX_ALL     = "[전체] ";
        private const string PREFIX_PARTY   = "[파티] ";
        private const string PREFIX_SYSTEM  = "[시스템] ";
        private const string NAME_SEPARATOR = " : ";
        private const string COLOR_OPEN     = "<color=#";
        private const string COLOR_CLOSE    = "</color>";
        private const char   TAG_END        = '>';
        private const char   LINE_BREAK     = '\n';

        // 입력 칸 채널 목록의 순서
        private const int DROPDOWN_ALL   = 0;
        private const int DROPDOWN_PARTY = 1;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 탭에 보일 줄인지. 전체 탭은 모든 채널을 보여 준다 </summary>
        public static bool Matches(EChatTab tab, EChatChannel channel)
        {
            switch (tab)
            {
                case EChatTab.Party:
                    return channel == EChatChannel.Party;
                case EChatTab.System:
                    return channel == EChatChannel.System;
                default:
                    return true;
            }
        }

        /// <summary> 한 줄을 붙인다. 색 값이 비어 있으면 기본 글자색을 쓴다. 본문과 이름은 서버가 태그를 막은 평문이다 </summary>
        public static void AppendLine(StringBuilder into, in ChatEntry entry, string allColor, string partyColor, string systemColor)
        {
            if (into.Length > 0)
            {
                into.Append(LINE_BREAK);
            }

            string color = (entry.Channel == EChatChannel.System) ? systemColor
                : (entry.Channel == EChatChannel.Party) ? partyColor : allColor;
            bool hasColor = !string.IsNullOrEmpty(color);
            if (hasColor)
            {
                into.Append(COLOR_OPEN).Append(color).Append(TAG_END);
            }

            if (entry.Channel == EChatChannel.System)
            {
                into.Append(PREFIX_SYSTEM).Append(entry.Text);
            }
            else
            {
                into.Append((entry.Channel == EChatChannel.Party) ? PREFIX_PARTY : PREFIX_ALL)
                    .Append(entry.Sender).Append(NAME_SEPARATOR).Append(entry.Text);
            }

            if (hasColor)
            {
                into.Append(COLOR_CLOSE);
            }
        }

        /// <summary> 입력 칸 채널 목록의 선택 번호를 보낼 채널로 바꾼다 </summary>
        public static EChatChannel ToSendChannel(int dropdownIndex)
        {
            return (dropdownIndex == DROPDOWN_PARTY) ? EChatChannel.Party : EChatChannel.All;
        }

        /// <summary> 탭을 고르면 입력 채널도 맞춘다. 파티 탭만 파티로, 나머지는 전체로 둔다 </summary>
        public static int ToDropdownIndex(EChatTab tab)
        {
            return (tab == EChatTab.Party) ? DROPDOWN_PARTY : DROPDOWN_ALL;
        }
    }
}
