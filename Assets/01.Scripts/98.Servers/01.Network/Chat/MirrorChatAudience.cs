// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 채팅을 한 번도 안 보낸 참가자도 받아야 해서 따로 명단을 두지 않고 현재 연결 목록을 훑는다.
    // Dictionary.Values의 foreach는 구조체 열거자라 할당이 없다
    /// <summary> 인증되어 활성인 Mirror 연결의 참가자 번호로 채팅을 보낸다 </summary>
    public sealed class MirrorChatAudience : IChatAudience
    {
        /****************************************
        *                Methods
        ****************************************/

        public void Send<T>(int participantId, T message) where T : struct, NetworkMessage
        {
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
            {
                if ((connection.authenticationData is SessionBinding binding) && (binding.IsActive)
                    && (binding.ParticipantId == participantId))
                {
                    connection.Send(message);
                    return;
                }
            }
        }

        public void SendToAll<T>(T message) where T : struct, NetworkMessage
        {
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
            {
                if ((connection.authenticationData is SessionBinding binding) && (binding.IsActive))
                {
                    connection.Send(message);
                }
            }
        }
    }
}
