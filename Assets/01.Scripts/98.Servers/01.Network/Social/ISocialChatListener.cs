namespace Shinshi.Network
{
    // 채팅 서버가 구현한다. 거절 안내 간격과 채팅 기록을 채팅 서버 한 곳에서 지키게 한다
    /// <summary> 백엔드를 거친 귓속말, 길드 채팅 한 줄의 늦은 결과를 받는 쪽, 메인 스레드에서만 부른다 </summary>
    public interface ISocialChatListener
    {
        void OnSocialChatRejected(int participantId, EChatReject reason);

        // API가 받아들여 전달함에 기록한 줄. sender는 보낸 사람의 표시 이름이다
        void OnSocialChatCommitted(int participantId, string characterId, string apiParticipantId, EChatChannel channel,
            string sender, string text);
    }
}
