namespace Shinshi.AI
{
    // 서버 패턴과 잔류 부채꼴 연쇄 표현 사이의 연결 자리다. 복제 계층이 이 알림을 클라이언트로 전달하는 구성을 가정한다. 구현은 아직 없다
    // 배치와 시각은 모두 서버가 확정한 값이고 클라이언트는 추첨, 판정을 하지 않고 그대로 그린다
    //
    // 시각은 모두 시작 알림의 값으로 계산한다(시전 시작 = castStartServerTime)
    //   시전 시작 ~ safeRevealSeconds: 여우불로 피난 방향(layout.SafeDirection 가운데, layout.SafeArc 폭)만 보여 준다
    //   safeRevealSeconds ~ firstBlastSeconds: 폭발 구역 전체의 바닥을 예고한다
    //   구역 k(0부터)의 폭발 시각은 castStartServerTime + firstBlastSeconds + k x blastIntervalSeconds다
    //   폭발한 구역은 마지막 폭발 뒤 lingerSeconds가 지날 때까지 위험이 남는다. 구역마다 따로 끝나지 않고 모두 같은 시각에 사라진다
    // 늦게 접속한 클라이언트가 복원하려면 복제 계층이 시작 알림의 값과 폭발 알림으로 센 진행 구역 수, 위험 종료와 취소 여부를 상태로 들고 있어야 한다. 이 알림만으로는 복원되지 않는다
    /// <summary> 잔류 부채꼴 연쇄의 시작, 구역 폭발, 위험 종료, 취소 알림 </summary>
    public interface IBossChainFanListener
    {
        /// <summary> 시작, 배치는 이 시점에 고정되며 시전이 끝날 때까지 바뀌지 않는다. 시전 시작 시각은 서버 시각이다 </summary>
        void OnChainFanStarted(int castInstanceId, in BossChainFanLayout layout, double castStartServerTime, float safeRevealSeconds,
            float firstBlastSeconds, float blastIntervalSeconds, float lingerSeconds);

        /// <summary> 구역 하나가 폭발했다. 늦게 처리돼도 폭발 시각은 예정 시각 그대로 알린다. hitCount는 그 순간 확정된 피격 수이며 이전 구역의 잔류 피격도 포함한다 </summary>
        void OnChainFanBlasted(int castInstanceId, int zoneIndex, double scheduledServerTime, double processedServerTime, int hitCount);

        /// <summary> 마지막 폭발 뒤 잔류 시간이 끝나 모든 구역이 안전해졌다 </summary>
        void OnChainFanDangerEnded(int castInstanceId, double scheduledServerTime);

        /// <summary> 중단됐다, 아직 폭발하지 않은 구역과 남은 잔류 위험은 정리한다. 이미 적용된 피해는 되돌리지 않는다 </summary>
        void OnChainFanCancelled(int castInstanceId, int blastedCount, bool dangerEnded, EBossInterruptReason reason);
    }
}
