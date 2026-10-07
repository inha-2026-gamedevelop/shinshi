// System
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Newtonsoft.Json.Linq;

// Project
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;

namespace Shinshi.AdminCli
{
    /// <summary> 캐릭터 ID 또는 이름 정확 검색으로 성장 상세를 조회한다 </summary>
    internal sealed class CharactersCommand : ICliCommand
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SHOW                      = "show";
        private const string NAME                      = "name";
        private const string CHARACTER_ID              = "characterId";
        private const string TRAINING                  = "training";
        private const string ACCOUNTS                  = "accounts";
        private const string MATCHED_BY                = "matchedBy";
        private const string MATCHED_CHARACTER_ID      = "matchedCharacterId";
        private const string MATCHED_CHARACTER_DELETED = "matchedCharacterDeleted";
        private const int    SINGLE_MATCH_COUNT        = 1;

        private static readonly string[] G_Usage =
        {
            "characters show <characterId> --reason <사유>  캐릭터 성장 상세 (감사 기록)",
            "characters show --name <이름> [--include-deleted] --reason <사유>",
            "                                               이름 정확 검색. 한 캐릭터일 때만 상세 조회",
            "                                               --json이면 상세 응답 전체를 출력"
        };

        private static readonly (string Property, string Label)[] G_DetailFields =
        {
            (CHARACTER_ID, "캐릭터 ID"),
            ("accountId", "계정 ID"),
            (NAME, "이름"),
            ("classId", "직업"),
            ("classDisplayName", "직업 이름"),
            ("essence", "정기"),
            ("shards", "파편"),
            ("highestEnhanceLevel", "최고 강화 단계")
        };

        private static readonly (string Property, string Label)[] G_TrainingFields =
        {
            ("attack", "공격"),
            ("health", "체력"),
            ("defense", "방어"),
            ("crit", "치명타"),
            ("energy", "기력"),
            ("total", "합계"),
            ("itemCap", "항목 상한"),
            ("totalCap", "합계 상한")
        };

        /****************************************
        *              Properties
        ****************************************/

        public string Name => "characters";
        public IReadOnlyList<string> Usage => G_Usage;

        /****************************************
        *                Methods
        ****************************************/

        public async Task<int> RunAsync(CliContext context, CliArguments arguments,
            CancellationToken cancellationToken)
        {
            string sub = arguments.Positional(0, "하위 명령(show)");
            if (!string.Equals(sub, SHOW, StringComparison.Ordinal))
            {
                throw new CliUsageException("알 수 없는 하위 명령입니다: characters " + sub);
            }

            CliArguments rest = arguments.SkipPositionals(1);
            bool byName = rest.HasOption(NAME);
            bool includeDeleted = rest.HasFlag(CliArguments.FLAG_INCLUDE_DELETED);
            if (((byName) && (rest.Positionals.Count != 0))
                || ((!byName) && (rest.Positionals.Count > SINGLE_MATCH_COUNT))
                || ((!byName) && (includeDeleted)))
            {
                throw new CliUsageException("캐릭터 ID 하나 또는 --name을 지정하세요. --include-deleted는 이름 검색에만 씁니다.");
            }

            rest.EnsureOnly(byName ? 0 : SINGLE_MATCH_COUNT, new[] { NAME, AdminCliOptions.REASON },
                new[] { CliArguments.FLAG_INCLUDE_DELETED });
            string reason = CliContext.RequireReason(rest);
            string? name = null;
            Guid characterId = Guid.Empty;
            if (byName)
            {
                name = rest.RequireOption(NAME);
                if (!CharacterNameRules.TryNormalize(name, out _, out _))
                {
                    throw new CliUsageException("캐릭터 이름 형식이 올바르지 않습니다.");
                }
            }
            else
            {
                characterId = CliFormat.ParseGuid(rest.Positional(0, CHARACTER_ID), CHARACTER_ID);
            }

            using AdminApiClient client = context.CreateClient();
            if (name != null)
            {
                AdminApiResponse<JObject> search = await client.SendAsync<JObject>(HttpMethod.Post,
                    AdminApiRoutes.ACCOUNTS_SEARCH, new AdminAccountSearchRequest(null, name, includeDeleted), false,
                    cancellationToken);
                if (search.Value[ACCOUNTS] is not JArray accounts)
                {
                    throw new CliUnavailableException("API의 캐릭터 검색 응답을 읽지 못했습니다.", false);
                }

                if (accounts.Count == 0)
                {
                    context.Warn("일치하는 캐릭터가 없습니다.");
                    return CliExitCodes.REJECTED;
                }

                if (accounts.Count != SINGLE_MATCH_COUNT)
                {
                    context.Warn("이름 검색 결과가 여러 캐릭터입니다. 캐릭터 ID로 조회하세요.");
                    return CliExitCodes.REJECTED;
                }

                if ((accounts[0] is not JObject account)
                    || (account[MATCHED_BY]?.Type != JTokenType.String)
                    || (account[MATCHED_CHARACTER_ID]?.Type != JTokenType.String)
                    || (account[MATCHED_CHARACTER_DELETED]?.Type != JTokenType.Boolean)
                    || (!string.Equals(account[MATCHED_BY]?.Value<string>(), AdminAccountQuery.MATCH_CHARACTER_NAME,
                        StringComparison.Ordinal))
                    || (!Guid.TryParse(account[MATCHED_CHARACTER_ID]?.Value<string>(), out characterId))
                    || (characterId == Guid.Empty)
                    || ((!includeDeleted) && (account[MATCHED_CHARACTER_DELETED]?.Value<bool>() == true)))
                {
                    throw new CliUnavailableException("API의 캐릭터 검색 응답을 읽지 못했습니다.", false);
                }
            }

            string path = new CliQuery().Add(AdminAccountQuery.REASON_PARAMETER, reason)
                .Append(string.Format(AdminApiRoutes.CHARACTER_FORMAT, characterId.ToString("D")));
            AdminApiResponse<JObject> response = await client.GetAsync<JObject>(path, cancellationToken);
            if (context.Json)
            {
                context.WriteJson(response.RawBody);
                return CliExitCodes.SUCCESS;
            }

            WriteDetail(context, response.Value);
            return CliExitCodes.SUCCESS;
        }

