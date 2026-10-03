namespace Shinshi.AI
{
    // 패턴 객체는 배치 고정과 회전 방향 추첨과 예고만 맡고, 활성된 별형 빔의 회전과 주기 피해와 정리는 별형 관리자가 맡는다
    // 슬롯은 예고 시작 때 예약하고 활성 때 그 예약을 구조로 바꾼다. 같은 슬롯을 두 번 세지 않고 정확히 한 번 돌려준다
    /// <summary> 별형 빔 예약과 활성 창구, 구현은 BossStarManager다 </summary>
    public interface IBossStarHost
    {
        /// <summary> 예고 시작 때 구조 하나의 슬롯을 예약한다. 빈 슬롯이 없거나 같은 구조 번호의 예약이 있으면 거짓이며 기존 구조는 지우지 않는다 </summary>
        bool TryReserve(int castInstanceId, int starId, string patternId);

        /// <summary> 예고 중 취소나 시작 실패로 예약을 돌려준다, 이미 활성됐거나 예약이 없으면 아무것도 하지 않는다 </summary>
        void ReleaseReservation(int starId);

        /// <summary> 예약을 활성 구조로 바꾼다. 활성 순간의 첫 타격은 관리자의 다음 Tick(같은 BossCombat 틱 안)이 처리하므로 여기서 피해를 내지 않는다. 예약이 없으면 거짓이다 </summary>
        bool TryActivate(in BossStarSpec spec);
    }
}
