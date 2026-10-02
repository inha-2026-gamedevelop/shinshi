namespace Shinshi.AI
{
    // 늦게 접속하거나 다시 관찰을 시작한 클라이언트가 날고 있는 투사체를 복원하는 데 필요한 값 묶음이다
    // 최종 전송 형식은 네트워크 담당이 정한다. 끝난 투사체는 보낼 필요가 없다
    /// <summary> 진행 중인 투사체 한 발의 현재 상태, Start는 발사 입력이고 나머지는 발사 뒤에 바뀐 값이다 </summary>
    public struct BossProjectileSnapshot
    {
        /****************************************
        *              Properties
        ****************************************/

        public BossProjectileStart Start { get; set; }

        public EBossProjectileFate Fate { get; set; }

        /// <summary> 소멸 위치까지 발사 위치에서 잰 거리, Fate가 Flying이 아닐 때만 뜻이 있다 </summary>
        public float EndDistance { get; set; }
    }
}
