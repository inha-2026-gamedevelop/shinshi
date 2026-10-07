// System
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;

namespace Shinshi.AdminCli
{
    /// <summary> 계정 검색, 상세(감사 기록), 강제 접속 종료 </summary>
    internal sealed class AccountsCommand : ICliCommand
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SEARCH         = "search";
        private const string SHOW           = "show";
        private const string KICK           = "kick";
        private const string CHARACTER_NAME = "character-name";

        private static readonly string[] G_Usage =
        {
            "accounts search <계정ID|캐릭터ID|초대 코드>    계정 검색 (초대 코드는 요청 본문으로 보냄)",
            "accounts search --character-name <이름> [--include-deleted]",
            "                                               이름 정확 검색. 기본은 활성 캐릭터만, 이름은 요청 본문으로 보냄",
            "accounts show <accountId> --reason <사유>      계정 상세. 민감 조회라 사유가 감사 로그에 남음",
            "accounts kick <accountId> [--keep-invites] --reason <사유>",
            "                                               기존 토큰 폐기(현재 접속 유지, 비밀번호 로그인은 막지 않음).",
            "                                               기본으로 미사용 초대도 폐기"
        };

        /****************************************
        *              Properties
        ****************************************/

        public string Name => "accounts";
        public IReadOnlyList<string> Usage => G_Usage;

        /****************************************
        *                Methods
        ****************************************/

        public Task<int> RunAsync(CliContext context, CliArguments arguments, CancellationToken cancellationToken)
        {
            string sub = arguments.Positional(0, "하위 명령(search, show, kick)");
            CliArguments rest = arguments.SkipPositionals(1);
            return sub switch
            {
                SEARCH => SearchAsync(context, rest, cancellationToken),
                SHOW   => ShowAsync(context, rest, cancellationToken),
                KICK   => KickAsync(context, rest, cancellationToken),
                _ => throw new CliUsageException("알 수 없는 하위 명령입니다: accounts " + sub)
            };
        }

        private static async Task<int> SearchAsync(CliContext context, CliArguments arguments,
            CancellationToken cancellationToken)
        {
            bool byName = arguments.HasOption(CHARACTER_NAME);
            bool includeDeleted = arguments.HasFlag(CliArguments.FLAG_INCLUDE_DELETED);
            // EnsureOnly의 남는 위치 인자 오류는 원문을 출력하므로 이름 검색의 충돌은 먼저 값 없이 거절한다
            if (((byName) && (arguments.Positionals.Count != 0))
                || ((!byName) && (arguments.Positionals.Count > 1))
                || ((!byName) && (includeDeleted)))
            {
                throw new CliUsageException("검색어 하나 또는 --character-name을 지정하세요. --include-deleted는 이름 검색에만 씁니다.");
            }

            arguments.EnsureOnly(byName ? 0 : 1, new[] { CHARACTER_NAME },
                new[] { CliArguments.FLAG_INCLUDE_DELETED });
            AdminAccountSearchRequest search;
            if (byName)
            {
                string name = arguments.RequireOption(CHARACTER_NAME);
                if (!CharacterNameRules.TryNormalize(name, out _, out _))
                {
                    throw new CliUsageException("캐릭터 이름 형식이 올바르지 않습니다.");
                }

                search = new AdminAccountSearchRequest(null, name, includeDeleted);
            }
            else
            {
                search = new AdminAccountSearchRequest(arguments.Positional(0, "검색어").Trim());
            }

            using AdminApiClient client = context.CreateClient();
            AdminApiResponse<AdminAccountSearchResponse> response = await client.SendAsync<AdminAccountSearchResponse>(
                HttpMethod.Post, AdminApiRoutes.ACCOUNTS_SEARCH, search, false, cancellationToken);
            if (context.Json)
            {
                context.WriteJson(response.RawBody);
                return CliExitCodes.SUCCESS;
            }

            if (byName)
            {
                context.Line("삭제된 캐릭터 포함: " + CliFormat.YesNo(response.Value.IncludeDeletedCharacters));
            }

            CliTable table = new CliTable("계정 ID", "찾은 근거", "근거 ID", "매칭 캐릭터 삭제", "생성", "활성 캐릭터 수", "접속 중");
            foreach (AdminAccountSummary account in response.Value.Accounts ?? Array.Empty<AdminAccountSummary>())
            {
                table.Add(account.AccountId, account.MatchedBy, CliFormat.Text(account.MatchedId),
                    account.MatchedCharacterDeleted.HasValue ? CliFormat.YesNo(account.MatchedCharacterDeleted.Value) : "-",
                    CliFormat.Time(account.CreatedAtUnixMs), CliFormat.Number(account.CharacterCount),
                    CliFormat.YesNo(account.Online));
            }

            context.WriteTable(table);
            return CliExitCodes.SUCCESS;
        }

