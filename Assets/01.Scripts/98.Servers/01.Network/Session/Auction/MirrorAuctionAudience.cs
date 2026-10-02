// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 참가자는 최대 4명이라 연결 목록을 그대로 훑는다. Dictionary.Values의 foreach는 구조체 열거자라 할당이 없다
    /// <summary> 인증된 Mirror 연결의 참가자 번호로 경매 대상을 찾는다 </summary>
    public sealed class MirrorAuctionAudience : IAuctionAudience
    {
        /****************************************
        *                Methods
        ****************************************/

        public bool IsConnected(int participantId)
        {
            return Find(participantId) != null;
        }

        public void SendState(int participantId, in AuctionStateMessage message)
        {
            Find(participantId)?.Send(message);
        }

        public void SendBidResult(int participantId, in AuctionBidResultMessage message)
        {
            Find(participantId)?.Send(message);
        }

        private static NetworkConnectionToClient Find(int participantId)
        {
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
            {
                if ((connection.authenticationData is SessionBinding binding) && (binding.IsActive)
                    && (binding.ParticipantId == participantId))
                {
                    return connection;
                }
            }

            return null;
        }
    }
}
