namespace Shinshi.AI
{
    // 정지는 성공도 실패도 아니다. 기믹을 건너뛰지 않고 보스는 무적으로 남으며 일반 선택도 열리지 않는다.
    // 끝내는 방법은 75% 부적진의 정지와 같은 서비스 장애 취소 경로를 쓰자는 제안이 있으나 아직 확정되지 않았다
    /// <summary> 색 결계 기믹이 진행하지 못하고 안전하게 멈춘 이유 </summary>
    public enum EWolbaekWardHaltReason
    {
        // 전장 중심과 반경을 얻지 못해 중앙 결계도 후보 검증도 할 수 없다
        ArenaUnavailable,

        // 검증된 후보 위치만으로는 세 지점의 이동 거리와 색 사이 거리 조건을 모두 채우는 배치를 만들 수 없다
        SitesInsufficient
    }
}
