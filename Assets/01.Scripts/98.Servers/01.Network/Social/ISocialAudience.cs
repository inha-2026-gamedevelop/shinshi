// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 소셜 서버들이 Mirror 연결을 직접 잡지 않게 한다. 시험은 가짜 구현으로 보낸 메시지와 접속 캐릭터를 정한다
    /// <summary> 인증된 참가자에게 소셜 메시지를 보내고 캐릭터 ID로 이 서버의 연결을 찾는 창구, 서버 메인 스레드에서만 부른다 </summary>
    public interface ISocialAudience
    {
        void Send<T>(int participantId, T message) where T : struct, NetworkMessage;

        // 이 서버 프로세스에 인증되어 활성인 캐릭터의 참가자 번호, 없으면 거짓
        bool TryFindParticipant(string characterId, out int participantId);

        // 캐릭터 ID가 있는 활성 연결이 하나라도 있으면 참. 전달함 꺼내기를 쉴지 정한다
        bool HasConnectedCharacters();
    }
}
