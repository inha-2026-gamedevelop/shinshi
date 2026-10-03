namespace Shinshi.AI
{
    /// <summary> 빔 한 줄의 표시 단계 </summary>
    public enum EBossBeamPhase
    {
        /// <summary> 서버가 활성을 알리기 전, 예고만 보인다 </summary>
        Telegraph = 0,

        /// <summary> 서버가 활성을 알렸고 지속 시간이 남았다 </summary>
        Active    = 1,

        /// <summary> 끝났다(수명 종료, 취소, 서버 확정 없이 예고 대기 시간이 지남) </summary>
        Ended     = 2
    }
}
