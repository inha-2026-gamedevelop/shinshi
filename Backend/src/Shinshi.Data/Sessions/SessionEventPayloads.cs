// System
using System;
using System.Collections.Generic;
using System.Linq;

// Third Party
using Newtonsoft.Json;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Sessions
{
    /// <summary> session_events.payload의 JSON 형식. 정산은 SESSION_ENDED에 저장된 이 내용만 적용한다 </summary>
    internal static class SessionEventPayloads
    {
        /****************************************
        *                Methods
        ****************************************/

        public static string ToJson(SessionEventInput entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            object payload = entry.EventKind switch
            {
                GameSessionCodes.EventKind.POTION_USED => new
                {
                    participantId = entry.ParticipantId,
                    definitionId = entry.DefinitionId,
                    bindingKind = entry.BindingKind
                },
                // revertsEventId는 0013 되돌림 유일 인덱스가 문자열로 비교하므로 소문자 "D" 형식으로 고정한다
                GameSessionCodes.EventKind.POTION_USE_REVERTED => new
                {
                    participantId = entry.ParticipantId,
                    revertsEventId = entry.RevertsEventId!.Value.ToString("D")
                },
                GameSessionCodes.EventKind.ESSENCE_ACCRUED or GameSessionCodes.EventKind.BASE_ESSENCE_ACCRUED => new
                {
                    participantId = entry.ParticipantId,
                    amount = entry.Amount
                },
                GameSessionCodes.EventKind.PARTICIPANT_DEPARTED => new
                {
                    participantId = entry.ParticipantId,
                    departReason = entry.EndReason
                },
                GameSessionCodes.EventKind.SESSION_ENDED => new SessionEndedPayload
                {
                    EndReason = entry.EndReason,
                    SettlementOperationId = entry.SettlementOperationId!.Value,
                    Grants = entry.Grants.Select(grant => new GrantPayload
                    {
                        ParticipantId = grant.ParticipantId,
                        Essence = grant.Essence,
                        Items = ToItems(grant.Items),
                        RefineShards = grant.RefineShards,
                        Tickets = grant.Tickets,
                        Currency = grant.Currency,
                        Talismans = ToItems(grant.Talismans)
                    }).ToList()
                },
                _ => new { payloadJson = entry.PayloadJson ?? string.Empty }
            };
            return JsonConvert.SerializeObject(payload, Formatting.None);
        }

        private static List<ItemPayload> ToItems(IEnumerable<SettlementItemInput> items)
        {
            return items.Select(item => new ItemPayload
            {
                DefinitionId = item.DefinitionId,
                BindingKind = item.BindingKind,
                Quantity = item.Quantity
            }).ToList();
        }

        public static SessionEndedPayload ReadEnded(string json)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(json);
            return JsonConvert.DeserializeObject<SessionEndedPayload>(json)
                ?? throw new InvalidOperationException("Stored SessionEnded payload is invalid.");
        }

        internal sealed class SessionEndedPayload
        {
            /****************************************
            *              Properties
            ****************************************/

            [JsonProperty("endReason")]             public int                EndReason             { get; set; }
            [JsonProperty("settlementOperationId")] public Guid               SettlementOperationId { get; set; }
            [JsonProperty("grants")]                public List<GrantPayload> Grants                { get; set; } = new List<GrantPayload>();
        }

        internal sealed class GrantPayload
        {
            /****************************************
            *              Properties
            ****************************************/

            [JsonProperty("participantId")] public Guid               ParticipantId { get; set; }
            [JsonProperty("essence")]       public long               Essence       { get; set; }
            [JsonProperty("items")]         public List<ItemPayload>  Items         { get; set; } = new List<ItemPayload>();

            // 0007 이전에 저장된 정산안에는 아래 칸이 없어 0과 빈 목록으로 읽힌다. 기존 정산 결과가 바뀌지 않는다
            [JsonProperty("refineShards")]  public long               RefineShards  { get; set; }
            [JsonProperty("tickets")]       public long               Tickets       { get; set; }
            [JsonProperty("currency")]      public long               Currency      { get; set; }
            [JsonProperty("talismans")]     public List<ItemPayload>  Talismans     { get; set; } = new List<ItemPayload>();
        }

        internal sealed class ItemPayload
        {
            /****************************************
            *              Properties
            ****************************************/

            [JsonProperty("definitionId")] public string DefinitionId { get; set; } = string.Empty;
            [JsonProperty("bindingKind")]  public string BindingKind  { get; set; } = string.Empty;
            [JsonProperty("quantity")]     public long   Quantity     { get; set; }
        }
    }
}
