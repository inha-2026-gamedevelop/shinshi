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

        private readonly PostgresReservationRepository _reservations;
        private readonly TimeProvider _timeProvider;

        /****************************************
        *                Methods
        ****************************************/

        public GameSessionReservationsController(IServerCallerAuthenticator callers, PostgresServerPoolRepository pool,
            PostgresReservationRepository reservations, TimeProvider timeProvider)
            : base(callers, pool)
        {
            _reservations = reservations;
            _timeProvider = timeProvider;
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
            ReservationOutcome outcome = await _reservations.ReserveAsync(input, cancellationToken);
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
