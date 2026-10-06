// System
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Mvc;

// Project
using Shinshi.Contracts.Admin;
using Shinshi.Data.Admin;

namespace Shinshi.Backend.Admin
{
    /// <summary> 경매 단계 출정 목록(Viewer). 멈춘 경매를 확인만 하고, 닫기는 유지 작업의 자동 복구가 한다 </summary>
    public sealed class AdminAuctionsController : AdminControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly PostgresAdminAuctionReader _auctions;
        private readonly AdminSessionRecoveryService _recovery;

        /****************************************
        *                Methods
        ****************************************/

        public AdminAuctionsController(PostgresAdminAuctionReader auctions, AdminSessionRecoveryService recovery)
        {
            _auctions = auctions;
            _recovery = recovery;
        }

        /// <summary> state가 없으면 멈춘 경매, Open이나 Closing이면 그 상태의 진행 중 품목이 있는 출정. 다른 state는 400이다 </summary>
        [HttpGet(AdminApiRoutes.AUCTIONS)]
        public async Task<ActionResult<AdminAuctionPage>> List(
            [FromQuery(Name = AdminApiRoutes.STATE_PARAMETER)] string? state,
            [FromQuery(Name = AdminLogQueryParameters.CURSOR)] string? cursor,
            [FromQuery(Name = AdminLogQueryParameters.LIMIT)] int? limit,
            CancellationToken cancellationToken)
        {
            if ((state != null) && (!PostgresAdminAuctionReader.ItemStates.Contains(state, StringComparer.Ordinal)))
            {
                throw new AdminRequestException("state must be Open or Closing.");
            }

            AdminAuctionPage page = await _auctions.ListAsync(state, AdminLogRequests.ReadCursor(cursor),
                AdminLogRequests.ReadLimit(limit), _recovery.IsLivenessDeferred, cancellationToken);
            Response.Headers.CacheControl = CacheControlValues.NO_STORE;
            return Ok(page);
        }
    }
}
