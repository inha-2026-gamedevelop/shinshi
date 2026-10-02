// System
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.AdminCli
{
    /// <summary> 세션 수동 복구(Admin). 실제 정산이나 장애 취소는 세션 배정 서버를 actor로 한 별도 작업이다 </summary>
    internal sealed class RecoverCommand : ICliCommand
    {
        /****************************************
        *                Fields
        ****************************************/

        // 명령줄 표기와 요청 본문 action 값
        private static readonly (string Cli, string Wire, string Description)[] G_Actions =
        {
            ("retry-settlement", AdminSessionRecoveryCodes.RETRY_SETTLEMENT,
                "저장된 종료 이벤트로 Settling 세션을 자동 복구와 같은 operationId로 정산합니다."),
            ("service-cancel", AdminSessionRecoveryCodes.SERVICE_CANCEL,
                "서버 소실이 확인된 Running 세션을 서비스 장애로 취소합니다(티켓, 물약 환급과 정기 보상)."),
            ("force-close", AdminSessionRecoveryCodes.FORCE_CLOSE,
                "격리된 Settling 세션을 강제로 종료합니다.")
        };

        private static readonly string[] G_Usage =
        {
            "recover <sessionId> --action retry-settlement|service-cancel|force-close --reason <사유>",
            "                                               세션 수동 복구 (Admin)"
        };

        /****************************************
        *              Properties
        ****************************************/

        public string Name => "recover";
        public IReadOnlyList<string> Usage => G_Usage;

        /****************************************
        *                Methods
        ****************************************/

        public async Task<int> RunAsync(CliContext context, CliArguments arguments, CancellationToken cancellationToken)
        {
            arguments.EnsureOnly(1, new[] { AdminCliOptions.ACTION, AdminCliOptions.REASON }, Array.Empty<string>());
            Guid sessionId = CliFormat.ParseGuid(arguments.Positional(0, "sessionId"), "sessionId");
            (string Cli, string Wire, string Description) action = ParseAction(arguments.RequireOption(AdminCliOptions.ACTION));
            string reason = CliContext.RequireReason(arguments);
            using AdminApiClient client = context.CreateClient();
            if (!context.TryBeginWrite(action.Description, new List<(string, string)>
                {
                    ("세션 ID", sessionId.ToString("D")),
                    ("action", action.Wire),
                    ("사유", reason)
                }, out string operationId))
            {
                return CliExitCodes.USAGE;
            }

            AdminApiResponse<AdminSessionRecoveryResponse> response = await client.SendAsync<AdminSessionRecoveryResponse>(
                HttpMethod.Post, string.Format(AdminApiRoutes.SESSION_RECOVERY_FORMAT, sessionId.ToString("D")),
                new AdminSessionRecoveryRequest(operationId, reason, action.Wire), true, cancellationToken);
            if (context.Json)
            {
                context.WriteJson(response.RawBody);
                return CliExitCodes.SUCCESS;
            }

            AdminSessionRecoveryResponse recovered = response.Value;
            context.Line("수동 복구를 확정했습니다.");
            context.WriteFields(new List<(string, string)>
            {
                ("세션 ID", recovered.SessionId),
                ("action", recovered.Action),
                ("operationId", CliFormat.Text(recovered.Operation?.OperationId)),
                ("복구 actorId", CliFormat.Text(recovered.RecoveryActorId)),
                ("복구 operationId", CliFormat.Text(recovered.RecoveryOperationId))
            });
            if ((recovered.RecoveryActorId != null) && (recovered.RecoveryOperationId != null))
            {
                context.Note("원장 확인: " + AdminCliApp.TOOL_NAME + " operation " + recovered.RecoveryActorId + " "
                    + recovered.RecoveryOperationId + " --reason <사유>");
            }

            return CliExitCodes.SUCCESS;
        }

        private static (string Cli, string Wire, string Description) ParseAction(string text)
        {
            foreach ((string Cli, string Wire, string Description) action in G_Actions)
            {
                if (string.Equals(action.Cli, text.Trim(), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(action.Wire, text.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return action;
                }
            }

            throw new CliUsageException("--action은 retry-settlement, service-cancel, force-close 중 하나입니다: " + text);
        }
    }
}
