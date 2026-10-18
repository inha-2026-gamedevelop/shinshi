// System
using System.Collections.Generic;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 채팅을 한 번도 안 보낸 참가자도 받아야 해서 따로 명단을 두지 않는다. 한 명에게 보낼 때는 처음 한 번만 연결을 훑어 찾고
    // 참가자 번호로 캐시한다. 캐시는 쓸 때마다 연결이 아직 살아 있고 같은 참가자인지 확인하며, 끊기면 Forget으로도 지운다.
    // Mirror의 연결 단위 전송(NetworkConnection.Send(ArraySegment))은 internal이라 전체 방송을 한 번 직렬화로 줄일 수 없다.
    // Dictionary.Values의 foreach는 구조체 열거자라 할당이 없다
    /// <summary> 인증되어 활성인 Mirror 연결의 참가자 번호로 채팅을 보낸다 </summary>
    public sealed class MirrorChatAudience : IChatAudience
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Dictionary<int, NetworkConnectionToClient> _connections = new Dictionary<int, NetworkConnectionToClient>();

        /****************************************
        *                Methods
        ****************************************/

        public void Send<T>(int participantId, T message) where T : struct, NetworkMessage
        {
            if (TryFind(participantId, out NetworkConnectionToClient connection))
            {
                connection.Send(message);
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

        public void Forget(int participantId)
        {
            _connections.Remove(participantId);
        }

        private bool TryFind(int participantId, out NetworkConnectionToClient connection)
        {
            if (_connections.TryGetValue(participantId, out connection))
            {
                if (IsLive(connection, participantId))
                {
                    return true;
                }

                _connections.Remove(participantId);
            }

            foreach (NetworkConnectionToClient candidate in NetworkServer.connections.Values)
            {
                if (IsLive(candidate, participantId))
                {
                    _connections[participantId] = candidate;
                    connection                  = candidate;
                    return true;
                }
            }

            connection = null;
            return false;
        }

        // 재접속으로 같은 번호가 새 연결에 붙었을 수 있어 서버 연결 목록의 현재 연결인지도 본다
        private static bool IsLive(NetworkConnectionToClient connection, int participantId)
        {
            return (connection != null)
                && (connection.authenticationData is SessionBinding binding) && (binding.IsActive)
                && (binding.ParticipantId == participantId)
                && (NetworkServer.connections.TryGetValue(connection.connectionId, out NetworkConnectionToClient current))
                && (ReferenceEquals(current, connection));
        }
    }
}
