namespace Shinshi.AI
{
    // 서버 패턴과 동시 원형 폭발 표현 사이의 연결 자리다. 복제 계층이 이 알림을 클라이언트로 전달하는 구성을 가정한다. 구현은 아직 없다
    // 13개 원은 시작 알림 하나의 layout으로 한꺼번에 알린다. 원마다 따로 알리지 않으므로 클라이언트는 모든 원의 예고를 같은 순간에 시작할 수 있다
    // 배치와 시각은 모두 서버가 확정한 값이고 클라이언트는 판정하지 않고 그대로 그린다
    //
    // 시각은 모두 시작 알림의 값으로 계산한다(시전 시작 = castStartServerTime)
    //   시전 시작 ~ strikeSeconds: layout의 13개 원을 바닥에 예고한다. 원 i의 중심과 반경은 layout.GetCenter(i), layout.GetRadius(i)다
    //   strikeSeconds: 13개 원이 같은 서버 시각에 한 번 판정한다. 폭발 뒤에는 남는 위험이 없다
    // 늦게 접속한 클라이언트가 복원하려면 복제 계층이 시작 알림의 값과 폭발, 취소 여부를 상태로 들고 있어야 한다. 이 알림만으로는 복원되지 않는다
    /// <summary> 동시 원형 폭발의 시작, 폭발, 취소 알림 </summary>
    public interface IBossCircleBurstListener
    {
        /// <summary> 시작, 배치는 이 시점에 고정되며 시전이 끝날 때까지 바뀌지 않는다. 폭발 예정 시각은 castStartServerTime + strikeSeconds다 </summary>
        void OnBurstStarted(int castInstanceId, in BossCircleBurstLayout layout, double castStartServerTime, float strikeSeconds);

        /// <summary> 13개 원이 폭발했다. 늦게 처리돼도 예정 시각은 그대로 알리고 실제 처리 시각은 따로 알린다. hitCount는 방어나 반격으로 막힌 유효 피격을 포함한 대상 수다 </summary>
        void OnBurstStruck(int castInstanceId, double scheduledServerTime, double processedServerTime, int hitCount);

        /// <summary> 중단됐다, 아직 폭발하지 않았다면 남은 예고를 정리한다. struck가 참이면 이미 폭발했다. 이미 적용된 피해는 되돌리지 않는다 </summary>
        void OnBurstCancelled(int castInstanceId, bool struck, EBossInterruptReason reason);
    }
}
