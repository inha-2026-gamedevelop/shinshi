namespace Shinshi.Common
{
    /// <summary> 일반 기술의 판정 형태, 예고 표시와 서버 판정이 같은 값을 쓴다 </summary>
    public enum ESkillShape
    {
        // 시전자 기준 부채꼴
        Cone       = 0,
        // 원형 범위
        Circle     = 1,
        // 직선 경로, 돌진처럼 시전자가 이동하며 판정
        Line       = 2,
        // 첫 적에서 소멸하는 투사체
        Projectile = 3,
        // 계약 동료 호출, 기술 자체는 판정 범위가 없다
        Summon     = 4,
        // 시전자 자신에게 거는 효과, 피해 판정 범위가 없다
        Buff       = 5
    }
}
