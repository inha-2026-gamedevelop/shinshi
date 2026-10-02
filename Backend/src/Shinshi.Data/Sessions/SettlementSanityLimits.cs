// System
using System;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Sessions
{
    /// <summary> 던전 서버가 보낸 추가 정기와 정산안의 상한. API는 정산을 다시 계산하지 않지만 침해된 서버가 경제를 무너뜨리지 못하게 막는다 </summary>
    public static class SettlementSanityLimits
    {
        /****************************************
        *                Fields
        ****************************************/

        // TODO(기획 미정): 출정 하나에서 참가자 한 명이 받을 수 있는 정기 최대값. 기본 정기 적립 합계, 추가 정기 적립 합계,
        // 정산안 정기, 장애 취소 지급액에 각각 적용한다. 드롭 표가 정해지면 그 최대값에 여유를 더해 바꾼다
        public const long MAX_SESSION_ESSENCE = 100000;

        // TODO(기획 미정): 정산안 지갑 칸과 부적 칸의 참가자당 상한. Unity Constants.Session.SETTLEMENT_MAX_SESSION_*와 같은 값이다.
        // 드롭 표 최대값(정련편 6웨이브 120, 티켓 처치당 최대 1장 180, 엽전 월백 하드 260)에 여유를 둔 임시값이다
        public const long MAX_SESSION_SHARDS     = 999;
        public const long MAX_SESSION_TICKETS    = 999;
        public const long MAX_SESSION_CURRENCY   = 9999;
        public const long MAX_TALISMAN_QUANTITY  = 999;
        public const int  MAX_GRANT_TALISMANS    = 64;

        // TODO(기획 미정): 정산 한 번에 참가자 한 명이 받을 수 있는 아이템 하나의 최대 수량
        public const long MAX_ITEM_QUANTITY = 999;

        // TODO(기획 미정): 콘텐츠 버전별 허용 definitionId 목록이 API에 생기면 접두 규칙 대신 목록으로 검사한다.
        // 지금은 GameDB 아이템 ID 규칙(item_ 접두, 소문자, 숫자, 밑줄)만 허용한다
        public const string ITEM_DEFINITION_PREFIX   = "item_";
        public const int    MAX_DEFINITION_ID_LENGTH = 64;

        // 출처: Assets/09.Data/Reward/RewardCatalog.asset의 _essenceItemId, _refineShardItemId,
        // _bossTicketItemId, _tradeCurrencyItemId. 지갑 자산을 account_items로 지급하면 별도 잔액 경로를 우회한다
        public const string ESSENCE_DEFINITION_ID  = "item_essence";
        public const string SHARD_DEFINITION_ID    = "item_refine_shard";
        public const string TICKET_DEFINITION_ID   = "item_boss_ticket";
        public const string CURRENCY_DEFINITION_ID = "item_yeopjeon";

        public const int REJECT_REASON = ContractCodes.RejectReason.SETTLEMENT_LIMIT_EXCEEDED;

        // 정산 아이템과 부적 귀속 허용 목록. Unity 정산 규칙의 Constants.Session.SETTLEMENT_ITEM_BINDING_KIND와 같은 값이다.
        // 사용자 결정(2026-10-01)으로 계정 귀속만 받는다. 캐릭터 귀속, 거래 가능 보상이 생기면 목록을 늘린다
        public const string ACCOUNT_BINDING_KIND = "Account";

        // 부적 효과 ID 형식. Assets TalismanCatalogSO.EFFECT_ID_PATTERN(^T\d{2}-\d[A-Z]$)과 Unity SettlementRules가 같은 규칙이다.
        // 아이템 ID(item_ 접두)와 겹치지 않으므로 account_items.definition_id에 효과 ID를 그대로 쓴다
        private const int  TALISMAN_ID_LENGTH       = 6;
        private const char TALISMAN_ID_PREFIX       = 'T';
        private const char TALISMAN_ID_SEPARATOR    = '-';
        private const int  TALISMAN_SEPARATOR_INDEX = 3;
        private const int  TALISMAN_CHOICE_INDEX    = 5;

        // TODO(기획 미정): 한 던전 인스턴스가 24시간 동안 종료 정산안으로 지급할 수 있는 정기 합계. 임시값은
        // 출정당 참가자 상한 * 최대 파티 * 하루 출정 추정(5분에 하나)이다. 드롭 표와 실측 출정 수가 정해지면 줄인다
        public const int  INSTANCE_ESSENCE_WINDOW_HOURS   = 24;
        public const long INSTANCE_SESSIONS_PER_DAY       = 288;
        public const long MAX_INSTANCE_DAILY_ESSENCE      = MAX_SESSION_ESSENCE * Reservations.ReservationInput.MAX_PARTY_SIZE
            * INSTANCE_SESSIONS_PER_DAY;

        private static readonly string[] G_AllowedBindingKinds = { ACCOUNT_BINDING_KIND };

        /****************************************
        *              Properties
        ****************************************/

        public static TimeSpan InstanceEssenceWindow => TimeSpan.FromHours(INSTANCE_ESSENCE_WINDOW_HOURS);

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 정산 아이템 귀속이 허용 목록에 있는지 본다. 대소문자까지 같아야 한다 </summary>
        public static bool IsAllowedBinding(string? bindingKind)
        {
            return !string.IsNullOrEmpty(bindingKind)
                && (Array.IndexOf(G_AllowedBindingKinds, bindingKind) >= 0);
        }

        /// <summary> 부적 효과 ID 형식(예 T01-1A)인지 본다. 대문자와 숫자 자리까지 같아야 한다 </summary>
        public static bool IsTalismanEffectId(string? effectId)
        {
            if ((effectId == null) || (effectId.Length != TALISMAN_ID_LENGTH) || (effectId[0] != TALISMAN_ID_PREFIX)
                || (effectId[TALISMAN_SEPARATOR_INDEX] != TALISMAN_ID_SEPARATOR))
            {
                return false;
            }

            for (int index = 1; index < TALISMAN_CHOICE_INDEX; ++index)
            {
                if ((index != TALISMAN_SEPARATOR_INDEX) && ((effectId[index] < '0') || (effectId[index] > '9')))
                {
                    return false;
                }
            }

            char choice = effectId[TALISMAN_CHOICE_INDEX];
            return (choice >= 'A') && (choice <= 'Z');
        }

        /// <summary> 정산 아이템 줄로 받는 ID인지 본다. 지갑 자산 ID는 아이템 줄에서만 거절하고 정산안의 지갑 칸으로 받는다 </summary>
        public static bool IsAllowedItem(string? definitionId)
        {
            if (string.IsNullOrEmpty(definitionId) || (definitionId.Length > MAX_DEFINITION_ID_LENGTH)
                || (definitionId.Length <= ITEM_DEFINITION_PREFIX.Length)
                || !definitionId.StartsWith(ITEM_DEFINITION_PREFIX, StringComparison.Ordinal))
            {
                return false;
            }

            if ((definitionId == ESSENCE_DEFINITION_ID) || (definitionId == SHARD_DEFINITION_ID)
                || (definitionId == TICKET_DEFINITION_ID) || (definitionId == CURRENCY_DEFINITION_ID))
            {
                return false;
            }

            foreach (char character in definitionId)
            {
                if (!(((character >= 'a') && (character <= 'z')) || ((character >= '0') && (character <= '9'))
                    || (character == '_')))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
