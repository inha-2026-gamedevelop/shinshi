namespace Shinshi.AI
{
    // IBossPatternStartResult로 시작을 거절한 패턴 중 이 표식을 단 패턴만, 두뇌가 같은 선택에서 그 정의를 빼고 한 번 더 고른다
    // 표식이 없는 패턴의 거절은 기존대로 기록 없이 재조회 시간 뒤에 다시 고른다. 월백 2페이즈 P07(착지 후보 없음)만 단다
    /// <summary> 시작이 거절되면 같은 선택에서 이 정의를 뺀 채 한 번 다시 고르게 하는 표식 </summary>
    public interface IBossPatternRetryOnReject
    {
    }
}
