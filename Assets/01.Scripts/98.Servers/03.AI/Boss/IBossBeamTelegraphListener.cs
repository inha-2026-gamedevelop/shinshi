namespace Shinshi.AI
{
    // 서버 패턴과 빔 예고 표현 사이의 연결 자리다. 복제 계층이 이 알림을 클라이언트로 전달하는 구성을 가정한다. 구현은 아직 없다
    // 빔이 활성된 뒤는 IBossBeamListener가 알린다. 패턴이 끝나도 빔은 남을 수 있어 두 알림을 나눴다
    // 한 시전이 빔을 여러 줄 설치하면 줄마다 예고 알림이 한 번씩 오고 빔 번호로 구분한다
    /// <summary> 빔 패턴의 예고 시작과 활성 전 취소 알림 </summary>
    public interface IBossBeamTelegraphListener
    {
        /// <summary> 예고 시작, shape는 이 시전이 끝날 때까지 바뀌지 않는다(타깃이 움직이거나 죽어도, 보스가 움직여도 그대로) </summary>
        void OnTelegraphStarted(int castInstanceId, int beamId, in BossBeamShape shape, float telegraphSeconds,
            float durationSeconds, float tickInterval);

        /// <summary> 활성 전에 중단됐다, 예약한 슬롯은 돌려줬다. 활성한 뒤에는 오지 않고 남은 빔은 IBossBeamListener가 알린다 </summary>
        void OnCancelled(int castInstanceId, int beamId, EBossInterruptReason reason);
    }
}
