namespace Shinshi.AI
{
    // 서버 연결(구조물 파괴 등)과 표현이 받는 알림이다. 패턴이 스스로 가진 상태로 정한 결과를 알리는 것이라
    // 이 알림을 받는 쪽이 게임 판정을 대신 정하지 않는다. 한 시전에서 한 번만 불린다
    /// <summary> 돌진이 정상 종료됐는지 충돌로 끝났는지와 그에 따른 회복 시간을 알린다 </summary>
    public interface IBossChargeOutcomeListener
    {
        /// <summary> 돌진이 끝났다, 충돌이 아니면 collision의 종류는 None이다. recoverySeconds는 실행 종료 경계부터 재는 회복 시간이다 </summary>
        void OnChargeEnded(int castInstanceId, EBossChargeEnd end, BossChargeCollision collision, float recoverySeconds);
    }
}
