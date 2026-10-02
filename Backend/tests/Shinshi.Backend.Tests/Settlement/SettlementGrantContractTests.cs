// System
using System;

// Third Party
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

// Project
using Shinshi.Contracts;

namespace Shinshi.Backend.Tests.Settlement
{
    /// <summary> 정산안 DTO의 지갑, 부적 칸 와이어 이름과 이전 형식 본문의 호환을 검사한다 </summary>
    public sealed class SettlementGrantContractTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string PARTICIPANT = "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbccdd";
        private const string EFFECT_ID   = "T01-1A";
        private const string BINDING     = "Account";
        private const long   ESSENCE     = 2100;
        private const long   SHARDS      = 120;
        private const long   TICKETS     = 2;
        private const long   CURRENCY    = 260;
        private const long   QUANTITY    = 3;

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void Serialize_WalletAndTalismanFields_UsesContractKeysAndRoundTrips()
        {
            SettlementGrant original = new SettlementGrant(PARTICIPANT, ESSENCE, Array.Empty<SettlementItemGrant>(),
                SHARDS, TICKETS, CURRENCY, new[] { new SettlementItemGrant(EFFECT_ID, BINDING, QUANTITY) });

            string text = JsonConvert.SerializeObject(original, ContractJson.CreateSettings());
            JObject json = JObject.Parse(text);
            SettlementGrant restored = JsonConvert.DeserializeObject<SettlementGrant>(text, ContractJson.CreateSettings())!;

            Assert.Equal(SHARDS, json["refineShards"]!.Value<long>());
            Assert.Equal(TICKETS, json["tickets"]!.Value<long>());
            Assert.Equal(CURRENCY, json["currency"]!.Value<long>());
            Assert.Equal(EFFECT_ID, json["talismans"]![0]!["definitionId"]!.Value<string>());
            Assert.Equal(SHARDS, restored.RefineShards);
            Assert.Equal(TICKETS, restored.Tickets);
            Assert.Equal(CURRENCY, restored.Currency);
            Assert.Equal(QUANTITY, Assert.Single(restored.Talismans).Quantity);
        }

        [Fact]
        public void Deserialize_PreviousShapeWithoutWalletFields_ReadsZeroAndNoTalismans()
        {
            string text = "{\"participantId\":\"" + PARTICIPANT + "\",\"essence\":" + ESSENCE + ",\"items\":[]}";

            SettlementGrant restored = JsonConvert.DeserializeObject<SettlementGrant>(text, ContractJson.CreateSettings())!;

            Assert.Equal(ESSENCE, restored.Essence);
            Assert.Equal(0L, restored.RefineShards);
            Assert.Equal(0L, restored.Tickets);
            Assert.Equal(0L, restored.Currency);
            Assert.Null(restored.Talismans);
        }

        [Fact]
        public void Constructor_PreviousOverload_FillsEmptyWalletAndTalismans()
        {
            SettlementGrant grant = new SettlementGrant(PARTICIPANT, ESSENCE, Array.Empty<SettlementItemGrant>());

            Assert.Equal(0L, grant.RefineShards + grant.Tickets + grant.Currency);
            Assert.Empty(grant.Talismans);
        }
    }
}
