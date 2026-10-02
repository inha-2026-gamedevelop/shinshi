// System
using System;

namespace Shinshi.Backend
{
    /// <summary> 게임 서버 서비스 자격으로 발급한 임대 토큰만 허용하는 엔드포인트 표시 </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
    public sealed class RequireServerLeaseAttribute : Attribute
    {
    }
}
