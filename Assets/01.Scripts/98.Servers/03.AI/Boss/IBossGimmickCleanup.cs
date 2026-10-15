namespace Shinshi.AI
{
    // IBossGimmick만으로는 두뇌가 기믹 진입 순간에 진행 중인 공격과 지속물을 정리할 수 없고 사망과 종료를 기믹에 알릴 수도 없다.
    // 기존 기믹 계약에 멤버를 더하면 구현과 테스트가 깨지므로 선택 계약으로 나눈다
    /// <summary> 기믹이 두뇌에 요청하는 진입 정리와 두뇌가 기믹에 알리는 정지 </summary>
    public interface IBossGimmickCleanup
    {
        /// <summary> 이번 틱에 기믹이 진입해 진행 중인 공격과 지속물을 정리해야 한다, 한 번 읽으면 내려간다 </summary>
        bool ConsumeCleanupRequest();

        /// <summary> 사망이나 전투 종료로 두뇌가 전투를 끊었다 </summary>
        void Stop(EBossInterruptReason reason);
    }
}
