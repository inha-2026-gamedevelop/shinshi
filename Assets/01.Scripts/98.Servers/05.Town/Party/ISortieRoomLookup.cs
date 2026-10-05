namespace Shinshi.Network
{
    /// <summary> 출정 대기열이 방이 아직 같은 상태로 줄을 서 있는지 묻는 창구 </summary>
    public interface ISortieRoomLookup
    {
        // 방이 있고 대기 단계이며 revision이 줄을 설 때와 같으면 참
        bool IsQueuedAt(int roomId, long revision);
    }
}
