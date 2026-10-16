// System
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

// Project
using Shinshi.Backend.Admin;
using Shinshi.Backend.Sessions;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data.Sessions;
using Shinshi.Data.Social;

namespace Shinshi.Backend.Social
{
    /// <summary> 소셜 경로 공통. 판정 순서는 서버 자격(401, 403) -> 형식(400) -> 기능 차단(409 FEATURE_DISABLED) -> 캐릭터별 한도(429) -> 저장소다.
    /// 상태 기준 멱등이라 operations를 쓰지 않으므로 기능 차단 거절에 재생할 저장 결과가 없다.
    /// 소셜은 호출 서버의 생존 판정을 하지 않으므로 heartbeat Touch를 하지 않는다(채팅, 꺼내기가 인스턴스 행 갱신을 늘리지 않게) </summary>
    [ApiController]
    public abstract class SocialControllerBase : GameServerControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly TimeProvider _timeProvider;
        private readonly ServiceFeatureGate? _features;

        /****************************************
        *              Properties
        ****************************************/

        protected PostgresSocialRepository Social { get; }
        protected SocialRequestLimiter Limiter { get; }

        /****************************************
        *                Methods
        ****************************************/

        protected SocialControllerBase(IServerCallerAuthenticator callers, PostgresServerPoolRepository pool,
            PostgresSocialRepository social, SocialRequestLimiter limiter, TimeProvider timeProvider,
            ServiceFeatureGate? features)
            : base(callers, pool)
        {
            ArgumentNullException.ThrowIfNull(social);
            ArgumentNullException.ThrowIfNull(limiter);
            ArgumentNullException.ThrowIfNull(timeProvider);
            Social        = social;
            Limiter       = limiter;
            _timeProvider = timeProvider;
            _features     = features;
        }

        protected static SocialCaller ToCaller(ServerCaller caller)
        {
            return new SocialCaller(caller.InstanceId, caller.Role);
        }

        protected long NowUnixMs()
        {
            return _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        }

        /// <summary> social 기능이 꺼져 있으면 true </summary>
        protected async Task<bool> IsBlockedAsync(CancellationToken cancellationToken)
        {
            return (_features != null)
                && (await _features.CheckFeatureAsync(ServiceFeatureKeys.SOCIAL, cancellationToken)
                    != ContractCodes.RejectReason.NONE);
        }

        protected SocialActionResponse ToResponse(int reasonCode, IReadOnlyList<Guid>? changed = null)
        {
            bool committed = reasonCode == ContractCodes.RejectReason.NONE;
            return new SocialActionResponse(
                committed ? ContractCodes.OperationStatus.COMMITTED : ContractCodes.OperationStatus.REJECTED, reasonCode,
                NowUnixMs(), (changed ?? Array.Empty<Guid>()).Select(id => id.ToString("D")).ToArray());
        }

        /// <summary> 200 확정, 409 업무 거절 </summary>
        protected static ObjectResult Respond(object body, int reasonCode)
        {
            return new ObjectResult(body)
            {
                StatusCode = reasonCode == ContractCodes.RejectReason.NONE
                    ? StatusCodes.Status200OK
                    : StatusCodes.Status409Conflict
            };
        }

        protected IActionResult TooMany(int retryAfterSeconds)
        {
            Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            return ApiResponseFactory.Reject(StatusCodes.Status429TooManyRequests, ContractCodes.RejectReason.RATE_LIMITED,
                string.Empty, _timeProvider);
        }

        protected IActionResult Rejection(int statusCode, int reasonCode)
        {
            return ApiResponseFactory.Reject(statusCode, reasonCode, string.Empty, _timeProvider);
        }

        protected static SocialDeliveryRow[] ToRows(IEnumerable<SocialDeliveryRecord> records)
        {
            return records.Select(record => new SocialDeliveryRow(record.DeliveryId, record.TargetCharacterId.ToString("D"),
                record.Kind, record.Channel, record.SenderCharacterId?.ToString("D") ?? string.Empty, record.SenderName,
                record.Payload, new DateTimeOffset(record.CreatedAt).ToUnixTimeMilliseconds())).ToArray();
        }
    }
}
