namespace Shinshi.AI
{
    // 마지막 노란 여우불이 부서진 순간 최종 성공과 함께 플레이어를 먼저 지켜야 같은 틱의 다른 피해와 전멸이 성공을 뒤집지 못한다.
    // 기믹은 자기 피해 게이트를 닫아 빨간 폭발을 무해하게 만들지만 보스 처치 정산까지 이어지는 동안 다른 피해원에서도 지키는 일은 이 창구가 맡는다.
    // 구현은 플레이어 서버 쪽이며 아직 없다
    /// <summary> 최종 성공이 확정된 순간 플레이어를 지키는 창구 </summary>
    public interface IWolbaekFinalGuard
    {
        /// <summary> 모든 참가자를 보스전 결말까지 피해와 사망에서 지킨다, 기믹이 최종 성공 알림보다 먼저 부른다 </summary>
        void GrantVictoryProtection(int castInstanceId, double serverTime);
    }
}
