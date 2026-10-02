// System
using System;

// Third Party
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

// Project
using Shinshi.Data.Admin;

namespace Shinshi.Backend.Admin
{
    /// <summary> 모든 어드민 컨트롤러의 기반. 기본으로 Viewer 이상의 어드민 토큰을 요구하고, 바인딩과 operationId/reason 오류를 400으로 바꾼다.
    /// 더 높은 역할은 컨트롤러나 액션에 RequireAdminRole을 붙인다(낮추는 것은 안 된다) </summary>
    [ApiController]
    [RequireAdminRole(EAdminRole.Viewer)]
    [ServiceFilter(typeof(AdminInvalidModelStateFilter), Order = AdminInvalidModelStateFilter.FILTER_ORDER)]
    [ServiceFilter(typeof(AdminExceptionFilter))]
    public abstract class AdminControllerBase : ControllerBase
    {
        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 미들웨어가 확인한 현재 운영자 </summary>
        protected AdminIdentity CurrentAdmin => AdminActors.Get(HttpContext);

        protected byte[]? RequestIpHash => AdminActors.GetRequestIpHash(HttpContext);

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 공유 ApiRejection 응답. 비밀값이 오갈 수 있는 어드민 응답은 모두 no-store다 </summary>
        protected ObjectResult Reject(int statusCode, int reasonCode)
        {
            Response.Headers.CacheControl = CacheControlValues.NO_STORE;
            return ApiResponseFactory.Reject(statusCode, reasonCode, string.Empty,
                HttpContext.RequestServices.GetRequiredService<TimeProvider>());
        }
    }
}
