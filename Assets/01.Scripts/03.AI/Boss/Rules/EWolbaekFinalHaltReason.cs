namespace Shinshi.AI
{
    // 정지는 성공도 실패도 아니다. 기믹을 건너뛰지 않고 보스는 무적으로 남으며 끝내는 방법은 정해지지 않았다
    /// <summary> 최후의 영역이 진행하지 못하고 안전하게 멈춘 이유 </summary>
    public enum EWolbaekFinalHaltReason
    {
        // 전장 중심과 반경을 얻지 못했다
        ArenaUnavailable,

        // 검증된 노란 여우불 위치가 모자란다
        SitesInsufficient,

        // 외곽 바닥을 제거할 구조가 없거나 제거를 거절했다
        FloorUnavailable,

        // 노란 여우불을 피격 대상으로 등록하지 못했다
        RegistryUnavailable
    }
}
