// System
using System;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 마을 계정 진행 메시지의 Mirror 직렬화, Weaver가 이 메서드를 쓴다 </summary>
    public static class ProgressMessageSerializer
    {
        // 메시지는 private readonly 필드라 Weaver 자동 직렬화가 내용을 쓰지 못한다. 여기서 순서를 고정한다

        /****************************************
        *                Methods
        ****************************************/

        public static void WriteIntroCompleteRequestMessage(this NetworkWriter writer, IntroCompleteRequestMessage value)
        {
            writer.WriteGuid(value.OperationId);
        }

        public static IntroCompleteRequestMessage ReadIntroCompleteRequestMessage(this NetworkReader reader)
        {
            return new IntroCompleteRequestMessage(reader.ReadGuid());
        }

        public static void WriteIntroCompleteResultMessage(this NetworkWriter writer, IntroCompleteResultMessage value)
        {
            writer.WriteGuid(value.OperationId);
            writer.WriteVarUInt((uint)value.Reason);
        }

        public static IntroCompleteResultMessage ReadIntroCompleteResultMessage(this NetworkReader reader)
        {
            Guid operationId = reader.ReadGuid();
            var  reason      = (ERejectReason)reader.ReadVarUInt();
            return new IntroCompleteResultMessage(operationId, reason);
        }
    }
}
