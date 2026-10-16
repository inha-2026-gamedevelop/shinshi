// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    // 메시지는 행에 저장된 채널, 보낸 사람 이름, 본문으로만 만든다. 로컬 응답에는 다른 발신자의 앞선 행도 들어 있어 요청 본문을 쓰면
    // 그 줄이 엉뚱한 내용으로 간다. 클라이언트 읽기 상한을 넘는 행은 보내지 않는다(받는 쪽 연결이 끊긴다). 그런 행은 만료로 사라진다
    /// <summary> 전달함 행을 채팅 줄 또는 소셜 알림 메시지로 바꾸는 순수 규칙 </summary>
    public static class SocialDeliveryRules
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 채팅 행(귓속말, 길드 채팅)이면 참 </summary>
        public static bool IsChat(SocialDeliveryRow row)
        {
            return (row != null) && ((row.Kind == SocialCodes.DeliveryKind.WHISPER) || (row.Kind == SocialCodes.DeliveryKind.GUILD_CHAT));
        }

        /// <summary> 채팅 행을 ChatBroadcastMessage로, 채널이나 길이가 계약 밖이면 거짓 </summary>
        public static bool TryBuildChat(SocialDeliveryRow row, out ChatBroadcastMessage message)
        {
            message = default;
            if ((!IsChat(row)) || (row.DeliveryId <= 0))
            {
                return false;
            }

            EChatChannel channel;
            if (row.Channel == SocialCodes.ChatChannel.WHISPER)
            {
                channel = EChatChannel.Whisper;
            }
            else if (row.Channel == SocialCodes.ChatChannel.GUILD)
            {
                channel = EChatChannel.Guild;
            }
            else
            {
                return false;
            }

            if ((string.IsNullOrEmpty(row.Payload)) || (row.Payload.Length > Constants.Chat.MAX_MESSAGE_LENGTH)
                || (string.IsNullOrEmpty(row.SenderName)) || (row.SenderName.Length > Constants.Chat.MAX_SENDER_LENGTH))
            {
                return false;
            }

            message = new ChatBroadcastMessage(channel, row.SenderName, row.Payload, row.DeliveryId);
            return true;
        }

        /// <summary> 알림 행을 SocialNoticeMessage로, 알림 종류나 길이가 계약 밖이면 거짓 </summary>
        public static bool TryBuildNotice(SocialDeliveryRow row, out SocialNoticeMessage message)
        {
            message = default;
            if ((row == null) || (row.Kind != SocialCodes.DeliveryKind.NOTICE) || (row.DeliveryId <= 0))
            {
                return false;
            }

            if ((row.Channel < SocialCodes.Notice.FRIEND_REQUESTED) || (row.Channel > SocialCodes.Notice.GUILD_MEMBER_LEFT))
            {
                return false;
            }

            string actor = row.SenderName ?? string.Empty;
            string guild = row.Payload ?? string.Empty;
            if ((actor.Length > Constants.Social.MAX_DISPLAY_NAME_LENGTH) || (guild.Length > Constants.Social.MAX_DISPLAY_NAME_LENGTH))
            {
                return false;
            }

            message = new SocialNoticeMessage((ESocialNotice)row.Channel, actor, guild, row.DeliveryId);
            return true;
        }
    }
}
