// System
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Shinshi.Network
{
    /// <summary> 요청 한 번을 실제로 보내는 계층, 게이트웨이가 스레드 풀에서 부른다 </summary>
    public interface IBackendTransport : IDisposable
    {
        // 게이트웨이가 시도마다 보내기 전에 묻는다. 거짓이면 보내지 않고 LeaseUnavailable로 끝낸다.
        // 회로 차단기를 거치기 전에 물어야 자격이 없는 시도가 백엔드 장애로 세어지지 않는다
        bool CanSend(BackendRequest request);

        // cancellationToken이 취소되면 OperationCanceledException으로 끝낸다. 게이트웨이는 그것으로 시간 초과를 판단한다.
        // 연결 실패는 예외로 던지고, HTTP 상태 코드는 예외 없이 결과에 담는다
        Task<BackendTransportResult> SendAsync(BackendRequest request, CancellationToken cancellationToken);
    }
}
