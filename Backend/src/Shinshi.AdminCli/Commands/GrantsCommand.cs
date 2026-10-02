// System
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.AdminCli
{
    /// <summary> 운영자 지급(우편함) 초안 작성, 조회, 실행, 미수령 취소 </summary>
    internal sealed class GrantsCommand : ICliCommand
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string CREATE  = "create";
        private const string SHOW    = "show";
        private const string EXECUTE = "execute";
        private const string CANCEL  = "cancel";

        // 확인 요약에 보여 줄 대상 ID 수. 나머지는 개수만 적는다
        private const int SUMMARY_TARGETS = 5;

        private static readonly string[] G_SourceKinds =
        {
            AdminGrantCodes.SOURCE_ADMIN_GRANT, AdminGrantCodes.SOURCE_RECOVERY_COMPENSATION
        };

        private static readonly string[] G_Usage =
        {
            "grants create --accounts <id,id,...|@파일> --reward <kind[:assetId[:binding]]=수량> (반복)",
            "              [--claim-days <1-365>] [--source-kind AdminGrant|RecoveryCompensation] [--source-ref <참조>]",
            "              --reason <사유>                  지급 초안 작성. 우편함에는 execute가 넣음",
            "grants show <grantId>                          지급 상태와 수령 집계",
            "grants execute <grantId> --reason <사유>       우편함에 넣음 (2명 이상, 보정 지급은 Admin)",
            "grants cancel <grantId> --reason <사유>        초안 취소 또는 미수령 우편 회수"
        };

        /****************************************
        *              Properties
        ****************************************/

        public string Name => "grants";
        public IReadOnlyList<string> Usage => G_Usage;

        /****************************************
        *                Methods
        ****************************************/

        public Task<int> RunAsync(CliContext context, CliArguments arguments, CancellationToken cancellationToken)
        {
            string sub = arguments.Positional(0, "하위 명령(create, show, execute, cancel)");
            CliArguments rest = arguments.SkipPositionals(1);
            return sub switch
            {
                CREATE  => CreateAsync(context, rest, cancellationToken),
                SHOW    => ShowAsync(context, rest, cancellationToken),
                EXECUTE => ActAsync(context, rest, true, cancellationToken),
                CANCEL  => ActAsync(context, rest, false, cancellationToken),
                _ => throw new CliUsageException("알 수 없는 하위 명령입니다: grants " + sub)
            };
        }

        private static async Task<int> CreateAsync(CliContext context, CliArguments arguments,
            CancellationToken cancellationToken)
        {
            arguments.EnsureOnly(0, new[]
            {
                AdminCliOptions.ACCOUNTS, AdminCliOptions.REWARD, AdminCliOptions.CLAIM_DAYS, AdminCliOptions.SOURCE_KIND,
                AdminCliOptions.SOURCE_REF, AdminCliOptions.REASON
            }, Array.Empty<string>());
            string[] accounts = AccountListSyntax.Parse(arguments.RequireOption(AdminCliOptions.ACCOUNTS), File.ReadAllText);
            AdminGrantReward[] rewards = ParseRewards(arguments.Options(AdminCliOptions.REWARD));
            int? claimDays = ParseClaimDays(arguments.Option(AdminCliOptions.CLAIM_DAYS));
            string sourceKind = ParseSourceKind(arguments.Option(AdminCliOptions.SOURCE_KIND));
            string? sourceRef = arguments.Option(AdminCliOptions.SOURCE_REF)?.Trim();
            if ((sourceRef != null) && ((sourceRef.Length == 0) || (sourceRef.Length > AdminGrantCodes.MAX_SOURCE_REF_LENGTH)))
            {
                throw new CliUsageException("--source-ref는 1에서 " + AdminGrantCodes.MAX_SOURCE_REF_LENGTH + "자입니다.");
            }

            if (string.Equals(sourceKind, AdminGrantCodes.SOURCE_RECOVERY_COMPENSATION, StringComparison.Ordinal)
                && (sourceRef == null))
            {
                throw new CliUsageException("RecoveryCompensation은 --source-ref(복구 세션 ID 등)가 필요합니다.");
            }

            string reason = CliContext.RequireReason(arguments);
            using AdminApiClient client = context.CreateClient();
            if (!context.TryBeginWrite("지급 초안을 만듭니다. 우편함에는 grants execute로 넣습니다.", new List<(string, string)>
                {
                    ("대상 계정", DescribeTargets(accounts)),
                    ("보상", string.Join(", ", rewards.Select(AssetSyntax.Describe))),
                    ("수령 기한", CliFormat.Number(claimDays ?? AdminGrantCodes.DEFAULT_CLAIM_DAYS) + "일"),
                    ("출처", sourceKind + (sourceRef == null ? string.Empty : " (" + sourceRef + ")")),
                    ("사유", reason)
                }, out string operationId))
            {
                return CliExitCodes.USAGE;
            }

            AdminApiResponse<AdminGrantCreateResponse> response = await client.SendAsync<AdminGrantCreateResponse>(
                HttpMethod.Post, AdminApiRoutes.GRANTS,
                new AdminGrantCreateRequest(operationId, reason, accounts, rewards, claimDays, sourceKind, sourceRef),
                true, cancellationToken);
            if (context.Json)
            {
                context.WriteJson(response.RawBody);
                return CliExitCodes.SUCCESS;
            }

            context.Line("지급 초안을 만들었습니다.");
            context.WriteFields(new List<(string, string)>
            {
                ("지급 ID", response.Value.GrantId),
                ("operationId", CliFormat.Text(response.Value.Operation?.OperationId))
            });
            context.Note("실행: " + AdminCliApp.TOOL_NAME + " grants execute " + response.Value.GrantId + " --reason <사유>");
            return CliExitCodes.SUCCESS;
        }

        private static async Task<int> ShowAsync(CliContext context, CliArguments arguments,
            CancellationToken cancellationToken)
        {
            arguments.EnsureOnly(1, Array.Empty<string>(), Array.Empty<string>());
            Guid grantId = CliFormat.ParseGuid(arguments.Positional(0, "grantId"), "grantId");
            using AdminApiClient client = context.CreateClient();
            AdminApiResponse<AdminGrantDetail> response = await client.GetAsync<AdminGrantDetail>(
                string.Format(AdminApiRoutes.GRANT_FORMAT, grantId.ToString("D")), cancellationToken);
            if (context.Json)
            {
                context.WriteJson(response.RawBody);
                return CliExitCodes.SUCCESS;
            }

            context.WriteFields(DetailFields(response.Value));
            return CliExitCodes.SUCCESS;
        }

        // 실행과 취소는 현재 지급 내용을 먼저 읽어 요약에 보여 준다. ID만 보고 확인하면 다른 지급을 실행하기 쉽다
        private static async Task<int> ActAsync(CliContext context, CliArguments arguments, bool execute,
            CancellationToken cancellationToken)
        {
            arguments.EnsureOnly(1, new[] { AdminCliOptions.REASON }, Array.Empty<string>());
            Guid grantId = CliFormat.ParseGuid(arguments.Positional(0, "grantId"), "grantId");
            string reason = CliContext.RequireReason(arguments);
            string id = grantId.ToString("D");
            using AdminApiClient client = context.CreateClient();
            AdminApiResponse<AdminGrantDetail> current = await client.GetAsync<AdminGrantDetail>(
                string.Format(AdminApiRoutes.GRANT_FORMAT, id), cancellationToken);
            List<(string, string)> summary = DetailFields(current.Value);
            summary.Add(("이번 사유", reason));
            string title = execute
                ? "지급을 실행해 대상 우편함에 넣습니다."
                : "지급을 취소합니다. 실행된 지급이면 아직 수령하지 않은 우편만 회수합니다.";
            if (!context.TryBeginWrite(title, summary, out string operationId))
            {
                return CliExitCodes.USAGE;
            }

            AdminApiResponse<AdminGrantActionResponse> response = await client.SendAsync<AdminGrantActionResponse>(
                HttpMethod.Post, string.Format(execute ? AdminApiRoutes.GRANT_EXECUTE_FORMAT : AdminApiRoutes.GRANT_CANCEL_FORMAT, id),
                new AdminGrantActionRequest(operationId, reason), true, cancellationToken);
            if (context.Json)
            {
                context.WriteJson(response.RawBody);
                return CliExitCodes.SUCCESS;
            }

            context.Line(execute ? "지급을 실행했습니다." : "지급을 취소했습니다.");
            context.WriteFields(DetailFields(response.Value.Grant));
            return CliExitCodes.SUCCESS;
        }

        private static List<(string, string)> DetailFields(AdminGrantDetail grant)
        {
            AdminGrantReward[] rewards = grant.Rewards ?? Array.Empty<AdminGrantReward>();
            return new List<(string, string)>
            {
                ("지급 ID", grant.GrantId),
                ("상태", grant.State),
                ("출처", grant.SourceKind + (string.IsNullOrEmpty(grant.SourceRef) ? string.Empty : " (" + grant.SourceRef + ")")),
                ("작성", CliFormat.Text(grant.CreatedBy) + ", " + CliFormat.Time(grant.CreatedAtUnixMs)),
                ("실행", CliFormat.Time(grant.ExecutedAtUnixMs)),
                ("취소", CliFormat.Time(grant.CancelledAtUnixMs)),
                ("작성 사유", CliFormat.Text(grant.Reason)),
                ("수령 기한", CliFormat.Number(grant.ClaimDays) + "일"),
                ("보상", string.Join(", ", rewards.Select(AssetSyntax.Describe))),
                ("대상 수", CliFormat.Number(grant.TargetCount)),
                ("전달/수령/대기/만료/취소", CliFormat.Number(grant.DeliveredCount) + "/" + CliFormat.Number(grant.ClaimedCount) + "/"
                    + CliFormat.Number(grant.OpenCount) + "/" + CliFormat.Number(grant.ExpiredCount) + "/"
                    + CliFormat.Number(grant.CancelledCount))
            };
        }

        private static AdminGrantReward[] ParseRewards(IReadOnlyList<string> texts)
        {
            if (texts.Count == 0)
            {
                throw new CliUsageException("--reward가 하나 이상 필요합니다. 예: --reward Currency=100");
            }

            if (texts.Count > AdminGrantCodes.MAX_REWARD_LINES)
            {
                throw new CliUsageException("보상 줄은 " + AdminGrantCodes.MAX_REWARD_LINES + "개까지입니다.");
            }

            AdminGrantReward[] rewards = new AdminGrantReward[texts.Count];
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < texts.Count; ++i)
            {
                rewards[i] = AssetSyntax.ParseReward(texts[i]);
                string key = rewards[i].AssetKind + ":" + rewards[i].AssetId + ":" + rewards[i].BindingKind;
                if (!keys.Add(key))
                {
                    throw new CliUsageException("같은 자산의 보상 줄이 두 번 있습니다. 수량을 합쳐 한 줄로 쓰세요: " + texts[i]);
                }
            }

            return rewards;
        }

        private static int? ParseClaimDays(string? text)
        {
            if (text == null)
            {
                return null;
            }

            int days = CliFormat.ParseInt(text, AdminCliOptions.CLAIM_DAYS);
            if ((days < AdminGrantCodes.MIN_CLAIM_DAYS) || (days > AdminGrantCodes.MAX_CLAIM_DAYS))
            {
                throw new CliUsageException("--claim-days는 " + AdminGrantCodes.MIN_CLAIM_DAYS + "에서 "
                    + AdminGrantCodes.MAX_CLAIM_DAYS + " 사이입니다.");
            }

            return days;
        }

        private static string ParseSourceKind(string? text)
        {
            if (text == null)
            {
                return AdminGrantCodes.SOURCE_ADMIN_GRANT;
            }

            foreach (string kind in G_SourceKinds)
            {
                if (string.Equals(kind, text.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return kind;
                }
            }

            throw new CliUsageException("--source-kind는 " + string.Join(", ", G_SourceKinds) + " 중 하나입니다: " + text);
        }

        private static string DescribeTargets(string[] accounts)
        {
            string shown = string.Join(", ", accounts.Take(SUMMARY_TARGETS));
            int rest = accounts.Length - SUMMARY_TARGETS;
            return accounts.Length + "개: " + shown + (rest > 0 ? " 외 " + rest + "개" : string.Empty);
        }
    }
}
