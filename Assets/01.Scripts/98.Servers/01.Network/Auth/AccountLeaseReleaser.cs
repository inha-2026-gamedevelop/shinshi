// System
using System;
using System.Globalization;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 마을 연결이 끊긴 계정의 접속 임대를 백엔드에서 푼다, 일시 실패는 확정 응답이 올 때까지 간격을 두고 다시 보낸다 </summary>
    public sealed class AccountLeaseReleaser : IBackendResponseListener
    {
        // 해제 경로는 멱등이라 같은 임대를 여러 번 보내도 결과가 같다. 마을 heartbeat는 그 인스턴스의 살아 있는 임대를 연결 여부와
        // 상관없이 모두 연장하므로, 해제를 포기하면 임대가 만료되지 않고 마을이 내려갈 때까지 같은 계정의 입장이 이미 접속 중으로
        // 막힌다(API 재시작, 서버 임대 갱신 사이 401, 회로 열림처럼 몇 초 이어지는 일시 실패에서 일어난다). 그래서 성공이나 확정
        // 거절이 올 때까지 간격 상한으로 계속 보낸다. 대기 수는 고정 배열 상한을 넘지 않는다.
        // 메인 스레드에서만 부른다. 해제 대기는 고정 배열에 두고 Tick은 대기 수만큼만 돈다

        /****************************************
        *                Fields
        ****************************************/

        private const int STATUS_UNAUTHORIZED = 401;

        private readonly BackendGateway _gateway;
        private readonly string         _instanceId;
        private readonly Func<double>   _clock;
        private readonly Action<string> _warn;
        private readonly Slot[]         _slots;
        private readonly Deferred[]     _deferred;

        private int _count         = 0;
        private int _deferredCount = 0;

        /****************************************
        *              Properties
        ****************************************/

        // 해제 응답을 기다리거나 재시도 시각을 기다리는 임대 수
        public int PendingCount => _count;

        // 던전 이관을 기다리며 해제를 미뤄 둔 임대 수
        public int DeferredCount => _deferredCount;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> clock은 단조 증가 초, null이면 게이트웨이 시계. warn은 null이면 기록하지 않는다 </summary>
        public AccountLeaseReleaser(BackendGateway gateway, string instanceId, Func<double> clock, Action<string> warn)
        {
            if (gateway == null)
            {
                throw new ArgumentNullException(nameof(gateway));
            }

            if (string.IsNullOrEmpty(instanceId))
            {
                throw new ArgumentException("인스턴스 ID가 필요하다", nameof(instanceId));
            }

            _gateway    = gateway;
            _instanceId = instanceId;
            _clock      = clock ?? BackendGateway.MonotonicSeconds;
            _warn       = warn;
            _slots      = new Slot[Constants.Network.MAX_PENDING_ACCOUNT_LEASE_RELEASES];
            _deferred   = new Deferred[Constants.Network.MAX_DEFERRED_ACCOUNT_LEASE_RELEASES];
        }

        /// <summary> 끊긴 연결의 임대 해제를 보낸다, 형식이 틀렸거나 대기가 가득 차면 거짓(만료로 풀린다) </summary>
        public bool Release(string accountLeaseId)
        {
            string leaseId = JoinTokenClaimsRules.ToAccountLeaseId(accountLeaseId);
            if (leaseId == null)
            {
                return false;
            }

            // 미뤄 둔 해제가 있으면 지금 보내는 것으로 대신한다
            RemoveDeferred(leaseId);

            if (IndexOfLease(leaseId) >= 0)
            {
                return true;
            }

            if (_count == _slots.Length)
            {
                _warn?.Invoke($"[AccountLease] 해제 대기가 상한({_slots.Length})이라 보내지 않는다, heartbeat 만료로 풀린다");
                return false;
            }

            // 연결 종료마다 한 번 만드는 경로 문자열이다. 재시도는 같은 문자열을 다시 쓴다
            string path = string.Format(CultureInfo.InvariantCulture, GameServerRoutes.ACCOUNT_LEASE_RELEASE_FORMAT, _instanceId,
                leaseId);
            int index = _count;
            _slots[index] = new Slot(leaseId, path);
            ++_count;
            Send(index);
            return true;
        }

        /// <summary> 던전 이관을 기다리는 임대의 해제를 delaySeconds 뒤로 미룬다, 그때까지 이관이 없으면 Tick이 푼다.
        /// 해제는 원래 마을 인스턴스 소유일 때만 효력이 있으므로 이관된 임대는 그대로 남는다. 목록이 가득 차면 거짓(만료로 풀린다) </summary>
        public bool ReleaseAfter(string accountLeaseId, double delaySeconds)
        {
            string leaseId = JoinTokenClaimsRules.ToAccountLeaseId(accountLeaseId);
            if (leaseId == null)
            {
                return false;
            }

            double due   = _clock() + Math.Max(0.0, delaySeconds);
            int    index = IndexOfDeferred(leaseId);
            if (index >= 0)
            {
                _deferred[index].DueTime = due;
                return true;
            }

            if (_deferredCount == _deferred.Length)
            {
                _warn?.Invoke($"[AccountLease] 미룬 해제가 상한({_deferred.Length})이라 미루지 않는다, heartbeat 만료로 풀린다");
                return false;
            }

            _deferred[_deferredCount] = new Deferred(leaseId, due);
            ++_deferredCount;
            return true;
        }

        /// <summary> 같은 임대로 새 연결이 들어왔으면 아직 보내지 않은 해제를 거둔다, 이미 보낸 해제는 거둘 수 없다 </summary>
        public bool Cancel(string accountLeaseId)
        {
            if (string.IsNullOrEmpty(accountLeaseId))
            {
                return false;
            }

            bool removedDeferred = RemoveDeferred(accountLeaseId);
            int  index           = IndexOfLease(accountLeaseId);
            if ((index < 0) || (_slots[index].RequestId != 0))
            {
                return removedDeferred;
            }

            RemoveAt(index);
            return true;
        }

        /// <summary> 매 프레임 부른다, 재시도 시각이 된 해제를 다시 보낸다 </summary>
        public void Tick()
        {
            if ((_count == 0) && (_deferredCount == 0))
            {
                return;
            }

            double now = _clock();
            TickDeferred(now);
            for (int i = 0; i < _count; ++i)
            {
                ref Slot slot = ref _slots[i];
                if ((slot.RequestId == 0) && (now >= slot.NextTime))
                {
                    Send(i);
                }
            }
        }

        // 게이트웨이가 메인 스레드의 Pump에서 부른다
        public void OnBackendResponse(in BackendResponse response)
        {
            int index = IndexOfRequest(response.RequestId);
            if (index < 0)
            {
                return;
            }

            ref Slot slot = ref _slots[index];
            slot.RequestId = 0;

            // 거절(이미 풀림, 다른 소유자, 없는 임대)은 다시 보내도 같다. 401은 서버 임대 갱신 사이의 거절이라 다시 보낸다
            if ((response.IsSucceeded)
                || ((response.Outcome == EBackendOutcome.Rejected) && (response.StatusCode != STATUS_UNAUTHORIZED)))
            {
                RemoveAt(index);
                return;
            }

            if (slot.Attempts == Constants.Network.ACCOUNT_LEASE_RELEASE_WARN_ATTEMPTS)
            {
                _warn?.Invoke($"[AccountLease] 해제를 {slot.Attempts}번 보냈지만 확정되지 않아 계속 다시 보낸다, " +
                    $"outcome={response.Outcome}, status={response.StatusCode}");
            }

            slot.NextTime = _clock() + RetryDelaySeconds(slot.Attempts, response.RetryAfterSeconds);
        }

        /// <summary> attempts번 보낸 뒤의 재시도 간격, 보낼수록 늘리되 상한을 넘지 않는다. 서버가 요청한 대기가 더 길면 그것을 따른다 </summary>
        public static double RetryDelaySeconds(int attempts, double retryAfterSeconds)
        {
            double delay = Math.Min(Constants.Network.ACCOUNT_LEASE_RELEASE_RETRY_SECONDS * Math.Max(1, attempts),
                Constants.Network.ACCOUNT_LEASE_RELEASE_MAX_RETRY_SECONDS);
            return Math.Max(delay, retryAfterSeconds);
        }

        private void Send(int index)
        {
            ref Slot slot = ref _slots[index];
            ++slot.Attempts;
            slot.NextTime = double.PositiveInfinity;

            // 본문 없는 멱등 해제다. 재시도는 이 클래스가 시각을 정해 하므로 게이트웨이 안에서는 다시 보내지 않는다
            BackendRequest request = BackendRequest.PostWithoutBody(slot.Path, null, null, Constants.Network.API_TIMEOUT_SECONDS);
            slot.RequestId = _gateway.Send(request, this);
        }

        // 기한이 지난 미룬 해제를 실제 해제로 옮긴다. 해제 대기가 가득 차 못 옮기면 남겨 두고 다음 Tick에 다시 본다
        private void TickDeferred(double now)
        {
            for (int i = _deferredCount - 1; i >= 0; --i)
            {
                if ((now < _deferred[i].DueTime) || (_count == _slots.Length))
                {
                    continue;
                }

                string leaseId = _deferred[i].LeaseId;
                RemoveDeferredAt(i);
                Release(leaseId);
            }
        }

        private int IndexOfDeferred(string leaseId)
        {
            for (int i = 0; i < _deferredCount; ++i)
            {
                if (string.Equals(_deferred[i].LeaseId, leaseId, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private bool RemoveDeferred(string leaseId)
        {
            int index = IndexOfDeferred(leaseId);
            if (index < 0)
            {
                return false;
            }

            RemoveDeferredAt(index);
            return true;
        }

        private void RemoveDeferredAt(int index)
        {
            int last = _deferredCount - 1;
            _deferred[index] = _deferred[last];
            _deferred[last]  = default;
            --_deferredCount;
        }

        private int IndexOfLease(string leaseId)
        {
            for (int i = 0; i < _count; ++i)
            {
                if (string.Equals(_slots[i].LeaseId, leaseId, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private int IndexOfRequest(int requestId)
        {
            if (requestId == 0)
            {
                return -1;
            }

            for (int i = 0; i < _count; ++i)
            {
                if (_slots[i].RequestId == requestId)
                {
                    return i;
                }
            }

            return -1;
        }

        // 순서는 쓰지 않으므로 마지막 항목으로 메우고, 비운 칸은 문자열 참조를 남기지 않게 지운다
        private void RemoveAt(int index)
        {
            int last = _count - 1;
            _slots[index] = _slots[last];
            _slots[last]  = default;
            --_count;
        }

        /****************************************
        *               Records
        ****************************************/

        private struct Deferred
        {
            public string LeaseId;
            public double DueTime;

            public Deferred(string leaseId, double dueTime)
            {
                LeaseId = leaseId;
                DueTime = dueTime;
            }
        }

        // 배열 안에서 ref로 제자리 수정하는 값 묶음이라 readonly로 두지 않는다
        private struct Slot
        {
            public string LeaseId;
            public string Path;
            public int    RequestId;
            public int    Attempts;
            public double NextTime;

            public Slot(string leaseId, string path)
            {
                LeaseId   = leaseId;
                Path      = path;
                RequestId = 0;
                Attempts  = 0;
                NextTime  = 0.0;
            }
        }
    }
}
