namespace Shinshi.Common
{
    /// <summary> 기술 조준 방식, Point만 Targeting 단계를 거친다 </summary>
    public enum ESkillAimMode
    {
        // 입력 순간의 커서 방향으로 바로 발동
        Direction = 0,
        // 범위를 띄운 뒤 클릭으로 지점을 확정
        Point     = 1,
        // 조준 없이 시전자 위치에서 발동
        Self      = 2
    }
}
