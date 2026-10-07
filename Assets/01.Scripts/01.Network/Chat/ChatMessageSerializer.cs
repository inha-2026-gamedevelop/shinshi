// 서드파티
using Mirror;

namespace Shinshi.Network
{
    /// <summary> 채팅 메시지의 Mirror 직렬화, Weaver가 이 메서드를 쓴다 </summary>
    public static class ChatMessageSerializer
    {
        // 메시지는 private readonly 필드라 Weaver 자동 직렬화가 내용을 쓰지 못한다. 여기서 순서를 고정한다

        /****************************************
        *                Methods
        ****************************************/

        public static void WriteChatRequestMessage(this NetworkWriter writer, ChatRequestMessage value)
        {
            writer.WriteByte((byte)value.Channel);
            writer.WriteString(value.Text);
        }

        public static ChatRequestMessage ReadChatRequestMessage(this NetworkReader reader)
        {
            var    channel = (EChatChannel)reader.ReadByte();
            string text    = reader.ReadString();
            return new ChatRequestMessage(channel, text);
        }

        public static void WriteChatBroadcastMessage(this NetworkWriter writer, ChatBroadcastMessage value)
        {
            writer.WriteByte((byte)value.Channel);
            writer.WriteString(value.Sender);
            writer.WriteString(value.Text);
        }

        public static ChatBroadcastMessage ReadChatBroadcastMessage(this NetworkReader reader)
        {
            var    channel = (EChatChannel)reader.ReadByte();
            string sender  = reader.ReadString();
            string text    = reader.ReadString();
            return new ChatBroadcastMessage(channel, sender, text);
        }

        public static void WriteChatRejectMessage(this NetworkWriter writer, ChatRejectMessage value)
        {
            writer.WriteByte((byte)value.Reason);
        }

        public static ChatRejectMessage ReadChatRejectMessage(this NetworkReader reader)
        {
            return new ChatRejectMessage((EChatReject)reader.ReadByte());
        }
    }
}
