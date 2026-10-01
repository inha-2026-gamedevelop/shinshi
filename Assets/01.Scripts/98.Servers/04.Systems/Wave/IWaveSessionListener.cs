namespace Shinshi.Systems
{
    // 한 처치가 웨이브를 끝내면 OnEnemyDefeated, OnWaveCleared, OnWaveSessionEnded 순서로 부른다.
    // 마지막 적의 개인 드롭 판정이 성공 정산 요청보다 먼저 들어가게 하기 위해서다
    /// <summary> 웨이브 세션이 바깥에 알리는 결과, 세션 바인딩이 구현해 개인 드롭 판정과 정산 요청으로 넘긴다 </summary>
    public interface IWaveSessionListener
    {
        /// <summary> 적이 처치됐다, 개인 드롭 판정의 원인 ID로 sourceId를 쓴다. 참가자가 아닌 원인이면 killerParticipantId는 빈 문자열이다 </summary>
        void OnEnemyDefeated(int waveIndex, string sourceId, string killerParticipantId);

        /// <summary> 웨이브의 예정 적과 남은 적을 모두 처치했다, waveIndex는 0부터 센다 </summary>
        void OnWaveCleared(int waveIndex);

        /// <summary> 세션이 끝났다, 한 번만 부르며 이유는 Success 또는 Timeout이다. 정산 결과는 여기서 판정하지 않는다 </summary>
        void OnWaveSessionEnded(in WaveSessionOutcome outcome);
    }
}
