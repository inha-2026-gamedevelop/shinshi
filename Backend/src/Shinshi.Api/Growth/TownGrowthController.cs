// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

// Project
using Shinshi.Backend.Sessions;
using Shinshi.Contracts;
using Shinshi.Data.Growth;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Growth
{
    /// <summary> 마을 서버의 캐릭터 성장 상태 조회. 그 캐릭터 계정이 이 마을에 접속해 있을 때만 읽는다 </summary>
    [ApiController]
    public sealed class TownGrowthController : GameServerControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly PostgresGrowthReader _reader;
        private readonly TimeProvider _timeProvider;

        /****************************************
        *                Methods
        ****************************************/

        public TownGrowthController(IServerCallerAuthenticator callers, PostgresServerPoolRepository pool,
            PostgresGrowthReader reader, TimeProvider timeProvider)
            : base(callers, pool)
        {
            _reader       = reader;
            _timeProvider = timeProvider;
        }

        /// <summary> 200 CharacterGrowthSnapshot. 캐릭터가 없으면 404, 계정 접속 임대가 이 마을에 없으면 409 WRONG_LOCATION이다 </summary>
        [HttpGet(GrowthRouteTemplates.GROWTH)]
        public async Task<IActionResult> Get(string characterId, CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.TOWN,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }
            if (!TryParseId(characterId, out Guid character))
            {
                return BadRequest();
            }

            Response.Headers.CacheControl = CacheControlValues.NO_STORE;
            EGrowthAccess access = await _reader.CheckTownAccessAsync(character, caller.InstanceId, cancellationToken);
            if (access == EGrowthAccess.NotFound)
            {
                return NotFound();
            }
            if (access == EGrowthAccess.WrongLocation)
            {
                return ApiResponseFactory.Reject(StatusCodes.Status409Conflict, ContractCodes.RejectReason.WRONG_LOCATION,
                    string.Empty, _timeProvider);
            }

            CharacterGrowthSnapshot? growth = await _reader.ReadAsync(character, cancellationToken);
            return growth == null ? NotFound() : Ok(growth);
        }
    }
}
