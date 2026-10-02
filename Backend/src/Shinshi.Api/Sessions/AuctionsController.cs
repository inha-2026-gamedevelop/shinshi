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
using Shinshi.Data.Auctions;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Sessions
{
    // 접속 여부와 마감 연장 판정은 던전 서버 몫이다. API는 배정 던전의 같은 boot인지, 출정 참가자인지, 경제 조건을 검증한다.
    // 응답은 공통 작업 결과다. committedRevision은 개시 0, 입찰 새 입찰 순번(다음 expectedBidSeq), 마감 품목 revision,
    // finish 출정 revision이다
    /// <summary> 보스 성공 출정의 파티 경매 개시, 입찰, 마감, 경매 단계 종료 </summary>
    [ApiController]
    public sealed class AuctionsController : GameServerControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        // DateTimeOffset으로 바꿀 수 있는 가장 큰 유닉스 ms. 넘으면 변환에서 예외가 나므로 형식 오류로 거절한다
        private static readonly long MAX_UNIX_MS = DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();

        private readonly PostgresPartyAuctionRepository _auctions;

        /****************************************
        *                Methods
        ****************************************/

        public AuctionsController(IServerCallerAuthenticator callers, PostgresServerPoolRepository pool,
            PostgresPartyAuctionRepository auctions)
            : base(callers, pool)
        {
            _auctions = auctions;
        }

        [HttpPost(GameServerRouteTemplates.AUCTIONS)]
        public async Task<IActionResult> Open(string sessionId, [FromBody] AuctionOpenRequest request,
            CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.DUNGEON,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }
            if (!TryParseId(sessionId, out Guid session) || !TryParseId(request.OperationId, out Guid operationId)
                || !TryParseId(request.AuctionId, out Guid auctionId)
                || !SettlementSanityLimits.IsAllowedItem(request.DefinitionId)
                || !TryParseIds(request.EligibleParticipantIds, 1, PartyAuctionRules.MAX_ELIGIBLE, out List<Guid> eligible))
            {
                return BadRequest();
            }

            await TouchAsync(caller, cancellationToken);
            OperationResult result = await _auctions.OpenAsync(caller.InstanceId, caller.BootId, new AuctionOpenInput
            {
                SessionId = session, OperationId = operationId, AuctionId = auctionId,
                DefinitionId = request.DefinitionId, EligibleParticipantIds = eligible
            }, cancellationToken);
            return ApiResponseFactory.FromOperation(result);
        }

        [HttpPost(GameServerRouteTemplates.AUCTION_BIDS)]
        public async Task<IActionResult> Bid(string sessionId, string auctionId, [FromBody] AuctionBidRequest request,
            CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.DUNGEON,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }
            if (!TryParseId(sessionId, out Guid session) || !TryParseId(auctionId, out Guid auction)
                || !TryParseId(request.OperationId, out Guid operationId)
                || !TryParseId(request.ParticipantId, out Guid participantId)
                || (request.Amount <= 0) || (request.ExpectedBidSeq < 0) || !IsUnixMs(request.AcceptedAtUnixMs))
            {
                return BadRequest();
            }

            await TouchAsync(caller, cancellationToken);
            OperationResult result = await _auctions.BidAsync(caller.InstanceId, caller.BootId, new AuctionBidInput
            {
                SessionId = session, AuctionId = auction, OperationId = operationId, ParticipantId = participantId,
                Amount = request.Amount, ExpectedBidSeq = request.ExpectedBidSeq,
                AcceptedAtUnixMs = request.AcceptedAtUnixMs
            }, cancellationToken);
            return ApiResponseFactory.FromOperation(result);
        }

        [HttpPost(GameServerRouteTemplates.AUCTION_CLOSE)]
        public async Task<IActionResult> Close(string sessionId, string auctionId, [FromBody] AuctionCloseRequest request,
            CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.DUNGEON,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }

            // 마감 순간 접속자가 0명이어도 낙찰과 소각은 진행되므로 빈 접속 집합을 받는다
            if (!TryParseId(sessionId, out Guid session) || !TryParseId(auctionId, out Guid auction)
                || !TryParseId(request.OperationId, out Guid operationId) || !IsUnixMs(request.EndedAtUnixMs)
                || !TryParseIds(request.ConnectedParticipantIds, 0, PartyAuctionRules.MAX_ELIGIBLE, out List<Guid> connected))
            {
                return BadRequest();
            }

            await TouchAsync(caller, cancellationToken);
            OperationResult result = await _auctions.CloseAsync(caller.InstanceId, caller.BootId, new AuctionCloseInput
            {
                SessionId = session, AuctionId = auction, OperationId = operationId,
                ConnectedParticipantIds = connected, EndedAtUnixMs = request.EndedAtUnixMs
            }, cancellationToken);
            return ApiResponseFactory.FromOperation(result);
        }

        [HttpPost(GameServerRouteTemplates.AUCTION_FINISH)]
        public async Task<IActionResult> Finish(string sessionId, [FromBody] AuctionFinishRequest request,
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
            OperationResult result = await _auctions.FinishAsync(caller.InstanceId, caller.BootId, session, operationId,
                cancellationToken);
            return ApiResponseFactory.FromOperation(result);
        }

        private static bool IsUnixMs(long value)
        {
            return (value > 0) && (value <= MAX_UNIX_MS);
        }

        // 중복 ID는 형식 오류다. 같은 참가자를 두 번 넣어 분배 인원을 부풀리지 못하게 한다
        private static bool TryParseIds(string[]? values, int minCount, int maxCount, out List<Guid> ids)
        {
            ids = new List<Guid>();
            if ((values == null) || (values.Length < minCount) || (values.Length > maxCount))
            {
                return false;
            }

            foreach (string value in values)
            {
                if (!TryParseId(value, out Guid id))
                {
                    return false;
                }
                ids.Add(id);
            }

            return ids.Distinct().Count() == ids.Count;
        }
    }
}
