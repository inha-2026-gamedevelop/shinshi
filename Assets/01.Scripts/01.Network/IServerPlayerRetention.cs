// 서드파티
using Mirror;

namespace Shinshi.Network
{
    /// <summary> 연결이 끊긴 플레이어 개체를 서버에 남기고 재접속 때 다시 붙이는 서버 판정 창구, 서버 부트스트랩이 매니저에 넣는다 </summary>
    public interface IServerPlayerRetention
    {
        // 참이면 개체의 소유권만 떼고 서버에 남겼다는 뜻이다. 매니저는 이어서 기본 정리를 하지만 남긴 개체는 지우지 않는다
        bool TryRetainPlayer(NetworkConnectionToClient connection);

        // 참이면 남겨 둔 같은 개체에 새 연결의 소유권을 줬다는 뜻이다. 매니저는 새 개체를 만들지 않는다
        bool TryReattachPlayer(NetworkConnectionToClient connection);

        // 참이면 캐릭터 없이 받는 연결(출정 종료 뒤 경매 전용 재입장)이라 매니저는 플레이어 개체를 만들지 않는다
        bool TryJoinWithoutPlayer(NetworkConnectionToClient connection);
    }
}
