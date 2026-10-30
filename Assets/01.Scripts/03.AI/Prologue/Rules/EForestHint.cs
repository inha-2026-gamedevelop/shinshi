namespace Shinshi.AI
{
    /// <summary> 숲에서 지금 보일 안내 한 가지, 위쪽 목표와 아래쪽 설명 문장은 이 값으로 정한다 </summary>
    public enum EForestHint
    {
        None               = 0,
        // 첫 번째 일반 산귀, 기본 공격을 아직 안 썼다
        BasicAttack        = 1,
        // 두 번째 일반 산귀가 나왔고 Q를 아직 안 썼다
        SkillQ             = 2,
        // 예고형 산귀가 공격 예고를 띄우고 있다
        LeaveDangerZone    = 3,
        // 부적까지 걸어가는 동안
        Move               = 4,
        // 기본 공격과 Q를 썼거나 쓸 차례가 아니고 첫 전투의 적이 남았다
        KillRemaining      = 5,
        // 일반 산귀를 모두 처치했지만 기본 공격을 아직 안 썼다
        UseAttackOnce      = 6,
        // 일반 산귀를 모두 처치했지만 Q를 아직 안 썼다
        UseSkillOnce       = 7,
        // 일반 산귀를 모두 처치했지만 둘 다 안 썼다
        UseBothOnce        = 8,
        // 예고형 산귀와 싸우는 중이고 예고가 지금은 없다
        FightTelegraphEnemy = 9
    }
}
