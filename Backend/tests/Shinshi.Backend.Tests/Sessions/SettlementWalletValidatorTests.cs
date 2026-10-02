// System
using System;
using System.Collections.Generic;
using System.Linq;

// Third Party
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Tests.Sessions
{
    /// <summary> 정산안 지갑 칸, 부적 칸과 기본 정기 적립 이벤트의 형식, 상한, 경제 규칙 거절을 검사한다 </summary>
    public sealed class SettlementWalletValidatorTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string EFFECT_ID = "T01-1A";
        private const long   ONE       = 1;

        /****************************************
        *                Methods
        ****************************************/

        [Theory]
        [InlineData("T01-1A", true)]
        [InlineData("T15-3Z", true)]
        [InlineData("t01-1A", false)]
        [InlineData("T01-1a", false)]
        [InlineData("T1-1A", false)]
        [InlineData("T01_1A", false)]
        [InlineData("T0A-1A", false)]
        [InlineData("T01-1AB", false)]
        [InlineData("item_t01_1a", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsTalismanEffectId_MatchesCatalogPattern(string? effectId, bool expected)
        {
            Assert.Equal(expected, SettlementSanityLimits.IsTalismanEffectId(effectId));
        }

        [Fact]
        public void GetRejectReason_WalletAndTalismansAtLimits_ReturnsNone()
        {
            SettlementGrantInput grant = Grant(SettlementSanityLimits.MAX_SESSION_SHARDS,
                SettlementSanityLimits.MAX_SESSION_TICKETS, SettlementSanityLimits.MAX_SESSION_CURRENCY,
                Talisman(EFFECT_ID, SettlementSanityLimits.MAX_TALISMAN_QUANTITY));

            Assert.Equal(ContractCodes.RejectReason.NONE, SessionEventValidator.GetRejectReason(new[] { Ended(grant) }));
        }

        [Theory]
        [InlineData(SettlementSanityLimits.MAX_SESSION_SHARDS + ONE, 0, 0)]
        [InlineData(0, SettlementSanityLimits.MAX_SESSION_TICKETS + ONE, 0)]
        [InlineData(0, 0, SettlementSanityLimits.MAX_SESSION_CURRENCY + ONE)]
        public void GetRejectReason_WalletAboveLimit_ReturnsSettlementLimitExceeded(long shards, long tickets,
            long currency)
        {
            AssertReason(SettlementSanityLimits.REJECT_REASON, Ended(Grant(shards, tickets, currency)));
        }

        [Theory]
        [InlineData(-ONE, 0, 0)]
        [InlineData(0, -ONE, 0)]
        [InlineData(0, 0, -ONE)]
        public void GetRejectReason_NegativeWallet_ReturnsSessionMismatch(long shards, long tickets, long currency)
        {
            AssertReason(ContractCodes.RejectReason.SESSION_MISMATCH, Ended(Grant(shards, tickets, currency)));
        }

        [Fact]
        public void GetRejectReason_CurrencyOnNonSuccess_ReturnsSettlementLimitExceeded()
        {
            SessionEventInput entry = Ended(Grant(0, 0, ONE));
            entry.EndReason = GameSessionCodes.EndReason.ALL_DEAD;

            AssertReason(SettlementSanityLimits.REJECT_REASON, entry);
        }

        [Fact]
        public void GetRejectReason_ShardsAndTicketsOnFailure_ReturnsNone()
        {
            // 정련편과 티켓은 확보 보상이라 실패해도 보존한다
            SessionEventInput entry = Ended(Grant(ONE, ONE, 0, Talisman(EFFECT_ID, ONE)));
            entry.EndReason = GameSessionCodes.EndReason.TIMEOUT;

            AssertReason(ContractCodes.RejectReason.NONE, entry);
        }

        [Theory]
        [InlineData("item_boss_material_common", SessionTestEnvironment.BINDING)]
        [InlineData("t01-1a", SessionTestEnvironment.BINDING)]
        [InlineData(EFFECT_ID, "Character")]
        [InlineData(EFFECT_ID, "account")]
        [InlineData(EFFECT_ID, "")]
        public void GetRejectReason_TalismanInvalidIdOrBinding_ReturnsSessionMismatch(string effectId, string binding)
        {
            SettlementItemInput talisman = new SettlementItemInput
            {
                DefinitionId = effectId,
                BindingKind = binding,
                Quantity = ONE
            };

            AssertReason(ContractCodes.RejectReason.SESSION_MISMATCH, Ended(Grant(0, 0, 0, talisman)));
        }

        [Fact]
        public void GetRejectReason_TalismanNonPositiveQuantity_ReturnsSessionMismatch()
        {
            AssertReason(ContractCodes.RejectReason.SESSION_MISMATCH, Ended(Grant(0, 0, 0, Talisman(EFFECT_ID, 0))));
        }

        [Fact]
        public void GetRejectReason_TalismanSplitAcrossRowsAboveLimit_ReturnsSettlementLimitExceeded()
        {
            SessionEventInput entry = Ended(Grant(0, 0, 0,
                Talisman(EFFECT_ID, SettlementSanityLimits.MAX_TALISMAN_QUANTITY), Talisman(EFFECT_ID, ONE)));

            AssertReason(SettlementSanityLimits.REJECT_REASON, entry);
        }

        [Fact]
        public void GetRejectReason_TalismanRowsAboveLimit_ReturnsSettlementLimitExceeded()
        {
            SettlementItemInput[] talismans = Enumerable.Range(0, SettlementSanityLimits.MAX_GRANT_TALISMANS + (int)ONE)
                .Select(_ => Talisman(EFFECT_ID, ONE)).ToArray();

            AssertReason(SettlementSanityLimits.REJECT_REASON, Ended(Grant(0, 0, 0, talismans)));
        }

        [Fact]
        public void GetRejectReason_WalletIdsStillRejectedAsItems()
        {
            // 지급 금지 목록은 아이템 줄에만 적용한다. 지갑 칸으로는 같은 자산을 받는다
            SettlementGrantInput grant = Grant(ONE, ONE, ONE);
            grant.Items.Add(new SettlementItemInput
            {
                DefinitionId = SettlementSanityLimits.SHARD_DEFINITION_ID,
                BindingKind = SessionTestEnvironment.BINDING,
                Quantity = ONE
            });

            AssertReason(ContractCodes.RejectReason.SESSION_MISMATCH, Ended(grant));
        }

        [Fact]
        public void GetRejectReason_BaseEssenceAboveLimit_ReturnsSettlementLimitExceeded()
        {
            Guid participant = Guid.NewGuid();
            SessionEventInput[] batch =
            {
                BaseEssence(ONE, participant, SettlementSanityLimits.MAX_SESSION_ESSENCE),
                BaseEssence(ONE + ONE, participant, ONE)
            };

            AssertReason(SettlementSanityLimits.REJECT_REASON, batch);
        }

        [Fact]
        public void GetRejectReason_BaseAndExtraAtLimitEach_ReturnsNone()
        {
            // 기본과 추가 적립은 각자 상한을 센다
            Guid participant = Guid.NewGuid();
            SessionEventInput[] batch =
            {
                BaseEssence(ONE, participant, SettlementSanityLimits.MAX_SESSION_ESSENCE),
                SessionEventTests.Essence(ONE + ONE, participant, SettlementSanityLimits.MAX_SESSION_ESSENCE)
            };

            AssertReason(ContractCodes.RejectReason.NONE, batch);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-ONE)]
        public void GetRejectReason_NonPositiveBaseEssence_ReturnsSessionMismatch(long amount)
        {
            AssertReason(ContractCodes.RejectReason.SESSION_MISMATCH, BaseEssence(ONE, Guid.NewGuid(), amount));
        }

        internal static SessionEventInput BaseEssence(long seq, Guid participantId, long amount)
        {
            return new SessionEventInput
            {
                EventSeq = seq,
                EventId = Guid.NewGuid(),
                EventKind = GameSessionCodes.EventKind.BASE_ESSENCE_ACCRUED,
                ParticipantId = participantId,
                Amount = amount
            };
        }

        internal static SettlementItemInput Talisman(string effectId, long quantity)
        {
            return new SettlementItemInput
            {
                DefinitionId = effectId,
                BindingKind = SettlementSanityLimits.ACCOUNT_BINDING_KIND,
                Quantity = quantity
            };
        }

        private static SettlementGrantInput Grant(long shards, long tickets, long currency,
            params SettlementItemInput[] talismans)
        {
            SettlementGrantInput grant = SessionEventTests.Grant(Guid.NewGuid(), 0);
            grant.RefineShards = shards;
            grant.Tickets = tickets;
            grant.Currency = currency;
            grant.Talismans = talismans.ToList();
            return grant;
        }

        private static SessionEventInput Ended(SettlementGrantInput grant)
        {
            return SessionEventTests.Ended(ONE, Guid.NewGuid(), new[] { grant });
        }

        private static void AssertReason(int expected, params SessionEventInput[] events)
        {
            IReadOnlyList<SessionEventInput> batch = events;
            Assert.Equal(expected, SessionEventValidator.GetRejectReason(batch));
            Assert.Equal(expected == ContractCodes.RejectReason.NONE, SessionEventValidator.IsValid(batch));
        }
    }
}
