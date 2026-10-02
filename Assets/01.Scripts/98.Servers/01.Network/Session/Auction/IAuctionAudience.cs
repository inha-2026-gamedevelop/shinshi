namespace Shinshi.Network
{
    // 출정이 끝나면 명단은 모두 이탈로 닫히므로 경매의 접속 여부는 실제 연결로 판단한다
    /// <summary> 경매가 참가자의 실제 접속을 묻고 상태를 보내는 창구, 서버 메인 스레드에서만 부른다 </summary>
    public interface IAuctionAudience
    {
        bool IsConnected(int participantId);
        void SendState(int participantId, in AuctionStateMessage message);
        void SendBidResult(int participantId, in AuctionBidResultMessage message);
    }
}
