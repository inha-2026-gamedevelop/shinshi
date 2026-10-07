// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 채팅 서버가 Mirror 연결을 직접 잡지 않게 한다. 시험은 가짜 구현으로 보낸 메시지를 모은다
    /// <summary> 인증된 참가자에게 채팅 메시지를 보내는 창구, 서버 메인 스레드에서만 부른다 </summary>
    public interface IChatAudience
    {
        void Send<T>(int participantId, T message) where T : struct, NetworkMessage;

        // 이 서버 프로세스에 인증된 모든 참가자
        void SendToAll<T>(T message) where T : struct, NetworkMessage;
    }
}
