// System
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;

namespace Shinshi.AdminCli
{
    /// <summary> 점검 모드, 기능 차단, 허용 빌드 조회와 저장. 저장은 현재 값을 먼저 읽어 지정하지 않은 항목을 그대로 보내고,
    /// 네 값(점검, 차단, 허용 빌드, 기준 revision)을 모두 주면 읽지 않고 그대로 보낸다 </summary>
    internal sealed class ServiceStateCommand : ICliCommand
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SHOW = "show";
        private const string SET  = "set";

        // 목록을 비우는 값. 빈 문자열 인자는 셸마다 다루는 방식이 달라 단어를 쓴다
        private const string EMPTY_LIST     = "none";
        private const char   LIST_SEPARATOR = ',';

        private static readonly string[] G_On  = { "on", "true", "yes" };
        private static readonly string[] G_Off = { "off", "false", "no" };

        private static readonly string[] G_Usage =
        {
            "service-state show                             점검, 기능 차단, 허용 빌드, revision",
            "service-state set [--maintenance on|off] [--disable <기능,...|none>] [--allow-builds <빌드,...|none>]",
            "                  [--expected-revision <n>] --reason <사유>",
            "                                               지정한 항목만 바꿈(Admin). 목록은 통째로 바뀜. 네 값을 모두 주면",
            "                                               현재 값을 읽지 않음(재시도 명령)"
        };

        /****************************************
        *              Properties
        ****************************************/

        public string Name => "service-state";
        public IReadOnlyList<string> Usage => G_Usage;

        /****************************************
        *                Methods
        ****************************************/

        public Task<int> RunAsync(CliContext context, CliArguments arguments, CancellationToken cancellationToken)
        {
            string sub = arguments.Positional(0, "하위 명령(show, set)");
            CliArguments rest = arguments.SkipPositionals(1);
            return sub switch
            {
                SHOW => ShowAsync(context, rest, cancellationToken),
                SET  => SetAsync(context, rest, cancellationToken),
                _ => throw new CliUsageException("알 수 없는 하위 명령입니다: service-state " + sub)
            };
        }

        private static async Task<int> ShowAsync(CliContext context, CliArguments arguments,
            CancellationToken cancellationToken)
        {
            arguments.EnsureOnly(0, Array.Empty<string>(), Array.Empty<string>());
            using AdminApiClient client = context.CreateClient();
            AdminApiResponse<AdminServiceStateResponse> response =
                await client.GetAsync<AdminServiceStateResponse>(AdminApiRoutes.SERVICE_STATE, cancellationToken);
            if (context.Json)
            {
                context.WriteJson(response.RawBody);
                return CliExitCodes.SUCCESS;
            }

            AdminServiceStateResponse state = response.Value;
            context.WriteFields(new List<(string, string)>
            {
                ("점검", OnOff(state.Maintenance)),
                ("차단 기능", DescribeList(state.DisabledFeatures)),
                ("허용 빌드", DescribeList(state.AllowedBuildIds)),
                ("revision", CliFormat.Number(state.Revision)),
                ("마지막 저장", CliFormat.Text(state.UpdatedBy) + ", " + CliFormat.Time(state.UpdatedAtUnixMs)),
                ("출처", state.Source),
                ("기능 키", DescribeList(state.KnownFeatures))
            });
            return CliExitCodes.SUCCESS;
        }

