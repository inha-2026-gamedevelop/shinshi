// Project
using Shinshi.Contracts;

namespace Shinshi.Launcher.Account
{
    /// <summary> 런처가 따로 다루는 인증 거절 코드 </summary>
    public static class AuthRejectCodes
    {
        /****************************************
        *                Fields
        ****************************************/

        // 아이디, 비밀번호 로그인의 거절 코드. 계정 설계에서 값을 정했지만 공유 계약에 아직 없어 여기 임시로 둔다
        // TODO(민성): ContractCodes.RejectReason에 INVALID_CREDENTIALS, ACCOUNT_LOCKED가 들어가면 그 상수로 바꾸고 이 둘을 지운다
        public const int INVALID_CREDENTIALS = 1205;
        public const int ACCOUNT_LOCKED      = 1206;

        public const int RATE_LIMITED = ContractCodes.RejectReason.RATE_LIMITED;
    }
}