        private static async Task<int> ShowAsync(CliContext context, CliArguments arguments,
            CancellationToken cancellationToken)
        {
            arguments.EnsureOnly(1, new[] { AdminCliOptions.REASON }, Array.Empty<string>());
            Guid accountId = CliFormat.ParseGuid(arguments.Positional(0, "accountId"), "accountId");
            string reason = CliContext.RequireReason(arguments);
            using AdminApiClient client = context.CreateClient();
            string path = new CliQuery().Add(AdminAccountQuery.REASON_PARAMETER, reason)
                .Append(string.Format(AdminApiRoutes.ACCOUNT_FORMAT, accountId.ToString("D")));
            AdminApiResponse<AdminAccountDetailResponse> response =
                await client.GetAsync<AdminAccountDetailResponse>(path, cancellationToken);
            if (context.Json)
            {
                context.WriteJson(response.RawBody);
                return CliExitCodes.SUCCESS;
            }

            WriteDetail(context, response.Value);
            return CliExitCodes.SUCCESS;
        }

        private static async Task<int> KickAsync(CliContext context, CliArguments arguments,
            CancellationToken cancellationToken)
        {
            arguments.EnsureOnly(1, new[] { AdminCliOptions.REASON }, new[] { CliArguments.FLAG_KEEP_INVITES });
            Guid accountId = CliFormat.ParseGuid(arguments.Positional(0, "accountId"), "accountId");
            string reason = CliContext.RequireReason(arguments);
            bool revokeInvites = !arguments.HasFlag(CliArguments.FLAG_KEEP_INVITES);
            using AdminApiClient client = context.CreateClient();
            if (!context.TryBeginWrite("계정의 기존 토큰(로그인 묶음, 접근, 갱신, 게임 세션, 입장 토큰)을 폐기합니다. "
                + "현재 접속은 유지되고, 비밀번호 로그인은 막지 않아 같은 아이디와 비밀번호로 다시 로그인할 수 있습니다.",
                new List<(string, string)>
                {
                    ("계정 ID", accountId.ToString("D")),
                    ("미사용 초대 폐기", CliFormat.YesNo(revokeInvites)),
                    ("사유", reason)
                }, out string operationId))
            {
                return CliExitCodes.USAGE;
            }

            AdminApiResponse<AdminAccountKickResponse> response = await client.SendAsync<AdminAccountKickResponse>(
                HttpMethod.Post, string.Format(AdminApiRoutes.ACCOUNT_KICK_FORMAT, accountId.ToString("D")),
                new AdminAccountKickRequest(operationId, reason, revokeInvites), true, cancellationToken);
            if (context.Json)
            {
                context.WriteJson(response.RawBody);
                return CliExitCodes.SUCCESS;
            }

            AdminAccountKickResponse kicked = response.Value;
            context.Line("강제 종료를 확정했습니다. 기존 토큰을 폐기했고 현재 접속은 유지됩니다.");
            context.Line("비밀번호 로그인은 막지 않습니다. 로그인 차단은 제재(P1) 기능입니다.");
            List<(string, string)> fields = new List<(string, string)>
            {
                ("계정 ID", kicked.AccountId),
                ("미사용 초대 폐기", CliFormat.YesNo(kicked.InvitesRevoked)),
                ("현재 접속 유지", CliFormat.YesNo(kicked.CurrentSessionKept))
            };
            if (kicked.Operation != null)
            {
                fields.AddRange(CommandOutput.OperationFields(kicked.Operation));
            }

            context.WriteFields(fields);
            return CliExitCodes.SUCCESS;
        }

