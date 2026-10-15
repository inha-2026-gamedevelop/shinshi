namespace Shinshi.AI
{
    /// <summary> 타 목록을 가진 무철 시전의 종류, 표현이 뷰를 고르는 데 쓴다 </summary>
    public enum EBossStrikeChainKind : byte
    {
        // 1페이즈 P05 콤보, 모든 타가 전방 부채꼴이다
        Combo,

        // 2페이즈 P01, P02, P04, P05 순차 타격, 타마다 형태와 방향 오프셋이 다르다
        Sequence
    }
}
