// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.Data.Admin.Grants
{
    /// <summary> 지급 실행에 필요한 역할. 대상 2명 이상의 일괄 지급과 복구 보정 지급(RecoveryCompensation)은 Admin 이상만 실행한다 </summary>
    public static class AdminGrantPolicy
    {
        /****************************************
        *                Fields
        ****************************************/

        // 이 수 이상의 계정에 보내는 지급은 일괄 지급이다
        public const int BATCH_GRANT_MIN_TARGETS = 2;

        /****************************************
        *                Methods
        ****************************************/

        // 보정 지급은 장애 취소 지급과 겹쳐 이중 보상이 되기 쉬워 대상이 한 명이어도 복구 권한(Admin)과 같은 역할을 요구한다
        public static bool RequiresAdminToExecute(int targetCount, string sourceKind)
        {
            return (targetCount >= BATCH_GRANT_MIN_TARGETS)
                || (sourceKind == AdminGrantCodes.SOURCE_RECOVERY_COMPENSATION);
        }
    }
}
