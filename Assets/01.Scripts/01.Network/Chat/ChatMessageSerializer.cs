// System
using System.IO;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

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
            writer.WriteLong(value.DeliveryId);
        }

        public static ChatBroadcastMessage ReadChatBroadcastMessage(this NetworkReader reader)
        {
            var    channel    = (EChatChannel)reader.ReadByte();
            string sender     = reader.ReadString();
            string text       = reader.ReadString();
            long   deliveryId = reader.ReadLong();
            return new ChatBroadcastMessage(channel, sender, text, deliveryId);
        }

        public static void WriteChatRejectMessage(this NetworkWriter writer, ChatRejectMessage value)
        {
            writer.WriteByte((byte)value.Reason);
        }

        public static ChatRejectMessage ReadChatRejectMessage(this NetworkReader reader)
        {
            return new ChatRejectMessage((EChatReject)reader.ReadByte());
        }

        // 대상 이름은 클라이언트가 캐릭터 이름 규칙으로 먼저 정리해 보내므로 정상 요청은 상한을 넘지 않는다.
        // 본문 길이는 서버가 TooLong으로 안내해야 해서 여기서 막지 않는다
        public static void WriteChatWhisperRequestMessage(this NetworkWriter writer, ChatWhisperRequestMessage value)
        {
            writer.WriteString(value.TargetName);
            writer.WriteString(value.Text);
        }

        public static ChatWhisperRequestMessage ReadChatWhisperRequestMessage(this NetworkReader reader)
        {
            string targetName = reader.ReadString();
            if ((targetName != null) && (targetName.Length > SocialLimits.MAX_NAME_INPUT_LENGTH))
            {
                throw new InvalidDataException("귓속말 대상 이름이 계약 상한을 넘는다");
            }

            string text = reader.ReadString();
            return new ChatWhisperRequestMessage(targetName, text);
        }
    }
}
