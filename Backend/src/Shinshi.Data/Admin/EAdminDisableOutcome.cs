namespace Shinshi.Data.Admin
{
    /// <summary> 운영자 비활성화 결과 </summary>
    public enum EAdminDisableOutcome
    {
        Disabled         = 0,
        NotFound         = 1,
        AlreadyDisabled  = 2,
        SelfTarget       = 3,

        // 잠금을 얻고 보니 행위자가 이미 비활성이다(다른 Owner가 먼저 비활성화했다)
        ActorDisabled    = 4,

        // 대상이 활성 Owner의 마지막 한 명이다
        LastOwner        = 5,

        // 잠금을 얻고 보니 행위자의 역할이 인증 때보다 낮다
        ActorRoleLowered = 6
    }
}
