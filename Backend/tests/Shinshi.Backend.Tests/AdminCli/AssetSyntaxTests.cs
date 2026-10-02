// System
using System;
using System.IO;

// Third Party
using Xunit;

// Project
using Shinshi.AdminCli;
using Shinshi.Contracts.Admin;

namespace Shinshi.Backend.Tests.AdminCli
{
    /// <summary> 보상 표기, 자산 표기, 지급 대상 목록 문법 </summary>
    public sealed class AssetSyntaxTests
    {
        /****************************************
        *                Methods
        ****************************************/

        [Theory]
        [InlineData("Currency=100", "Currency", "", "", 100)]
        [InlineData("tickets=3", "Tickets", "", "", 3)]
        [InlineData(" Essence = 7 ", "Essence", "", "", 7)]
        [InlineData("AccountItem:item_herb=2", "AccountItem", "item_herb", "Account", 2)]
        [InlineData("accountitem:T01-1A:Account=1", "AccountItem", "T01-1A", "Account", 1)]
        [InlineData("Shards=1000000", "Shards", "", "", 1000000)]
        public void ParseReward_ValidText_ReturnsContractReward(string text, string kind, string assetId, string binding,
            long quantity)
        {
            AdminGrantReward reward = AssetSyntax.ParseReward(text);

            Assert.Equal(kind, reward.AssetKind);
            Assert.Equal(assetId, reward.AssetId);
            Assert.Equal(binding, reward.BindingKind);
            Assert.Equal(quantity, reward.Quantity);
        }

        [Theory]
        [InlineData("Currency")]
        [InlineData("Currency=0")]
        [InlineData("Currency=-5")]
        [InlineData("Currency=1000001")]
        [InlineData("Currency=abc")]
        [InlineData("Gold=10")]
        [InlineData("Currency:x=10")]
        [InlineData("AccountItem=1")]
        [InlineData("AccountItem::Account=1")]
        [InlineData("AccountItem:a:b:c=1")]
        [InlineData("AccountItem:item_herb:=1")]
        [InlineData("")]
        public void ParseReward_InvalidText_ThrowsUsage(string text)
        {
            Assert.Throws<CliUsageException>(() => AssetSyntax.ParseReward(text));
        }

        [Fact]
        public void ParseAsset_AccountItemWithoutBinding_DefaultsToAccount()
        {
            AssetSpec asset = AssetSyntax.ParseAsset("AccountItem:item_herb");

            Assert.Equal(AdminGrantCodes.ASSET_ACCOUNT_ITEM, asset.Kind);
            Assert.Equal(AssetSyntax.DEFAULT_BINDING_KIND, asset.BindingKind);
            Assert.Equal("AccountItem:item_herb:Account", asset.ToString());
        }

        [Fact]
        public void Describe_ParsedReward_RoundTripsToSameText()
        {
            Assert.Equal("AccountItem:item_herb:Account=4",
                AssetSyntax.Describe(AssetSyntax.ParseReward("AccountItem:item_herb=4")));
            Assert.Equal("Currency=9", AssetSyntax.Describe(AssetSyntax.ParseReward("currency=9")));
        }

        [Fact]
        public void AccountListParse_FileWithCommentsAndSeparators_ReturnsIdsInOrder()
        {
            Guid first = Guid.NewGuid();
            Guid second = Guid.NewGuid();
            Guid third = Guid.NewGuid();
            string content = "# 보상 대상\n" + first + ",\r\n  " + second + "\t" + third + "\n\n";

            string[] ids = AccountListSyntax.Parse("@targets.txt", path =>
            {
                Assert.Equal("targets.txt", path);
                return content;
            });

            Assert.Equal(new[] { first.ToString("D"), second.ToString("D"), third.ToString("D") }, ids);
        }

        [Fact]
        public void AccountListParse_InvalidInput_ThrowsUsage()
        {
            Guid id = Guid.NewGuid();

            Assert.Throws<CliUsageException>(() => AccountListSyntax.Parse(id + "," + id, NoFile));
            Assert.Throws<CliUsageException>(() => AccountListSyntax.Parse("not-a-guid", NoFile));
            Assert.Throws<CliUsageException>(() => AccountListSyntax.Parse(Guid.Empty.ToString("D"), NoFile));
            Assert.Throws<CliUsageException>(() => AccountListSyntax.Parse("@missing.txt",
                _ => throw new FileNotFoundException("missing")));
            Assert.Throws<CliUsageException>(() => AccountListSyntax.Parse("@only-comments.txt", _ => "# none\n"));
        }

        [Fact]
        public void AccountListParse_OverLimit_ThrowsUsage()
        {
            string[] many = new string[AdminGrantCodes.MAX_TARGET_ACCOUNTS + 1];
            for (int i = 0; i < many.Length; ++i)
            {
                many[i] = Guid.NewGuid().ToString("D");
            }

            Assert.Throws<CliUsageException>(() => AccountListSyntax.Parse(string.Join(",", many), NoFile));
            Assert.Equal(AdminGrantCodes.MAX_TARGET_ACCOUNTS,
                AccountListSyntax.Parse(string.Join(",", many, 0, AdminGrantCodes.MAX_TARGET_ACCOUNTS), NoFile).Length);
        }

        private static string NoFile(string path)
        {
            throw new InvalidOperationException("No file expected: " + path);
        }
    }
}
