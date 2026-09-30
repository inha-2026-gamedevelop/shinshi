namespace Shinshi.Common
{
    /// <summary> 기술 실행 단계, Targeting 취소는 자원과 쿨다운을 쓰지 않는다 </summary>
    public enum ESkillPhase
    {
        Idle       = 0,
        Targeting  = 1,
        Validating = 2,
        Windup     = 3,
        Active     = 4,
        Recovery   = 5
    }
}
