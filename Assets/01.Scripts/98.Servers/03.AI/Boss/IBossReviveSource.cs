namespace Shinshi.AI
{
    // 부활 직후 보호는 부활한 시각에서 잰다. 기믹이 틱마다 생사 변화를 보고 시각을 추정하면 큰 틱에서 보호가 뒤로 밀리므로,
    // 공용 부활을 처리하는 쪽이 실제 부활 시각을 기록해 이 창구로 알려야 한다. 구현은 아직 없다
    /// <summary> 참가자의 마지막 부활 서버 시각 조회 </summary>
    public interface IBossReviveSource
    {
        /// <summary> 이 참가자가 마지막으로 부활한 서버 시각을 돌려준다, 한 번도 부활하지 않았으면 거짓이다 </summary>
        bool TryGetLastReviveServerTime(int combatId, out double serverTime);
    }
}
