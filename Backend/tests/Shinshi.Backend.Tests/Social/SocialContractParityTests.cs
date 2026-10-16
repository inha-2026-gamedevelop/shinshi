// System
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

// Third Party
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;

namespace Shinshi.Backend.Tests.Social
{
    /// <summary> 소셜 거절 코드 번호가 ContractCodes와 ERejectReason에 같은 값으로 있는지,
    /// DB CHECK와 SocialLimits, GuildNameRules의 숫자 일치, 길드 이름 정규화 </summary>
    public sealed class SocialContractParityTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string MIGRATION_0019   = "0019_social";
        private const string REJECT_ENUM_PATH = "Assets/01.Scripts/00.Common/Types/ERejectReason.cs";
        private const string CHAT_CONSTANTS   = "Assets/01.Scripts/00.Common/Constants/Constants.chat.cs";
        private const string SOCIAL_NETWORK   = "Assets/01.Scripts/01.Network/Social/";
        private const string CHAT_CHANNEL     = "Assets/01.Scripts/01.Network/Chat/EChatChannel.cs";
        private const string SHARED_SOCIAL    = "Assets/01.Scripts/00.Common/Contracts/Social/SocialLimits.cs";

        private static readonly Regex G_Member = new Regex(@"^\s*([A-Za-z0-9_]+)\s*=\s*(\d+)\s*,?\s*(//.*)?$", RegexOptions.Compiled);

        private static readonly Dictionary<string, int> G_Expected = new Dictionary<string, int>
        {
            ["SOCIAL_TARGET_NOT_FOUND"] = 1600, ["SOCIAL_TARGET_SELF"] = 1601, ["SOCIAL_NOT_CONNECTED"] = 1602,
            ["FRIEND_ALREADY"] = 1610, ["FRIEND_LIMIT"] = 1611, ["FRIEND_TARGET_LIMIT"] = 1612,
            ["FRIEND_REQUEST_LIMIT"] = 1613, ["FRIEND_TARGET_REQUESTS_FULL"] = 1614, ["FRIEND_REQUEST_NOT_FOUND"] = 1615,
            ["WHISPER_TARGET_OFFLINE"] = 1620, ["GUILD_NAME_INVALID"] = 1630, ["GUILD_NAME_TAKEN"] = 1631,
            ["GUILD_ALREADY_MEMBER"] = 1632, ["GUILD_TARGET_IN_GUILD"] = 1633, ["GUILD_NOT_FOUND"] = 1634,
            ["GUILD_NOT_MEMBER"] = 1635, ["GUILD_NOT_LEADER"] = 1636, ["GUILD_FULL"] = 1637,
            ["GUILD_LEADER_MUST_DELEGATE"] = 1638, ["GUILD_APPLICATION_NOT_FOUND"] = 1639, ["GUILD_INVITE_NOT_FOUND"] = 1640,
            ["GUILD_APPLICATION_LIMIT"] = 1641, ["GUILD_PENDING_FULL"] = 1642, ["CHARACTER_IS_GUILD_LEADER"] = 1643,
            ["GUILD_TARGET_INVITES_FULL"] = 1644, ["GUILD_LEADER_CHANGED"] = 1645
        };

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void RejectCodes_ContractCodesAndUnityEnum_MatchDesignTable()
        {
            Dictionary<string, int> shared = Constants(typeof(ContractCodes.RejectReason));
            Dictionary<string, int> members = ReadMembers(REJECT_ENUM_PATH)
                .ToDictionary(pair => Normalize(pair.Key), pair => pair.Value);

            foreach (KeyValuePair<string, int> pair in G_Expected)
            {
                Assert.Equal(pair.Value, shared[pair.Key]);
                Assert.Equal(pair.Value, members[Normalize(pair.Key)]);
                // 같은 번호를 다른 이름이 쓰지 않는다
                Assert.Single(shared, entry => entry.Value == pair.Value);
            }
        }

        [Theory]
        [InlineData(typeof(SocialCodes.Presence), "EPresence.cs")]
        [InlineData(typeof(SocialCodes.GuildRole), "EGuildRole.cs")]
        [InlineData(typeof(SocialCodes.Notice), "ESocialNotice.cs")]
        public void SocialCodes_ContractAndUnityEnum_Match(Type holder, string enumFile)
        {
            Dictionary<string, int> shared = Constants(holder);
            Dictionary<string, int> members = ReadMembers(SOCIAL_NETWORK + enumFile)
                .ToDictionary(pair => Normalize(pair.Key), pair => pair.Value);

            Assert.Equal(members.Count, shared.Count);
            foreach (KeyValuePair<string, int> pair in shared)
            {
                Assert.Equal(pair.Value, members[Normalize(pair.Key)]);
            }
        }

