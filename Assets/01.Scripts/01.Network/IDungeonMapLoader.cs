namespace Shinshi.Network
{
    /// <summary> 던전 서버가 첫 입장 계약에 맞는 전장 씬으로 바꾸는 창구, 서버 매니저가 구현한다 </summary>
    public interface IDungeonMapLoader
    {
        /// <summary> 지금 올라와 있거나 올리는 중인 전장 씬 이름(경로 아님) </summary>
        string CurrentMapName { get; }

        /// <summary> 맵 전환을 시작했고 서버 씬 로드가 아직 끝나지 않았다 </summary>
        bool IsLoading { get; }

        /// <summary> 인증된 클라이언트에게 알리지 않고 서버만 씬을 바꾼다, 시작하지 못하면 거짓 </summary>
        bool TryBeginLoad(string mapName);
    }
}
