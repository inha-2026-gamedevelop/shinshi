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
    /// <summary> 운영자 계정 목록, 생성, 비활성화(Owner). 생성 응답의 초기 비밀번호와 TOTP 비밀은 한 번만 보여 주고 저장하지 않는다 </summary>
    internal sealed class OperatorsCommand : ICliCommand
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string LIST    = "list";
        private const string CREATE  = "create";
        private const string DISABLE = "disable";

        private static readonly string[] G_Usage =
        {
            "operators list                                 운영자 목록 (Owner)",
            "operators create --user <이름> --role viewer|operator|admin|owner --reason <사유>",
            "                                               운영자 생성. 초기 비밀번호와 TOTP 비밀을 한 번만 출력",
            "operators disable <adminId> --reason <사유>    운영자 비활성화와 세션 폐기"
        };

        /****************************************
        *              Properties
        ****************************************/

        public string Name => "operators";
        public IReadOnlyList<string> Usage => G_Usage;

        /****************************************
        *                Methods
        ****************************************/

        public Task<int> RunAsync(CliContext context, CliArguments arguments, CancellationToken cancellationToken)
        {
            string sub = arguments.Positional(0, "하위 명령(list, create, disable)");
            CliArguments rest = arguments.SkipPositionals(1);
            return sub switch
            {
                LIST    => ListAsync(context, rest, cancellationToken),
                CREATE  => CreateAsync(context, rest, cancellationToken),
                DISABLE => DisableAsync(context, rest, cancellationToken),
                _ => throw new CliUsageException("알 수 없는 하위 명령입니다: operators " + sub)
            };
        }

        private static async Task<int> ListAsync(CliContext context, CliArguments arguments,
            CancellationToken cancellationToken)
        {
            arguments.EnsureOnly(0, Array.Empty<string>(), Array.Empty<string>());
            using AdminApiClient client = context.CreateClient();
            AdminApiResponse<AdminOperatorListResponse> response =
                await client.GetAsync<AdminOperatorListResponse>(AdminApiRoutes.OPERATORS, cancellationToken);
            if (context.Json)
            {
                context.WriteJson(response.RawBody);
                return CliExitCodes.SUCCESS;
            }

            CliTable table = new CliTable("운영자 ID", "로그인 이름", "역할", "생성", "비활성", "잠금 해제");
            foreach (AdminOperatorSummary summary in response.Value.Operators ?? Array.Empty<AdminOperatorSummary>())
            {
                table.Add(summary.AdminId, summary.LoginName, ContractCodeNames.Role(summary.Role),
                    CliFormat.Time(summary.CreatedAtUnixMs), CliFormat.Time(summary.DisabledAtUnixMs),
                    CliFormat.Time(summary.LockedUntilUnixMs));
            }

            context.WriteTable(table);
            return CliExitCodes.SUCCESS;
        }

        private static async Task<int> CreateAsync(CliContext context, CliArguments arguments,
            CancellationToken cancellationToken)
        {
            arguments.EnsureOnly(0, new[] { AdminCliOptions.USER, AdminCliOptions.ROLE, AdminCliOptions.REASON },
                Array.Empty<string>());
            string user = arguments.RequireOption(AdminCliOptions.USER).Trim();
            string roleText = arguments.RequireOption(AdminCliOptions.ROLE);
            if (!ContractCodeNames.TryParseRole(roleText, out int role))
            {
                throw new CliUsageException("--role은 viewer, operator, admin, owner 중 하나입니다: " + roleText);
            }

            string reason = CliContext.RequireReason(arguments);
            using AdminApiClient client = context.CreateClient();
            if (!context.TryBeginWrite("운영자를 만듭니다.", new List<(string, string)>
                {
                    ("로그인 이름", user),
                    ("역할", ContractCodeNames.Role(role)),
                    ("사유", reason)
                }, out string operationId))
            {
                return CliExitCodes.USAGE;
            }

            AdminApiResponse<AdminOperatorCreateResponse> response = await client.SendAsync<AdminOperatorCreateResponse>(
                HttpMethod.Post, AdminApiRoutes.OPERATORS, new AdminOperatorCreateRequest(operationId, reason, user, role),
                true, cancellationToken);
            AdminOperatorCreateResponse created = response.Value;
            bool hasSecrets = created.InitialPassword != null;
            if (context.Json)
            {
                context.WriteJson(response.RawBody);
            }
            else
            {
                context.Line("운영자를 만들었습니다.");
                List<(string, string)> fields = new List<(string, string)>
                {
                    ("운영자 ID", created.AdminId),
                    ("로그인 이름", created.LoginName),
                    ("역할", ContractCodeNames.Role(created.Role))
                };
                if (hasSecrets)
                {
                    fields.Add(("초기 비밀번호", created.InitialPassword!));
                    fields.Add(("TOTP 비밀(Base32)", CliFormat.Text(created.TotpSecretBase32)));
                    fields.Add(("TOTP URI", CliFormat.Text(created.TotpUri)));
                }

                context.WriteFields(fields);
            }

            if (hasSecrets)
            {
                context.Warn("위 초기 비밀번호와 TOTP 비밀은 다시 볼 수 없습니다. 이 도구는 저장하지 않으니 지금 당사자에게 안전하게 전달하고"
                    + " 터미널 기록에서 지우세요.");
            }
            else
            {
                context.Warn("같은 operationId의 재전송이라 비밀값이 응답에 없습니다. 비밀값을 잃었으면 이 운영자를 비활성화하고 다시 만드세요.");
            }

            return CliExitCodes.SUCCESS;
        }

        private static async Task<int> DisableAsync(CliContext context, CliArguments arguments,
            CancellationToken cancellationToken)
        {
            arguments.EnsureOnly(1, new[] { AdminCliOptions.REASON }, Array.Empty<string>());
            Guid adminId = CliFormat.ParseGuid(arguments.Positional(0, "adminId"), "adminId");
            string reason = CliContext.RequireReason(arguments);
            using AdminApiClient client = context.CreateClient();
            if (!context.TryBeginWrite("운영자를 비활성화합니다. 열린 어드민 세션도 함께 폐기됩니다.", new List<(string, string)>
                {
                    ("운영자 ID", adminId.ToString("D")),
                    ("사유", reason)
                }, out string operationId))
            {
                return CliExitCodes.USAGE;
            }

            AdminApiResponse<OperationResult> response = await client.SendAsync<OperationResult>(HttpMethod.Post,
                string.Format(AdminApiRoutes.OPERATOR_DISABLE_FORMAT, adminId.ToString("D")),
                new AdminOperatorDisableRequest(operationId, reason), true, cancellationToken);
            CommandOutput.WriteOperation(context, response, "운영자를 비활성화했습니다.");
            return CliExitCodes.SUCCESS;
        }
    }
}
