// System
using System;
using System.Collections.Generic;
using System.Linq;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Sessions
{
    /// <summary> 정산으로 지급할 계정 아이템 또는 부적 한 묶음. 부적 줄의 DefinitionId는 효과 ID다 </summary>
    public sealed class SettlementItemInput
    {
        /****************************************
        *              Properties
        ****************************************/

        public string DefinitionId { get; set; } = string.Empty;
        public string BindingKind  { get; set; } = string.Empty;
        public long   Quantity     { get; set; }
    }

    /// <summary> 던전 서버가 계산한 참가자 한 명의 정산안. API는 다시 계산하지 않는다 </summary>
    public sealed class SettlementGrantInput
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid                      ParticipantId { get; set; }
        public long                      Essence       { get; set; }
        public List<SettlementItemInput> Items         { get; set; } = new List<SettlementItemInput>();

        // 지갑 칸. 정련편은 캐릭터, 티켓과 엽전은 계정 지갑에 지급한다
        public long                      RefineShards  { get; set; }
        public long                      Tickets       { get; set; }
        public long                      Currency      { get; set; }

        // 미등록 부적. 계정 귀속 인벤토리(account_items)에 효과 ID별로 쌓는다
        public List<SettlementItemInput> Talismans     { get; set; } = new List<SettlementItemInput>();
    }

    /// <summary> 순번 있는 세션 이벤트 한 건. 종류별로 쓰는 필드만 채운다 </summary>
    public sealed class SessionEventInput
    {
        /****************************************
        *              Properties
        ****************************************/

        public long                       EventSeq              { get; set; }
        public Guid                       EventId               { get; set; }
        public int                        EventKind             { get; set; }
        public Guid?                      ParticipantId         { get; set; }
        public string                     DefinitionId          { get; set; } = string.Empty;
        public string                     BindingKind           { get; set; } = string.Empty;
        public long                       Amount                { get; set; }
        public int                        EndReason             { get; set; }
        public Guid?                      SettlementOperationId { get; set; }
        public List<SettlementGrantInput> Grants                { get; set; } = new List<SettlementGrantInput>();
        public string                     PayloadJson           { get; set; } = string.Empty;
    }

    /// <summary> 1초 단위 이벤트 배치. 형식이 틀리면 저장소에 들어오기 전에 요청 형식 오류로 거절한다 </summary>
    public static class SessionEventValidator
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int MAX_BATCH_EVENTS  = 256;
        public const int MAX_PAYLOAD_CHARS = 8192;
        public const int MAX_GRANT_ITEMS   = 64;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 배치 안에서 순번이 1씩 이어지고 ID가 겹치지 않으며 SESSION_ENDED가 마지막에만 있는지 본다 </summary>
        public static bool IsValid(IReadOnlyList<SessionEventInput> events)
        {
            return GetRejectReason(events) == ContractCodes.RejectReason.NONE;
        }

        /// <summary> 형식 오류와 정산 상한 초과를 구분하고 유효한 배치는 NONE을 반환한다 </summary>
        public static int GetRejectReason(IReadOnlyList<SessionEventInput> events)
        {
            if ((events == null) || (events.Count == 0) || (events.Count > MAX_BATCH_EVENTS))
            {
                return ContractCodes.RejectReason.SESSION_MISMATCH;
            }
            if (events.Any(entry => entry == null)
                || (events.Select(entry => entry.EventId).Distinct().Count() != events.Count))
            {
                return ContractCodes.RejectReason.SESSION_MISMATCH;
            }

            for (int index = 0; index < events.Count; ++index)
            {
                SessionEventInput entry = events[index];
                if ((entry.EventId == Guid.Empty) || (entry.EventSeq <= 0))
                {
                    return ContractCodes.RejectReason.SESSION_MISMATCH;
                }
                if ((index > 0) && (entry.EventSeq != events[index - 1].EventSeq + 1))
                {
                    return ContractCodes.RejectReason.SESSION_MISMATCH;
                }
                if ((entry.PayloadJson ?? string.Empty).Length > MAX_PAYLOAD_CHARS)
                {
                    return ContractCodes.RejectReason.SESSION_MISMATCH;
                }

                bool last = index == events.Count - 1;
                int reason = GetEntryRejectReason(entry, last);
                if (reason != ContractCodes.RejectReason.NONE)
                {
                    return reason;
                }
            }

            // 단일 이벤트 상한을 통과한 뒤 합산하므로 배치 크기 내에서 long 합계가 넘치지 않는다. 기본, 추가 적립은 따로 센다
            if (events.Where(entry => IsAccrual(entry.EventKind))
                .GroupBy(entry => (entry.EventKind, entry.ParticipantId!.Value))
                .Any(group => group.Sum(entry => entry.Amount) > SettlementSanityLimits.MAX_SESSION_ESSENCE))
            {
                return SettlementSanityLimits.REJECT_REASON;
            }

            return ContractCodes.RejectReason.NONE;
        }

        /// <summary> 출정 원장에 정기를 쌓는 이벤트인지 본다. ESSENCE_ACCRUED는 추가, BASE_ESSENCE_ACCRUED는 기본 정기다 </summary>
        public static bool IsAccrual(int eventKind)
        {
            return (eventKind == GameSessionCodes.EventKind.ESSENCE_ACCRUED)
                || (eventKind == GameSessionCodes.EventKind.BASE_ESSENCE_ACCRUED);
        }

        private static int GetEntryRejectReason(SessionEventInput entry, bool last)
        {
            switch (entry.EventKind)
            {
                case GameSessionCodes.EventKind.LOOT:
                    return ContractCodes.RejectReason.NONE;
                case GameSessionCodes.EventKind.POTION_USED:
                    return (HasParticipant(entry) && !string.IsNullOrWhiteSpace(entry.DefinitionId)
                        && !string.IsNullOrWhiteSpace(entry.BindingKind))
                        ? ContractCodes.RejectReason.NONE : ContractCodes.RejectReason.SESSION_MISMATCH;
                case GameSessionCodes.EventKind.ESSENCE_ACCRUED:
                case GameSessionCodes.EventKind.BASE_ESSENCE_ACCRUED:
                    if (!HasParticipant(entry) || (entry.Amount <= 0))
                    {
                        return ContractCodes.RejectReason.SESSION_MISMATCH;
                    }
                    return entry.Amount > SettlementSanityLimits.MAX_SESSION_ESSENCE
                        ? SettlementSanityLimits.REJECT_REASON : ContractCodes.RejectReason.NONE;
                case GameSessionCodes.EventKind.SESSION_ENDED:
                    return last ? GetEndRejectReason(entry) : ContractCodes.RejectReason.SESSION_MISMATCH;
                default:
                    return ContractCodes.RejectReason.SESSION_MISMATCH;
            }
        }

        private static int GetEndRejectReason(SessionEventInput entry)
        {
            // 서비스 장애 취소는 API 복구 경로만 기록한다. 던전 서버가 보낼 수 있는 종료 원인이 아니다
            if ((entry.EndReason < GameSessionCodes.EndReason.SUCCESS)
                || (entry.EndReason >= GameSessionCodes.EndReason.SERVICE_CANCELLED))
            {
                return ContractCodes.RejectReason.SESSION_MISMATCH;
            }
            if (!entry.SettlementOperationId.HasValue || (entry.SettlementOperationId.Value == Guid.Empty))
            {
                return ContractCodes.RejectReason.SESSION_MISMATCH;
            }
            if ((entry.Grants == null) || entry.Grants.Any(grant => grant == null)
                || (entry.Grants.Select(grant => grant.ParticipantId).Distinct().Count() != entry.Grants.Count))
            {
                return ContractCodes.RejectReason.SESSION_MISMATCH;
            }

            foreach (SettlementGrantInput grant in entry.Grants)
            {
                int reason = GetGrantRejectReason(grant, entry.EndReason);
                if (reason != ContractCodes.RejectReason.NONE)
                {
                    return reason;
                }
            }

            return ContractCodes.RejectReason.NONE;
        }

        // 형식 오류는 SESSION_MISMATCH, 상한과 경제 규칙 위반은 SETTLEMENT_LIMIT_EXCEEDED다
        private static int GetGrantRejectReason(SettlementGrantInput grant, int endReason)
        {
            if ((grant.ParticipantId == Guid.Empty) || (grant.Essence < 0) || (grant.Items == null)
                || (grant.Talismans == null) || (grant.RefineShards < 0) || (grant.Tickets < 0) || (grant.Currency < 0))
            {
                return ContractCodes.RejectReason.SESSION_MISMATCH;
            }
            if ((grant.Essence > SettlementSanityLimits.MAX_SESSION_ESSENCE)
                || (grant.RefineShards > SettlementSanityLimits.MAX_SESSION_SHARDS)
                || (grant.Tickets > SettlementSanityLimits.MAX_SESSION_TICKETS)
                || (grant.Currency > SettlementSanityLimits.MAX_SESSION_CURRENCY))
            {
                return SettlementSanityLimits.REJECT_REASON;
            }

            // 엽전은 보스 처치 성공의 확정 기본 보상에서만 발행한다. 일반전 여부는 저장소가 세션 종류로 본다
            if ((grant.Currency > 0) && (endReason != GameSessionCodes.EndReason.SUCCESS))
            {
                return SettlementSanityLimits.REJECT_REASON;
            }

            int reason = GetLinesRejectReason(grant.Items, MAX_GRANT_ITEMS, SettlementSanityLimits.MAX_ITEM_QUANTITY,
                SettlementSanityLimits.IsAllowedItem);
            if (reason != ContractCodes.RejectReason.NONE)
            {
                return reason;
            }

            return GetLinesRejectReason(grant.Talismans, SettlementSanityLimits.MAX_GRANT_TALISMANS,
                SettlementSanityLimits.MAX_TALISMAN_QUANTITY, SettlementSanityLimits.IsTalismanEffectId);
        }

        // 아이템 줄과 부적 줄의 공통 검사. 허용 ID, 계정 귀속, 양수 수량, 줄 수, 같은 ID 합계 상한을 본다
        private static int GetLinesRejectReason(List<SettlementItemInput> lines, int maxLines, long maxQuantity,
            Func<string?, bool> isAllowedId)
        {
            if (lines.Count > maxLines)
            {
                return SettlementSanityLimits.REJECT_REASON;
            }
            if (lines.Any(line => (line == null) || !isAllowedId(line.DefinitionId)
                || !SettlementSanityLimits.IsAllowedBinding(line.BindingKind) || (line.Quantity <= 0)))
            {
                return ContractCodes.RejectReason.SESSION_MISMATCH;
            }
            if (lines.Any(line => line.Quantity > maxQuantity))
            {
                return SettlementSanityLimits.REJECT_REASON;
            }

            // 같은 ID를 여러 줄로 나눠 상한을 피하지 못하게 합친 수량도 본다. 각 줄이 상한 이하라 합은 넘치지 않는다
            if (lines.GroupBy(line => (line.DefinitionId, line.BindingKind))
                .Any(group => group.Sum(line => line.Quantity) > maxQuantity))
            {
                return SettlementSanityLimits.REJECT_REASON;
            }

            return ContractCodes.RejectReason.NONE;
        }

        private static bool HasParticipant(SessionEventInput entry)
        {
            return entry.ParticipantId.HasValue && (entry.ParticipantId.Value != Guid.Empty);
        }
    }

    /// <summary> 진단용 체크포인트 저장 결과 </summary>
    public enum ECheckpointStatus
    {
        Saved           = 0,
        Duplicate       = 1,
        SessionMismatch = 2,
        WrongPhase      = 3,
        AheadOfEvents   = 4
    }

    /// <summary> 30초 주기 진단용 체크포인트. 경제 복구에는 쓰지 않는다 </summary>
    public sealed class CheckpointInput
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int MAX_STATE_BYTES = 262144;

        /****************************************
        *              Properties
        ****************************************/

        public long   CheckpointSeq { get; }
        public long   LastEventSeq  { get; }
        public int    SchemaVersion { get; }
        public byte[] State         { get; }

        /****************************************
        *                Methods
        ****************************************/

        public CheckpointInput(long checkpointSeq, long lastEventSeq, int schemaVersion, byte[] state)
        {
            CheckpointSeq = checkpointSeq;
            LastEventSeq  = lastEventSeq;
            SchemaVersion = schemaVersion;
            State         = state ?? Array.Empty<byte>();
        }

        public bool IsValid()
        {
            return (CheckpointSeq > 0) && (LastEventSeq >= 0) && (SchemaVersion > 0) && (State.Length <= MAX_STATE_BYTES);
        }
    }
}
