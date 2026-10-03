namespace Shinshi.AI
{
    // 패턴 객체는 배치 고정과 예고와 설치만 맡고, 활성된 빔의 수명과 주기 피해와 정리는 빔 관리자가 맡는다
    // 슬롯은 예고 시작 때 빔마다 예약하고 활성 때 그 예약을 빔으로 바꾼다. 같은 슬롯을 두 번 세지 않고 정확히 한 번 돌려준다
    /// <summary> 빔 예약과 활성 창구, 구현은 BossBeamManager다 </summary>
    public interface IBossBeamHost
    {
        /// <summary> 예고 시작 때 빔 한 줄의 슬롯을 예약한다. 빈 슬롯이 없거나 같은 빔 번호의 예약이 있으면 거짓이며 기존 빔은 지우지 않는다 </summary>
        bool TryReserve(int castInstanceId, int beamId, string patternId);

        /// <summary> 예고 중 취소나 시작 실패로 예약을 돌려준다, 이미 활성됐거나 예약이 없으면 아무것도 하지 않는다 </summary>
        void ReleaseReservation(int beamId);

        /// <summary> 예약을 활성 빔으로 바꾼다. 활성 순간의 첫 타격은 관리자의 다음 Tick(같은 BossCombat 틱 안)이 처리하므로 여기서 피해를 내지 않는다. 예약이 없으면 거짓이다 </summary>
        bool TryActivate(in BossBeamSpec spec);
    }
}
