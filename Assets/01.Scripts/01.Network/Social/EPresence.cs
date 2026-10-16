namespace Shinshi.Network
{
    /// <summary> 친구, 길드원의 접속 위치. 백엔드가 계정 임대의 서버 역할로 정하며 값을 바꾸지 않는다 </summary>
    public enum EPresence
    {
        Offline = 0,
        Town    = 1,
        Dungeon = 2
    }
}
