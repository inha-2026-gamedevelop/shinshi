// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 파이프로 세션을 한 번 받아 본 결과, 수신 스레드가 만들어 메인 스레드로 넘긴다 </summary>
    public sealed class LauncherReceiveResult
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly ELauncherSessionFailure _failure;
        private readonly LauncherHandoff         _handoff;
        private readonly long                    _receivedTimestamp;

        // 실패 원인이 된 예외 타입 이름, 예외 메시지는 담지 않는다
        private readonly string                  _detail;

        /****************************************
        *              Properties
        ****************************************/

        public ELauncherSessionFailure Failure           => _failure;
        public LauncherHandoff         Handoff           => _handoff;
        public long                    ReceivedTimestamp => _receivedTimestamp;
        public string                  Detail            => _detail;
        public bool                    IsReceived        => _failure == ELauncherSessionFailure.None;

        /****************************************
        *                Methods
        ****************************************/

        private LauncherReceiveResult(ELauncherSessionFailure failure, LauncherHandoff handoff, long receivedTimestamp,
            string detail)
        {
            _failure           = failure;
            _handoff           = handoff;
            _receivedTimestamp = receivedTimestamp;
            _detail            = detail;
        }

        public static LauncherReceiveResult Received(LauncherHandoff handoff, long receivedTimestamp)
        {
            return new LauncherReceiveResult(ELauncherSessionFailure.None, handoff, receivedTimestamp, null);
        }

        public static LauncherReceiveResult Failed(ELauncherSessionFailure failure, string detail = null)
        {
            return new LauncherReceiveResult(failure, null, 0, detail);
        }
    }
}
