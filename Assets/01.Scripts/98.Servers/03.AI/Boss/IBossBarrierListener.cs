namespace Shinshi.AI
{
    // 결계 전개의 상태 알림이다. 복제 계층이 클라이언트로 전달하는 구성을 가정하며 구현은 아직 없다
    // 서버 프로세스의 C# 호출은 원격 클라이언트에 닿지 않으므로 실제 연결에서는 ClientRpc와 늦은 접속 복원이 따로 필요하다
    /// <summary> 결계 전개의 예고 시작, 활성, 시퀀스 완료, 정리 알림 </summary>
    public interface IBossBarrierListener
    {
        /// <summary> 보스가 전개 위치에 닿아 요력 방출과 위험 구역 예고를 시작했다, 이 시점의 값으로 이후 단계를 재현할 수 있다 </summary>
        void OnBarrierStarted(in BossBarrierStart start);

        /// <summary> 예고가 끝나 위험 구역이 활성됐다, 이 시각부터 피해가 난다 </summary>
        void OnBarrierActivated(int barrierId, double activateServerTime);

        /// <summary> 전개 시퀀스가 끝났다, 위험 구역은 유지되고 2페이즈 인계는 이 알림을 받은 쪽이 맡는다 </summary>
        void OnBarrierCompleted(int barrierId, double completeServerTime);

        /// <summary> 위험 구역을 정리했다, 전투 종료와 사망과 리셋이 부른다. 예고 전에 끊기면 barrierId가 없어 0이다 </summary>
        void OnBarrierCleared(int barrierId, EBossInterruptReason reason);
    }
}
