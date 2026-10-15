namespace Shinshi.AI
{
    /// <summary> 월백 하드 0% 최후의 영역 기믹의 진행 단계 </summary>
    public enum EWolbaekFinalState
    {
        // 아직 발동하지 않았다, 체력이 0이어도 앞선 기믹의 인계를 기다리는 중이면 여기에 머문다
        Dormant,

        // 바닥 제거 예고와 노란 여우불 사냥이 진행 중이다, 보스는 무적이고 사망도 보류된다
        Active,

        // 마지막 노란 여우불을 부숴 최종 성공이 확정됐다, 사망 보류가 풀려 보스 처치가 이어진다
        Succeeded,

        // 파티가 전멸했다, 보스전 실패는 세션이 확정한다
        Failed,

        // 전장 정보나 바닥 구조가 없어 안전하게 멈췄다, 보스는 무적으로 남고 끝내는 방법은 정해지지 않았다
        Halted,

        // 사망이나 전투 종료로 멈췄다
        Stopped
    }
}
