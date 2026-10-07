// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 성장, 생활 중계는 스냅숏을 보내기만 하고 들고 있지 않는다. 거래 사전 검사가 마지막 값을 볼 수 있게, 창구를 감싸 보내는 길목에서
    // 참가자별 마지막 성장, 생활 스냅숏을 기억한다. 메시지는 그대로 전달한다. 스냅숏은 드물게 보내므로 박싱 비용은 무시한다
    /// <summary> 마을 참가자에게 가는 성장, 생활 스냅숏을 기억하는 ITownAudience 장식자 </summary>
    public sealed class TownSnapshotTap : ITownAudience
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly ITownAudience                         _inner;
        private readonly Dictionary<int, GrowthSnapshotMessage> _growth = new Dictionary<int, GrowthSnapshotMessage>();
        private readonly Dictionary<int, LifeSnapshotMessage>   _life   = new Dictionary<int, LifeSnapshotMessage>();

        /****************************************
        *                Methods
        ****************************************/

        public TownSnapshotTap(ITownAudience inner)
        {
            _inner = inner ?? throw new System.ArgumentNullException(nameof(inner));
        }

        public bool TryGetGrowth(int participantId, out GrowthSnapshotMessage snapshot)
        {
            return _growth.TryGetValue(participantId, out snapshot);
        }

        public bool TryGetLife(int participantId, out LifeSnapshotMessage snapshot)
        {
            return _life.TryGetValue(participantId, out snapshot);
        }

        /// <summary> 연결이 끊긴 참가자의 기억을 지운다 </summary>
        public void Forget(int participantId)
        {
            _growth.Remove(participantId);
            _life.Remove(participantId);
        }

        public bool TryGetPosition(int participantId, out Vector3 position)
        {
            return _inner.TryGetPosition(participantId, out position);
        }

        public void Send<T>(int participantId, T message) where T : struct, NetworkMessage
        {
            // 형식을 먼저 걸러 다른 메시지는 박싱하지 않는다
            if (typeof(T) == typeof(GrowthSnapshotMessage))
            {
                _growth[participantId] = (GrowthSnapshotMessage)(object)message;
            }
            else if (typeof(T) == typeof(LifeSnapshotMessage))
            {
                _life[participantId] = (LifeSnapshotMessage)(object)message;
            }

            _inner.Send(participantId, message);
        }

        public void SendToAll<T>(T message) where T : struct, NetworkMessage
        {
            _inner.SendToAll(message);
        }
    }
}
