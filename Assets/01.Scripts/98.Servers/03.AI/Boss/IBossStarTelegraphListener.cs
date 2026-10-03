namespace Shinshi.AI
{
    // 서버 패턴과 별형 빔 예고 표현 사이의 연결 자리다. 복제 계층이 이 알림을 클라이언트로 전달하는 구성을 가정한다. 구현은 아직 없다
    // 별형 빔이 활성된 뒤는 IBossStarListener가 알린다. 한 시전은 구조 하나를 설치하고 구조 안의 빔은 꼭짓점 번호 순서다
    /// <summary> 별형 빔 패턴의 예고 시작과 활성 전 취소 알림 </summary>
    public interface IBossStarTelegraphListener
    {
        /// <summary> 예고 시작, shape(중심, 반경, 기준 방향, 꼭짓점 수, 연결, 폭, 회전 방향과 속도)는 이 시전이 끝날 때까지 바뀌지 않는다 </summary>
        void OnTelegraphStarted(int castInstanceId, int starId, in BossStarShape shape, float telegraphSeconds,
            float durationSeconds, float tickInterval);

        /// <summary> 활성 전에 중단됐다, 예약한 슬롯은 돌려줬다. 활성한 뒤에는 오지 않고 남은 구조는 IBossStarListener가 알린다 </summary>
        void OnCancelled(int castInstanceId, int starId, EBossInterruptReason reason);
    }
}