        private static void WriteDetail(CliContext context, AdminAccountDetailResponse detail)
        {
            context.Line("계정 " + detail.AccountId);
            List<(string, string)> fields = new List<(string, string)>
            {
                ("생성", CliFormat.Time(detail.CreatedAtUnixMs)),
                ("revision", CliFormat.Number(detail.Revision)),
                ("유효 로그인 묶음", CliFormat.Number(detail.ActiveTokenFamilies))
            };
            if (detail.Wallet != null)
            {
                AdminWalletView wallet = detail.Wallet;
                fields.Add(("재화", CliFormat.Number(wallet.Currency)));
                fields.Add(("입장권(예약)", CliFormat.Number(wallet.Tickets) + " (" + CliFormat.Number(wallet.TicketsReserved) + ")"));
                fields.Add(("행동력(기준 시각)", CliFormat.Number(wallet.ActionValue) + " ("
                    + CliFormat.Time(wallet.ActionAnchorUnixMs) + ")"));
            }
            else
            {
                fields.Add(("지갑", "없음"));
            }

            if (detail.Lease != null)
            {
                AdminAccountLeaseView lease = detail.Lease;
                fields.Add(("접속 임대", lease.InstanceId + " (역할 " + lease.Role + ", 만료 " + CliFormat.Time(lease.ExpiresAtUnixMs)
                    + ", 접속 중 " + CliFormat.YesNo(lease.Online) + ")"));
            }
            else
            {
                fields.Add(("접속 임대", "없음"));
            }

            context.WriteFields(fields);

            context.Line();
            context.Line("캐릭터");
            CliTable characters = new CliTable("캐릭터 ID", "직업", "정기", "파편", "revision");
            foreach (AdminCharacterView character in detail.Characters ?? Array.Empty<AdminCharacterView>())
            {
                characters.Add(character.CharacterId, CliFormat.Number(character.ClassId), CliFormat.Number(character.Essence),
                    CliFormat.Number(character.Shards), CliFormat.Number(character.Revision));
            }

            context.WriteTable(characters);

            context.Line();
            context.Line("계정 아이템");
            CliTable items = new CliTable("정의 ID", "귀속", "수량", "예약");
            foreach (AdminAccountItemView item in detail.Items ?? Array.Empty<AdminAccountItemView>())
            {
                items.Add(item.DefinitionId, item.BindingKind, CliFormat.Number(item.Quantity), CliFormat.Number(item.Reserved));
            }

            context.WriteTable(items);

            context.Line();
            context.Line("최근 출정");
            CliTable sessions = new CliTable("세션 ID", "모드", "맵", "세션 상태", "참가 상태", "결과", "생성", "종료");
            foreach (AdminAccountSessionView session in detail.RecentSessions ?? Array.Empty<AdminAccountSessionView>())
            {
                sessions.Add(session.SessionId, session.Mode, session.MapId, session.SessionState, session.ParticipantState,
                    CliFormat.Text(session.Result), CliFormat.Time(session.CreatedAtUnixMs),
                    CliFormat.Time(session.ClosedAtUnixMs));
            }

            context.WriteTable(sessions);

            context.Line();
            context.Line("초대");
            CliTable invites = new CliTable("초대 ID", "만료", "사용/최대", "마지막 사용", "폐기");
            foreach (AdminInviteView invite in detail.Invites ?? Array.Empty<AdminInviteView>())
            {
                invites.Add(invite.InviteId, CliFormat.Time(invite.ExpiresAtUnixMs),
                    CliFormat.Number(invite.UsedCount) + "/" + CliFormat.Number(invite.MaxUses),
                    CliFormat.Time(invite.UsedAtUnixMs), CliFormat.Time(invite.RevokedAtUnixMs));
            }

            context.WriteTable(invites);
        }
    }
}