        private static async Task<int> SetAsync(CliContext context, CliArguments arguments,
            CancellationToken cancellationToken)
        {
            arguments.EnsureOnly(0, new[]
            {
                AdminCliOptions.MAINTENANCE, AdminCliOptions.DISABLE, AdminCliOptions.ALLOW_BUILDS,
                AdminCliOptions.EXPECTED_REVISION, AdminCliOptions.REASON
            }, Array.Empty<string>());
            string? maintenanceText = arguments.Option(AdminCliOptions.MAINTENANCE);
            string? disableText = arguments.Option(AdminCliOptions.DISABLE);
            string? buildsText = arguments.Option(AdminCliOptions.ALLOW_BUILDS);
            if ((maintenanceText == null) && (disableText == null) && (buildsText == null))
            {
                throw new CliUsageException("바꿀 항목(--maintenance, --disable, --allow-builds)을 하나 이상 주세요.");
            }

            bool? maintenance = maintenanceText == null ? null : ParseOnOff(maintenanceText);
            string? revisionText = arguments.Option(AdminCliOptions.EXPECTED_REVISION);
            long? expectedRevision = revisionText == null
                ? null
                : CliFormat.ParseLong(revisionText, AdminCliOptions.EXPECTED_REVISION);
            string reason = CliContext.RequireReason(arguments);

            // 네 값을 모두 주면 현재 값을 읽지 않고 그대로 보낸다. 재시도 명령이 이 경로다. 생략한 항목을 최신 값으로 다시 채우면
            // 그 사이 다른 운영자가 바꾼 값이 섞여 요청 본문이 달라지고, 같은 operationId 재전송이 IDEMPOTENCY_CONFLICT가 된다
            bool complete = (maintenance != null) && (disableText != null) && (buildsText != null)
                && (expectedRevision != null);
            using AdminApiClient client = context.CreateClient();
            AdminServiceStateResponse? current = complete
                ? null
                : (await client.GetAsync<AdminServiceStateResponse>(AdminApiRoutes.SERVICE_STATE,
                    cancellationToken)).Value;
            IReadOnlyList<string> known = current?.KnownFeatures ?? ServiceFeatureKeys.All.ToArray();
            string[] disabled = disableText == null
                ? current!.DisabledFeatures ?? Array.Empty<string>()
                : ParseFeatures(disableText, known);
            string[] builds = buildsText == null
                ? current!.AllowedBuildIds ?? Array.Empty<string>()
                : ParseBuilds(buildsText);
            bool nextMaintenance = maintenance ?? current!.Maintenance;
            long revision = expectedRevision ?? current!.Revision;

            List<(string, string)> summary = current == null
                ? new List<(string, string)>
                {
                    ("점검", OnOff(nextMaintenance) + " (현재 값을 읽지 않고 그대로 보냄)"),
                    ("차단 기능", DescribeList(disabled)),
                    ("허용 빌드", DescribeList(builds))
                }
                : new List<(string, string)>
                {
                    ("점검", Change(OnOff(current.Maintenance), OnOff(nextMaintenance))),
                    ("차단 기능", Change(DescribeList(current.DisabledFeatures), DescribeList(disabled))),
                    ("허용 빌드", Change(DescribeList(current.AllowedBuildIds), DescribeList(builds)))
                };
            summary.Add(("기준 revision", CliFormat.Number(revision)));
            summary.Add(("사유", reason));
            if (builds.Length == 0)
            {
                summary.Add(("주의", "허용 빌드가 비면 어떤 빌드도 로그인할 수 없습니다"));
            }

            if (!context.TryBeginWrite("서비스 상태를 저장합니다.", summary, out string operationId))
            {
                return CliExitCodes.USAGE;
            }

            WriteRetryCommand(context, nextMaintenance, disabled, builds, revision, reason, operationId);
            AdminApiResponse<OperationResult> response = await client.SendAsync<OperationResult>(HttpMethod.Put,
                AdminApiRoutes.SERVICE_STATE,
                new AdminServiceStateUpdateRequest(operationId, reason, nextMaintenance, disabled, builds, revision), true,
                cancellationToken);
            CommandOutput.WriteOperation(context, response, "서비스 상태를 저장했습니다.");
            return CliExitCodes.SUCCESS;
        }

        // 보내기 전에 실제로 보낼 네 값과 operationId를 모두 담은 재시도 명령을 남긴다. 응답을 잃은 뒤 이 명령은 현재 값을
        // 다시 읽지 않으므로 같은 본문이 가고 서버가 처음 확정한 결과를 돌려준다
        private static void WriteRetryCommand(CliContext context, bool maintenance, IReadOnlyList<string> disabled,
            IReadOnlyList<string> builds, long revision, string reason, string operationId)
        {
            if ((!CanWriteList(disabled)) || (!CanWriteList(builds)) || reason.Any(char.IsControl))
            {
                context.Warn("  보낼 사유나 목록에 제어 문자 또는 CLI 목록으로 보존할 수 없는 값이 있어 재시도 명령으로 옮길 수 없습니다. "
                    + "응답을 잃으면 service-state show로 결과를 확인하세요.");
                return;
            }

            context.Note("  응답을 받지 못하면 PowerShell 7.3 이상(기본 Windows/Standard 인자 전달 모드)에서 현재 값을 다시 읽지 않는 아래 명령으로 "
                + "그대로 다시 보내세요(사유도 똑같이):");
            context.Note("  service-state set --" + AdminCliOptions.MAINTENANCE + " " + (maintenance ? G_On[0] : G_Off[0])
                + " --" + AdminCliOptions.DISABLE + "=" + Quote(WriteList(disabled))
                + " --" + AdminCliOptions.ALLOW_BUILDS + "=" + Quote(WriteList(builds))
                + " --" + AdminCliOptions.EXPECTED_REVISION + " " + revision.ToString(CultureInfo.InvariantCulture)
                + " --" + CliArguments.OPTION_OPERATION_ID + " " + operationId
                + " --" + AdminCliOptions.REASON + "=" + Quote(reason));
        }