        private static void WriteDetail(CliContext context, JObject detail)
        {
            context.Line("캐릭터 " + Text(detail[CHARACTER_ID]));
            context.WriteFields(Fields(detail, G_DetailFields));
            context.Line();
            context.Line("수련");
            context.WriteFields(Fields(detail[TRAINING] as JObject, G_TrainingFields));

            foreach (JProperty property in detail.Properties())
            {
                if (IsSummaryProperty(property.Name))
                {
                    continue;
                }

                context.Line();
                context.Line(ProfileLabel(property.Name));
                context.Line(CliFormat.Json(property.Value));
            }
        }

        private static List<(string Label, string Value)> Fields(JObject? body,
            IReadOnlyList<(string Property, string Label)> definitions)
        {
            List<(string Label, string Value)> fields = new List<(string Label, string Value)>();
            foreach ((string property, string label) in definitions)
            {
                fields.Add((label, Text(body?[property])));
            }

            return fields;
        }

        private static bool IsSummaryProperty(string name)
        {
            if (string.Equals(name, TRAINING, StringComparison.Ordinal))
            {
                return true;
            }

            foreach ((string property, string _) in G_DetailFields)
            {
                if (string.Equals(name, property, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ProfileLabel(string name)
        {
            return name switch
            {
                "appearanceJson"   => "외형",
                "createdAtUnixMs"  => "생성 시각 (UTC Unix ms)",
                "deletedAtUnixMs"  => "삭제 시각 (UTC Unix ms)",
                "revision"         => "캐릭터 revision",
                "loadoutRevision"  => "편성 revision",
                "combatStats"      => "전투 능력치",
                "equipment"        => "장비",
                "talismanProgress" => "부적 성장",
                "talismanLoadout"  => "부적 편성",
                "accountProgress"  => "계정 진행도",
                "life"             => "생활",
                "wallet"           => "계정 지갑",
                "items"            => "계정 아이템",
                "lease"            => "접속 임대",
                "online"           => "접속 중",
                "recentSessions"   => "최근 출정",
                "otherCharacters"  => "다른 캐릭터",
                _ => name
            };
        }
        private static string Text(JToken? token)
        {
            return token?.Type == JTokenType.String ? CliFormat.Text(token.Value<string>()) : CliFormat.Json(token);
        }
    }
}
