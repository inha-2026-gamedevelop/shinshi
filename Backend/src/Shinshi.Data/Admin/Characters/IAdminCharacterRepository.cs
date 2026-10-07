// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.Data.Admin
{
    /// <summary> 읽기 전용 캐릭터 프로필 스냅샷. 감사는 호출자가 조회 전에 확정한다 </summary>
    public interface IAdminCharacterRepository
    {
        /****************************************
        *                Methods
        ****************************************/

        Task<AdminCharacterDetailResponse?> ReadDetailAsync(Guid characterId,
            CancellationToken cancellationToken = default);
    }
}

