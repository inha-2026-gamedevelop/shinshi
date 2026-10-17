namespace Shinshi.AI
{
    // 컷신처럼 보스 Animator와 표시 위치를 잠시 통째로 쓰는 표현이 구현한다. BossView.AcquireExternalControl로 제어권을 얻는다.
    // 처치, Reset, 전투 종료, 파괴처럼 BossView가 먼저 자기 표현을 이어 가야 하는 순간에는 BossView가 이 알림으로 제어권을 거둔다.
    // 알림을 받은 쪽은 자기가 만든 객체와 바꾼 표현 상태만 즉시 되돌리고 BossView의 모션은 건드리지 않는다. 처치 모션은 알림 뒤에 시작된다
    /// <summary> BossView의 몸 표현을 잠시 넘겨받는 소유자 </summary>
    public interface IBossViewControlOwner
    {
        /// <summary> BossView가 제어권을 거뒀다. 자기 객체와 바꾼 상태만 즉시 정리한다 </summary>
        void OnViewControlRevoked(EBossViewControlRevoke reason);
    }
}
