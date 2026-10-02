// System
using System;

// Third Party
using Xunit;

// Project
using Shinshi.AdminCli;

namespace Shinshi.Backend.Tests.AdminCli
{
    /// <summary> 명령줄 인자 파서와 허용 목록 검사 </summary>
    public sealed class CliArgumentParserTests
    {
        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void Parse_MixedTokens_SplitsPositionalsOptionsAndFlags()
        {
            CliArguments arguments = CliArgumentParser.Parse(new[]
            {
                "grants", "create", "--reward", "Currency=10", "--json", "--reward=AccountItem:item_herb=2",
                "--reason", "보상 지급", "--yes"
            });

            Assert.Equal(new[] { "grants", "create" }, arguments.Positionals);
            Assert.Equal(new[] { "Currency=10", "AccountItem:item_herb=2" }, arguments.Options("reward"));
            Assert.Equal("보상 지급", arguments.Option("reason"));
            Assert.True(arguments.HasFlag(CliArguments.FLAG_JSON));
            Assert.True(arguments.HasFlag(CliArguments.FLAG_YES));
            Assert.False(arguments.HasFlag(CliArguments.FLAG_ALL));
        }

        [Fact]
        public void Parse_NegativeNumberAfterOption_TakesItAsValue()
        {
            CliArguments arguments = CliArgumentParser.Parse(new[] { "adjust", "id", "--delta", "-25" });

            Assert.Equal("-25", arguments.Option("delta"));
            Assert.Equal(new[] { "adjust", "id" }, arguments.Positionals);
        }

        [Fact]
        public void Parse_EndOfOptions_TreatsRestAsPositionals()
        {
            CliArguments arguments = CliArgumentParser.Parse(new[] { "accounts", "search", "--", "--looks-like-option" });

            Assert.Equal(new[] { "accounts", "search", "--looks-like-option" }, arguments.Positionals);
        }

        [Fact]
        public void Parse_ShortHelp_SetsHelpFlag()
        {
            Assert.True(CliArgumentParser.Parse(new[] { "ledger", "-h" }).HasFlag(CliArguments.FLAG_HELP));
        }

        [Theory]
        [InlineData("--reason")]
        [InlineData("--reason", "--yes")]
        [InlineData("--json=true")]
        [InlineData("--Reason", "x")]
        [InlineData("--=x")]
        public void Parse_MalformedOption_ThrowsUsage(params string[] args)
        {
            Assert.Throws<CliUsageException>(() => CliArgumentParser.Parse(args));
        }

        [Fact]
        public void Option_RepeatedSingleValue_ThrowsUsage()
        {
            CliArguments arguments = CliArgumentParser.Parse(new[] { "--reason", "a", "--reason", "b" });

            Assert.Throws<CliUsageException>(() => arguments.Option("reason"));
        }

        [Fact]
        public void EnsureOnly_UnknownOptionOrExtraPositional_ThrowsUsage()
        {
            CliArguments typo = CliArgumentParser.Parse(new[] { "--reasn", "x" });
            CliArguments extra = CliArgumentParser.Parse(new[] { "a", "b" });
            CliArguments flag = CliArgumentParser.Parse(new[] { "--all" });

            Assert.Throws<CliUsageException>(() => typo.EnsureOnly(0, new[] { "reason" }, Array.Empty<string>()));
            Assert.Throws<CliUsageException>(() => extra.EnsureOnly(1, Array.Empty<string>(), Array.Empty<string>()));
            Assert.Throws<CliUsageException>(() => flag.EnsureOnly(0, Array.Empty<string>(), Array.Empty<string>()));
        }

        [Fact]
        public void EnsureOnly_GlobalOptions_AreAlwaysAllowed()
        {
            CliArguments arguments = CliArgumentParser.Parse(new[]
            {
                "x", "--api", "http://127.0.0.1:5080", "--operation-id", Guid.NewGuid().ToString("D"), "--json", "--yes"
            });

            arguments.SkipPositionals(1).EnsureOnly(0, Array.Empty<string>(), Array.Empty<string>());
        }

        [Fact]
        public void RequireOption_BlankValue_ThrowsUsage()
        {
            CliArguments arguments = CliArgumentParser.Parse(new[] { "--reason", "  " });

            Assert.Throws<CliUsageException>(() => arguments.RequireOption("reason"));
        }
    }
}
