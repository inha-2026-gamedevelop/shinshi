// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Sessions;
using Shinshi.Data.Settlement;

namespace Shinshi.Backend.Sessions
{
    /// <summary> 배정 던전 서버의 시작, 순번 이벤트, 체크포인트, 정산. 결과와 수량은 서버가 정하고 API는 경제 조건만 검증한다 </summary>
    [ApiController]
    public sealed class GameSessionsController : GameServerControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly PostgresGameSessionRepository _sessions;
        private readonly PostgresSettlementRepository _settlement;
        private readonly TimeProvider _timeProvider;

        /****************************************
        *                Methods
        ****************************************/

        public GameSessionsController(IServerCallerAuthenticator callers, PostgresServerPoolRepository pool,
            PostgresGameSessionRepository sessions, PostgresSettlementRepository settlement, TimeProvider timeProvider)
            : base(callers, pool)
        {
            _sessions     = sessions;
            _settlement   = settlement;
            _timeProvider = timeProvider;
        }

        [HttpPost(GameServerRouteTemplates.START)]
        public async Task<IActionResult> Start(string sessionId, [FromBody] SessionStartRequest request,
            CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.DUNGEON,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }
            if (!TryParseId(sessionId, out Guid session) || !TryParseId(request.OperationId, out Guid operationId)
                || (request.ReadyParticipantIds == null))
            {
                return BadRequest();
            }

            List<Guid> ready = new List<Guid>(request.ReadyParticipantIds.Length);
            foreach (string value in request.ReadyParticipantIds)
            {
                if (!TryParseId(value, out Guid participantId))
                {
                    return BadRequest();
                }
                ready.Add(participantId);
            }

