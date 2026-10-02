// System
using System;

namespace Shinshi.Network
{
    /// <summary> 최소 회로 차단기, 연속 일시 실패가 쌓이면 잠시 호출을 막고 그 뒤 시험 호출 한 건으로 회복을 본다 </summary>
    public sealed class BackendCircuitBreaker
    {
        // 게이트웨이의 여러 백그라운드 작업이 동시에 부르므로 모든 상태를 잠금 안에서만 읽고 바꾼다.
        // 백엔드가 죽었을 때 요청마다 시간 제한(3초)과 재시도를 다 기다리면 저장 큐와 인증 대기가 함께 밀린다

        /****************************************
        *                Fields
        ****************************************/

        private readonly object       _lock = new object();
        private readonly Func<double> _clock;
        private readonly int          _failureThreshold;
        private readonly double       _openSeconds;

        private EBackendCircuitState _state               = EBackendCircuitState.Closed;
        private int                  _consecutiveFailures = 0;
        private double               _openedAt            = 0.0;

        /****************************************
        *              Properties
        ****************************************/

        public EBackendCircuitState State
        {
            get
            {
                lock (_lock)
                {
                    return _state;
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public BackendCircuitBreaker(Func<double> clock, int failureThreshold, double openSeconds)
        {
            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            if (failureThreshold <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(failureThreshold));
            }

            if (openSeconds <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(openSeconds));
            }

            _clock            = clock;
            _failureThreshold = failureThreshold;
            _openSeconds      = openSeconds;
        }

        /// <summary> 지금 보내도 되면 참, 열린 시간이 지났으면 시험 호출 한 건만 허용한다 </summary>
        public bool TryAcquire()
        {
            lock (_lock)
            {
                switch (_state)
                {
                    case EBackendCircuitState.Closed:
                        return true;
                    case EBackendCircuitState.Open:
                        if ((_clock() - _openedAt) < _openSeconds)
                        {
                            return false;
                        }

                        _state = EBackendCircuitState.HalfOpen;
                        return true;
                    default:
                        // 시험 호출 결과가 오기 전에는 더 보내지 않는다
                        return false;
                }
            }
        }

        /// <summary> 시도 결과를 기록한다, 일시 실패(시간 초과, 연결 실패, 5xx)만 실패로 센다 </summary>
        public void Record(bool transientFailure)
        {
            lock (_lock)
            {
                if (!transientFailure)
                {
                    _consecutiveFailures = 0;
                    _state               = EBackendCircuitState.Closed;
                    return;
                }

                ++_consecutiveFailures;
                if ((_state == EBackendCircuitState.HalfOpen) || (_consecutiveFailures >= _failureThreshold))
                {
                    _state    = EBackendCircuitState.Open;
                    _openedAt = _clock();
                }
            }
        }
    }
}
