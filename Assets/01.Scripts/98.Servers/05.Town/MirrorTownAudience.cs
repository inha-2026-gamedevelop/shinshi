// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 마을은 접속자가 많아 보낼 때마다 연결 목록을 훑지 않고 참가자 번호로 바로 찾는다.
    // Dictionary.Values의 foreach는 구조체 열거자라 전체 전송에 할당이 없다
    /// <summary> 마을 참가자 번호와 Mirror 연결의 대응 </summary>
    public sealed class MirrorTownAudience : ITownAudience
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Dictionary<int, NetworkConnectionToClient> _connections;

        /****************************************
        *                Methods
        ****************************************/

        public MirrorTownAudience(int capacity)
        {
            _connections = new Dictionary<int, NetworkConnectionToClient>(capacity);
        }

        public void Add(int participantId, NetworkConnectionToClient connection)
        {
            _connections[participantId] = connection;
        }

        public void Remove(int participantId)
        {
            _connections.Remove(participantId);
        }

        public bool TryGetPosition(int participantId, out Vector3 position)
        {
            position = default;
            if ((!_connections.TryGetValue(participantId, out NetworkConnectionToClient connection))
                || (connection.identity == null))
            {
                return false;
            }

            position = connection.identity.transform.position;
            return true;
        }

        public void Send<T>(int participantId, T message) where T : struct, NetworkMessage
        {
            if (_connections.TryGetValue(participantId, out NetworkConnectionToClient connection))
            {
                connection.Send(message);
            }
        }

        public void SendToAll<T>(T message) where T : struct, NetworkMessage
        {
            foreach (NetworkConnectionToClient connection in _connections.Values)
            {
                connection.Send(message);
            }
        }
    }
}
