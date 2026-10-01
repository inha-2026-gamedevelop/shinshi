namespace Shinshi.AI
{
    // 디렉터가 활성 수를 관리하므로 적의 퇴장은 반드시 디렉터를 거쳐 여기로 온다.
    // 개인 드롭은 여기서 판정하지 않는다. 처치자는 기록용이며 보상 자격은 참가자 명단이 정한다
    /// <summary> 스폰 디렉터가 적의 출현과 퇴장을 알리는 대상, 웨이브 세션이 구현한다 </summary>
    public interface ISpawnProgressListener
    {
        /// <summary> 요청한 적이 활성 슬롯을 받아 전장에 나왔다 </summary>
        void OnEnemySpawned(int waveIndex, string sourceId);

        /// <summary> 적이 처치됐다, 참가자가 아닌 원인으로 죽었으면 killerParticipantId는 빈 문자열이다 </summary>
        void OnEnemyDefeated(int waveIndex, string sourceId, string killerParticipantId);

        // TODO(기웅): 기획 미정 - 처치 없이 사라진 적을 웨이브 완료와 보상에 어떻게 셀지 정해지지 않았다
        /// <summary> 적이 처치 없이 전장에서 빠졌다 </summary>
        void OnEnemyDespawned(int waveIndex, string sourceId);
    }
}
