namespace Shinshi.UI
{
    /// <summary> 던전 진행 줄에서 웨이브 한 칸의 상태 </summary>
    public enum EWaveNodeState
    {
        Pending, // 아직 차례가 아님
        Current, // 지금 싸우는 중이거나 다음에 싸울 차례
        Cleared, // 모두 처치함
    }
}