            await TouchAsync(caller, cancellationToken);
            OperationResult result = await _sessions.StartAsync(caller.InstanceId, session, operationId, ready,
                cancellationToken);
            return ApiResponseFactory.FromOperation(result);
        }

        [HttpPost(GameServerRouteTemplates.EVENTS)]
        public async Task<IActionResult> AppendEvents(string sessionId, [FromBody] SessionEventBatchRequest request,
            CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.DUNGEON,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }
            if (!TryParseId(sessionId, out Guid session) || !TryParseId(request.OperationId, out Guid operationId))
            {
                return BadRequest();
            }

            List<SessionEventInput>? events = ToEvents(request.Events);
            if (events == null)
            {
                return BadRequest();
            }

            int reason = SessionEventValidator.GetRejectReason(events);
            if (reason == ContractCodes.RejectReason.SETTLEMENT_LIMIT_EXCEEDED)
            {
                return ApiResponseFactory.Reject(StatusCodes.Status400BadRequest, reason, string.Empty, _timeProvider);
            }
            if (reason != ContractCodes.RejectReason.NONE)
            {
                return BadRequest();
            }

            await TouchAsync(caller, cancellationToken);
            OperationResult result = await _sessions.AppendEventsAsync(caller.InstanceId, session, operationId, events,
                cancellationToken);
            return ApiResponseFactory.FromOperation(result);
        }

        [HttpPost(GameServerRouteTemplates.CHECKPOINTS)]
        public async Task<IActionResult> SaveCheckpoint(string sessionId, [FromBody] SessionCheckpointRequest request,
            CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.DUNGEON,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }
            if (!TryParseId(sessionId, out Guid session) || !TryDecode(request.StateBase64, out byte[] state))
            {
                return BadRequest();
            }

            CheckpointInput input = new CheckpointInput(request.CheckpointSeq, request.LastEventSeq, request.SchemaVersion,
                state);
            if (!input.IsValid())
            {
                return BadRequest();
            }

            ECheckpointStatus status = await _sessions.SaveCheckpointAsync(caller.InstanceId, session, input,
                cancellationToken);
            int reason = status switch
            {
                ECheckpointStatus.Saved         => ContractCodes.RejectReason.NONE,
                ECheckpointStatus.Duplicate     => ContractCodes.RejectReason.NONE,
                ECheckpointStatus.WrongPhase    => ContractCodes.RejectReason.SESSION_TERMINAL,
                ECheckpointStatus.AheadOfEvents => ContractCodes.RejectReason.STALE_REVISION,
                _                               => ContractCodes.RejectReason.SESSION_MISMATCH
            };
            if (reason == ContractCodes.RejectReason.NONE)
            {
                return NoContent();
            }

            return ApiResponseFactory.Reject(StatusCodes.Status409Conflict, reason, string.Empty, _timeProvider);
        }

        [HttpPost(GameServerRouteTemplates.SETTLEMENT)]
        public async Task<IActionResult> Settle(string sessionId, [FromBody] SessionSettlementRequest request,
            CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.DUNGEON,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }
            if (!TryParseId(sessionId, out Guid session) || !TryParseId(request.OperationId, out Guid operationId))
            {
                return BadRequest();
            }

            await TouchAsync(caller, cancellationToken);
            OperationResult result = await _settlement.SettleAsync(caller.InstanceId, session, operationId,
                cancellationToken);
            return ApiResponseFactory.FromOperation(result);
        }

        private static List<SessionEventInput>? ToEvents(SessionEventEntry[]? entries)
        {
            if (entries == null)
            {
                return null;
            }

            List<SessionEventInput> events = new List<SessionEventInput>(entries.Length);
            foreach (SessionEventEntry entry in entries)
            {
                if ((entry == null) || !TryParseId(entry.EventId, out Guid eventId))
                {
                    return null;
                }

                SessionEventInput input = new SessionEventInput
                {
                    EventSeq = entry.EventSeq,
                    EventId = eventId,
                    EventKind = entry.EventKind,
                    DefinitionId = entry.DefinitionId ?? string.Empty,
                    BindingKind = entry.BindingKind ?? string.Empty,
                    Amount = entry.Amount,
                    EndReason = entry.EndReason,
                    PayloadJson = entry.PayloadJson ?? string.Empty
                };
                if (!string.IsNullOrEmpty(entry.ParticipantId))
                {
                    if (!TryParseId(entry.ParticipantId, out Guid participantId))
                    {
                        return null;
                    }
                    input.ParticipantId = participantId;
                }
                if (!string.IsNullOrEmpty(entry.SettlementOperationId))
                {
                    if (!TryParseId(entry.SettlementOperationId, out Guid settlementOperationId))
                    {
                        return null;
                    }
                    input.SettlementOperationId = settlementOperationId;
                }
                if (!string.IsNullOrEmpty(entry.RevertsEventId))
                {
                    if (!TryParseId(entry.RevertsEventId, out Guid revertsEventId))
                    {
                        return null;
                    }
                    input.RevertsEventId = revertsEventId;
                }

                List<SettlementGrantInput>? grants = ToGrants(entry.Grants);
                if (grants == null)
                {
                    return null;
                }
                input.Grants = grants;
                events.Add(input);
            }

            return events;
        }

        private static List<SettlementGrantInput>? ToGrants(SettlementGrant[]? grants)
        {
            List<SettlementGrantInput> result = new List<SettlementGrantInput>();
            foreach (SettlementGrant grant in grants ?? Array.Empty<SettlementGrant>())
            {
                if ((grant == null) || !TryParseId(grant.ParticipantId, out Guid participantId))
                {
                    return null;
                }

                // 지갑, 부적 칸이 없는 이전 서버의 정산안은 0과 빈 목록으로 읽힌다
                result.Add(new SettlementGrantInput
                {
                    ParticipantId = participantId,
                    Essence = grant.Essence,
                    Items = ToItemInputs(grant.Items),
                    RefineShards = grant.RefineShards,
                    Tickets = grant.Tickets,
                    Currency = grant.Currency,
                    Talismans = ToItemInputs(grant.Talismans)
                });
            }

            return result;
        }

        private static List<SettlementItemInput> ToItemInputs(SettlementItemGrant[]? items)
        {
            return (items ?? Array.Empty<SettlementItemGrant>())
                .Where(item => item != null)
                .Select(item => new SettlementItemInput
                {
                    DefinitionId = item.DefinitionId ?? string.Empty,
                    BindingKind = item.BindingKind ?? string.Empty,
                    Quantity = item.Quantity
                }).ToList();
        }

        private static bool TryDecode(string? value, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            // 형식 오류는 요청 거절로 처리하고 원문은 로그에 남기지 않는다
            try
            {
                bytes = Convert.FromBase64String(value);
            }
            catch (FormatException)
            {
                return false;
            }

            return true;
        }
    }
}
