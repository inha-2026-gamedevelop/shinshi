// System
using System;
using System.Globalization;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 서버 프로세스의 백엔드 존재 신호, 임대 발급 -> 풀 등록 -> heartbeat와 임대 갱신을 맡는다 </summary>
    public sealed class ServerPresence : IBackendResponseListener
    {
        // 모든 호출은 메인 스레드에서 한다. 요청은 게이트웨이로 비동기로 보내고 결과는 Pump에서 받는다.
        //
        // heartbeat 주기는 전투 틱이 아니라 ServerProcessDriver의 Update와 단조 시계로 센다. 출정이 없어 전투 틱이
        // 세션을 열지 않아도 신호가 나가야 하고, 반대로 메인 스레드가 멈추면 신호도 멈춰야 API가 죽은 프로세스를 배정에서 뺀다.
        // 그래서 스레드 풀 타이머로 따로 보내지 않는다.
        //
        // bootId는 프로세스마다 새로 만든다. 같은 bootId로 되살릴 수 없는 거절(409 SESSION_TERMINAL, 409 SESSION_MISMATCH =
        // 다른 bootId)만 Lost로 보고, 종료 경로가 프로세스를 내리면 supervisor가 새 bootId로 다시 띄운다. 그 밖의 4xx, 5xx,
        // 503(같은 인스턴스에 이전 boot가 아직 살아 있는 경우 포함)은 SERVER_PRESENCE_RETRY_SECONDS 간격으로 다시 보낸다.
        // 일시 거절 하나로 프로세스를 내리면 진행 중인 출정이 통째로 장애 취소된다.
        //
        // 임대, heartbeat, 드레인은 회로 차단기를 거치지 않는다(BackendRequest.BypassesCircuit). 이 요청들의 Retry-After는
        // heartbeat 주기까지만 따른다. 길게 따르면 15초 부재 한계를 넘겨 API가 살아 있는 프로세스를 소실로 본다

        /****************************************
        *                Fields
        ****************************************/

        private const string GUID_FORMAT                = "D";
        private const double MILLISECONDS_PER_SECOND    = 1000.0;
        private const double LEASE_RENEW_LIFETIME_RATIO = 0.5;
        private const int    STATUS_UNAUTHORIZED        = 401;
        private const int    STATUS_CONFLICT            = 409;
        private const int    NO_INSTANCE_STATE          = -1;

        private readonly BackendGateway    _gateway;
        private readonly ServerCredentials _credentials;
        private readonly Func<double>      _clock;
        private readonly Action<string>    _warn;
        private readonly string            _instanceId;
        private readonly int               _serverRole;
        private readonly string            _bootId;
        private readonly string            _address;
        private readonly int               _port;
        private readonly string            _buildId;
        private readonly string            _contentVersion;
        private readonly string            _heartbeatPath;
        private readonly string            _drainPath;

        private EServerPresenceStage _stage                = EServerPresenceStage.Idle;
        private int                  _leaseRequestId       = 0;
        private int                  _registerRequestId    = 0;
        private int                  _heartbeatRequestId   = 0;
        private int                  _drainRequestId       = 0;
        private double               _leaseSentAt          = 0.0;
        private double               _heartbeatSentAt      = 0.0;
        private double               _drainSentAt          = 0.0;
        private double               _nextLeaseTime        = 0.0;
        private double               _nextRegisterTime     = 0.0;
        private double               _nextHeartbeatTime    = 0.0;
        private double               _nextDrainTime        = 0.0;
        private int                  _instanceState        = NO_INSTANCE_STATE;
        private double               _instanceStateSentAt  = 0.0;
        private int                  _unauthorizedCount    = 0;
        private bool                 _drainRequested       = false;
        private double               _drainRequestedAt     = 0.0;
        private bool                 _drainCompleted       = false;
        private string               _lostReason           = null;

        /****************************************
        *              Properties
        ****************************************/

        public EServerPresenceStage Stage      => _stage;
        public string               BootId     => _bootId;
        public bool                 IsLost     => _stage == EServerPresenceStage.Lost;
        public bool                 IsRunning  => _stage == EServerPresenceStage.Running;

        // 사람이 읽는 Lost 원인, 자격 값은 들어 있지 않다
        public string               LostReason => _lostReason;

        // 마지막 성공 heartbeat의 GameSessionCodes.InstanceState, 받은 적이 없으면 -1
        public int                  InstanceState => _instanceState;

        public bool                 IsDrainCompleted => _drainCompleted;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> address와 port는 클라이언트가 이 서버에 붙을 주소다. clock은 단조 증가 초, null이면 게이트웨이 시계 </summary>
        public ServerPresence(BackendGateway gateway, ServerCredentials credentials, EServerRole role, string address, int port,
            string buildId, string contentVersion, Func<double> clock, Action<string> warn)
        {
            if ((gateway == null) || (credentials == null))
            {
                throw new ArgumentNullException((gateway == null) ? nameof(gateway) : nameof(credentials));
            }

            if ((role != EServerRole.Town) && (role != EServerRole.Dungeon))
            {
                throw new ArgumentOutOfRangeException(nameof(role));
            }

            if (string.IsNullOrEmpty(address))
            {
                throw new ArgumentException("접속 주소가 필요하다", nameof(address));
            }

            _gateway        = gateway;
            _credentials    = credentials;
            _clock          = clock ?? BackendGateway.MonotonicSeconds;
            _warn           = warn;
            _instanceId     = credentials.InstanceId;
            _serverRole     = (role == EServerRole.Town) ? ContractCodes.ServerRole.TOWN : ContractCodes.ServerRole.DUNGEON;
            _bootId         = Guid.NewGuid().ToString(GUID_FORMAT);
            _address        = address;
            _port           = port;
            _buildId        = buildId ?? string.Empty;
            _contentVersion = contentVersion ?? string.Empty;
            _heartbeatPath  = string.Format(CultureInfo.InvariantCulture, GameServerRoutes.SERVER_HEARTBEAT_FORMAT, _instanceId);
            _drainPath      = string.Format(CultureInfo.InvariantCulture, GameServerRoutes.SERVER_DRAIN_FORMAT, _instanceId);
        }

        /// <summary> 서버를 연 직후 한 번 부른다, 첫 임대를 요청한다 </summary>
        public void Start()
        {
            if (_stage != EServerPresenceStage.Idle)
            {
                return;
            }

            _stage = EServerPresenceStage.Leasing;
            SendLease(_clock());
        }

        /// <summary> 매 프레임 부른다, 시각이 된 임대 갱신, 등록 재시도, heartbeat, 드레인 재시도를 보낸다 </summary>
        public void Tick()
        {
            if ((_stage == EServerPresenceStage.Idle) || (_stage == EServerPresenceStage.Lost))
            {
                return;
            }

            double now = _clock();
            if ((_leaseRequestId == 0) && (now >= _nextLeaseTime))
            {
                SendLease(now);
            }

            if ((_stage == EServerPresenceStage.Registering) && (_registerRequestId == 0) && (now >= _nextRegisterTime)
                && (_credentials.HasValidLease))
            {
                SendRegister(now);
            }

            // 드레인을 보낸 뒤에도 종료까지 heartbeat를 이어야 API가 정상 종료와 소실을 구분한다
            if ((_stage == EServerPresenceStage.Running) && (_heartbeatRequestId == 0) && (now >= _nextHeartbeatTime)
                && (_credentials.HasValidLease))
            {
                SendHeartbeat(now);
            }

            // 드레인이 실패했으면 성공할 때까지 다시 보낸다. 종료 경로는 RECYCLE_DRAIN_WAIT_SECONDS까지만 기다린다
            if ((_drainRequested) && (!_drainCompleted) && (_drainRequestId == 0) && (now >= _nextDrainTime)
                && (_credentials.HasValidLease))
            {
                SendDrain(now);
            }
        }

        /// <summary> 새 출정 배정을 멈추게 드레인을 요청한다, 성공할 때까지 Tick이 다시 보낸다. 풀 등록 전이면 바로 완료로 본다 </summary>
        public void RequestDrain()
        {
            if (_drainRequested)
            {
                return;
            }

            double now = _clock();
            _drainRequested   = true;
            _drainRequestedAt = now;
            if (_stage != EServerPresenceStage.Running)
            {
                _drainCompleted = true;
                return;
            }

            // 임대가 잠시 없으면 새 임대를 받은 뒤 Tick이 보낸다
            _nextDrainTime = now;
            if (_credentials.HasValidLease)
            {
                SendDrain(now);
            }
        }

        /// <summary> since 이후에 보낸 heartbeat가 출정 종료를 알렸는지, 예약 뒤 IDLE 또는 이 프로세스가 요청하지 않은 DRAINING </summary>
        public bool ReportsSortieEndedSince(double since)
        {
            if ((_instanceState == NO_INSTANCE_STATE) || (_instanceStateSentAt < since))
            {
                return false;
            }

            if (_instanceState == GameSessionCodes.InstanceState.IDLE)
            {
                return true;
            }

            // 정산이나 예약 해제 뒤 API가 인스턴스를 Draining으로 둔다. 이 프로세스가 드레인을 요청한 뒤에 보낸 신호의
            // Draining은 자기 요청의 결과라 출정 종료 신호로 세지 않는다
            return (_instanceState == GameSessionCodes.InstanceState.DRAINING)
                && ((!_drainRequested) || (_instanceStateSentAt < _drainRequestedAt));
        }

        // 게이트웨이가 메인 스레드의 Pump에서 부른다
        public void OnBackendResponse(in BackendResponse response)
        {
            int requestId = response.RequestId;
            if ((requestId == 0) || (_stage == EServerPresenceStage.Lost))
            {
                return;
            }

            if (requestId == _leaseRequestId)
            {
                _leaseRequestId = 0;
                OnLeaseResponse(in response);
                return;
            }

            if (requestId == _registerRequestId)
            {
                _registerRequestId = 0;
                OnRegisterResponse(in response);
                return;
            }

            if (requestId == _heartbeatRequestId)
            {
                _heartbeatRequestId = 0;
                OnHeartbeatResponse(in response);
                return;
            }

            if (requestId == _drainRequestId)
            {
                _drainRequestId = 0;
                OnDrainResponse(in response);
            }
        }

        /// <summary> 같은 bootId로는 되살릴 수 없는 거절인지, 409와 SESSION_TERMINAL 또는 SESSION_MISMATCH(다른 bootId)만 참 </summary>
        public static bool IsUnrecoverable(in BackendResponse response)
        {
            return (response.Outcome == EBackendOutcome.Rejected) && (response.StatusCode == STATUS_CONFLICT)
                && ((response.ReasonCode == ContractCodes.RejectReason.SESSION_TERMINAL)
                    || (response.ReasonCode == ContractCodes.RejectReason.SESSION_MISMATCH));
        }

        /// <summary> 생존 신호의 Retry-After를 heartbeat 주기까지만 따른다, 0 이하나 NaN은 0이다 </summary>
        public static double ClampLivenessRetryAfter(double retryAfterSeconds)
        {
            if (!(retryAfterSeconds > 0.0))
            {
                return 0.0;
            }

            return Math.Min(retryAfterSeconds, Constants.Network.SERVER_HEARTBEAT_INTERVAL_SECONDS);
        }

        private void SendLease(double now)
        {
            BackendRequest request = BackendRequest.Post(GameServerRoutes.SERVER_LEASE,
                new ServerLeaseRequest(_instanceId, _serverRole, _bootId), typeof(ServerLeaseResponse), null,
                Constants.Network.API_TIMEOUT_SECONDS, true);
            _leaseSentAt    = now;
            _nextLeaseTime  = double.PositiveInfinity;
            _leaseRequestId = _gateway.Send(request, this);
        }

        private void SendRegister(double now)
        {
            BackendRequest request = BackendRequest.Post(GameServerRoutes.SERVER_REGISTER,
                new ServerRegisterRequest(_instanceId, _bootId, _serverRole, _address, _port, _buildId, _contentVersion),
                typeof(ServerInstanceResponse), null, Constants.Network.API_TIMEOUT_SECONDS);
            _nextRegisterTime  = now + Constants.Network.SERVER_PRESENCE_RETRY_SECONDS;
            _registerRequestId = _gateway.Send(request, this);
        }

        private void SendHeartbeat(double now)
        {
            BackendRequest request = BackendRequest.Post(_heartbeatPath, new ServerHeartbeatRequest(_bootId),
                typeof(ServerInstanceResponse), null, Constants.Network.API_TIMEOUT_SECONDS, true);
            _heartbeatSentAt    = now;
            _nextHeartbeatTime  = now + Constants.Network.SERVER_HEARTBEAT_INTERVAL_SECONDS;
            _heartbeatRequestId = _gateway.Send(request, this);
        }

        private void SendDrain(double now)
        {
            BackendRequest request = BackendRequest.Post(_drainPath, new ServerHeartbeatRequest(_bootId),
                typeof(ServerInstanceResponse), null, Constants.Network.API_TIMEOUT_SECONDS, true);
            _drainSentAt    = now;
            _nextDrainTime  = now + Constants.Network.SERVER_PRESENCE_RETRY_SECONDS;
            _drainRequestId = _gateway.Send(request, this);
        }

        private void OnLeaseResponse(in BackendResponse response)
        {
            double now = _clock();
            if (IsUnrecoverable(in response))
            {
                MarkLost($"임대 발급 거절, status={response.StatusCode}, reason={response.ReasonCode}");
                return;
            }

            // 되살릴 수 없는 거절이 아니면 다시 받는다. 503은 같은 인스턴스의 이전 boot가 아직 살아 있는 경우다
            if ((!response.IsSucceeded) || (!TryReadLease(response.Payload as ServerLeaseResponse, out string token,
                out double lifetime)))
            {
                _nextLeaseTime = now + Math.Max(Constants.Network.SERVER_PRESENCE_RETRY_SECONDS,
                    ClampLivenessRetryAfter(response.RetryAfterSeconds));
                _warn?.Invoke($"[Presence] 임대 발급 실패, 다시 받는다, outcome={response.Outcome}, status={response.StatusCode}, " +
                    $"reason={response.ReasonCode}");
                return;
            }

            // 보낸 시각부터 세면 응답 지연만큼 일찍 만료로 본다. 늦게 보면 만료된 토큰을 싣게 된다
            double expiresAt = _leaseSentAt + lifetime;
            _credentials.SetLease(token, expiresAt);
            _nextLeaseTime = expiresAt - Math.Min(Constants.Network.SERVER_LEASE_RENEW_MARGIN_SECONDS,
                lifetime * LEASE_RENEW_LIFETIME_RATIO);
            if (_stage == EServerPresenceStage.Leasing)
            {
                _stage            = EServerPresenceStage.Registering;
                _nextRegisterTime = now;
            }
        }

        private void OnRegisterResponse(in BackendResponse response)
        {
            if ((response.IsSucceeded) && (response.Payload is ServerInstanceResponse instance)
                && (instance.ReasonCode == ContractCodes.RejectReason.NONE))
            {
                if (instance.State == GameSessionCodes.InstanceState.OFFLINE)
                {
                    MarkLost("등록 응답이 Offline");
                    return;
                }

                _unauthorizedCount = 0;
                _stage             = EServerPresenceStage.Running;
                RecordState(instance.State, _clock());
                _nextHeartbeatTime = _clock() + Constants.Network.SERVER_HEARTBEAT_INTERVAL_SECONDS;
                return;
            }

            if (response.Outcome == EBackendOutcome.Rejected)
            {
                HandleRejected(in response, "등록");
                return;
            }

            // 503(같은 인스턴스의 이전 boot가 아직 살아 있음)은 Retry-After 뒤에 다시 등록한다
            _nextRegisterTime = Math.Max(_nextRegisterTime, _clock() + response.RetryAfterSeconds);
            _warn?.Invoke($"[Presence] 풀 등록 일시 실패, 다시 부른다, outcome={response.Outcome}, status={response.StatusCode}");
        }

        private void OnHeartbeatResponse(in BackendResponse response)
        {
            if ((response.IsSucceeded) && (response.Payload is ServerInstanceResponse instance)
                && (instance.ReasonCode == ContractCodes.RejectReason.NONE))
            {
                // 장애 취소로 Offline이 된 boot는 되살릴 수 없다. 새 bootId로 다시 떠야 한다
                if (instance.State == GameSessionCodes.InstanceState.OFFLINE)
                {
                    MarkLost("heartbeat 응답이 Offline");
                    return;
                }

                _unauthorizedCount = 0;
                RecordState(instance.State, _heartbeatSentAt);
                return;
            }

            if (response.Outcome == EBackendOutcome.Rejected)
            {
                HandleRejected(in response, "heartbeat");
                return;
            }

            // 일시 실패는 다음 주기에 다시 보낸다. Retry-After는 heartbeat 주기까지만 따라 부재 한계 안에서 다시 보낸다
            _nextHeartbeatTime = Math.Max(_nextHeartbeatTime, _clock() + ClampLivenessRetryAfter(response.RetryAfterSeconds));
            _warn?.Invoke($"[Presence] heartbeat 일시 실패, outcome={response.Outcome}, status={response.StatusCode}");
        }

        private void OnDrainResponse(in BackendResponse response)
        {
            if ((response.IsSucceeded) && (response.Payload is ServerInstanceResponse instance)
                && (instance.ReasonCode == ContractCodes.RejectReason.NONE))
            {
                _drainCompleted = true;
                RecordState(instance.State, _drainSentAt);
                return;
            }

            // 인스턴스가 이미 Offline이거나 다른 boot 소유면 드레인할 대상이 없다
            if (IsUnrecoverable(in response))
            {
                _drainCompleted = true;
                MarkLost($"드레인 거절, status={response.StatusCode}, reason={response.ReasonCode}");
                return;
            }

            if ((response.Outcome == EBackendOutcome.Rejected) && (response.StatusCode == STATUS_UNAUTHORIZED))
            {
                RenewLeaseAfterUnauthorized("드레인");
            }

            _nextDrainTime = Math.Max(_nextDrainTime, _clock() + ClampLivenessRetryAfter(response.RetryAfterSeconds));
            _warn?.Invoke($"[Presence] 드레인 실패, 다시 보낸다, outcome={response.Outcome}, status={response.StatusCode}");
        }

        // 409 SESSION_TERMINAL(오프라인), 409 SESSION_MISMATCH(다른 bootId, 인스턴스 없음)만 이 프로세스로 되살릴 수 없다.
        // 401은 API가 임대 토큰을 받지 않은 경우라 새 임대를 받고, 그 밖의 거절은 재시도 간격 뒤에 다시 보낸다
        private void HandleRejected(in BackendResponse response, string what)
        {
            if (IsUnrecoverable(in response))
            {
                MarkLost($"{what} 거절, status={response.StatusCode}, reason={response.ReasonCode}");
                return;
            }

            if (response.StatusCode == STATUS_UNAUTHORIZED)
            {
                RenewLeaseAfterUnauthorized(what);
            }

            double retryAt = _clock() + Math.Max(Constants.Network.SERVER_PRESENCE_RETRY_SECONDS,
                ClampLivenessRetryAfter(response.RetryAfterSeconds));
            if (_stage == EServerPresenceStage.Registering)
            {
                _nextRegisterTime = Math.Max(_nextRegisterTime, retryAt);
            }
            else
            {
                _nextHeartbeatTime = Math.Max(_nextHeartbeatTime, retryAt);
            }

            _warn?.Invoke($"[Presence] {what} 거절, 다시 보낸다, status={response.StatusCode}, reason={response.ReasonCode}");
        }

        // 첫 401은 바로 새 임대를 받고, 이어지는 401은 재시도 간격을 둬 발급 경로를 두드리지 않는다
        private void RenewLeaseAfterUnauthorized(string what)
        {
            ++_unauthorizedCount;
            _credentials.ClearLease();
            if (_leaseRequestId == 0)
            {
                double now = _clock();
                _nextLeaseTime = (_unauthorizedCount <= 1) ? now : (now + Constants.Network.SERVER_PRESENCE_RETRY_SECONDS);
            }

            _warn?.Invoke($"[Presence] {what} 자격 거절, 임대를 다시 받는다, count={_unauthorizedCount}");
        }

        private void RecordState(int state, double sentAt)
        {
            // 늦게 도착한 옛 응답이 새 상태를 덮지 않게 보낸 시각으로 비교한다
            if (sentAt < _instanceStateSentAt)
            {
                return;
            }

            _instanceState       = state;
            _instanceStateSentAt = sentAt;
        }

        private void MarkLost(string reason)
        {
            _stage      = EServerPresenceStage.Lost;
            _lostReason = reason;
            _warn?.Invoke($"[Presence] 서버 풀 존재를 잃었다, 프로세스를 내린다, instance={_instanceId}, {reason}");
        }

        private static bool TryReadLease(ServerLeaseResponse lease, out string token, out double lifetime)
        {
            token    = null;
            lifetime = 0.0;
            if ((lease == null) || (!BackendLaunchSettings.IsHeaderSafe(lease.LeaseToken)))
            {
                return false;
            }

            double remaining = (lease.ExpiresAtUnixMs - lease.ServerTimeUnixMs) / MILLISECONDS_PER_SECOND;
            if (!(remaining > 0.0))
            {
                return false;
            }

            token    = lease.LeaseToken;
            lifetime = Math.Min(remaining, Constants.Network.MAX_SERVER_LEASE_SECONDS);
            return true;
        }
    }
}
