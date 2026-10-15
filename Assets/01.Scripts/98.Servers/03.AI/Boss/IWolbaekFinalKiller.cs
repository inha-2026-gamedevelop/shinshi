namespace Shinshi.AI
{
    // 추락과 중복 적중 즉사는 피해량 계산이 아니라 서버 사망 확정이다. 방어와 반격과 방어력이 개입하지 않아야 하므로 피해 판정기를 거치지 않는 창구를 둔다.
    // 부활 직후 보호가 즉사를 막는지는 호출하는 기믹이 정하며, 이 창구는 부르면 죽인다. 구현은 플레이어 서버 쪽이며 아직 없다
    /// <summary> 최후의 영역의 서버 사망 확정 창구 </summary>
    public interface IWolbaekFinalKiller
    {
        /// <summary> 살아 있는 참가자를 사망 처리한다, 이미 쓰러졌거나 찾지 못하면 거짓이다 </summary>
        bool TryKill(int combatId, int castInstanceId, EWolbaekFinalDeath cause);
    }
}
