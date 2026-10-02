namespace Shinshi.Network
{
    // 경매 대상 연결이 설치되지 않은 호스트(시험, 연결 없는 실행)용이다. 접속 자격자가 없으니 경매 없이 출정을 닫는다
    /// <summary> 아무도 접속하지 않은 경매 대상 </summary>
    public sealed class DisconnectedAuctionAudience : IAuctionAudience
    {
        /****************************************
        *                Methods
        ****************************************/

        public bool IsConnected(int participantId)
        {
            return false;
        }

        public void SendState(int participantId, in AuctionStateMessage message)
        {
        }

        public void SendBidResult(int participantId, in AuctionBidResultMessage message)
        {
        }
    }
}
