namespace Shinshi.AI
{
    /// <summary> 무철 순차 타격의 한 타가 판정하는 형태 </summary>
    public enum EMucheolSequenceShape
    {
        // 보스 중심 부채꼴, 조준 방향에서 오프셋 각도만큼 돌린 방향이 기준이다
        Fan,

        // 보스 중심에서 조준 방향(오프셋 적용)으로 뻗는 직사각형
        Line,

        // 보스 중심 원형
        Circle
    }
}
