// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.AspNetCore.Mvc;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Reservations;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Sessions
{
    /// <summary> 마을 서버의 출정 준비 확인. 읽기 전용이라 작업 행과 원장을 남기지 않는다. 던전 서버와 플레이어 토큰은 쓸 수 없다 </summary>
    [ApiController]
    public sealed class SortieReadinessController : GameServerControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly PostgresReadinessRepository _readiness;
        private readonly TimeProvider _timeProvider;

        /****************************************
        *                Methods
        ****************************************/

        public SortieReadinessController(IServerCallerAuthenticator callers, PostgresServerPoolRepository pool,
            PostgresReadinessRepository readiness, TimeProvider timeProvider)
            : base(callers, pool)
        {
            _readiness    = readiness;
            _timeProvider = timeProvider;
        }

        /// <summary> 200과 ReadinessResponse. 서버 자격이 없으면 401, 마을이 아니면 403, 형식 오류는 400이다 </summary>
        [HttpPost(GameServerRouteTemplates.READINESS)]
        public async Task<IActionResult> Read([FromBody] ReadinessRequest? request, CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.TOWN,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }

            List<(Guid AccountId, Guid CharacterId)>? participants = ToParticipants(request);
            if ((participants == null) || !PostgresReadinessRepository.IsValid(participants))
            {
                return BadRequest();
            }

            Response.Headers.CacheControl = CacheControlValues.NO_STORE;
            IReadOnlyList<ReadinessParticipantStatus> statuses = await _readiness.ReadAsync(caller.InstanceId,
                participants, cancellationToken);
            return Ok(new ReadinessResponse(statuses.ToArray(), _timeProvider.GetUtcNow().ToUnixTimeMilliseconds()));
        }

        private static List<(Guid AccountId, Guid CharacterId)>? ToParticipants(ReadinessRequest? request)
        {
            if ((request == null) || (request.Participants == null))
            {
                return null;
            }

            List<(Guid AccountId, Guid CharacterId)> participants = new List<(Guid, Guid)>(request.Participants.Length);
            foreach (ReadinessParticipant participant in request.Participants)
            {
                if ((participant == null) || !TryParseId(participant.AccountId, out Guid accountId)
                    || !TryParseId(participant.CharacterId, out Guid characterId))
                {
                    return null;
                }
                participants.Add((accountId, characterId));
            }

            return participants;
        }
    }
}
