namespace Shinshi.AI
{
    // 정지는 성공도 실패도 아니다. 기믹을 건너뛰지 않고 보스는 무적으로 남으며 일반 선택도 열리지 않는다.
    // 이 상태를 어떻게 끝낼지(전투 취소, 재시도, 다른 처리)는 아직 정해지지 않았고 알림을 받은 세션과 기획이 정한다
    /// <summary> 부적진 기믹이 진행하지 못하고 안전하게 멈춘 이유 </summary>
    public enum EWolbaekHaltReason
    {
        // 전장 중심과 반경을 얻지 못해 구조물을 놓을 곳도 보스가 갈 곳도 정할 수 없다
        ArenaUnavailable,

        // 전환 시간이 끝났는데 월백이 전장 중앙에 닿지 못했다, 벽이나 서버 몸체의 이동 거절 때문이다
        MovementBlocked
    }
}
