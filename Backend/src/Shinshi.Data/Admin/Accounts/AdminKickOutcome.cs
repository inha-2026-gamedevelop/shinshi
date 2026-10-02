// System
using System;

namespace Shinshi.Data.Admin
{
    /// <summary> 강제 종료 결과. 계정이 없으면 Revoked가 null이다 </summary>
    public sealed class AdminKickOutcome
    {
        /****************************************
        *              Properties
        ****************************************/

        public AdminRevokedTokens? Revoked { get; }

        // 그 시각에 살아 있던 계정 임대의 게임 서버. 강제 종료는 이 임대를 끊지 않는다
        public Guid? ConnectedInstanceId { get; }

        public bool IsFound => Revoked != null;

        /****************************************
        *                Methods
        ****************************************/

        private AdminKickOutcome(AdminRevokedTokens? revoked, Guid? connectedInstanceId)
        {
            Revoked             = revoked;
            ConnectedInstanceId = connectedInstanceId;
        }

        public static AdminKickOutcome NotFound() { return new AdminKickOutcome(null, null); }

        public static AdminKickOutcome Kicked(AdminRevokedTokens revoked, Guid? connectedInstanceId)
        {
            ArgumentNullException.ThrowIfNull(revoked);
            return new AdminKickOutcome(revoked, connectedInstanceId);
        }
    }
}
