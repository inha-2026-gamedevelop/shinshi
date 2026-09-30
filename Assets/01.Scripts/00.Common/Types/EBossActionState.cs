namespace Shinshi.Common
{
    /// <summary> 보스 패턴 선택과 실행의 반복 단계 </summary>
    public enum EBossActionState
    {
        SelectTarget  = 0,
        ReadContext   = 1,
        SelectPattern = 2,
        Telegraph     = 3,
        Execute       = 4,
        Recover       = 5
    }
}
