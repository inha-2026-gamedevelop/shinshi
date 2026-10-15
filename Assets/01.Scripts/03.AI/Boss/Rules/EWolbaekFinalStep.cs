namespace Shinshi.AI
{
    /// <summary> 최후의 영역 안의 단계, 진행 중에만 의미가 있다 </summary>
    public enum EWolbaekFinalStep
    {
        // 중앙만 남는다는 예고 중이다, 아직 바닥이 그대로이고 노란 여우불도 없다
        Collapse,

        // 외곽 바닥이 사라졌고 노란 여우불이 서 있다, 빨간 여우불 묶음이 반복된다
        Hunt
    }
}
