namespace Shinshi.Common
{
    /// <summary> 일반 적과 보스 공격의 판정, 예고 형태 </summary>
    public enum EEnemyAttackShape
    {
        // 공격자 전방 부채꼴
        Fan        = 0,
        // 공격자 중심 원형
        Circle     = 1,
        // 폭을 가진 경로로 이동하며 판정
        Dash       = 2,
        // 직선으로 날아가는 투사체
        Projectile = 3,
        // 타깃 위치에 고정되는 원형 장판
        TargetArea = 4,
        // 공격자 전방 직사각형
        Line       = 5
    }
}
