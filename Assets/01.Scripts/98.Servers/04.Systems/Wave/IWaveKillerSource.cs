namespace Shinshi.Systems
{
    // 처치자는 기록용이고 보상 자격은 참가자 명단이 정한다. 구현이 없으면 웨이브는 처치자를 빈 문자열로 통지한다
    /// <summary> 죽은 적의 마지막 타격 참가자를 알려 주는 선택 창구, 공통 판정기의 피해 알림을 모으는 쪽이 구현한다 </summary>
    public interface IWaveKillerSource
    {
        /// <summary> 적의 전투 번호로 처치한 참가자의 ParticipantKey를 찾는다, 참가자가 아닌 원인이면 false </summary>
        bool TryGetKiller(int enemyCombatId, out string participantKey);
    }
}
