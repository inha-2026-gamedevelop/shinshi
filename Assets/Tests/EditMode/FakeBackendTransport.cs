// System
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Network;

namespace Shinshi.Tests
{
    /// <summary> 게이트웨이 시험용 가짜 전송기, 응답을 함수로 정하고 받은 요청과 횟수를 기록한다 </summary>
    public sealed class FakeBackendTransport : IBackendTransport
    {
        // 게이트웨이는 스레드 풀에서 부르므로 기록은 스레드 안전한 형태로 둔다

        /****************************************
        *                Fields
        ****************************************/

        private const int WAIT_TIMEOUT_MILLISECONDS = 5000;
        private const int WAIT_POLL_MILLISECONDS    = 5;

        private readonly Func<BackendRequest, CancellationToken, Task<BackendTransportResult>> _respond;
        private readonly ConcurrentQueue<BackendRequest>                                       _requests = new ConcurrentQueue<BackendRequest>();

        private int _calls = 0;

        /****************************************
        *              Properties
        ****************************************/

        public int  Calls      => Volatile.Read(ref _calls);
        public bool IsDisposed { get; private set; }

        public ConcurrentQueue<BackendRequest> Requests => _requests;

        /****************************************
        *                Methods
        ****************************************/

        public FakeBackendTransport(Func<BackendRequest, CancellationToken, Task<BackendTransportResult>> respond)
        {
            _respond = respond;
        }

        public static FakeBackendTransport Always(int statusCode, string body)
        {
            return new FakeBackendTransport((request, token) => Task.FromResult(new BackendTransportResult(statusCode, body)));
        }

        // 취소될 때까지 응답하지 않는다. 게이트웨이의 시간 제한 경로를 시험한다
        public static FakeBackendTransport Never()
        {
            return new FakeBackendTransport(async (request, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return default;
            });
        }

        // null이면 모든 요청을 보낸다. 서버 임대가 없는 상황을 시험할 때 넣는다
        public Func<BackendRequest, bool> CanSendFilter { get; set; }

        public bool CanSend(BackendRequest request)
        {
            Func<BackendRequest, bool> filter = CanSendFilter;
            return (filter == null) || (filter(request));
        }

        public Task<BackendTransportResult> SendAsync(BackendRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            _requests.Enqueue(request);
            return _respond(request, cancellationToken);
        }

        public void Dispose()
        {
            IsDisposed = true;
        }

        /// <summary> 결과 count개가 Pump 대기 큐에 들어올 때까지 기다린다, 시간 안에 안 오면 시험 실패 </summary>
        public static void WaitForCompleted(BackendGateway gateway, int count)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (gateway.CompletedCount < count)
            {
                if (watch.ElapsedMilliseconds > WAIT_TIMEOUT_MILLISECONDS)
                {
                    Assert.Fail($"게이트웨이 결과가 {WAIT_TIMEOUT_MILLISECONDS}ms 안에 {count}개 오지 않았다");
                }

                Thread.Sleep(WAIT_POLL_MILLISECONDS);
            }
        }

        /// <summary> 재시도 지연을 기다리지 않고 요청 값만 기록하는 대기 함수 </summary>
        public static Func<TimeSpan, CancellationToken, Task> ImmediateDelay(ConcurrentQueue<TimeSpan> recorded)
        {
            return (delay, token) =>
            {
                recorded.Enqueue(delay);
                return Task.CompletedTask;
            };
        }
    }
}
