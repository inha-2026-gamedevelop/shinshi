namespace Shinshi.AI
{
    // 서버 패턴과 격자 순차 빔 표현 사이의 연결 자리다. 복제 계층이 이 알림을 클라이언트로 전달하는 구성을 가정한다. 구현은 아직 없다
    // 배치와 줄 순서와 시각은 모두 서버가 확정한 값이고 클라이언트는 추첨, 추적, 판정을 하지 않고 그대로 그린다
    //
    // 줄 번호는 발사 순서다. 가로 N줄이 먼저고 세로 N줄이 뒤이며 줄마다 서버가 발급한 줄 번호를 갖는다
    // 가로 줄 k의 타격 시각은 castStartServerTime + firstStrikeSeconds + k x strikeIntervalSeconds이고
    // 세로 줄(k >= N)은 가로 마지막 줄의 타격 시각 + axisGapSeconds + (k - N) x strikeIntervalSeconds다(axisGapSeconds는 가로 마지막 타격에서 세로 첫 타격까지의 시간)
    // 그 줄의 강조 예고는 타격 시각보다 firstStrikeSeconds 앞서 시작해 타격 시각에 끝난다. 타격한 줄은 그 순간 안전해진다
    // 늦게 접속한 클라이언트가 복원하려면 복제 계층이 시작 알림의 값과 예고, 타격 알림으로 센 진행 줄 수와 취소 여부를 상태로 들고 있어야 한다. 이 알림만으로는 복원되지 않는다
    /// <summary> 격자 순차 빔의 시작, 줄 예고, 줄 타격, 취소 알림 </summary>
    public interface IBossGridBeamListener
    {
        /// <summary> 시작, 배치는 이 시점에 고정되며 시전이 끝날 때까지 바뀌지 않는다. 시전 시작 시각은 서버 시각이다 </summary>
        void OnGridStarted(int castInstanceId, in BossGridBeamLayout layout, double castStartServerTime, float firstStrikeSeconds,
            float strikeIntervalSeconds, float axisGapSeconds);

        /// <summary> 줄 하나의 강조 예고가 시작됐다. 예정 시각보다 늦게 처리돼도 예고 시작 시각은 예정 시각 그대로 알린다 </summary>
        void OnLineTelegraphed(int castInstanceId, int lineId, EBossGridBeamAxis axis, int lineIndex, double telegraphServerTime,
            double strikeServerTime);

        /// <summary> 줄 하나의 타격이 확정됐다. 늦게 처리돼도 대상 위치는 처리 시점의 현재 위치다(과거 위치를 복원하지 않는다) </summary>
        void OnLineStruck(int castInstanceId, int lineId, EBossGridBeamAxis axis, int lineIndex, double scheduledServerTime,
            double processedServerTime, int hitCount);

        /// <summary> 중단됐다, 예고만 알린 줄과 아직 예고하지 않은 줄은 타격하지 않는다. 이미 적용된 피해는 되돌리지 않는다 </summary>
        void OnGridCancelled(int castInstanceId, int telegraphedCount, int struckCount, EBossInterruptReason reason);
    }
}
