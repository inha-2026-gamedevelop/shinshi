// System
using System.Threading;
using System.Threading.Tasks;

namespace Shinshi.Data.Admin
{
    /// <summary> 어드민 bearer를 활성 세션과 활성 운영자로 확인한다. 게임 토큰은 해시 용도가 달라 찾지 못한다 </summary>
    public interface IAdminSessionValidator
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 만료, 폐기, 비활성 운영자, 다른 용도의 토큰이면 null이다 </summary>
        Task<AdminIdentity?> ValidateAsync(string token, CancellationToken cancellationToken = default);
    }
}
