// System
using System;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.Data.Admin
{
    /// <summary> 복구 대상 목록 행. 세션 열에 배정 서버의 현재 등록 상태와 판정 결과를 더한다 </summary>
    internal sealed class AdminRecoveryRow : AdminSessionRow
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid     ServerInstanceId  { get; set; }
        public Guid?    ServerBootId      { get; set; }
        public string   ServerState       { get; set; } = string.Empty;
        public DateTime ServerHeartbeatAt { get; set; }
        public bool     AssignedBootAlive { get; set; }
        public bool     SettlingStale     { get; set; }
        public bool     InstanceLost      { get; set; }

        /****************************************
        *                Methods
        ****************************************/

        // 판정 보류 중에는 오래된 heartbeat가 DB, API 단절 탓일 수 있어 소실로 표시하지 않는다
        public AdminRecoverySession ToRecoveryContract(int quarantineFailures, bool livenessDeferred)
        {
            AdminServerInstanceState instance = new AdminServerInstanceState(ServerInstanceId.ToString("D"),
                AdminLogValues.ToText(ServerBootId), ServerState, AdminLogValues.ToUnixMs(ServerHeartbeatAt),
                AssignedBootAlive);
            return new AdminRecoverySession(ToContract(), instance, RecoveryFailures >= quarantineFailures, SettlingStale,
                (InstanceLost) && (!livenessDeferred));
        }
    }
}
