// System
using System;
using System.Collections.Generic;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Prologue
{
    /// <summary> 프롤로그 입장 결과. 성공이면 Ticket이 있고 ReasonCode는 NONE이다 </summary>
    public sealed class PrologueEntryOutcome
    {
        /****************************************
        *              Properties
        ****************************************/

        public int         ReasonCode { get; }
        public JoinTicket? Ticket     { get; }

        /****************************************
        *                Methods
        ****************************************/

        private PrologueEntryOutcome(int reasonCode, JoinTicket? ticket)
        {
            ReasonCode = reasonCode;
            Ticket     = ticket;
        }

        public static PrologueEntryOutcome Issued(JoinTicket ticket)
        {
            ArgumentNullException.ThrowIfNull(ticket);
            return new PrologueEntryOutcome(ContractCodes.RejectReason.NONE, ticket);
        }

        public static PrologueEntryOutcome Rejected(int reasonCode)
        {
            if (reasonCode == ContractCodes.RejectReason.NONE)
            {
                throw new ArgumentOutOfRangeException(nameof(reasonCode));
            }

            return new PrologueEntryOutcome(reasonCode, null);
        }
    }

    /// <summary> 던전 서버가 보내는 단계 저장 입력. 범위 검사는 저장소가 거절 사유로 돌려준다 </summary>
    public sealed class PrologueStageInput
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid DungeonInstanceId { get; }
        public Guid SessionId         { get; }
        public Guid OperationId       { get; }
        public Guid CharacterId       { get; }
        public int  Stage             { get; }
        public int  WolbaekPotions    { get; }
        public bool TalismanChecked   { get; }

        /****************************************
        *                Methods
        ****************************************/

        public PrologueStageInput(Guid dungeonInstanceId, Guid sessionId, Guid operationId, Guid characterId, int stage,
            int wolbaekPotions, bool talismanChecked)
        {
            if ((dungeonInstanceId == Guid.Empty) || (sessionId == Guid.Empty) || (operationId == Guid.Empty)
                || (characterId == Guid.Empty))
            {
                throw new ArgumentException("Dungeon, session, operation and character IDs are required.");
            }

            DungeonInstanceId = dungeonInstanceId;
            SessionId         = sessionId;
            OperationId       = operationId;
            CharacterId       = characterId;
            Stage             = stage;
            WolbaekPotions    = wolbaekPotions;
            TalismanChecked   = talismanChecked;
        }

        /// <summary> 단계 저장 요청이 받을 수 있는 범위인가. 완료(DONE)는 완료 경로로만 기록한다 </summary>
        public bool IsInRange()
        {
            return (Stage >= PrologueCodes.Stage.INTRO) && (Stage < PrologueCodes.Stage.DONE)
                && (WolbaekPotions >= 0) && (WolbaekPotions <= PrologueCodes.MAX_POTIONS);
        }
    }

    /// <summary> 성장 메뉴 안내 완료 기록 결과. 거절이어도 목록은 비어 있을 수 있다 </summary>
    public sealed class MenuGuideOutcome
    {
        /****************************************
        *              Properties
        ****************************************/

        public int                   ReasonCode       { get; }
        public IReadOnlyList<string> MenuIds          { get; }
        public long                  ServerTimeUnixMs { get; }

        /****************************************
        *                Methods
        ****************************************/

        public MenuGuideOutcome(int reasonCode, IReadOnlyList<string> menuIds, long serverTimeUnixMs)
        {
            ArgumentNullException.ThrowIfNull(menuIds);
            ReasonCode       = reasonCode;
            MenuIds          = menuIds;
            ServerTimeUnixMs = serverTimeUnixMs;
        }
    }

    /// <summary> 메뉴 안내 ID 형식. 0023의 CHECK(^[a-z0-9_]{1,40}$)와 같다 </summary>
    public static class MenuGuideRules
    {
        /****************************************
        *                Methods
        ****************************************/

        public static bool IsValidMenuId(string? menuId)
        {
            if (string.IsNullOrEmpty(menuId) || (menuId.Length > PrologueCodes.MAX_MENU_ID_LENGTH))
            {
                return false;
            }

            foreach (char character in menuId)
            {
                bool valid = ((character >= 'a') && (character <= 'z')) || ((character >= '0') && (character <= '9'))
                    || (character == '_');
                if (!valid)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
