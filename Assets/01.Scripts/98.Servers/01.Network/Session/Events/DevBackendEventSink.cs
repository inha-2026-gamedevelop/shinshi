#if UNITY_EDITOR || DEVELOPMENT_BUILD
// Unity
using UnityEngine;

namespace Shinshi.Network
{
    /// <summary> 서비스 자격 없이 던전을 시험할 때 쓰는 개발 전용 저장 어댑터, 기록만 남기고 바로 저장 성공을 알린다 </summary>
    public sealed class DevBackendEventSink : IBackendEventSink
    {
        // 저장 성공으로 처리하므로 경제 결과가 어디에도 남지 않는다. 출시 빌드에는 타입 자체가 없어 실수로 쓸 수 없다

        /****************************************
        *                Methods
        ****************************************/

        public bool TrySend(string sessionId, long attemptId, SessionEvent[] events, int count, IBackendEventSinkListener listener)
        {
            if ((listener == null) || (events == null) || (count <= 0) || (count > events.Length))
            {
                return false;
            }

            // 배치는 1초에 한 번 이하라 이때만 문자열을 만든다
            Debug.Log($"[SessionEvents] 개발 저장 - session={sessionId}, seq={events[0].Seq}~{events[count - 1].Seq}, count={count}");
            listener.OnBatchCompleted(attemptId, EEventBatchResult.Committed);
            return true;
        }
    }
}
#endif
