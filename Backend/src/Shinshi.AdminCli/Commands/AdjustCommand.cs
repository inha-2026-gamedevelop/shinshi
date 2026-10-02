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
    /// <summary> 계정 하나의 자산을 직접 늘리거나 줄인다. 차감은 가용량까지만 하고 모자란 양은 shortfall로 돌아온다 </summary>
    internal sealed class AdjustCommand : ICliCommand
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly string[] G_Usage =
        {
            "adjust <accountId> --asset <kind[:assetId[:binding]]> --delta <+n|-n> [--character <characterId>] --reason <사유>",
            "                                               개인 재화 조정. Essence, Shards는 --character 필요"
        };

        /****************************************
        *              Properties
        ****************************************/

        public string Name => "adjust";
        public IReadOnlyList<string> Usage => G_Usage;

        /****************************************
        *                Methods
        ****************************************/

        public async Task<int> RunAsync(CliContext context, CliArguments arguments, CancellationToken cancellationToken)
        {
            arguments.EnsureOnly(1, new[]
            {
                AdminCliOptions.ASSET, AdminCliOptions.DELTA, AdminCliOptions.CHARACTER, AdminCliOptions.REASON
            }, Array.Empty<string>());
            Guid accountId = CliFormat.ParseGuid(arguments.Positional(0, "accountId"), "accountId");
            AssetSpec asset = AssetSyntax.ParseAsset(arguments.RequireOption(AdminCliOptions.ASSET));
            long delta = CliFormat.ParseLong(arguments.RequireOption(AdminCliOptions.DELTA), AdminCliOptions.DELTA);
            if ((delta == 0) || (delta > AdminGrantCodes.MAX_ADJUSTMENT) || (delta < -AdminGrantCodes.MAX_ADJUSTMENT))
            {
                throw new CliUsageException("--delta는 0이 아니고 절댓값이 " + AdminGrantCodes.MAX_ADJUSTMENT + " 이하입니다.");
            }

            bool characterAsset = string.Equals(asset.Kind, AdminGrantCodes.ASSET_ESSENCE, StringComparison.Ordinal)
                || string.Equals(asset.Kind, AdminGrantCodes.ASSET_SHARDS, StringComparison.Ordinal);
            string? characterText = arguments.Option(AdminCliOptions.CHARACTER);
            if (characterAsset && (characterText == null))
            {
                throw new CliUsageException(asset.Kind + "는 캐릭터 재화라 --character가 필요합니다.");
            }

            if ((!characterAsset) && (characterText != null))
            {
                throw new CliUsageException(asset.Kind + "는 계정 자산이라 --character를 받지 않습니다.");
            }

            string? characterId = characterText == null
                ? null
                : CliFormat.ParseGuid(characterText, "--" + AdminCliOptions.CHARACTER).ToString("D");
            string reason = CliContext.RequireReason(arguments);
            using AdminApiClient client = context.CreateClient();
            if (!context.TryBeginWrite(delta > 0 ? "자산을 늘립니다." : "자산을 줄입니다. 가용량이 모자라면 그만큼은 차감하지 않습니다.",
                new List<(string, string)>
                {
                    ("계정 ID", accountId.ToString("D")),
                    ("캐릭터 ID", CliFormat.Text(characterId)),
                    ("자산", asset.ToString()),
                    ("변화량", (delta > 0 ? "+" : string.Empty) + CliFormat.Number(delta)),
                    ("사유", reason)
                }, out string operationId))
            {
                return CliExitCodes.USAGE;
            }

            AdminApiResponse<AdminAdjustmentResponse> response = await client.SendAsync<AdminAdjustmentResponse>(
                HttpMethod.Post, string.Format(AdminApiRoutes.ACCOUNT_ADJUSTMENTS_FORMAT, accountId.ToString("D")),
                new AdminAdjustmentRequest(operationId, reason, asset.Kind, asset.AssetId, asset.BindingKind, characterId,
                    delta), true, cancellationToken);
            if (context.Json)
            {
                context.WriteJson(response.RawBody);
                return CliExitCodes.SUCCESS;
            }

            AdminAdjustmentResponse adjusted = response.Value;
            context.Line("조정을 확정했습니다.");
            context.WriteFields(new List<(string, string)>
            {
                ("요청 변화량", CliFormat.Number(adjusted.RequestedDelta)),
                ("적용 변화량", CliFormat.Number(adjusted.AppliedDelta)),
                ("부족분", CliFormat.Number(adjusted.Shortfall)),
                ("적용 후 잔액", CliFormat.Number(adjusted.BalanceAfter)),
                ("operationId", CliFormat.Text(adjusted.Operation?.OperationId))
            });
            if (adjusted.Shortfall > 0)
            {
                context.Warn("가용량이 모자라 " + CliFormat.Number(adjusted.Shortfall) + "만큼 차감하지 못했습니다. 따로 판단하세요.");
            }

            return CliExitCodes.SUCCESS;
        }
    }
}
