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
    /// <summary> 초대 코드 발급(새 계정 또는 기기 변경 재발급)과 폐기. 코드 원문은 한 번만 보여 주고 저장하지 않는다 </summary>
    internal sealed class InvitesCommand : ICliCommand
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string ISSUE  = "issue";
        private const string REVOKE = "revoke";

        private static readonly string[] G_Usage =
        {
            "invites issue [--account <accountId>] --max-uses <1-10> --hours <1-168> [--keep-sessions] --reason <사유>",
            "                                               계정 없이 주면 새 계정, 있으면 기기 변경 재발급(기존 세션 폐기)",
            "invites revoke <inviteId> --reason <사유>      초대 코드 폐기"
        };

        /****************************************
        *              Properties
        ****************************************/

        public string Name => "invites";
        public IReadOnlyList<string> Usage => G_Usage;

        /****************************************
        *                Methods
        ****************************************/

        public Task<int> RunAsync(CliContext context, CliArguments arguments, CancellationToken cancellationToken)
        {
            string sub = arguments.Positional(0, "하위 명령(issue, revoke)");
            CliArguments rest = arguments.SkipPositionals(1);
            return sub switch
            {
                ISSUE  => IssueAsync(context, rest, cancellationToken),
                REVOKE => RevokeAsync(context, rest, cancellationToken),
                _ => throw new CliUsageException("알 수 없는 하위 명령입니다: invites " + sub)
            };
        }

        private static async Task<int> IssueAsync(CliContext context, CliArguments arguments,
            CancellationToken cancellationToken)
        {
            arguments.EnsureOnly(0, new[]
            {
                AdminCliOptions.ACCOUNT, AdminCliOptions.MAX_USES, AdminCliOptions.HOURS, AdminCliOptions.REASON
            }, new[] { CliArguments.FLAG_KEEP_SESSIONS });
            string? accountText = arguments.Option(AdminCliOptions.ACCOUNT);
            string? accountId = accountText == null
                ? null
                : CliFormat.ParseGuid(accountText, "--" + AdminCliOptions.ACCOUNT).ToString("D");
            int maxUses = CliFormat.ParseInt(arguments.RequireOption(AdminCliOptions.MAX_USES), AdminCliOptions.MAX_USES);
            int hours = CliFormat.ParseInt(arguments.RequireOption(AdminCliOptions.HOURS), AdminCliOptions.HOURS);
            if ((maxUses < AdminInviteIssueRequest.MIN_USES) || (maxUses > AdminInviteIssueRequest.MAX_USES))
            {
                throw new CliUsageException("--max-uses는 " + AdminInviteIssueRequest.MIN_USES + "에서 "
                    + AdminInviteIssueRequest.MAX_USES + " 사이입니다.");
            }

            if ((hours < AdminInviteIssueRequest.MIN_HOURS) || (hours > AdminInviteIssueRequest.MAX_HOURS))
            {
                throw new CliUsageException("--hours는 " + AdminInviteIssueRequest.MIN_HOURS + "에서 "
                    + AdminInviteIssueRequest.MAX_HOURS + " 사이입니다.");
            }

            bool keepSessions = arguments.HasFlag(CliArguments.FLAG_KEEP_SESSIONS);
            if (keepSessions && (accountId == null))
            {
                throw new CliUsageException("--keep-sessions는 --account와 함께(기기 변경 재발급)만 씁니다.");
            }

            string reason = CliContext.RequireReason(arguments);
            using AdminApiClient client = context.CreateClient();
            if (!context.TryBeginWrite(accountId == null ? "새 계정용 초대 코드를 발급합니다." : "기기 변경용 초대 코드를 재발급합니다.",
                new List<(string, string)>
                {
                    ("계정", accountId ?? "새 계정"),
                    ("최대 사용", CliFormat.Number(maxUses)),
                    ("유효 시간", CliFormat.Number(hours) + "시간"),
                    ("기존 세션, 이전 초대 폐기", accountId == null ? CliFormat.NONE : CliFormat.YesNo(!keepSessions)),
                    ("사유", reason)
                }, out string operationId))
            {
                return CliExitCodes.USAGE;
            }

            // null이면 서버 기본값(폐기)이다. --keep-sessions일 때만 false를 보낸다
            bool? revokeExisting = keepSessions ? false : null;
            AdminApiResponse<AdminInviteIssueResponse> response = await client.SendAsync<AdminInviteIssueResponse>(
                HttpMethod.Post, AdminApiRoutes.INVITES,
                new AdminInviteIssueRequest(operationId, reason, accountId, maxUses, hours, revokeExisting), true,
                cancellationToken);
            AdminInviteIssueResponse issued = response.Value;
            if (context.Json)
            {
                context.WriteJson(response.RawBody);
            }
            else
            {
                context.Line("초대 코드를 발급했습니다.");
                List<(string, string)> fields = new List<(string, string)>
                {
                    ("초대 ID", issued.InviteId),
                    ("계정 ID", issued.AccountId),
                    ("최대 사용", CliFormat.Number(issued.MaxUses)),
                    ("만료", CliFormat.Time(issued.ExpiresAtUnixMs))
                };
                if (issued.InviteCode != null)
                {
                    fields.Add(("초대 코드", issued.InviteCode));
                }

                context.WriteFields(fields);
            }

            if (issued.InviteCode != null)
            {
                context.Warn("초대 코드 원문은 다시 볼 수 없습니다. 이 도구는 저장하지 않으니 지금 당사자에게 전달하세요.");
            }
            else
            {
                context.Warn("같은 operationId의 재전송이라 코드 원문이 응답에 없습니다. 코드를 잃었으면 이 초대를 폐기하고 새로 발급하세요.");
            }

            return CliExitCodes.SUCCESS;
        }

        private static async Task<int> RevokeAsync(CliContext context, CliArguments arguments,
            CancellationToken cancellationToken)
        {
            arguments.EnsureOnly(1, new[] { AdminCliOptions.REASON }, Array.Empty<string>());
            Guid inviteId = CliFormat.ParseGuid(arguments.Positional(0, "inviteId"), "inviteId");
            string reason = CliContext.RequireReason(arguments);
            using AdminApiClient client = context.CreateClient();
            if (!context.TryBeginWrite("초대 코드를 폐기합니다. 이미 만든 로그인 묶음은 폐기하지 않습니다.", new List<(string, string)>
                {
                    ("초대 ID", inviteId.ToString("D")),
                    ("사유", reason)
                }, out string operationId))
            {
                return CliExitCodes.USAGE;
            }

            AdminApiResponse<OperationResult> response = await client.SendAsync<OperationResult>(HttpMethod.Post,
                string.Format(AdminApiRoutes.INVITE_REVOKE_FORMAT, inviteId.ToString("D")),
                new AdminInviteRevokeRequest(operationId, reason), true, cancellationToken);
            CommandOutput.WriteOperation(context, response, "초대 코드를 폐기했습니다.");
            return CliExitCodes.SUCCESS;
        }
    }
}
