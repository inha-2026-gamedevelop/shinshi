namespace Shinshi.AI
{
    // 패턴 객체는 위치 고정과 예고와 설치만 맡고, 활성된 장판의 수명과 주기 피해와 정리는 패턴이 끝나도 이어지므로 장판 관리자가 맡는다
    // 슬롯은 예고 시작 때 예약하고 활성 때 그 예약을 장판으로 바꾼다. 같은 슬롯을 두 번 세지 않고 정확히 한 번 돌려준다
    /// <summary> 장판 예약과 활성 창구, 구현은 BossAreaManager다 </summary>
    public interface IBossAreaHost
    {
        /// <summary> 예고 시작 때 슬롯 하나를 예약한다. 같은 패턴의 예약과 활성 장판이 maxConcurrent개이면 거짓이며 기존 장판은 지우지 않는다 </summary>
        bool TryReserve(int castInstanceId, string patternId, int maxConcurrent);

        /// <summary> 장판 번호를 따로 두는 예약, 같은 패턴의 예약과 활성 장판이 maxConcurrent개이면 거짓이다. 이미 같은 장판 번호의 예약이 있어도 거짓이다 </summary>
        bool TryReserve(int castInstanceId, int areaId, string patternId, int maxConcurrent);

        /// <summary> 패턴 ID와 상관없이 보스의 모든 장판(예약과 활성)이 maxTotal개이면 거짓인 예약, 기존 장판은 지우지 않는다. 검사와 예약이 한 번에 이뤄져 사이에 조건이 변해도 초과하지 않는다 </summary>
        bool TryReserveWithinTotal(int castInstanceId, int areaId, string patternId, int maxTotal);

        /// <summary> 예고 중 취소나 시작 실패로 예약을 돌려준다, 이미 활성됐거나 예약이 없으면 아무것도 하지 않는다. 키는 장판 번호다(단일 장판은 시전 번호와 같다) </summary>
        void ReleaseReservation(int areaId);

        /// <summary> 예약을 활성 장판으로 바꾼다. 활성 순간의 첫 타격은 관리자의 다음 Tick(같은 BossCombat 틱 안)이 처리하므로 여기서 피해를 내지 않는다. 예약이 없으면 거짓이다 </summary>
        bool TryActivate(in BossAreaSpec spec);
    }
}
