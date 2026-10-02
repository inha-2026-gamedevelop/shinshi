// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 인증 메시지의 Mirror 직렬화, Weaver가 이 메서드를 쓴다 </summary>
    public static class JoinMessageSerializer
    {
        // 메시지는 private readonly 필드라 Weaver 자동 직렬화가 내용을 쓰지 못한다. 여기서 순서를 고정한다.
        // 연결마다 한 번 오가는 메시지라 문자열을 그대로 보낸다. 길이는 서버가 읽은 뒤 상한으로 자른다

        /****************************************
        *                Methods
        ****************************************/

        public static void WriteJoinRequestMessage(this NetworkWriter writer, JoinRequestMessage value)
        {
            writer.WriteVarUInt((uint)value.ProtocolVersion);
            writer.WriteString(value.ContentHash);
            writer.WriteString(value.JoinToken);
        }

        public static JoinRequestMessage ReadJoinRequestMessage(this NetworkReader reader)
        {
            int    protocolVersion = (int)reader.ReadVarUInt();
            string contentHash     = reader.ReadString();
            string joinToken       = reader.ReadString();
            return new JoinRequestMessage(protocolVersion, contentHash, joinToken);
        }

        public static void WriteJoinResponseMessage(this NetworkWriter writer, JoinResponseMessage value)
        {
            writer.WriteVarUInt((uint)value.Reason);
            writer.WriteVarUInt((uint)value.ParticipantId);
        }

        public static JoinResponseMessage ReadJoinResponseMessage(this NetworkReader reader)
        {
            var reason        = (ERejectReason)reader.ReadVarUInt();
            int participantId = (int)reader.ReadVarUInt();
            return new JoinResponseMessage(reason, participantId);
        }
    }
}
