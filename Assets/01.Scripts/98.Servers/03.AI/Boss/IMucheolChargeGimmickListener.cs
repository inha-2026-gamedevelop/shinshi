namespace Shinshi.AI
{
    // 기믹이 이미 정한 그로기 진행을 알리는 것이라 받는 쪽이 그로기 판정을 대신 정하지 않는다. 서버 연결(BossMucheolEventRelay)이 표현 사건으로 바꾼다.
    // 사망과 전투 종료로 멈출 때는 알리지 않는다. 그때는 복제 상태가 전용 기록을 모두 비운다
    /// <summary> 무철 돌진 유도 기믹의 마지막 파괴 확인, 그로기 시작, 그로기 취소 알림 </summary>
    public interface IMucheolChargeGimmickListener
    {
        /// <summary> 마지막 지정 구조물 파괴가 확인됐다, castInstanceId는 부순 시전이고 모르면 0이다. confirmServerTime은 확인 시각이다 </summary>
        void OnGroggyPending(int castInstanceId, int structureId, double confirmServerTime);

        /// <summary> 그로기가 시작됐다, endServerTime부터는 그로기가 아니다 </summary>
        void OnGroggyStarted(int castInstanceId, int structureId, double startServerTime, double endServerTime);

        /// <summary> 대기 중이거나 진행 중인 그로기를 사망과 전투 종료가 아닌 이유로 접었다 </summary>
        void OnGroggyCancelled(int castInstanceId);
    }
}
