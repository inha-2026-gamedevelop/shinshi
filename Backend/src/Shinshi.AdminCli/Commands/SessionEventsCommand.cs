// System
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.AdminCli
{
    /// <summary> 출정 세션 재생. 세션 행, 참가자, 이벤트 전체, 관련 operation과 원장 </summary>
    internal sealed class SessionEventsCommand : ICliCommand
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly string[] G_Usage =
        {
            "session-events <sessionId> --reason <사유>     세션 이벤트, 참가자, 원장 재생"
        };

        /****************************************
        *              Properties
        ****************************************/

        public string Name => "session-events";
        public IReadOnlyList<string> Usage => G_Usage;

        /****************************************
        *                Methods
        ****************************************/

        public async Task<int> RunAsync(CliContext context, CliArguments arguments, CancellationToken cancellationToken)
        {
            arguments.EnsureOnly(1, new[] { AdminCliOptions.REASON }, Array.Empty<string>());
            Guid sessionId = CliFormat.ParseGuid(arguments.Positional(0, "sessionId"), "sessionId");
            CliQuery query = new CliQuery();
            LogOptions.AddReason(arguments, query);
            using AdminApiClient client = context.CreateClient();
            AdminApiResponse<AdminSessionEventsResponse> response = await client.GetAsync<AdminSessionEventsResponse>(
                query.Append(string.Format(AdminApiRoutes.SESSION_EVENTS_FORMAT, sessionId.ToString("D"))),
                cancellationToken);
            if (context.Json)
            {
                context.WriteJson(response.RawBody);
                return CliExitCodes.SUCCESS;
            }

            AdminSessionEventsResponse replay = response.Value;
            context.WriteFields(SessionFields(replay.Session));

            context.Line();
            context.Line("참가자");
            CliTable participants = new CliTable("참가 ID", "계정", "캐릭터", "상태", "정기", "기본 정기", "정산");
            foreach (AdminSessionParticipant participant in replay.Participants ?? Array.Empty<AdminSessionParticipant>())
            {
                participants.Add(participant.ParticipantId, participant.AccountId, participant.CharacterId, participant.State,
                    CliFormat.Number(participant.EssenceAccrued), CliFormat.Number(participant.BaseEssenceAccrued),
                    CliFormat.Time(participant.SettledAtUnixMs));
            }

            context.WriteTable(participants);

            context.Line();
            context.Line("이벤트");
            CliTable events = new CliTable("seq", "종류", "참가 ID", "확정", "operationId", "payload");
            foreach (AdminSessionEvent sessionEvent in replay.Events ?? Array.Empty<AdminSessionEvent>())
            {
                events.Add(CliFormat.Number(sessionEvent.EventSeq), CliFormat.Number(sessionEvent.EventKind),
                    CliFormat.Text(sessionEvent.ParticipantId), CliFormat.Time(sessionEvent.CommittedAtUnixMs),
                    CliFormat.Text(sessionEvent.OperationId), CliFormat.Json(sessionEvent.Payload));
            }

            context.WriteTable(events);

            context.Line();
            context.Line("operation");
            LogOutput.WriteOperations(context, replay.Operations);

            context.Line();
            context.Line("원장");
            LogOutput.WriteLedger(context, replay.Ledger);
            return CliExitCodes.SUCCESS;
        }

        public static List<(string, string)> SessionFields(AdminSessionSummary session)
        {
            return new List<(string, string)>
            {
                ("세션 ID", session.SessionId),
                ("상태", session.State + " / 결과 " + CliFormat.Text(session.Result)),
                ("모드, 맵", session.Mode + ", " + session.MapId),
                ("시작 인원", CliFormat.Number(session.StartingCount)),
                ("서버", CliFormat.Text(session.InstanceId) + " (boot " + CliFormat.Text(session.InstanceBootId) + ")"),
                ("종료 사유", CliFormat.Number(session.EndReason)),
                ("이벤트 seq", CliFormat.Number(session.LastEventSeq) + " (종료 " + CliFormat.Number(session.EndedEventSeq) + ")"),
                ("정산 operationId", CliFormat.Text(session.SettlementOperationId)),
                ("자동 복구 실패", CliFormat.Number(session.RecoveryFailures) + " (다음 " + CliFormat.Time(session.NextRecoveryAtUnixMs)
                    + ")"),
                ("생성, 시작", CliFormat.Time(session.CreatedAtUnixMs) + ", " + CliFormat.Time(session.StartedAtUnixMs)),
                ("정산 시작, 종료", CliFormat.Time(session.SettlingAtUnixMs) + ", " + CliFormat.Time(session.ClosedAtUnixMs))
            };
        }
    }
}
