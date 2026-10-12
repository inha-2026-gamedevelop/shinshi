// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> ACK 전 이벤트를 보존하는 고정 크기 저장 큐, 모든 호출과 완료 콜백은 Unity 메인 스레드에서만 한다 </summary>
    public sealed class SessionEventQueue : IBackendEventSinkListener
    {
        /****************************************
        *                Fields
        ****************************************/

        private const double RETRY_MULTIPLIER     = 2.0;
        private const string QUEUE_WARNING        = "[SessionEventQueue] 이벤트 저장 큐 한계에 도달했다. 저장 대기 이벤트는 보존한다";
        private const string SEND_EXCEPTION_ERROR = "[SessionEventQueue] 백엔드 이벤트 전송 시작 중 예외가 발생했다. 동일 배치를 재시도한다";
        private const string TERMINAL_ERROR       = "[SessionEventQueue] 저장이 확정 거절돼 큐를 멈춘다. 출정은 서비스 장애 취소로 끝난다";

        private readonly string            _sessionId;
        private readonly IBackendEventSink _sink;
        private readonly SessionEvent[]    _ring;
        private readonly SessionEvent[]    _batch;

        // _ring과 같은 칸에 넣은 시각(마지막 Tick의 서버 시각). 관측 지표의 가장 오래된 미확인 나이만 쓴다
        private readonly double[]          _enqueuedAt;

        private int    _head                     = 0;
        private int    _count                    = 0;
        private int    _batchCount               = 0;
        private int    _highWatermark            = 0;
        private long   _lastSequence             = 0;
        private long   _lastAcknowledgedSequence = 0;
        private long   _lastAttemptId            = 0;
        private long   _activeAttemptId          = 0;
        private long   _rejectedCount            = 0;
        private long   _retryCount               = 0;
        private double _serverTime               = 0.0;
        private double _nextSendTime             = 0.0;
        private double _nextWarningTime          = double.NegativeInfinity;
        private double _retryDelaySeconds        = Constants.Session.EVENT_RETRY_INITIAL_SECONDS;
        private bool   _hasServerTime            = false;
        private bool   _inFlight                 = false;
        private bool   _sending                  = false;
        private bool   _retryPending             = false;
        private bool   _warningPending           = false;
        private bool _acknowledging;
        private long _immediateThroughSequence;
        private Action<SessionEvent> _committed;
        private Delegate[] _committedObservers = Array.Empty<Delegate>();
        public event Action<SessionEvent> Committed
        {
            add
            {
                _committed += value;
                _committedObservers = _committed?.GetInvocationList() ?? Array.Empty<Delegate>();
            }
            remove
            {
                _committed -= value;
                _committedObservers = _committed?.GetInvocationList() ?? Array.Empty<Delegate>();
            }
        }
        private bool   _isTerminal               = false;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 진행 중인 배치를 포함한 미확인 이벤트 수 </summary>
        public int QueuedCount => _count;

        public string SessionId              => _sessionId;
        public int    Capacity               => _ring.Length;
        public int    BatchCapacity          => _batch.Length;
        public int    HighWatermark          => _highWatermark;

        /// <summary> 큐 용량 또는 순번 한계로 거절한 이벤트 수, 잘못된 batch 인자 거절은 제외한다 </summary>
        public long RejectedCount            => _rejectedCount;

        /// <summary> 실패한 배치의 재전송 시도 수, 최초 전송은 제외한다 </summary>
        public long RetryCount               => _retryCount;
        public long LastAcknowledgedSequence => _lastAcknowledgedSequence;

        /// <summary> 가장 오래된 미확인 이벤트가 큐에 머문 초, 비었으면 0. 틱 단위로만 정확하다 </summary>
        public double OldestAgeSeconds => (_count == 0) ? 0.0 : Math.Max(0.0, _serverTime - _enqueuedAt[_head]);

        /// <summary> 확정 거절을 받아 더는 보내지도 받지도 않는다, 남은 이벤트는 API 장애 취소가 처리한다 </summary>
        public bool IsTerminal => _isTerminal;

        /****************************************
        *                Methods
        ****************************************/

        public SessionEventQueue(string sessionId, IBackendEventSink sink,
            int capacity = Constants.Session.EVENT_QUEUE_CAPACITY,
            int batchCapacity = Constants.Session.EVENT_BATCH_CAPACITY)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                throw new ArgumentException("세션 ID가 필요하다", nameof(sessionId));
            }

            if (sink == null)
            {
                throw new ArgumentNullException(nameof(sink));
            }

            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "저장 큐 상한은 1 이상이다");
            }

            if (batchCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(batchCapacity), "저장 배치 상한은 1 이상이다");
            }

            _sessionId  = sessionId;
            _sink       = sink;
            _ring       = new SessionEvent[capacity];
            _batch      = new SessionEvent[batchCapacity];
            _enqueuedAt = new double[capacity];
        }

        /// <summary> 빈자리가 있으면 확정 payload에 다음 순번을 붙인다, 거절 시 sequence는 0이고 기존 큐는 유지된다 </summary>
        public bool TryEnqueue(in SessionEventData data, out long sequence)
        {
            sequence = 0;
            if (_isTerminal)
            {
                return false;
            }

            if ((_count == _ring.Length) || (_lastSequence == long.MaxValue))
            {
                RecordRejection(1);
                return false;
            }

            EnqueueUnchecked(in data);
            sequence = _lastSequence;
            return true;
        }

        /// <summary> 앞 count개를 순서대로 모두 수락하거나 모두 거절한다, null과 0 이하 또는 배열 범위 밖 count도 거절한다 </summary>
        public bool TryEnqueueBatch(SessionEventData[] data, int count)
        {
            if ((_isTerminal) || (data == null) || (count <= 0) || (count > data.Length))
            {
                return false;
            }

            if ((count > (_ring.Length - _count)) || (count > (long.MaxValue - _lastSequence)))
            {
                RecordRejection(count);
                return false;
            }

            for (int i = 0; i < count; ++i)
            {
                EnqueueUnchecked(in data[i]);
            }

            return true;
        }

        /// <summary> 첫 유효 호출부터 배치 시계를 시작한다, 비유한 시각과 이전 시각은 상태를 바꾸지 않고 무시한다 </summary>
        public void Tick(double serverTime)
        {
            if ((_sending) || (_acknowledging) || (double.IsNaN(serverTime)) || (double.IsInfinity(serverTime)) ||
                ((_hasServerTime) && (serverTime < _serverTime)))
            {
                return;
            }

            if (!_hasServerTime)
            {
                _hasServerTime = true;
                _nextSendTime  = serverTime + Constants.Session.EVENT_BATCH_INTERVAL_SECONDS;
            }

            _serverTime = serverTime;
            if ((_lastAcknowledgedSequence < _immediateThroughSequence) && !_retryPending && !_inFlight)
            {
                _nextSendTime = serverTime;
            }
            WarnIfDue();
            TrySendIfDue();
        }

        public void OnBatchCompleted(long attemptId, EEventBatchResult result, double retryAfterSeconds = 0.0)
        {
            if ((!_inFlight) || (attemptId != _activeAttemptId))
            {
                return;
            }

            _inFlight        = false;
            _activeAttemptId = 0;
            switch (result)
            {
                case EEventBatchResult.Committed:
                    AcknowledgeBatch();
                    return;
                case EEventBatchResult.Terminal:
                    // 같은 배치를 다시 보내도 같은 거절이 온다. 다음 배치로 넘기면 순번에 구멍이 생겨 API가 받지 않는다
                    _isTerminal = true;
                    Debug.LogError(TERMINAL_ERROR);
                    return;
                default:
                    ScheduleRetry(retryAfterSeconds);
                    return;
            }
        }

        public void RequestImmediateSend()
        {
            _immediateThroughSequence = _lastSequence;
        }

        private void EnqueueUnchecked(in SessionEventData data)
        {
            int tail = (_head + _count) % _ring.Length;
            _ring[tail]       = new SessionEvent(++_lastSequence, in data);
            _enqueuedAt[tail] = _serverTime;
            ++_count;
            _highWatermark = Math.Max(_highWatermark, _count);
        }

        private void RecordRejection(int count)
        {
            _rejectedCount = _rejectedCount > (long.MaxValue - count)
                ? long.MaxValue
                : _rejectedCount + count;
            _warningPending = true;
        }

        private void WarnIfDue()
        {
            if ((!_warningPending) || (_serverTime < _nextWarningTime))
            {
                return;
            }

            _warningPending  = false;
            _nextWarningTime = _serverTime + Constants.Session.EVENT_QUEUE_WARNING_INTERVAL_SECONDS;
            Debug.LogWarning(QUEUE_WARNING);
        }

        private void TrySendIfDue()
        {
            if ((_isTerminal) || (_inFlight) || (_count == 0) || (_serverTime < _nextSendTime))
            {
                return;
            }

            if (_lastAttemptId == long.MaxValue)
            {
                _warningPending = true;
                return;
            }

            if (_batchCount == 0)
            {
                PrepareBatch();
            }

            if ((_retryPending) && (_retryCount < long.MaxValue))
            {
                ++_retryCount;
            }

            long attemptId = ++_lastAttemptId;
            _activeAttemptId = attemptId;
            _inFlight        = true;
            _sending         = true;

            // 동기 ACK가 와도 전송 호출이 끝나기 전에 다음 배치가 같은 배열을 덮지 못하게 한다
            bool started = false;
            try
            {
                started = _sink.TrySend(_sessionId, attemptId, _batch, _batchCount, this);
            }
            catch (Exception exception)
            {
                Debug.LogError(SEND_EXCEPTION_ERROR);
                Debug.LogException(exception);
            }
            finally
            {
                _sending = false;
                if (!started)
                {
                    OnBatchCompleted(attemptId, EEventBatchResult.Transient);
                }
            }
        }

        private void PrepareBatch()
        {
            _batchCount = Math.Min(_count, _batch.Length);
            for (int i = 0; i < _batchCount; ++i)
            {
                _batch[i] = _ring[(_head + i) % _ring.Length];
            }
        }

        private void ScheduleRetry(double retryAfterSeconds)
        {
            _retryPending      = true;
            _nextSendTime      = _serverTime + Math.Max(_retryDelaySeconds, retryAfterSeconds);
            _retryDelaySeconds = Math.Min(_retryDelaySeconds * RETRY_MULTIPLIER,
                Constants.Session.EVENT_RETRY_MAX_SECONDS);
        }

        private void AcknowledgeBatch()
        {
            _acknowledging = true;
            _lastAcknowledgedSequence = _batch[_batchCount - 1].Seq;
            for (int i = 0; i < _batchCount; ++i)
            {
                Delegate[] observers = _committedObservers;
                if (observers == null)
                {
                    continue;
                }
                for (int observer = 0; observer < observers.Length; ++observer)
                {
                    try
                    {
                        ((Action<SessionEvent>)observers[observer])(_batch[i]);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                }
            }
            for (int i = 0; i < _batchCount; ++i)
            {
                _ring[_head] = default;
                _batch[i]    = default;
                _head        = (_head + 1) % _ring.Length;
            }

            _acknowledging = false;
            _count            -= _batchCount;
            _batchCount        = 0;
            _retryPending      = false;
            _retryDelaySeconds = Constants.Session.EVENT_RETRY_INITIAL_SECONDS;
            _nextSendTime      = _serverTime + Constants.Session.EVENT_BATCH_INTERVAL_SECONDS;
        }
    }
}
