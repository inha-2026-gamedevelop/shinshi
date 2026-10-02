// System
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

// Project
using Shinshi.Backend.Admin;
using Shinshi.Contracts;
using Shinshi.Data.Reservations;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Sessions
{
    /// <summary> 마을 서버의 출정 예약, 취소, 입장권과 던전 서버의 접속 임대 이관 </summary>
    [ApiController]
    public sealed class GameSessionReservationsController : GameServerControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        // 점검, 기능 차단 거절은 아무것도 확정하지 않는다
        private const long NO_REVISION = 0;

        private readonly PostgresReservationRepository _reservations;
        private readonly TimeProvider _timeProvider;
        private readonly ServiceFeatureGate _features;
        private readonly ILogger<GameSessionReservationsController> _logger;

        /****************************************
        *                Methods
        ****************************************/

        public GameSessionReservationsController(IServerCallerAuthenticator callers, PostgresServerPoolRepository pool,
            PostgresReservationRepository reservations, TimeProvider timeProvider, ServiceFeatureGate features,
            ILogger<GameSessionReservationsController> logger)
            : base(callers, pool)
        {
            _reservations = reservations;
            _timeProvider = timeProvider;
            _features     = features;
            _logger       = logger;
        }

        [HttpPost(GameServerRouteTemplates.RESERVATIONS)]
        public async Task<IActionResult> Reserve([FromBody] ReservationRequest request, CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.TOWN,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }

            ReservationInput? input = ToInput(caller.InstanceId, request);
            if ((input == null) || !input.IsValid())
            {
                return BadRequest();
            }

            await TouchAsync(caller, cancellationToken);

            // 점검과 기능 차단은 새 예약만 막는다. 이미 예약하거나 시작한 세션의 입장권, 임대 이관, 정산은 그대로 진행한다.
            // 막는 동안에도 같은 operationId 재전송은 저장된 처음 결과를 받는다. 차단 거절 자체는 저장하지 않으므로
            // 한 번도 실행되지 않은 operationId는 차단이 풀린 뒤 다시 보내면 새로 실행된다
            int blocked = await _features.CheckNewSortieAsync(request.Mode, cancellationToken);
            ReservationOutcome? outcome = (blocked == ContractCodes.RejectReason.NONE)
                ? await _reservations.ReserveAsync(input, cancellationToken)
                : await FindStoredWhileBlockedAsync(input, cancellationToken);
            if (outcome == null)
            {
                OperationResult refused = new OperationResult(request.OperationId, ContractCodes.OperationStatus.REJECTED,
                    blocked, NO_REVISION, _timeProvider.GetUtcNow().ToUnixTimeMilliseconds(), string.Empty);
                return new ObjectResult(new ReservationResponse(refused, string.Empty, string.Empty, string.Empty, 0, 0,
                    Array.Empty<ReservedParticipant>())) { StatusCode = StatusOf(refused) };
            }

            ReservedSessionView? session = outcome.Session;
            ReservationResponse body = new ReservationResponse(outcome.Result,
                session?.SessionId.ToString("D") ?? string.Empty,
                session?.InstanceId.ToString("D") ?? string.Empty,
                session?.Address ?? string.Empty,
                session?.Port ?? 0,
                session?.ExpiresAt.ToUnixTimeMilliseconds() ?? 0,
                session?.Participants.Select(participant => new ReservedParticipant(
                    participant.ParticipantId.ToString("D"), participant.AccountId.ToString("D"),
                    participant.CharacterId.ToString("D"))).ToArray() ?? Array.Empty<ReservedParticipant>());
            return new ObjectResult(body) { StatusCode = StatusOf(outcome.Result) };
        }

        [HttpPost(GameServerRouteTemplates.RESERVATION_CANCEL)]
        public async Task<IActionResult> Cancel(string sessionId, [FromBody] ReservationCancelRequest request,
            CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.TOWN,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }
            if (!TryParseId(sessionId, out Guid session) || !TryParseId(request.OperationId, out Guid operationId))
            {
                return BadRequest();
            }

            OperationResult result = await _reservations.CancelAsync(caller.InstanceId, session, operationId,
                cancellationToken);
            return ApiResponseFactory.FromOperation(result);
        }

        [HttpPost(GameServerRouteTemplates.JOIN_TICKETS)]
        public async Task<IActionResult> IssueJoinTickets(string sessionId, CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.TOWN,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }
            if (!TryParseId(sessionId, out Guid session))
            {
                return BadRequest();
            }

            await TouchAsync(caller, cancellationToken);
            JoinTicketsOutcome outcome = await _reservations.IssueJoinTicketsAsync(caller.InstanceId, session,
                cancellationToken);
            JoinTicketsResponse body = new JoinTicketsResponse(session.ToString("D"), outcome.Tickets.ToArray(),
                outcome.ReasonCode, _timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
            return new ObjectResult(body)
            {
                StatusCode = outcome.ReasonCode == ContractCodes.RejectReason.NONE
                    ? StatusCodes.Status200OK : StatusCodes.Status409Conflict
            };
        }

        [HttpPost(GameServerRouteTemplates.LEASE_TRANSFER)]
        public async Task<IActionResult> TransferLease(string sessionId, [FromBody] LeaseTransferRequest request,
            CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.DUNGEON,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }
            if (!TryParseId(sessionId, out Guid session) || !TryParseId(request.ParticipantId, out Guid participantId)
                || !TryParseId(request.NewLeaseId, out Guid newLeaseId))
            {
                return BadRequest();
            }

            await TouchAsync(caller, cancellationToken);
            LeaseTransferOutcome outcome = await _reservations.TransferParticipantLeaseAsync(caller.InstanceId, session,
                participantId, newLeaseId, cancellationToken);
            if (outcome.IsRetryable)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            bool transferred = outcome.ReasonCode == ContractCodes.RejectReason.NONE;
            LeaseTransferResponse body = new LeaseTransferResponse(participantId.ToString("D"),
                transferred ? outcome.LeaseId.ToString("D") : string.Empty,
                transferred ? outcome.ExpiresAt.ToUnixTimeMilliseconds() : 0, outcome.ReasonCode,
                _timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
            return new ObjectResult(body)
            {
                StatusCode = transferred ? StatusCodes.Status200OK : StatusCodes.Status409Conflict
            };
        }

        // 차단 판정은 이미 끝났으므로 저장 결과를 못 읽으면 차단 거절로 답한다. 효과는 없고 같은 ID로 다시 보내면 된다
        private async Task<ReservationOutcome?> FindStoredWhileBlockedAsync(ReservationInput input,
            CancellationToken cancellationToken)
        {
            try
            {
                return await _reservations.FindStoredAsync(input, cancellationToken);
            }
            catch (DbException exception)
            {
                // 예외 메시지에 연결 정보가 섞일 수 있어 형식 이름만 남긴다
                _logger.LogWarning("Stored reservation lookup failed while sorties are blocked ({ExceptionType}).",
                    exception.GetType().Name);
                return null;
            }
        }

        private static ReservationInput? ToInput(Guid townInstanceId, ReservationRequest request)
        {
            if ((request.Participants == null) || !Guid.TryParse(request.OperationId, out Guid operationId))
            {
                return null;
            }

            List<ParticipantReservationInput> participants = new List<ParticipantReservationInput>();
            foreach (ReservationParticipant participant in request.Participants)
            {
                if ((participant == null) || !Guid.TryParse(participant.AccountId, out Guid accountId)
                    || !Guid.TryParse(participant.CharacterId, out Guid characterId))
                {
                    return null;
                }

                List<PotionReservationInput> potions = (participant.Potions ?? Array.Empty<ReservationPotion>())
                    .Where(potion => potion != null)
                    .Select(potion => new PotionReservationInput(potion.DefinitionId, potion.BindingKind, potion.Quantity))
                    .ToList();
                participants.Add(new ParticipantReservationInput(accountId, characterId, participant.CharacterRevision,
                    potions));
            }

            return new ReservationInput(townInstanceId, operationId, request.Mode, request.MapId, request.ContentVersion,
                participants);
        }
    }
}
