// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 참가자는 최대 4명이라 연결 목록을 그대로 훑는다. Dictionary.Values의 foreach는 구조체 열거자라 할당이 없다
    /// <summary> 인증된 Mirror 연결의 참가자 번호로 출정 상태, 포기 결과, 웨이브 진행을 보낸다 </summary>
    public sealed class MirrorSortieAudience : ISortieAudience, IWaveProgressAudience, ISortieRewardAudience
    {
        /****************************************
        *                Methods
        ****************************************/

        public void SendStatus(int participantId, in SortieStatusMessage message)
        {
            Find(participantId)?.Send(message);
        }

        public void SendAbandonResult(int participantId, in SortieAbandonResultMessage message)
        {
            Find(participantId)?.Send(message);
        }

        public void SendStopVote(int participantId, in SortieStopVoteStateMessage message)
        {
            Find(participantId)?.Send(message);
        }

        public void SendWaveProgress(int participantId, in WaveProgressMessage message)
        {
            Find(participantId)?.Send(message);
        }

        public void SendReward(int participantId, in SortieRewardMessage message)
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
