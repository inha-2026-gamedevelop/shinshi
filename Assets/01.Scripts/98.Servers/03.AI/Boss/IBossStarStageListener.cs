namespace Shinshi.AI
{
    // 서버 패턴과 단계형 별 모양 빔 표현 사이의 연결 자리다. 복제 계층이 이 알림을 클라이언트로 전달하는 구성을 가정한다. 구현은 아직 없다
    // 배치와 단계 각도와 시각은 모두 서버가 확정한 값이고 클라이언트는 추첨, 추적, 판정을 하지 않고 그대로 그린다
    //
    // 단계 k의 각도는 stepDegrees x k 이다(항상 양수, 기준 방향에서 오른쪽으로 돈다). 단계 k의 타격 시각은 castStartServerTime + firstStrikeSeconds + k x strikeIntervalSeconds 이고
    // 그 단계의 위치 예고는 이전 단계의 타격 시각(첫 단계는 시전 시작)부터 자기 타격 시각까지다
    // 늦게 접속한 클라이언트가 복원하려면 복제 계층이 시작 알림의 값과 타격 알림으로 센 진행 단계 수와 취소 여부를 상태로 들고 있어야 한다. 이 알림만으로는 복원되지 않는다
    /// <summary> 단계형 별 모양 빔의 시작, 단계 타격, 취소 알림 </summary>
    public interface IBossStarStageListener
    {
        /// <summary> 시작, 배치와 단계 각도는 이 시점에 고정되며 시전이 끝날 때까지 바뀌지 않는다. 시전 시작 시각은 서버 시각이다 </summary>
        void OnStageSequenceStarted(int castInstanceId, int starId, in BossStarShape shape, float stepDegrees, int stageCount,
            double castStartServerTime, float firstStrikeSeconds, float strikeIntervalSeconds);

        /// <summary> 단계 타격이 확정됐다. scheduledServerTime은 예정 시각이고 processedServerTime은 실제로 처리한 틱 시각이다. 늦게 처리돼도 대상 위치는 처리 시점의 현재 위치다 </summary>
        void OnStageStruck(int castInstanceId, int starId, int stageIndex, float rotationDegrees, double scheduledServerTime,
            double processedServerTime, int hitCount);

        /// <summary> 중단됐다, nextStageIndex부터는 타격하지 않는다. 이미 적용된 피해는 되돌리지 않는다 </summary>
        void OnStageSequenceCancelled(int castInstanceId, int starId, int nextStageIndex, EBossInterruptReason reason);
    }
}