        [Fact]
        public void ChatChannelCodes_ContractAndUnityEnum_Match()
        {
            Dictionary<string, int> members = ReadMembers(CHAT_CHANNEL);
            Assert.Equal(SocialCodes.ChatChannel.GUILD, members["Guild"]);
            Assert.Equal(SocialCodes.ChatChannel.WHISPER, members["Whisper"]);
        }

        [Fact]
        public void SharedContracts_Source_LivesInUnityContractsFolder()
        {
            // 백엔드가 같은 계약을 복제하지 않고 Unity 공용 계약 폴더의 소스를 링크한다
            Assert.True(File.Exists(FindRepositoryFile(SHARED_SOCIAL)));
            Assert.Equal("Shinshi.Contracts", typeof(SocialLimits).Assembly.GetName().Name);
        }

        [Fact]
        public void ChatLength_ContractAndUnityConstant_Match()
        {
            string source = File.ReadAllText(FindRepositoryFile(CHAT_CONSTANTS));
            Match match = Regex.Match(source, @"MAX_MESSAGE_LENGTH\s*=\s*(\d+)");
            Assert.True(match.Success);
            Assert.Equal(SocialLimits.MAX_CHAT_LENGTH, int.Parse(match.Groups[1].Value));
        }

        [Fact]
        public async Task MigrationChecks_Sql_MatchLimitsAndNameRules()
        {
            string sql = await MigrationTestResources.ReadUpAsync(MIGRATION_0019);

            Assert.Contains("member_count BETWEEN 0 AND " + SocialLimits.MAX_GUILD_MEMBERS, sql, StringComparison.Ordinal);
            Assert.Contains("{" + GuildNameRules.MIN_LENGTH + "," + GuildNameRules.MAX_LENGTH + "}", sql, StringComparison.Ordinal);
            Assert.Contains("char_length(payload) <= " + SocialLimits.MAX_CHAT_LENGTH, sql, StringComparison.Ordinal);
            Assert.True(ServiceFeatureKeys.IsKnown(ServiceFeatureKeys.SOCIAL));
            Assert.Contains(ServiceFeatureKeys.SOCIAL, ServiceFeatureKeys.All);
        }

        [Theory]
        [InlineData(" 청룡단 ", "청룡단", "청룡단")]
        [InlineData("ＡＢｃ12", "ABc12", "abc12")]
        [InlineData("Dragons8", "Dragons8", "dragons8")]
        public void GuildName_ValidInputs_Normalize(string input, string name, string key)
        {
            Assert.True(GuildNameRules.TryNormalize(input, out string normalized, out string normalizedKey));
            Assert.Equal((name, key), (normalized, normalizedKey));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("가")]
        [InlineData("아홉글자길드이름임")]
        [InlineData("청룡 단")]
        [InlineData("청룡!")]
        [InlineData("ㄱㄴㄷ")]
        public void GuildName_InvalidInputs_Rejected(string? input)
        {
            Assert.False(GuildNameRules.TryNormalize(input, out _, out _));
        }

        private static Dictionary<string, int> Constants(Type holder)
        {
            return holder.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => (field.IsLiteral) && (field.FieldType == typeof(int)))
                .ToDictionary(field => field.Name, field => (int)field.GetRawConstantValue()!);
        }

        private static string Normalize(string name)
        {
            return name.Replace("_", string.Empty).ToLowerInvariant();
        }

        private static Dictionary<string, int> ReadMembers(string relativePath)
        {
            Dictionary<string, int> members = new Dictionary<string, int>();
            foreach (string line in File.ReadAllLines(FindRepositoryFile(relativePath)))
            {
                Match match = G_Member.Match(line);
                if (match.Success)
                {
                    members[match.Groups[1].Value] = int.Parse(match.Groups[2].Value);
                }
            }

            return members;
        }

        private static string FindRepositoryFile(string relativePath)
        {
            string? directory = AppContext.BaseDirectory;
            while (directory != null)
            {
                string candidate = Path.Combine(directory, relativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
                directory = Path.GetDirectoryName(directory);
            }

            throw new FileNotFoundException("Repository file not found.", relativePath);
        }
    }
}
