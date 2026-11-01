// System
using System;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.UI;

namespace Shinshi.Tests
{
    public class MailBoxClaimFailureTextTests
    {
        // 우편 수령 거절 안내: 보관함 가득은 한도에 닿은 품목을 알리고, 획득 캐릭터 삭제는 보관함 가득과 다르게 보인다

        /****************************************
        *                Fields
        ****************************************/

        private const long MS_PER_DAY = 86400000L;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void InventoryFull_OnSessionShards_NamesShardsAndAcquiringCharacter()
        {
            InboxEntry entry = Entry("SessionReward", false, new InboxReward("Shards", string.Empty, string.Empty, 120));

            string text = MailBoxViewRules.ClaimFailureText(ERejectReason.InventoryFull, entry);

            StringAssert.Contains("정련편 보관 한도", text);
            StringAssert.Contains("보상을 얻은 캐릭터 기준", text);
            Assert.AreNotEqual(RejectText.Describe(ERejectReason.InventoryFull), text);
        }

        [Test]
        public void InventoryFull_ListsOnlyLimitedRewardsOnce()
        {
            InboxEntry entry = Entry("SessionReward", false,
                new InboxReward("Tickets", string.Empty, string.Empty, 2),
                new InboxReward("Currency", string.Empty, string.Empty, 500),
                new InboxReward("Tickets", string.Empty, string.Empty, 1));

            string text = MailBoxViewRules.ClaimFailureText(ERejectReason.InventoryFull, entry);

            StringAssert.StartsWith("보스 입장권 보관 한도", text);
            StringAssert.DoesNotContain("엽전", text);
            StringAssert.DoesNotContain("캐릭터 기준", text);
        }

        [Test]
        public void InventoryFull_WithoutLimitedReward_FallsBackToCommonText()
        {
            InboxEntry entry = Entry("AdminGrant", false, new InboxReward("Currency", string.Empty, string.Empty, 100000));

            Assert.AreEqual(RejectText.Describe(ERejectReason.InventoryFull),
                MailBoxViewRules.ClaimFailureText(ERejectReason.InventoryFull, entry));
            Assert.AreEqual(RejectText.Describe(ERejectReason.InventoryFull),
                MailBoxViewRules.ClaimFailureText(ERejectReason.InventoryFull, null));
        }

        [Test]
        public void CharacterNotFound_IsNotShownAsInventoryFull()
        {
            InboxEntry entry = Entry("SessionReward", false, new InboxReward("Shards", string.Empty, string.Empty, 20));

            string text = MailBoxViewRules.ClaimFailureText(ERejectReason.CharacterNotFound, entry);

            StringAssert.Contains("삭제", text);
            StringAssert.DoesNotContain("가득", text);
        }

        [Test]
        public void OtherReasons_UseCommonText()
        {
            InboxEntry entry = Entry("SessionReward", false, new InboxReward("Shards", string.Empty, string.Empty, 20));

            Assert.AreEqual(RejectText.Describe(ERejectReason.InboxExpired),
                MailBoxViewRules.ClaimFailureText(ERejectReason.InboxExpired, entry));
        }

        [Test]
        public void Body_RecoveryShards_SaysAcquiringCharacterReceives()
        {
            InboxEntry recovery = Entry("SessionReward", false, new InboxReward("Shards", string.Empty, string.Empty, 120));
            InboxEntry admin    = Entry("AdminGrant", true, new InboxReward("Shards", string.Empty, string.Empty, 120));

            StringAssert.Contains("보상을 얻은 캐릭터가 받습니다", MailBoxViewRules.Body(recovery));
            StringAssert.Contains("현재 캐릭터로 받습니다", MailBoxViewRules.Body(admin));
            StringAssert.DoesNotContain("보상을 얻은 캐릭터", MailBoxViewRules.Body(admin));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static InboxEntry Entry(string sourceKind, bool requiresCharacter, params InboxReward[] rewards)
        {
            return new InboxEntry(Guid.NewGuid().ToString("D"), sourceKind, 0, 30L * MS_PER_DAY, rewards, requiresCharacter);
        }
    }
}
