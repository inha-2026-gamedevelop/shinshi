namespace Shinshi.Combat
{
    /// <summary> 서버 피해 적용 창구, 중복 적중 차단과 전방 반격 방어와 방어 감소와 생사 전이를 한곳에서 판정한다. 구현은 서버 전용이라 서버 어셈블리에 둔다 </summary>
    public interface IDamageResolver
    {
        // 서버 틱 안에서 판정 도형과 서버 시각으로 적중을 확정한 뒤에만 부른다.
        // 전용 서버에는 카메라, UI가 없고 애니메이션 종료 시점은 클라이언트마다 달라서
        // 그 신호로 부르면 서버와 클라이언트의 피격 결과가 갈린다

        /// <summary> 피해 한 건을 판정하고 적용한다, 같은 동작과 대상 조합은 한 번만 적용된다 </summary>
        DamageResult Apply(in DamageRequest request);

        // 번호는 이 전투 수명 안에서만 유효하고 수명이 끝나면 적중 기록과 함께 버린다. 보상 원장의 영구 ID로 쓰지 않는다

        /// <summary> 시전, 동작 번호를 발급한다, CombatIds.NONE은 돌려주지 않는다 </summary>
        int IssueInstanceId();
    }
}
