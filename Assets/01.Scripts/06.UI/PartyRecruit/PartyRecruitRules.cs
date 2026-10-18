// System
using System;
using System.Collections.Generic;
using System.Text;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> Party recruit list filter and text rules, no Unity dependency </summary>
    public static class PartyRecruitRules
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int MAX_MEMBERS = Constants.Session.MAX_PARTY_SIZE;

        private const string MODE_NORMAL_TEXT   = "일반전";
        private const string MODE_BOSS_TEXT     = "보스전";
        private const string EASY_TEXT          = "쉬움";
        private const string HARD_TEXT          = "어려움";
        private const string PHASE_OPEN_TEXT    = "모집 중";
        private const string PHASE_COUNT_TEXT   = "출정 준비";
        private const string PHASE_BUSY_TEXT    = "출정 대기";
        private const string PHASE_DEPART_TEXT  = "출정 중";
        private const string CLASS_SEPARATOR    = " · ";

        private static readonly string[] G_CLASS_NAMES = { "전사", "마법사", "소환사", "검사", "마검사", "궁수" };

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> Fills into with indices of rooms whose name or content id contains the query, all rooms when empty </summary>
        public static void Filter(PartyRoomSummary[] rooms, string query, List<int> into)
        {
            if (into == null)
            {
                return;
            }

            into.Clear();
            if (rooms == null)
            {
                return;
            }

            string trimmed = (query == null) ? string.Empty : query.Trim();
            for (int i = 0; i < rooms.Length; ++i)
            {
                if ((trimmed.Length == 0) || (Matches(in rooms[i], trimmed)))
                {
                    into.Add(i);
                }
            }
        }

        /// <summary> Only open rooms with a free seat can be joined </summary>
        public static bool IsJoinable(in PartyRoomSummary room)
        {
            return (room.Phase == EPartyPhase.Open) && (room.MemberCount < MAX_MEMBERS);
        }

        public static string DescribeMode(EPartyMode mode)
        {
            return (mode == EPartyMode.Boss) ? MODE_BOSS_TEXT : MODE_NORMAL_TEXT;
        }

        public static string DescribeDifficulty(sbyte difficulty)
        {
            if (difficulty == GameSessionCodes.Difficulty.EASY)
            {
                return EASY_TEXT;
            }

            return (difficulty == GameSessionCodes.Difficulty.HARD) ? HARD_TEXT : string.Empty;
        }

        public static string DescribePhase(EPartyPhase phase)
        {
            switch (phase)
            {
                case EPartyPhase.Open:
                    return PHASE_OPEN_TEXT;
                case EPartyPhase.Countdown:
                    return PHASE_COUNT_TEXT;
                case EPartyPhase.Departing:
                    return PHASE_DEPART_TEXT;
                default:
                    return PHASE_BUSY_TEXT;
            }
        }

        /// <summary> Summarizes per-class counts like "전사 1 · 궁수 2", empty when nobody is listed </summary>
        public static string DescribeClasses(in PartyRoomSummary room)
        {
            byte[] counts = { room.Warriors, room.Sorcerers, room.Summoners, room.Swordsmen, room.Mageknights, room.Archers };
            var builder = new StringBuilder();
            for (int i = 0; i < counts.Length; ++i)
            {
                if (counts[i] == 0)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(CLASS_SEPARATOR);
                }

                builder.Append(G_CLASS_NAMES[i]).Append(' ').Append(counts[i]);
            }

            return builder.ToString();
        }

        private static bool Matches(in PartyRoomSummary room, string query)
        {
            return Contains(room.Name, query) || Contains(room.ContentId, query);
        }

        private static bool Contains(string source, string query)
        {
            return (!string.IsNullOrEmpty(source)) && (source.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
