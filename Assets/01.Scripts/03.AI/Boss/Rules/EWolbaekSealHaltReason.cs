namespace Shinshi.AI
{
    // 정지는 성공도 실패도 아니다. 기믹을 건너뛰지 않고 보스는 무적으로 남으며 일반 선택도 열리지 않는다.
    // 끝내는 방법(서비스 장애 취소, 재시도)은 정해지지 않았다
    /// <summary> 여우불 유도와 봉인 핵 기믹이 진행하지 못하고 안전하게 멈춘 이유 </summary>
    public enum EWolbaekSealHaltReason
    {
        // 전장 중심과 반경을 얻지 못해 핵 후보를 검증할 수 없다
        ArenaUnavailable,

        // 검증된 후보만으로는 한 차례에 필요한 핵 수를 최소 간격을 지켜 놓을 수 없다
        SitesInsufficient
    }
}
