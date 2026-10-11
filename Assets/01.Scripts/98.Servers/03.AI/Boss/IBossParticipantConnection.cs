namespace Shinshi.AI
{
    /// <summary> 전투 대상 ID의 현재 접속 상태를 하드 추적 예고에 제공한다 </summary>
    public interface IBossParticipantConnection
    {
        bool IsConnected(int combatId);
    }
}
