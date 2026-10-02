// System
using System;

// Third Party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Shinshi.Backend.Inbox
{
    /// <summary> 우편함 요청의 계정별 1분 한도와 본문 크기 상한을 모델 바인딩 전에 적용하는 리소스 필터.
    /// 계정은 앞선 bearer 미들웨어가 정한 플레이어이며, 없으면 통과시켜 액션이 401로 답한다 </summary>
    public sealed class InboxRequestGateFilter : IResourceFilter
    {
        // 액션 안에서 한도를 세면 [FromBody] 바인딩이 먼저 끝나, 한도를 다 쓴 토큰의 요청도 본문 전체를 읽고 역직렬화한다.
        // 리소스 필터는 바인딩 전에 돌아 거절된 요청의 본문을 읽지 않는다

        /****************************************
        *                Fields
        ****************************************/

        // 수령 요청은 GUID 세 개뿐이라 수백 바이트면 충분하다. Content-Length가 이보다 크면 읽지 않고 413이다
        public const int MAX_BODY_BYTES = 4096;

        private readonly InboxRequestLimiter _limiter;
        private readonly TimeProvider _timeProvider;
        private readonly EInboxRequestKind _kind;

        /****************************************
        *                Methods
        ****************************************/

        public InboxRequestGateFilter(InboxRequestLimiter limiter, TimeProvider timeProvider, EInboxRequestKind kind)
        {
            ArgumentNullException.ThrowIfNull(limiter);
            ArgumentNullException.ThrowIfNull(timeProvider);
            _limiter      = limiter;
            _timeProvider = timeProvider;
            _kind         = kind;
        }

        public void OnResourceExecuting(ResourceExecutingContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            HttpContext http = context.HttpContext;
            http.Response.Headers.CacheControl = CacheControlValues.NO_STORE;
            Guid? accountId = AuthActors.GetPlayer(http)?.AccountId;
            if (accountId == null)
            {
                return;
            }

            bool acquired = _kind == EInboxRequestKind.Claim
                ? _limiter.TryAcquireClaim(accountId.Value)
                : _limiter.TryAcquireList(accountId.Value);
            if (!acquired)
            {
                context.Result = AuthFailureLimiter.RejectTooMany(http, _timeProvider);
                return;
            }

            // 청크 전송처럼 길이가 없는 본문은 액션의 RequestSizeLimit이 서버(Kestrel)에서 같은 상한으로 끊는다
            if (http.Request.ContentLength > MAX_BODY_BYTES)
            {
                context.Result = new StatusCodeResult(StatusCodes.Status413PayloadTooLarge);
            }
        }

        public void OnResourceExecuted(ResourceExecutedContext context)
        {
        }
    }
}