        private static bool CanWriteList(IReadOnlyList<string> items)
        {
            return (!items.Any(item => (item.Length == 0) || (item.IndexOf(LIST_SEPARATOR) >= 0)
                    || (!string.Equals(item, item.Trim(), StringComparison.Ordinal)) || item.Any(char.IsControl)))
                && (items.Distinct(StringComparer.Ordinal).Count() == items.Count)
                && (!((items.Count == 1) && string.Equals(items[0].Trim(), EMPTY_LIST, StringComparison.OrdinalIgnoreCase)));
        }

        private static string WriteList(IReadOnlyList<string> items)
        {
            return items.Count == 0 ? EMPTY_LIST : string.Join(LIST_SEPARATOR, items);
        }

        // PowerShell의 작은따옴표 문자열은 변수, 명령 구분자, 역슬래시를 원문 그대로 보존한다
        private static string Quote(string value)
        {
            return "'" + value.Replace("'", "''", StringComparison.Ordinal)
                .Replace("\u2018", "\u2018\u2018", StringComparison.Ordinal)
                .Replace("\u2019", "\u2019\u2019", StringComparison.Ordinal)
                .Replace("\u201A", "\u201A\u201A", StringComparison.Ordinal)
                .Replace("\u201B", "\u201B\u201B", StringComparison.Ordinal) + "'";
        }

        private static bool ParseOnOff(string text)
        {
            string trimmed = text.Trim();
            if (G_On.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            if (G_Off.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            throw new CliUsageException("--maintenance는 on 또는 off입니다: " + text);
        }

        private static string[] ParseFeatures(string text, IReadOnlyList<string> known)
        {
            string[] features = SplitList(text, AdminCliOptions.DISABLE);
            foreach (string feature in features)
            {
                if (!known.Contains(feature, StringComparer.Ordinal))
                {
                    throw new CliUsageException("알 수 없는 기능 키입니다(" + string.Join(", ", known) + "): " + feature);
                }
            }

            return features;
        }

        private static string[] ParseBuilds(string text)
        {
            string[] builds = SplitList(text, AdminCliOptions.ALLOW_BUILDS);
            if (builds.Length > AdminServiceStateUpdateRequest.MAX_BUILD_IDS)
            {
                throw new CliUsageException("허용 빌드는 " + AdminServiceStateUpdateRequest.MAX_BUILD_IDS + "개까지입니다.");
            }

            foreach (string build in builds)
            {
                if (build.Length > AdminServiceStateUpdateRequest.MAX_BUILD_ID_LENGTH)
                {
                    throw new CliUsageException("buildId는 " + AdminServiceStateUpdateRequest.MAX_BUILD_ID_LENGTH
                        + "자 이하입니다: " + build);
                }
            }

            return builds;
        }

        // 목록은 통째로 바뀌므로 none으로만 비울 수 있다. 중복은 실수일 가능성이 커서 거절한다
        private static string[] SplitList(string text, string optionName)
        {
            if (string.Equals(text.Trim(), EMPTY_LIST, StringComparison.OrdinalIgnoreCase))
            {
                return Array.Empty<string>();
            }

            string[] items = text.Split(LIST_SEPARATOR).Select(item => item.Trim()).ToArray();
            if (items.Any(item => item.Length == 0))
            {
                throw new CliUsageException("--" + optionName + "에 빈 항목이 있습니다. 목록을 비우려면 none을 쓰세요.");
            }

            if (items.Distinct(StringComparer.Ordinal).Count() != items.Length)
            {
                throw new CliUsageException("--" + optionName + "에 중복 항목이 있습니다: " + text);
            }

            return items;
        }

        private static string OnOff(bool value)
        {
            return value ? "켜짐" : "꺼짐";
        }

        private static string DescribeList(IReadOnlyList<string>? items)
        {
            return ((items == null) || (items.Count == 0)) ? "(없음)" : string.Join(", ", items);
        }

        private static string Change(string before, string after)
        {
            return string.Equals(before, after, StringComparison.Ordinal) ? before + " (그대로)" : before + " -> " + after;
        }
    }
}
