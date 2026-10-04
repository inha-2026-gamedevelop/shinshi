// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Network;

namespace Shinshi.Tests
{
    /// <summary> 마을 서버 시험용 가짜 창구, 보낸 메시지를 기록하고 캐릭터 위치를 시험이 정한다 </summary>
    public sealed class FakeTownAudience : ITownAudience
    {
        // 시험 기록이라 메시지를 object로 담는다(박싱은 시험에서만 생긴다)

        /****************************************
        *                Fields
        ****************************************/

        private readonly Dictionary<int, Vector3>            _positions  = new Dictionary<int, Vector3>();
        private readonly List<KeyValuePair<int, object>>     _sent       = new List<KeyValuePair<int, object>>();
        private readonly List<object>                        _broadcasts = new List<object>();

        /****************************************
        *              Properties
        ****************************************/

        public List<object> Broadcasts => _broadcasts;

        /****************************************
        *                Methods
        ****************************************/

        public void SetPosition(int participantId, Vector3 position)
        {
            _positions[participantId] = position;
        }

        public void ClearPosition(int participantId)
        {
            _positions.Remove(participantId);
        }

        public bool TryGetPosition(int participantId, out Vector3 position)
        {
            return _positions.TryGetValue(participantId, out position);
        }

        public void Send<T>(int participantId, T message) where T : struct, NetworkMessage
        {
            _sent.Add(new KeyValuePair<int, object>(participantId, message));
        }

        public void SendToAll<T>(T message) where T : struct, NetworkMessage
        {
            _broadcasts.Add(message);
        }

        public int Count<T>(int participantId) where T : struct
        {
            int count = 0;
            for (int i = 0; i < _sent.Count; ++i)
            {
                if ((_sent[i].Key == participantId) && (_sent[i].Value is T))
                {
                    ++count;
                }
            }

            return count;
        }

        /// <summary> 이 참가자에게 마지막으로 보낸 T, 없으면 거짓 </summary>
        public bool TryGetLast<T>(int participantId, out T message) where T : struct
        {
            for (int i = _sent.Count - 1; i >= 0; --i)
            {
                if ((_sent[i].Key == participantId) && (_sent[i].Value is T found))
                {
                    message = found;
                    return true;
                }
            }

            message = default;
            return false;
        }

        public void Clear()
        {
            _sent.Clear();
            _broadcasts.Clear();
        }
    }
}
