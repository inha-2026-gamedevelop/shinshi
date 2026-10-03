namespace Shinshi.AI
{
    // 늦게 접속하거나 다시 관찰을 시작한 클라이언트가 진행 중인 빔을 복원하는 데 필요한 값 묶음이다. 최종 전송 형식은 네트워크 담당이 정한다
    // 끝난 빔은 보낼 필요가 없다. 예고 중인 빔은 활성 여부만 거짓으로 보낸다
    /// <summary> 빔 한 줄의 현재 상태 </summary>
    public struct BossBeamSnapshot
    {
        public BossBeamStart Start { get; set; }

        /// <summary> 서버가 활성을 알렸는가 </summary>
        public bool IsActivated { get; set; }

        /// <summary> 활성된 서버 시각, IsActivated가 거짓이면 뜻이 없다 </summary>
        public double ActivateServerTime { get; set; }

        public EBossAreaFate Fate { get; set; }

        /// <summary> 서버가 마지막으로 알린 타격 회차, 없으면 음수 </summary>
        public int LastTickIndex { get; set; }

        /// <summary> 그 회차가 도래한 서버 시각 </summary>
        public double LastTickServerTime { get; set; }
    }
}
