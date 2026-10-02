// System
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 게임 서버가 백엔드 API를 부르는 유일한 창구, 호출은 비동기이고 결과는 메인 스레드의 Pump에서만 전달한다 </summary>
    public sealed class BackendGateway : IDisposable
    {
        // 틱 안에서 HTTP를 기다리면 그 프로세스의 모든 연결이 함께 멈춘다. 그래서 Send는 바로 돌아오고,
        // 전송, 시간 제한, 재시도는 스레드 풀에서 돌며, 끝난 결과만 큐에 넣는다. Unity 객체와 게임 상태는
        // Pump 안의 응답 처리기에서만 만진다. 응답이 오기까지 대상이 사라지거나 단계가 바뀔 수 있으므로
        // 받는 쪽은 Send가 돌려준 요청 번호로 자기 요청인지 보고 상태를 다시 확인한다.
        // 재시도는 operationId가 있는 요청만 한다. 없는 요청을 다시 보내면 API가 같은 효과를 두 번 낼 수 있다

        /****************************************
        *                Fields
        ****************************************/

        private const int STATUS_REQUEST_TIMEOUT    = 408;
        private const int STATUS_TOO_MANY_REQUESTS  = 429;
        private const int STATUS_SUCCESS_MIN        = 200;
        private const int STATUS_SUCCESS_MAX        = 299;
        private const int STATUS_SERVER_ERROR_MIN   = 500;

        private readonly IBackendTransport                        _transport;
        private readonly BackendCircuitBreaker                    _circuit;
        private readonly Func<TimeSpan, CancellationToken, Task>  _delay;
        private readonly Action<string>                           _warn;
        private readonly Action<string>                           _error;
        private readonly float[]                                  _retryDelaysSeconds;
        private readonly ConcurrentQueue<Completion>              _completed = new ConcurrentQueue<Completion>();
        private readonly CancellationTokenSource                  _shutdown  = new CancellationTokenSource();

        private int  _lastRequestId = 0;
        private int  _inFlight      = 0;
        private bool _isDisposed    = false;

        /****************************************
        *              Properties
        ****************************************/

        // 보냈지만 아직 결과가 큐에 들어가지 않은 요청 수, 저장 지연 진단에 쓴다
        public int InFlightCount => Volatile.Read(ref _inFlight);

        // Pump를 기다리는 결과 수
        public int CompletedCount => _completed.Count;

        public EBackendCircuitState CircuitState => _circuit.State;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> clock은 단조 증가 초, delay와 로그는 null이면 기본값을 쓴다 </summary>
        public BackendGateway(IBackendTransport transport, Func<double> clock, Func<TimeSpan, CancellationToken, Task> delay,
            Action<string> warn, Action<string> error)
        {
            if (transport == null)
            {
                throw new ArgumentNullException(nameof(transport));
            }

            _transport          = transport;
            _circuit            = new BackendCircuitBreaker(clock ?? MonotonicSeconds,
                Constants.Network.BACKEND_CIRCUIT_FAILURE_THRESHOLD, Constants.Network.BACKEND_CIRCUIT_OPEN_SECONDS);
            _delay              = delay ?? Task.Delay;
            _warn               = warn ?? IgnoreLog;
            _error              = error ?? IgnoreLog;
            _retryDelaysSeconds = Constants.Network.G_ApiRetryDelaysSeconds;
        }

        /// <summary> 프로세스 시작부터의 단조 증가 초, 벽시계 보정에 흔들리지 않는다 </summary>
        public static double MonotonicSeconds()
        {
            return Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        }

        /// <summary> 요청을 보내고 요청 번호를 돌려준다, 결과는 반드시 한 번 이후의 Pump에서 listener로 온다 </summary>
        public int Send(BackendRequest request, IBackendResponseListener listener)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (listener == null)
            {
                throw new ArgumentNullException(nameof(listener));
            }

            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(BackendGateway));
            }

            int requestId = NextRequestId();

            // 상한을 넘으면 보내지 않지만 결과 전달 경로는 같게 둔다. 호출자가 실패 처리를 한 곳에만 쓰게 한다.
            // 생존 신호는 ServerPresence가 종류별로 한 건씩만 띄우므로 상한에 넣지 않는다. 데이터 요청이 밀려도 heartbeat는 나간다
            if ((!request.BypassesCircuit) && (Volatile.Read(ref _inFlight) >= Constants.Network.MAX_BACKEND_IN_FLIGHT_REQUESTS))
            {
                _warn($"[Backend] 응답 대기 요청이 상한이라 보내지 않는다, path={request.Path}, op={request.OperationId}");
                _completed.Enqueue(new Completion(listener,
                    new BackendResponse(requestId, EBackendOutcome.Overloaded, 0, 0, null, 0)));
                return requestId;
            }

            Interlocked.Increment(ref _inFlight);

            // Unity 동기화 문맥을 잡지 않도록 스레드 풀에서 시작한다. 잡으면 await 뒤가 메인 스레드로 돌아와 틱을 쓴다
            Task.Run(() => RunAsync(requestId, request, listener));
            return requestId;
        }

        /// <summary> 메인 스레드에서 매 프레임 부른다, 끝난 요청의 결과를 프레임당 상한까지 전달한다 </summary>
        public int Pump()
        {
            int delivered = 0;
            while ((delivered < Constants.Network.MAX_BACKEND_COMPLETIONS_PER_PUMP) && (_completed.TryDequeue(out Completion completion)))
            {
                ++delivered;
                try
                {
                    BackendResponse response = completion.Response;
                    completion.Listener.OnBackendResponse(in response);
                }
                catch (Exception exception)
                {
                    // 처리기 하나의 결함이 다른 요청의 결과 전달을 막지 않게 한다. 메시지에 응답 값이 섞일 수 있어 타입만 남긴다
                    _error($"[Backend] 응답 처리기 예외 ({exception.GetType().Name}), requestId={completion.Response.RequestId}");
                }
            }

            return delivered;
        }

        /// <summary> 진행 중인 요청을 취소하고 남은 결과를 버린다, 서버 종료 때 한 번 부른다 </summary>
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            // 실행 중인 작업이 아직 토큰을 읽으므로 CancellationTokenSource는 취소만 하고 해제하지 않는다
            _shutdown.Cancel();
            while (_completed.TryDequeue(out _))
            {
            }

            _transport.Dispose();
        }

        private async Task RunAsync(int requestId, BackendRequest request, IBackendResponseListener listener)
        {
            BackendResponse response;
            try
            {
                response = await ExecuteWithRetryAsync(requestId, request).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                Interlocked.Decrement(ref _inFlight);
                return;
            }
            catch (Exception exception)
            {
                _error($"[Backend] 요청 처리 중 예외 ({exception.GetType().Name}), path={request.Path}, op={request.OperationId}");
                response = new BackendResponse(requestId, EBackendOutcome.InvalidResponse, 0, 0, null, 0);
            }

            Interlocked.Decrement(ref _inFlight);
            if (!_shutdown.IsCancellationRequested)
            {
                _completed.Enqueue(new Completion(listener, response));
            }
        }

        private async Task<BackendResponse> ExecuteWithRetryAsync(int requestId, BackendRequest request)
        {
            int  maxAttempts = request.IsRetryable ? (_retryDelaysSeconds.Length + 1) : 1;
            bool usesCircuit = !request.BypassesCircuit;
            BackendResponse last = default;
            for (int attempt = 1; attempt <= maxAttempts; ++attempt)
            {
                // 자격이 없어 보내지 못한 시도는 백엔드 상태와 무관하다. 회로를 잡기 전에 걸러 실패로도 성공으로도 세지 않는다
                if (!_transport.CanSend(request))
                {
                    return new BackendResponse(requestId, EBackendOutcome.LeaseUnavailable, last.StatusCode, last.ReasonCode,
                        null, attempt - 1);
                }

                // 생존 신호는 회로를 잡지 않는다. 반열림의 시험 호출 한 자리를 데이터 요청과 다투지 않게 결과도 세지 않는다
                if ((usesCircuit) && (!_circuit.TryAcquire()))
                {
                    _warn($"[Backend] 회로가 열려 보내지 않는다, path={request.Path}, op={request.OperationId}, attempt={attempt}");
                    return new BackendResponse(requestId, EBackendOutcome.CircuitOpen, last.StatusCode, last.ReasonCode, null,
                        attempt - 1);
                }

                last = await AttemptAsync(requestId, request, attempt).ConfigureAwait(false);
                bool transient = IsTransient(last.Outcome);
                if (usesCircuit)
                {
                    _circuit.Record(transient);
                }

                // Retry-After가 있으면 안에서 기다리지 않는다. 응답 대기 자리를 붙잡지 않고 호출자가 그 뒤로 다시 부른다
                if ((!transient) || (attempt == maxAttempts) || (last.RetryAfterSeconds > 0.0))
                {
                    break;
                }

                float delaySeconds = _retryDelaysSeconds[attempt - 1];
                _warn($"[Backend] 일시 실패로 같은 operationId를 다시 보낸다, path={request.Path}, op={request.OperationId}, " +
                    $"outcome={last.Outcome}, status={last.StatusCode}, attempt={attempt}, delay={delaySeconds}");
                await _delay(TimeSpan.FromSeconds(delaySeconds), _shutdown.Token).ConfigureAwait(false);
            }

            if (!last.IsSucceeded)
            {
                _warn($"[Backend] 요청 실패, path={request.Path}, op={request.OperationId}, outcome={last.Outcome}, " +
                    $"status={last.StatusCode}, reasonCode={last.ReasonCode}, attempts={last.Attempts}");
            }

            return last;
        }

        private async Task<BackendResponse> AttemptAsync(int requestId, BackendRequest request, int attempt)
        {
            BackendTransportResult result;
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));
                try
                {
                    result = await _transport.SendAsync(request, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!_shutdown.IsCancellationRequested)
                {
                    return new BackendResponse(requestId, EBackendOutcome.Timeout, 0, 0, null, attempt);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // 연결 실패 예외 메시지에는 주소가 들어갈 수 있어 타입만 남긴다
                    _warn($"[Backend] 전송 실패 ({exception.GetType().Name}), path={request.Path}, op={request.OperationId}, attempt={attempt}");
                    return new BackendResponse(requestId, EBackendOutcome.NetworkError, 0, 0, null, attempt);
                }
            }

            return Classify(requestId, request, result, attempt);
        }

        private static BackendResponse Classify(int requestId, BackendRequest request, in BackendTransportResult result, int attempt)
        {
            int status = result.StatusCode;
            if ((status >= STATUS_SUCCESS_MIN) && (status <= STATUS_SUCCESS_MAX))
            {
                if (request.ResponseType == null)
                {
                    return new BackendResponse(requestId, EBackendOutcome.Succeeded, status, 0, null, attempt);
                }

                if (!BackendJson.TryDeserialize(result.Body, request.ResponseType, out object payload))
                {
                    return new BackendResponse(requestId, EBackendOutcome.InvalidResponse, status, 0, null, attempt);
                }

                return new BackendResponse(requestId, EBackendOutcome.Succeeded, status, 0, payload, attempt);
            }

            int reasonCode = BackendJson.ReadReasonCode(result.Body);
            if ((status == STATUS_REQUEST_TIMEOUT) || (status == STATUS_TOO_MANY_REQUESTS) || (status >= STATUS_SERVER_ERROR_MIN))
            {
                return new BackendResponse(requestId, EBackendOutcome.ServerError, status, reasonCode, null, attempt,
                    result.RetryAfterSeconds);
            }

            return new BackendResponse(requestId, EBackendOutcome.Rejected, status, reasonCode, null, attempt);
        }

        // 다시 보내면 결과가 달라질 수 있는 실패만 고른다. 거절과 계약 불일치는 몇 번을 보내도 같다
        private static bool IsTransient(EBackendOutcome outcome)
        {
            return (outcome == EBackendOutcome.Timeout) || (outcome == EBackendOutcome.NetworkError)
                || (outcome == EBackendOutcome.ServerError);
        }

        // 메인 스레드에서만 부르므로 잠금이 필요 없다
        private int NextRequestId()
        {
            ++_lastRequestId;

            // 음수로 돌면 1부터 다시 쓴다. 0은 요청 없음이다
            if (_lastRequestId <= 0)
            {
                _lastRequestId = 1;
            }

            return _lastRequestId;
        }

        private static void IgnoreLog(string message) { }

        /****************************************
        *              Completion
        ****************************************/

        private readonly struct Completion
        {
            private readonly IBackendResponseListener _listener;
            private readonly BackendResponse          _response;

            public IBackendResponseListener Listener => _listener;
            public BackendResponse          Response => _response;

            public Completion(IBackendResponseListener listener, in BackendResponse response)
            {
                _listener = listener;
                _response = response;
            }
        }
    }
}
