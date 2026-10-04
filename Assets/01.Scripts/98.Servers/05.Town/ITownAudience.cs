// Unity
using UnityEngine;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 마을 서버들이 Mirror 연결을 직접 잡지 않게 한다. 시험은 가짜 구현으로 보낸 메시지와 위치를 바꾼다
    /// <summary> 마을 참가자에게 메시지를 보내고 서버 캐릭터 위치를 묻는 창구, 서버 메인 스레드에서만 부른다 </summary>
    public interface ITownAudience
    {
        // 서버 권위 캐릭터의 현재 위치, 연결이나 캐릭터가 없으면 거짓
        bool TryGetPosition(int participantId, out Vector3 position);

        void Send<T>(int participantId, T message) where T : struct, NetworkMessage;

        void SendToAll<T>(T message) where T : struct, NetworkMessage;
    }
}
