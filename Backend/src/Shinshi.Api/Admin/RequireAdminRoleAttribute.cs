// System
using System;

// Project
using Shinshi.Data.Admin;

namespace Shinshi.Backend.Admin
{
    /// <summary> 어드민 접근 토큰과 최소 역할을 요구한다. 컨트롤러와 액션에 함께 붙으면 더 높은 역할을 요구한다 </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class RequireAdminRoleAttribute : Attribute, IAdminEndpointMetadata
    {
        /****************************************
        *              Properties
        ****************************************/

        public EAdminRole Role { get; }

        /****************************************
        *                Methods
        ****************************************/

        public RequireAdminRoleAttribute(EAdminRole role)
        {
            if (!Enum.IsDefined(role))
            {
                throw new ArgumentOutOfRangeException(nameof(role));
            }

            Role = role;
        }
    }
}
