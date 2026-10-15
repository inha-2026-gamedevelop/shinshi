namespace Shinshi.AI
{
    // 최대 체력 비율 피해는 대상의 최대 체력을 알아야 방어 전 피해량으로 바꿀 수 있다. ICombatTarget에는 체력 읽기가 없어
    // 출정 명단을 읽는 쪽이 같이 구현하는 별도 창구를 둔다. 구현은 서버 연결이 하며 아직 없다
    /// <summary> 참가자 최대 체력 조회 </summary>
    public interface IBossParticipantHealth
    {
        /// <summary> 대상의 최대 체력을 돌려준다, 대상을 찾지 못하거나 0 이하이면 거짓이다 </summary>
        bool TryGetMaxHealth(int combatId, out float maxHealth);
    }
}
