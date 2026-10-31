// System
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

// Third Party
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

// Project
using Shinshi.Backend.Prologue;
using Shinshi.Backend.Tests.Auth;
using Shinshi.Contracts;
using Shinshi.Data.Auth;
using Shinshi.Data.Growth;
using Shinshi.Data.Prologue;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Tests.Prologue
{
    /// <summary> DB 없이 확인하는 프롤로그 계약: 출정 계약 변환, 교환 응답 필드, 경로, 메뉴 ID 형식, 보상 이벤트 거절, 콘텐츠 기본값 </summary>
    public sealed class PrologueContractTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int SAVED_POTIONS = 3;

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void FromRow_PrologueMode_MapsModeAndKeepsSavedStage()
        {
            JoinSortieContract sortie = JoinSortieContract.FromRow(GameSessionStates.MODE_PROLOGUE, PrologueCodes.MAP_ID,
                PrologueCodes.BATTLE_ID, PrologueCodes.BOSS_ID, (short)GameSessionCodes.Difficulty.EASY,
                PrologueCodes.Stage.WOLBAEK, SAVED_POTIONS, true);

            Assert.Equal(GameSessionCodes.Mode.PROLOGUE, sortie.Mode);
            Assert.Equal(PrologueCodes.Stage.WOLBAEK, sortie.PrologueStage);
            Assert.Equal(SAVED_POTIONS, sortie.PrologueWolbaekPotions);
            Assert.True(sortie.PrologueTalismanChecked);
            Assert.Equal(GameSessionCodes.Difficulty.EASY, sortie.Difficulty);
        }

        [Theory]
        [InlineData(GameSessionStates.MODE_NORMAL, GameSessionCodes.Mode.NORMAL)]
        [InlineData(GameSessionStates.MODE_BOSS, GameSessionCodes.Mode.BOSS)]
        public void FromRow_OtherModes_DropPrologueState(string mode, int expected)
        {
            JoinSortieContract sortie = JoinSortieContract.FromRow(mode, "region_forest", null, null, null,
                PrologueCodes.Stage.ENDING, SAVED_POTIONS, true);

            Assert.Equal(expected, sortie.Mode);
            Assert.Equal(0, sortie.PrologueStage);
            Assert.Equal(0, sortie.PrologueWolbaekPotions);
            Assert.False(sortie.PrologueTalismanChecked);
        }

        [Fact]
        public void ExchangeResponse_PrologueFields_RoundTripAndDefaultWhenMissing()
        {
            JoinTokenExchangeResponse response = new JoinTokenExchangeResponse(Guid.NewGuid().ToString("D"),
                Guid.NewGuid().ToString("D"), ContractCodes.ServerRole.DUNGEON, Guid.NewGuid().ToString("D"),
                Guid.NewGuid().ToString("D"), 1, null, 3, "name", GameSessionCodes.Mode.PROLOGUE, PrologueCodes.MAP_ID,
                PrologueCodes.BATTLE_ID, PrologueCodes.BOSS_ID, GameSessionCodes.Difficulty.EASY, null, null, null, null,
                null, null, PrologueCodes.Stage.FOREST, SAVED_POTIONS, true);
            JsonSerializerSettings settings = ContractJson.CreateSettings();
            JObject json = JObject.Parse(JsonConvert.SerializeObject(response, settings));

            Assert.Equal(PrologueCodes.Stage.FOREST, (int)json["prologueStage"]!);
            Assert.Equal(SAVED_POTIONS, (int)json["prologueWolbaekPotions"]!);
            Assert.True((bool)json["prologueTalismanChecked"]!);

            json.Remove("prologueStage");
            json.Remove("prologueWolbaekPotions");
            json.Remove("prologueTalismanChecked");
            JoinTokenExchangeResponse? legacy = JsonConvert.DeserializeObject<JoinTokenExchangeResponse>(json.ToString(),
                settings);
            Assert.NotNull(legacy);
            Assert.Equal(PrologueCodes.Stage.INTRO, legacy!.PrologueStage);
            Assert.Equal(0, legacy.PrologueWolbaekPotions);
            Assert.False(legacy.PrologueTalismanChecked);
            Assert.Equal(GameSessionCodes.Mode.PROLOGUE, legacy.Mode);
        }

        [Fact]
        public void RouteTemplates_MatchSharedRoutes()
        {
            string id = Guid.NewGuid().ToString("D");
            Assert.Equal(string.Format(CultureInfo.InvariantCulture, PrologueRoutes.STATE_FORMAT, id),
                PrologueRouteTemplates.STATE.Replace("{characterId}", id));
            Assert.Equal(string.Format(CultureInfo.InvariantCulture, PrologueRoutes.STAGE_FORMAT, id),
                PrologueRouteTemplates.STAGE.Replace("{sessionId}", id));
            Assert.Equal(string.Format(CultureInfo.InvariantCulture, PrologueRoutes.COMPLETE_FORMAT, id),
                PrologueRouteTemplates.COMPLETE.Replace("{sessionId}", id));
            Assert.Equal(string.Format(CultureInfo.InvariantCulture, PrologueRoutes.MENU_GUIDE_COMPLETE_FORMAT, "skill_tree"),
                PrologueRouteTemplates.MENU_GUIDE_COMPLETE.Replace("{menuId}", "skill_tree"));
            Assert.Equal(PrologueRoutes.ENTRY, PrologueRouteTemplates.ENTRY);
            Assert.Equal(PrologueRoutes.SKIP, PrologueRouteTemplates.SKIP);
            Assert.Equal(PrologueRoutes.MENU_GUIDES, PrologueRouteTemplates.MENU_GUIDES);
        }

        [Theory]
        [InlineData("skill_tree", true)]
        [InlineData("a", true)]
        [InlineData("menu_09", true)]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData("Skill", false)]
        [InlineData("skill-tree", false)]
        [InlineData("메뉴", false)]
        [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false)]
        public void IsValidMenuId_MatchesMigrationCheck(string? menuId, bool expected)
        {
            Assert.Equal(expected, MenuGuideRules.IsValidMenuId(menuId));
        }

        [Fact]
        public void IsValidMenuId_AcceptsMaximumLength()
        {
            Assert.True(MenuGuideRules.IsValidMenuId(new string('a', PrologueCodes.MAX_MENU_ID_LENGTH)));
        }

        [Theory]
        [InlineData(PrologueCodes.Stage.INTRO, 0, true)]
        [InlineData(PrologueCodes.Stage.ENDING, PrologueCodes.MAX_POTIONS, true)]
        [InlineData(PrologueCodes.Stage.DONE, 0, false)]
        [InlineData(-1, 0, false)]
        [InlineData(PrologueCodes.Stage.WOLBAEK, PrologueCodes.MAX_POTIONS + 1, false)]
        [InlineData(PrologueCodes.Stage.WOLBAEK, -1, false)]
        public void StageInput_IsInRange(int stage, int potions, bool expected)
        {
            PrologueStageInput input = new PrologueStageInput(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), stage, potions, false);

            Assert.Equal(expected, input.IsInRange());
        }

        [Fact]
        public void HasPrologueReward_ZeroGrantsPass_RewardsReject()
        {
            Guid participant = Guid.NewGuid();
            List<SessionEventInput> zero = new List<SessionEventInput>
            {
                new SessionEventInput
                {
                    EventSeq = 1, EventId = Guid.NewGuid(), EventKind = GameSessionCodes.EventKind.SESSION_ENDED,
                    EndReason = GameSessionCodes.EndReason.SUCCESS, SettlementOperationId = Guid.NewGuid(),
                    Grants = new List<SettlementGrantInput> { new SettlementGrantInput { ParticipantId = participant } }
                }
            };
            List<SessionEventInput> essence = new List<SessionEventInput>
            {
                new SessionEventInput
                {
                    EventSeq = 1, EventId = Guid.NewGuid(), EventKind = GameSessionCodes.EventKind.ESSENCE_ACCRUED,
                    ParticipantId = participant, Amount = 1
                }
            };
            List<SessionEventInput> paid = new List<SessionEventInput>
            {
                new SessionEventInput
                {
                    EventSeq = 1, EventId = Guid.NewGuid(), EventKind = GameSessionCodes.EventKind.SESSION_ENDED,
                    EndReason = GameSessionCodes.EndReason.SUCCESS, SettlementOperationId = Guid.NewGuid(),
                    Grants = new List<SettlementGrantInput>
                    {
                        new SettlementGrantInput { ParticipantId = participant, RefineShards = 1 }
                    }
                }
            };

            Assert.False(PostgresGameSessionRepository.HasPrologueReward(zero));
            Assert.True(PostgresGameSessionRepository.HasPrologueReward(essence));
            Assert.True(PostgresGameSessionRepository.HasPrologueReward(paid));
        }

        [Fact]
        public void GrowthContent_PrologueGrant_DefaultsToEmptyBundle()
        {
            GrowthContent.GrantBundle bundle = GrowthContent.LoadEmbedded().Progression.PrologueCompletionGrant;

            Assert.Equal(GrowthContent.PROLOGUE_COMPLETION_GRANT_ID, bundle.GrantId);
            Assert.Empty(bundle.Items);
            Assert.Empty(bundle.Equipment);
            Assert.Equal(0, bundle.ActionEnergy);
        }

        [Fact]
        public void GrowthContent_PrologueGrantWithActionEnergy_IsRejected()
        {
            JObject content = JObject.Parse(ReadEmbeddedJson());
            content["progression"]!["prologueCompletionGrant"] = JObject.FromObject(new
            {
                grantId = "grant_prologue_completion",
                items = new[] { new { itemId = "item_potion_small", quantity = 1 } },
                equipment = Array.Empty<object>(),
                actionEnergy = 1
            });

            Assert.Throws<InvalidDataException>(() => GrowthContent.Parse(content.ToString()));
        }

        [Fact]
        public void GrowthContent_PrologueGrantWithItems_Parses()
        {
            JObject content = JObject.Parse(ReadEmbeddedJson());
            content["progression"]!["prologueCompletionGrant"] = JObject.FromObject(new
            {
                grantId = "grant_prologue_completion",
                items = new[] { new { itemId = "item_potion_small", quantity = 2 } },
                equipment = Array.Empty<object>(),
                actionEnergy = 0
            });

            GrowthContent parsed = GrowthContent.Parse(content.ToString());
            Assert.Single(parsed.Progression.PrologueCompletionGrant.Items);
        }

        [Fact]
        public void DerivedToken_IsDeterministicAuthenticAndPurposeBound()
        {
            AuthTokenHasher hasher = new AuthTokenHasher(AuthTokenHasherTests.CreateOptions());
            string first = hasher.CreateDerivedToken(AuthTokenHasher.PROLOGUE_JOIN_DOMAIN, "account\noperation");
            string again = hasher.CreateDerivedToken(AuthTokenHasher.PROLOGUE_JOIN_DOMAIN, "account\noperation");
            string other = hasher.CreateDerivedToken(AuthTokenHasher.PROLOGUE_JOIN_DOMAIN, "other\noperation");

            Assert.Equal(first, again);
            Assert.NotEqual(first, other);
            Assert.True(hasher.IsAuthentic(first));
            Assert.Throws<ArgumentException>(() => hasher.CreateDerivedToken(AuthTokenHasher.JOIN_DOMAIN, "seed"));
        }

        private static string ReadEmbeddedJson()
        {
            using Stream stream = typeof(GrowthContent).Assembly.GetManifestResourceStream(GrowthContent.RESOURCE_NAME)!;
            using StreamReader reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
