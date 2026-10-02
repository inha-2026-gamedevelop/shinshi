// System
using System;
using System.Globalization;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 마을 연결이 끊긴 계정의 접속 임대를 백엔드에서 푼다, 일시 실패는 간격을 두고 몇 번 더 보낸다 </summary>
    public sealed class AccountLeaseReleaser : IBackendResponseListener
    {
        // 해제 경로는 멱등이라 같은 임대를 여러 번 보내도 결과가 같다. 그래도 끝없이 보내지 않는다. 끝내 실패하면 계정 임대는
        // 백엔드 계약대로 heartbeat 만료로 풀린다. 그동안 같은 계정의 새 로그인은 이미 접속 중으로 거절될 수 있다.
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

        private int _count = 0;

        /****************************************
        *              Properties
        ****************************************/

        // 해제 응답을 기다리거나 재시도 시각을 기다리는 임대 수
        public int PendingCount => _count;

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
        }

        /// <summary> 끊긴 연결의 임대 해제를 보낸다, 형식이 틀렸거나 대기가 가득 차면 거짓(만료로 풀린다) </summary>
        public bool Release(string accountLeaseId)
        {
            string leaseId = JoinTokenClaimsRules.ToAccountLeaseId(accountLeaseId);
            if (leaseId == null)
            {
                return false;
            }

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

        /// <summary> 같은 임대로 새 연결이 들어왔으면 아직 보내지 않은 해제를 거둔다, 이미 보낸 해제는 거둘 수 없다 </summary>
        public bool Cancel(string accountLeaseId)
        {
            if (string.IsNullOrEmpty(accountLeaseId))
            {
                return false;
            }

            int index = IndexOfLease(accountLeaseId);
            if ((index < 0) || (_slots[index].RequestId != 0))
            {
                return false;
            }

            RemoveAt(index);
            return true;
        }

        /// <summary> 매 프레임 부른다, 재시도 시각이 된 해제를 다시 보낸다 </summary>
        public void Tick()
        {
            if (_count == 0)
            {
                return;
            }

            double now = _clock();
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

            if (slot.Attempts >= Constants.Network.ACCOUNT_LEASE_RELEASE_MAX_ATTEMPTS)
            {
                _warn?.Invoke($"[AccountLease] 해제를 {slot.Attempts}번 보내고 포기한다, heartbeat 만료로 풀린다, " +
                    $"outcome={response.Outcome}, status={response.StatusCode}");
                RemoveAt(index);
                return;
            }

            slot.NextTime = _clock() + Math.Max(Constants.Network.ACCOUNT_LEASE_RELEASE_RETRY_SECONDS, response.RetryAfterSeconds);
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
