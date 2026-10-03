namespace Shinshi.AI
{
    // 늦게 접속하거나 다시 관찰을 시작한 클라이언트가 진행 중인 별형 빔을 복원하는 데 필요한 값 묶음이다. 최종 전송 형식은 네트워크 담당이 정한다
    // 회전각은 보내지 않는다. 활성 서버 시각과 회전 속도로 표현이 현재 서버 시각의 각도를 계산한다
    /// <summary> 별형 빔 구조 하나의 현재 상태 </summary>
    public struct BossStarSnapshot
    {
        public BossStarStart Start { get; set; }

        public bool IsActivated { get; set; }

        /// <summary> 활성된 서버 시각, IsActivated가 거짓이면 뜻이 없다 </summary>
        public double ActivateServerTime { get; set; }

        public EBossAreaFate Fate { get; set; }

        /// <summary> 서버가 마지막으로 알린 타격 회차, 없으면 음수 </summary>
        public int LastTickIndex { get; set; }

        public double LastTickServerTime { get; set; }
    }
}
