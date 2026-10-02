namespace Shinshi.AI
{
    // 늦게 접속하거나 다시 관찰을 시작한 클라이언트가 진행 중인 장판을 복원하는 데 필요한 값 묶음이다
    // 최종 전송 형식은 네트워크 담당이 정한다. 끝난 장판은 보낼 필요가 없다
    /// <summary> 진행 중인 장판 한 개의 현재 상태, Start는 활성 입력이고 나머지는 활성 뒤에 바뀐 값이다 </summary>
    public struct BossAreaSnapshot
    {
        /****************************************
        *              Properties
        ****************************************/

        public BossAreaStart Start { get; set; }

        public EBossAreaFate Fate { get; set; }

        /// <summary> 서버가 마지막으로 알린 타격 회차, 없으면 음수. 복원한 표현이 같은 회차를 다시 표시하지 않게 하는 값이다 </summary>
        public int LastTickIndex { get; set; }

        /// <summary> 그 회차가 도래한 서버 시각 </summary>
        public double LastTickServerTime { get; set; }
    }
}
