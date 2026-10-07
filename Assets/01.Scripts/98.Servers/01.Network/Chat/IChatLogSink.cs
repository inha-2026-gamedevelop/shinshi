namespace Shinshi.Network
{
    // 신고를 판정할 근거를 남기는 창구, 시험은 가짜 구현으로 기록된 줄만 모은다
    /// <summary> 서버가 수락해 방송한 채팅 한 줄을 기록하는 창구, 서버 메인 스레드에서만 부른다 </summary>
    public interface IChatLogSink
    {
        // 거절한 요청은 부르지 않는다. text는 방송한 정리 본문. connParticipantId는 이 프로세스 안의 번호이고,
        // apiParticipantId(API가 정한 참가자 ID)와 characterId(입장 교환 값)는 없으면 null이다
        void Accepted(int connParticipantId, string apiParticipantId, string characterId, EChatChannel channel, string sender, string text);
    }
}
