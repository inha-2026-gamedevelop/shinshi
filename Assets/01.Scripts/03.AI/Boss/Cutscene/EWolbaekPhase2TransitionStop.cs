namespace Shinshi.AI
{
    /// <summary> 2페이즈 전환 컷신이 끝난 이유 </summary>
    public enum EWolbaekPhase2TransitionStop
    {
        None         = 0,
        Completed    = 1, // 서버 종료 시각에 정상 종료
        Superseded   = 2, // 더 새 전환 사건이 왔다
        SessionReset = 3, // 새 연결이나 새 전투로 기록을 처음부터 시작한다
        Unbound      = 4, // 복제 상태 연결 해제
        Disabled     = 5, // 컷신 재생기 비활성화나 파괴
        Revoked      = 6, // BossView가 처치, Reset, 전투 종료로 제어권을 거뒀다
        Unavailable  = 7  // 장비, 애니메이터, 제어권이 없어 시작하지 못했다
    }
}
