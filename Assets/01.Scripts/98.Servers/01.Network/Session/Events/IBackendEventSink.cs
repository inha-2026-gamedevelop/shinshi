namespace Shinshi.Network
{
    /// <summary> 세션 이벤트를 HTTPS JSON 저장 경로로 비차단 전송하는 어댑터, Unity 메인 스레드에서 호출한다 </summary>
    public interface IBackendEventSink
    {
        // events의 앞 count개는 완료 콜백까지 읽기 전용으로 빌린다. 고치지 않고 완료 뒤 참조를 남기지 않는다.
        // 실패 뒤 재시도도 같은 순번과 payload를 저장하며, API가 세션과 순번으로 중복 효과를 막는다.
        // HTTP 응답은 Unity 메인 스레드로 넘겨야 한다. 동기 구현은 이 호출 안에서 완료를 알릴 수 있다.
        // 시작한 전송은 완료 콜백으로만 끝낸다. 시작 뒤 예외를 던지면 큐가 같은 배치를 다시 시작할 수 있다
        /// <summary> 시작 성공이면 참과 완료 콜백 1회, 시작 불가면 거짓과 콜백 없음, 틱을 기다리거나 차단하지 않는다 </summary>
        bool TrySend(string sessionId, long attemptId, SessionEvent[] events, int count,
            IBackendEventSinkListener listener);
    }
}
