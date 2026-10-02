// System
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Shinshi.Data.Settlement
{
    /// <summary> 잔액 변경과 같은 트랜잭션에 economy_ledger를 추가하는 공용 경로. 구현은 원장 작업이 제공한다 </summary>
    public interface IEconomyLedgerWriter
    {
        /****************************************
        *                Methods
        ****************************************/

        // context의 연결과 트랜잭션만 쓴다. 실패는 예외로 알려 효과 전체를 되돌린다. 빈 목록은 아무것도 쓰지 않는다
        Task AppendAsync(OperationExecutionContext context, IReadOnlyList<EconomyLedgerLine> entries,
            CancellationToken cancellationToken);
    }

    /// <summary> 추가 전용 원장 한 줄. 자산 구분은 0003 economy_ledger의 CHECK 제약과 같다. 자산별 delta 합계가 잔액과 같아야 한다 </summary>
    public sealed class EconomyLedgerLine
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid   AccountId    { get; }
        public Guid?  CharacterId  { get; }
        public string AssetKind    { get; }
        public string AssetId      { get; }
        public string BindingKind  { get; }
        public string EntryKey     { get; }
        public long   Delta        { get; }
        public long   BalanceAfter { get; }
        public string Reason       { get; }
        public string SourceType   { get; }
        public string SourceId     { get; }
        public Guid   OperationId  { get; }
        public string ActorKind    { get; }
        public Guid   ActorId      { get; }

        /****************************************
        *                Methods
        ****************************************/

        public EconomyLedgerLine(Guid accountId, Guid? characterId, string assetKind, string assetId, string bindingKind,
            long delta, long balanceAfter, string reason, string sourceType, string sourceId, Guid operationId,
            string actorKind, Guid actorId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(assetKind);
            ArgumentNullException.ThrowIfNull(assetId);
            ArgumentNullException.ThrowIfNull(bindingKind);
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceType);
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
            ArgumentException.ThrowIfNullOrWhiteSpace(actorKind);
            if (delta == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(delta), "A ledger entry must change the balance.");
            }
            if (balanceAfter < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(balanceAfter));
            }

            AccountId    = accountId;
            CharacterId  = characterId;
            AssetKind    = assetKind;
            AssetId      = assetId;
            BindingKind  = bindingKind;
            Delta        = delta;
            BalanceAfter = balanceAfter;
            Reason       = reason;
            SourceType   = sourceType;
            SourceId     = sourceId;
            OperationId  = operationId;
            ActorKind    = actorKind;
            ActorId      = actorId;

            // 한 작업 안에서 자산 하나는 한 줄만 쓴다. 재전달 때 같은 키가 나와 원장 중복을 막는다
            EntryKey = assetKind + ":" + accountId.ToString("N") + ":" + (characterId?.ToString("N") ?? string.Empty)
                + ":" + assetId + ":" + bindingKind;
        }
    }

    /// <summary> 출정 경로가 쓰는 원장 문자열 값 </summary>
    public static class EconomyLedgerCodes
    {
        /****************************************
        *                Fields
        ****************************************/

        // 0003 economy_ledger.asset_kind와 같은 값. Tickets, Currency, AccountItem은 character_id 없이,
        // Essence, Shards는 캐릭터별로 쓴다. 미등록 부적은 asset_id가 효과 ID인 AccountItem이다
        public const string ASSET_TICKET   = "Tickets";
        public const string ASSET_ITEM     = "AccountItem";
        public const string ASSET_ESSENCE  = "Essence";
        public const string ASSET_SHARDS   = "Shards";
        public const string ASSET_CURRENCY = "Currency";

        public const string REASON_SESSION_START_TICKET  = "SessionStartTicket";
        public const string REASON_SESSION_POTION_USE    = "SessionPotionUse";
        public const string REASON_SESSION_SETTLEMENT    = "SessionSettlement";
        public const string REASON_SERVICE_CANCEL_REFUND = "ServiceCancelRefund";
        public const string REASON_SERVICE_CANCEL_GRANT  = "ServiceCancelEssence";

        public const string SOURCE_GAME_SESSION = "GameSession";
        public const string ACTOR_SERVER        = "Server";
    }
}
