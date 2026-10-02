// System
using System;

namespace Shinshi.Data.Admin
{
    /// <summary> 수동 복구 전후에 읽는 세션 상태. 감사 행의 변경 전후 값으로도 쓴다 </summary>
    public sealed class AdminRecoveryTarget
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid      SessionId             { get; }
        public string    State                 { get; }
        public string?   Result                { get; }
        public Guid      InstanceId            { get; }
        public long?     EndedEventSeq         { get; }
        public Guid?     SettlementOperationId { get; }
        public int       RecoveryFailures      { get; }
        public DateTime? NextRecoveryAt        { get; }
        public long      Revision              { get; }

        /****************************************
        *                Methods
        ****************************************/

        internal AdminRecoveryTarget(AdminSessionRow row)
        {
            SessionId             = row.SessionId;
            State                 = row.State;
            Result                = row.Result;
            InstanceId            = row.InstanceId;
            EndedEventSeq         = row.EndedEventSeq;
            SettlementOperationId = row.SettlementOperationId;
            RecoveryFailures      = row.RecoveryFailures;
            NextRecoveryAt        = row.NextRecoveryAt;
            Revision              = row.Revision;
        }

        /// <summary> 감사 JSON 객체. 세션 상태만 담고 참가자 개인 정보나 토큰은 담지 않는다 </summary>
        public object ToAuditState()
        {
            return new
            {
                State,
                Result,
                InstanceId = InstanceId.ToString("D"),
                EndedEventSeq,
                SettlementOperationId = AdminLogValues.ToText(SettlementOperationId),
                RecoveryFailures,
                NextRecoveryAtUnixMs = AdminLogValues.ToUnixMs(NextRecoveryAt),
                Revision
            };
        }
    }
}
