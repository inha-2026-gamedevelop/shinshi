// System
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Newtonsoft.Json.Linq;

// Project
using Shinshi.Admin.Web.Infrastructure;
using Shinshi.Contracts.Admin;

namespace Shinshi.Admin.Web.Models
{
    /// <summary> 확정된 계정 한 건. 이름 검색이면 일치한 캐릭터 ID도 담는다 </summary>
    public sealed record ResolvedAccount(string AccountId, string? CharacterId, string? CharacterName, string MatchedBy,
        bool CharacterDeleted, bool Online, int CharacterCount, int? ClassId = null);

    /// <summary> 조회 결과. 일치 없음과 API 오류를 구분한다 </summary>
    public sealed record AccountLookupResult(IReadOnlyList<ResolvedAccount> Matches, string Error)
    {
        public bool IsSuccess => string.IsNullOrEmpty(Error);
    }

    /// <summary> UUID면 그대로 계정 ID로 쓰고, 아니면 어드민 API 이름 검색으로 푼다 </summary>
    public sealed class AccountLookup
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly AdminWebApiClient _api;

        /****************************************
        *                Methods
        ****************************************/

        public AccountLookup(AdminWebApiClient api)
        {
            ArgumentNullException.ThrowIfNull(api);
            _api = api;
        }

        /// <summary> 캐릭터 이름 또는 계정 ID를 계정으로 푼다. UUID는 API를 부르지 않는다 </summary>
        public async Task<AccountLookupResult> ResolveAsync(string input, bool includeDeleted = false,
            CancellationToken cancellationToken = default)
        {
            TargetToken token = TargetInput.Classify(input ?? string.Empty);
            if (token.Text.Length == 0)
            {
                return new AccountLookupResult(Array.Empty<ResolvedAccount>(), "캐릭터 이름 또는 계정 ID를 입력하세요.");
            }
            if (token.Kind == ETargetKind.Uuid)
            {
                return new AccountLookupResult(new[]
                {
                    new ResolvedAccount(token.Text, null, null, AdminAccountQuery.MATCH_ACCOUNT_ID, false, false, 0)
                }, string.Empty);
            }
            return await SearchAsync(new AdminAccountSearchRequest(null!, token.Text, includeDeleted), token.Text,
                cancellationToken);
        }

        /// <summary> 초대 코드 원문으로 계정을 찾는다. 코드는 POST 본문으로만 보낸다 </summary>
        public Task<AccountLookupResult> ResolveInviteAsync(string code, CancellationToken cancellationToken = default)
        {
            string trimmed = code?.Trim() ?? string.Empty;
            return trimmed.Length == 0
                ? Task.FromResult(new AccountLookupResult(Array.Empty<ResolvedAccount>(), "초대 코드를 입력하세요."))
                : SearchAsync(new AdminAccountSearchRequest(trimmed), null, cancellationToken);
        }

        /// <summary> 계정 ID나 캐릭터 ID(UUID)로 검색해 실제 계정을 확인한다 </summary>
        public Task<AccountLookupResult> SearchIdAsync(string uuid, CancellationToken cancellationToken = default)
        {
            return SearchAsync(new AdminAccountSearchRequest(uuid), null, cancellationToken);
        }

        private async Task<AccountLookupResult> SearchAsync(AdminAccountSearchRequest request, string? typedName,
            CancellationToken cancellationToken)
        {
            AdminWebApiResult<JToken> result = await _api.SendAsync<JToken>(HttpMethod.Post,
                AdminApiRoutes.ACCOUNTS_SEARCH, request, false, cancellationToken);
            if (!result.IsSuccess)
            {
                return new AccountLookupResult(Array.Empty<ResolvedAccount>(), result.ErrorMessage);
            }
            return new AccountLookupResult(ParseMatches(result.Value, typedName), string.Empty);
        }

        /// <summary> 검색 응답의 accounts 배열을 확정 계정 목록으로 바꾼다 </summary>
        public static IReadOnlyList<ResolvedAccount> ParseMatches(JToken? response, string? typedName)
        {
            List<ResolvedAccount> list = new();
            if (response?["accounts"] is not JArray accounts)
            {
                return list;
            }
            foreach (JToken item in accounts)
            {
                string? accountId = item["accountId"]?.ToString();
                if (!TargetInput.IsUuid(accountId, out string account))
                {
                    continue;
                }
                string matchedBy = item["matchedBy"]?.ToString() ?? string.Empty;
                bool byCharacter = (matchedBy == AdminAccountQuery.MATCH_CHARACTER_NAME)
                    || (matchedBy == AdminAccountQuery.MATCH_CHARACTER_ID);
                string? matchedCharacter = item["matchedCharacterId"]?.ToString();
                if ((string.IsNullOrEmpty(matchedCharacter)) && (byCharacter))
                {
                    matchedCharacter = item["matchedId"]?.ToString();
                }
                string? characterName = item["matchedCharacterName"]?.ToString();
                if (string.IsNullOrEmpty(characterName) && (matchedBy == AdminAccountQuery.MATCH_CHARACTER_NAME))
                {
                    characterName = typedName;
                }
                list.Add(new ResolvedAccount(account,
                    TargetInput.IsUuid(matchedCharacter, out string character) ? character : null,
                    string.IsNullOrEmpty(characterName) ? null : characterName, matchedBy,
                    item["matchedCharacterDeleted"]?.Type == JTokenType.Boolean && item["matchedCharacterDeleted"]!.Value<bool>(),
                    item["online"]?.Type == JTokenType.Boolean && item["online"]!.Value<bool>(),
                    item["characterCount"]?.Type == JTokenType.Integer ? item["characterCount"]!.Value<int>() : 0,
                    item["matchedCharacterClassId"]?.Type == JTokenType.Integer ? item["matchedCharacterClassId"]!.Value<int>() : null));
            }
            return list;
        }

        public static string ShortId(string id)
        {
            return id.Length > 8 ? id.Substring(0, 8) : id;
        }

        public static string Describe(ResolvedAccount account)
        {
            string head = string.IsNullOrEmpty(account.CharacterName) ? "계정" : account.CharacterName!;
            return head + " · " + ShortId(account.AccountId);
        }
    }
}
