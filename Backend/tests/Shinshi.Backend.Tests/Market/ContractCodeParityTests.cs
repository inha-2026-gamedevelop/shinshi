// System
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

// Third Party
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Settlement;

namespace Shinshi.Backend.Tests.Market
{
    /// <summary> Unity enum(ERejectReason, EEconomyReason)과 ContractCodes, 원장 문자열의 이름과 값 일치. enum 소스를 읽어 비교한다 </summary>
    public sealed class ContractCodeParityTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string TYPES_DIRECTORY = "Assets/01.Scripts/00.Common/Types";

        private static readonly Regex G_Member = new Regex(@"^\s*([A-Za-z0-9_]+)\s*=\s*(\d+)\s*,?\s*(//.*)?$", RegexOptions.Compiled);

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void RejectReason_ContractCodes_MatchEnumValues()
        {
            Dictionary<string, int> enumMembers = ReadEnum("ERejectReason.cs");
            AssertConstantsMatch(typeof(ContractCodes.RejectReason), enumMembers);
        }

        [Fact]
        public void EconomyReason_ContractCodes_MatchEnumValues()
        {
            Dictionary<string, int> enumMembers = ReadEnum("EEconomyReason.cs");
            AssertConstantsMatch(typeof(ContractCodes.EconomyReason), enumMembers);
            Assert.Equal(enumMembers.Count, typeof(ContractCodes.EconomyReason).GetFields(BindingFlags.Public | BindingFlags.Static).Length);
        }

        [Fact]
        public void RejectReason_MarketAndTradeBlocks_AreAssignedExactly()
        {
            Dictionary<string, int> members = ReadEnum("ERejectReason.cs");
            string[] market = { "MarketListingLimit", "MarketNotTradable", "MarketPriceOutOfRange", "MarketSoldOut", "MarketQuantityChanged",
                "MarketPriceChanged", "MarketExpired", "MarketSelfPurchase", "MarketEscrowed", "MarketNotOwner" };
            string[] trade = { "TradeItemChanged", "TradeNotTradable", "TradeInventoryFull", "TradeInsufficientCurrency", "TradeSameAccount",
                "TradeNotInTown", "TradeBusy", "TradeTooFar", "TradeRequestExpired", "TradeExpired" };

            for (int index = 0; index < market.Length; ++index)
            {
                Assert.Equal(1400 + index, members[market[index]]);
            }
            Assert.Equal(1410, members["CharacterHasMarketListing"]);

            for (int index = 0; index < trade.Length; ++index)
            {
                Assert.Equal(1500 + index, members[trade[index]]);
            }

            Assert.Equal(members.Values.Count, members.Values.Distinct().Count());
        }

        [Fact]
        public void LedgerCodes_MapReason_ProducesContractValueForEveryNewReason()
        {
            Dictionary<string, int> enumMembers = ReadEnum("EEconomyReason.cs");
            MethodInfo? mapReason = typeof(LedgerWriter).GetMethod("MapReason", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(mapReason);

            string[] names = { "MarketEscrow", "MarketReturn", "MarketPurchase", "MarketProceeds", "MarketReceive", "TradeGive", "TradeReceive" };
            string[] constants =
            {
                EconomyLedgerCodes.REASON_MARKET_ESCROW, EconomyLedgerCodes.REASON_MARKET_RETURN, EconomyLedgerCodes.REASON_MARKET_PURCHASE,
                EconomyLedgerCodes.REASON_MARKET_PROCEEDS, EconomyLedgerCodes.REASON_MARKET_RECEIVE, EconomyLedgerCodes.REASON_TRADE_GIVE,
                EconomyLedgerCodes.REASON_TRADE_RECEIVE
            };

            for (int index = 0; index < names.Length; ++index)
            {
                Assert.Equal(names[index], constants[index]);
                Assert.Equal(enumMembers[names[index]], (int)mapReason!.Invoke(null, new object[] { constants[index] })!);
            }

            Assert.Equal(31, enumMembers["MarketEscrow"]);
            Assert.Equal(37, enumMembers["TradeReceive"]);
        }

        private static void AssertConstantsMatch(Type holder, Dictionary<string, int> enumMembers)
        {
            Dictionary<string, int> byNormalizedName = enumMembers.ToDictionary(pair => Normalize(pair.Key), pair => pair.Value);

            foreach (FieldInfo field in holder.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                string key = Normalize(field.Name);
                Assert.True(byNormalizedName.ContainsKey(key), holder.Name + "." + field.Name + " has no enum member");
                Assert.Equal(byNormalizedName[key], (int)field.GetRawConstantValue()!);
            }
        }

        private static string Normalize(string name)
        {
            return name.Replace("_", string.Empty).ToLowerInvariant();
        }

        private static Dictionary<string, int> ReadEnum(string fileName)
        {
            string? directory = AppContext.BaseDirectory;
            string? path = null;
            while (directory != null)
            {
                string candidate = Path.Combine(directory, TYPES_DIRECTORY, fileName);
                if (File.Exists(candidate))
                {
                    path = candidate;
                    break;
                }
                directory = Path.GetDirectoryName(directory);
            }

            Assert.NotNull(path);

            Dictionary<string, int> members = new Dictionary<string, int>();
            foreach (string line in File.ReadAllLines(path))
            {
                Match match = G_Member.Match(line);
                if (match.Success)
                {
                    members[match.Groups[1].Value] = int.Parse(match.Groups[2].Value);
                }
            }

            return members;
        }
    }
}
