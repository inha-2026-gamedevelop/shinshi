namespace Shinshi.Network
{
    /// <summary> 전송 완료를 Unity 메인 스레드에서 받는 대상, 성공은 배치 전체의 내구 저장 ACK다 </summary>
    public interface IBackendEventSinkListener
    {
        /// <summary> 전송에 받은 attemptId를 그대로 돌려준다, 일시 실패는 같은 배치를 재시도하고 확정 거절은 큐를 멈춘다.
        /// retryAfterSeconds는 API가 정한 최소 대기(초)이고 일시 실패에서만 쓴다 </summary>
        void OnBatchCompleted(long attemptId, EEventBatchResult result, double retryAfterSeconds = 0.0);
    }
}
