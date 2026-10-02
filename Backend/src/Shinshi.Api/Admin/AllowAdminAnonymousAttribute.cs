// System
using System;

namespace Shinshi.Backend.Admin
{
    /// <summary> 어드민 토큰 없이 부르는 어드민 엔드포인트(로그인). 허용 네트워크 검사는 그대로 받는다 </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class AllowAdminAnonymousAttribute : Attribute, IAdminEndpointMetadata
    {
    }
}
