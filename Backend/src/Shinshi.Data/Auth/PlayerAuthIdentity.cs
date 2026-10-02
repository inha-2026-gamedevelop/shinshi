// System
using System;

namespace Shinshi.Data.Auth
{
    /// <summary> 저장된 접근 토큰의 계정과 갱신 토큰 묶음 </summary>
    public sealed class PlayerAuthIdentity
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid AccountId { get; }
        public Guid FamilyId { get; }
        public Guid RefreshTokenId { get; }

        /****************************************
        *                Methods
        ****************************************/

        public PlayerAuthIdentity(Guid accountId, Guid familyId, Guid refreshTokenId)
        {
            AccountId      = accountId;
            FamilyId       = familyId;
            RefreshTokenId = refreshTokenId;
        }
    }
}
