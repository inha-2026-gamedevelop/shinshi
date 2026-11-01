namespace Shinshi.Network
{
    /// <summary> 보스전 토벌 중단 투표의 단계, Passed가 되면 서버가 출정 전체를 포기로 끝낸다 </summary>
    public enum ESortieStopVoteState : byte
    {
        None   = 0,
        Open   = 1,
        Passed = 2,
        Failed = 3
    }
}
