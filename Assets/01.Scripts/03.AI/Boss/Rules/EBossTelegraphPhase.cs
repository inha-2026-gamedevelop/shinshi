namespace Shinshi.AI
{
    /// <summary> 예고 표현 한 건의 진행 단계, Finished와 Cancelled는 끝이라 다시 살아나지 않는다 </summary>
    public enum EBossTelegraphPhase : byte
    {
        /// <summary> 범위를 보여 주는 중이고 서버가 발동을 알리기 전이다 </summary>
        Telegraph = 0,

        /// <summary> 서버가 발동을 알렸다, 돌진은 이동 중이거나 이동을 마치고 실행 끝을 기다린다 </summary>
        Execute = 1,

        /// <summary> 정상 종료 </summary>
        Finished = 2,

        /// <summary> 발동 전이나 돌진 중에 중단됐거나 서버 확정이 끝내 오지 않아 지웠다 </summary>
        Cancelled = 3
    }
}
