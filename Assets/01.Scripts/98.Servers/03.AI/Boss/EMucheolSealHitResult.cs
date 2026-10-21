namespace Shinshi.AI
{
    /// <summary> 보스 타격 한 건을 봉인이 어떻게 처리했는지, 서버 피해 판정기가 읽는다 </summary>
    public enum EMucheolSealHitResult
    {
        // 표식이 나오기 전이거나 봉인이 없다, 타수에 넣지 않는다
        NotActive,

        // 지속 피해나 소환물 공격이다
        Excluded,

        // 방향을 결정할 수 없는 타격이다
        Unclassifiable,

        // 같은 공격 동작이 이미 처리됐다
        Duplicate,

        // 활성 방향이 아니다, 벌칙 없이 타수만 오르지 않는다
        WrongDirection,

        // 활성 방향의 유효타로 타수가 올랐다
        Counted,

        // 이 타격으로 현재 방향이 채워져 다음 방향으로 넘어갔다
        DirectionCleared,

        // 이 타격으로 마지막 방향이 채워져 봉인이 깨졌다, 이 타격은 체력 피해가 없다
        Broken
    }
}
