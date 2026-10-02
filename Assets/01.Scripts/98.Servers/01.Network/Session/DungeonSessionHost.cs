// System
using System;
using System.Globalization;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;

namespace Shinshi.Network
{
    /// <summary> 던전 프로세스의 출정 하나를 이끈다, 입장 명단을 모아 start 영수증 뒤 세션을 열고 종료 저장 뒤 정산을 부른다 </summary>
    public sealed class DungeonSessionHost : IParticipantAdmission, IBackendResponseListener, IDisposable
    {
        // 단계: 대기(입장자를 명단에 등록하고 접속 임대를 이 던전으로 옮김) -> start 확정 -> 세션 시작(시작 인원과 배율 고정)
        // -> 진행 -> 종료 저장 확인 -> 정산 -> 재활용. 백엔드 응답은 Pump에서 오므로 여기서는 결과만 기록하고, 세션 시작은
        // 다음 서버 틱의 BeginTick에서 한다. 그래야 세션 시각이 전투 틱 시각과 같은 축에 있고, 틱 중간에 참가 집합이 바뀌지 않는다.
        //
        // 명단 닫힘(포기, 유예 만료, 세션 종료)은 프로세스 명단(ServerSessionRegistry)에도 옮겨 적는다.
        // 인증기는 그 명단으로 재합류를 거절하므로 둘이 어긋나면 정산이 끝난 참가자가 다시 들어온다.
        //
        // 시작 전 규칙: 끊긴 참가자는 닫지 않고 돌아오기를 기다린다(유예 상한은 예약 만료다). 끊긴 사람이 있거나 접속 임대
        // 이관이 끝나지 않았으면 start를 보내지 않는다. 이탈(Departed)한 사람이 생기면 start를 아예 보내지 않고 예약 만료가
        // 티켓을 돌려주게 둔다. 일부만 시작하면 남은 사람의 티켓이 시작 없이 소모된다.
        //
        // 출정은 프로세스당 한 번이다. 끝나면(TerminalReason) 세션 상태를 되돌려 다음 출정에 쓰지 않고 프로세스를 내린다.
        // 남은 상태(명단, 보존소, 레지스트리, 전투 개체)를 하나라도 덜 지우면 다음 파티에 이전 출정이 섞인다.
        //
        // 출정 ID는 첫 입장 토큰의 sortieId다. 서비스 자격이 없는 개발 실행은 토큰에 출정이 없어 프로세스 안에서 만든
        // 개발 출정 ID로 시작 준비 시간 뒤 바로 시작하고, 저장과 정산은 기록만 남긴다

        /****************************************
        *                Fields
        ****************************************/

        private const string GUID_FORMAT          = "D";
        private const int    STATUS_BAD_REQUEST   = 400;
        private const int    STATUS_CONFLICT      = 409;

        private enum ELeaseTransferState
        {
            Waiting  = 0,
            InFlight = 1,
            Done     = 2,
            Rejected = 3
        }

        private struct LeaseSlot
        {
            public int                 ParticipantId;
            public string              ApiParticipantId;
            public string              NewLeaseId;
            public int                 RequestId;
            public double              RetryAt;
            public ELeaseTransferState State;
        }

        private readonly ParticipantRoster     _roster;
        private readonly ServerSessionRegistry _registry;
        private readonly SessionConfigSO       _config;
        private readonly BackendGateway        _gateway;
        private readonly bool                  _allowLocalSession;
        private readonly Action<string>        _warn;
        private readonly IBackendEventSink     _eventSink;
        private readonly string                _localSessionId;
        private readonly string[]              _readyIds;
        private readonly LeaseSlot[]           _leases;
        private readonly int[]                 _absentIds;

        private string                _sessionId          = null;
        private bool                  _usesBackend        = false;
        private SessionEventQueue     _events             = null;
        private DungeonSession        _session            = null;
        private SessionSettlement     _settlement         = null;
        private byte[]                _lootSalt           = null;
        private double                _serverTime         = 0.0;
        private double                _localStartTime     = 0.0;
        private double                _nextStartTime      = 0.0;
        private int                   _startRequestId     = 0;
        private int                   _leaseCount         = 0;
        private int                   _absentCount        = 0;
        private bool                  _hasLocalStartTime  = false;
        private bool                  _startDirty         = false;
        private bool                  _startRetryPending  = false;
        private bool                  _startCommitted     = false;
        private bool                  _startRejected      = false;
        private bool                  _lobbyBlocked       = false;
        private bool                  _inTick             = false;
        private bool                  _disposed           = false;
        private ESortieTerminalReason _terminalReason     = ESortieTerminalReason.None;

        /****************************************
        *              Properties
        ****************************************/

        public ParticipantRoster Roster      => _roster;
        public string            SessionId   => _sessionId;
        public bool              UsesBackend => _usesBackend;

        // 시작 전에는 null이다
        public DungeonSession    Session    => _session;
        public SessionSettlement Settlement => _settlement;

        public bool HasStarted       => _session != null;
        public bool IsSessionRunning => (_session != null) && (_session.IsRunning);
        public bool IsStartRejected  => _startRejected;

        // 시작 전 이탈자가 있어 start를 보내지 않고 예약 만료를 기다린다
        public bool IsLobbyBlocked   => _lobbyBlocked;

        // 시작 전에 연결이 끊겨 돌아오기를 기다리는 참가자 수
        public int  LobbyAbsentCount => _absentCount;

        /// <summary> None이 아니면 이 출정은 끝났고 프로세스를 재활용한다 </summary>
        public ESortieTerminalReason TerminalReason => _terminalReason;
        public bool                  IsTerminal     => _terminalReason != ESortieTerminalReason.None;

        // 보존 기록 만료로 명단을 닫는 것은 시작 뒤만이다. 시작 전 끊김의 유예 상한은 예약 만료다
        public bool ClosesParticipantOnRetentionExpiry => HasStarted;

        // 초, 안전 복귀 지점이 없을 때 부활을 다시 평가하는 간격
        public float SafePointRetryInterval => _config.SafePointRetryInterval;

        /// <summary> 접속 임대 이관이 확정 거절된 참가자, 인증된 연결을 끊어야 한다. 메인 스레드에서 통지한다 </summary>
        public event Action<int> ParticipantEvicted;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> GameDB 초기화 뒤 만든다. gateway가 null이면 allowLocalSession일 때만 개발 출정으로 돈다, eventSink는 저장 경로를 바꿀 때만 넣는다 </summary>
        public DungeonSessionHost(SessionConfigSO config, ServerSessionRegistry registry, BackendGateway gateway,
            bool allowLocalSession, Action<string> warn, IBackendEventSink eventSink = null)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if ((gateway == null) && (!allowLocalSession))
            {
                throw new ArgumentException("출시 던전은 백엔드 게이트웨이가 필요하다", nameof(gateway));
            }

            _config            = config;
            _registry          = registry;
            _gateway           = gateway;
            _allowLocalSession = allowLocalSession;
            _warn              = warn;
            _eventSink         = eventSink;
            _roster            = new ParticipantRoster(config);
            _readyIds          = new string[Constants.Session.MAX_PARTY_SIZE];
            _leases            = new LeaseSlot[Constants.Session.MAX_PARTY_SIZE];
            _absentIds         = new int[Constants.Session.MAX_PARTY_SIZE];
            _localSessionId    = Guid.NewGuid().ToString(GUID_FORMAT);
            _roster.ParticipantClosed += OnParticipantClosed;
        }

        // 인증기가 명단 결합 뒤 메인 스레드에서 부른다. 시작 뒤에는 원래 참가자의 재접속만 받는다
        public ERejectReason Admit(in JoinTokenClaims claims, SessionBinding binding)
        {
            if ((binding == null) || (_disposed) || (IsTerminal))
            {
                return ERejectReason.SessionMismatch;
            }

            ERejectReason reason = ResolveSession(claims.SortieId);
            if (reason != ERejectReason.None)
            {
                return reason;
            }

            int participantId = binding.ParticipantId;
            if (_roster.TryGet(participantId, out ParticipantSnapshot existing))
            {
                return (existing.ConnectionState == EParticipantConnectionState.Departed)
                    ? ERejectReason.AlreadySettled
                    : ERejectReason.None;
            }

            if ((_roster.IsSealed) || (_session != null) || (_startCommitted))
            {
                return ERejectReason.SessionMismatch;
            }

            string apiParticipantId = null;
            if ((_usesBackend) && (!SessionContractIds.TryGetApiParticipantId(binding.ParticipantKey, out apiParticipantId)))
            {
                return ERejectReason.SessionMismatch;
            }

            if (!_roster.TryAdd(participantId, binding.ParticipantKey))
            {
                return ERejectReason.NoCapacity;
            }

            if (_usesBackend)
            {
                AddLeaseSlot(participantId, apiParticipantId);
            }

            _startDirty = true;
            return ERejectReason.None;
        }

        /// <summary> 서버 틱 맨 앞에서 부른다, 세션이 진행 중이면 이번 틱을 열고 참이다. 시작 전에는 시작 조건만 본다 </summary>
        public bool BeginTick(double serverTime)
        {
            if ((_disposed) || (_inTick) || (double.IsNaN(serverTime)) || (double.IsInfinity(serverTime))
                || (serverTime < _serverTime))
            {
                return false;
            }

            _serverTime = serverTime;
            if (_session == null)
            {
                TickLobby(serverTime);
                if (_session == null)
                {
                    return false;
                }
            }

            if (!_session.BeginTick(serverTime))
            {
                return false;
            }

            _inTick = true;
            return true;
        }

        /// <summary> 피해와 사망 뒤, 부활과 복제 전에 부른다, 종료를 판정하고 종료 저장이 확인되면 정산을 부른다 </summary>
        public void CompleteTick()
        {
            if (!_inTick)
            {
                return;
            }

            _inTick = false;
            _session.CompleteTick();

            // 서비스 장애 취소는 API 복구 작업이 정산하므로 여기서 부르지 않는다
            if ((_session.CanReturn) && (_session.EndReason != ESessionEndReason.ServiceCancelled))
            {
                _settlement.TryBegin(_serverTime);
            }

            _settlement.Tick(_serverTime);
            UpdateTerminal();

            // TODO(민성): 진행 중이면 Constants.Network.CHECKPOINT_INTERVAL_SECONDS마다 진단용 체크포인트(SessionCheckpointRequest,
            // lastEventSeq는 저장 큐의 LastAcknowledgedSequence)를 checkpoints 경로로 보낸다. 경제 복구에는 쓰지 않는다
        }

        /// <summary> 연결이 끊겨 캐릭터를 서버에 남겼거나 지웠을 때 부른다, 시각은 마지막 서버 틱 시각이다 </summary>
        public bool OnParticipantDetached(int participantId, double serverTime)
        {
            if (_session == null)
            {
                return MarkLobbyAbsent(participantId);
            }

            return (IsSessionRunning) && (_session.TryDisconnect(participantId, serverTime));
        }

        /// <summary> 같은 참가자가 캐릭터를 다시 넘겨받았을 때 부른다, 유예가 끝났으면 거짓 </summary>
        public bool OnParticipantAttached(int participantId, double serverTime)
        {
            if (_session == null)
            {
                return ClearLobbyAbsent(participantId);
            }

            return (IsSessionRunning) && (_session.TryReconnect(participantId, serverTime));
        }

        /// <summary> 전투가 확정한 생사를 명단에 옮긴다, 유예 중 사망도 같은 축에 기록한다 </summary>
        public void ReportLifeState(int participantId, EParticipantLifeState state)
        {
            _roster.TrySetLifeState(participantId, state);
        }

        /// <summary> 보스 사망이 확정된 틱에 부른다, BeginTick과 CompleteTick 사이에서만 받는다 </summary>
        public bool ReportBossDefeated()
        {
            return (_inTick) && (_session.ReportBossDefeated());
        }

        /// <summary> 연결이 끊긴 캐릭터를 유예 동안 남길지, 진행 중인 세션의 닫히지 않은 원래 참가자만 남긴다 </summary>
        public bool CanRetain(int participantId)
        {
            return (IsSessionRunning) && (!_roster.IsClosed(participantId));
        }

        /// <summary> 부활 대기와 보스 공용 횟수를 본다, 진행 중인 세션이 아니면 SessionTerminal </summary>
        public ERejectReason CheckRevive(int participantId, double deathTime, double serverTime)
        {
            return (IsSessionRunning) ? _session.CheckRevive(participantId, deathTime, serverTime) : ERejectReason.SessionTerminal;
        }

        /// <summary> 안전 지점을 찾은 뒤 부활을 확정한다, 보스전은 공용 횟수를 하나 쓴다 </summary>
        public ERejectReason CommitRevive(int participantId, double deathTime, double serverTime, out float healthRate)
        {
            healthRate = 0f;
            return (IsSessionRunning)
                ? _session.CommitRevive(participantId, deathTime, serverTime, out healthRate)
                : ERejectReason.SessionTerminal;
        }

        /// <summary> 사망한 참가자가 들어갈 상태, 보스전에서 공용 횟수가 없으면 관전이다 </summary>
        public EParticipantLifeState LifeStateOnDeath()
        {
            return (IsSessionRunning) ? _session.LifeStateOnDeath() : EParticipantLifeState.Dead;
        }

        /// <summary> 출정을 받은 뒤 API가 이 인스턴스를 다시 IDLE로 보고했을 때 부른다, 예약이 해제돼 출정을 닫는다 </summary>
        public void OnReservationReleased()
        {
            if ((_disposed) || (IsTerminal) || (_sessionId == null))
            {
                return;
            }

            // 끝난 세션은 정산을 마친 API가 인스턴스를 IDLE로 돌린 것일 수 있다. 정산 결과로 종료 원인을 정하게 둔다
            if ((_session != null) && (_session.IsEnded))
            {
                return;
            }

            // 시작 뒤라면 API가 이미 출정을 취소한 것이다. 전투를 멈추고 장애 취소로 닫는다
            if (IsSessionRunning)
            {
                _session.CancelService(_serverTime);
            }

            SetTerminal(ESortieTerminalReason.ReservationReleased);
        }

        // 게이트웨이가 메인 스레드의 Pump에서 부른다. start와 접속 임대 이관 응답이 이 개체로 온다
        public void OnBackendResponse(in BackendResponse response)
        {
            if ((_disposed) || (response.RequestId == 0))
            {
                return;
            }

            if (response.RequestId == _startRequestId)
            {
                _startRequestId = 0;
                OnStartResponse(in response);
                return;
            }

            for (int i = 0; i < _leaseCount; ++i)
            {
                ref LeaseSlot slot = ref _leases[i];
                if ((slot.State == ELeaseTransferState.InFlight) && (slot.RequestId == response.RequestId))
                {
                    slot.RequestId = 0;
                    OnLeaseTransferResponse(ref slot, in response);
                    return;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _roster.ParticipantClosed -= OnParticipantClosed;
            _session?.Dispose();
        }

        private void OnStartResponse(in BackendResponse response)
        {
            if (IsTerminal)
            {
                return;
            }

            if ((response.IsSucceeded) && (response.Payload is OperationResult result))
            {
                if (result.Status == ContractCodes.OperationStatus.COMMITTED)
                {
                    CommitStart();
                    return;
                }

                ScheduleStartRetry(0.0);
                return;
            }

            if ((response.Outcome != EBackendOutcome.Rejected) || (!IsPermanentStartRejection(in response)))
            {
                ScheduleStartRetry(response.RetryAfterSeconds);
                return;
            }

            // 아직 오지 않은 참가자가 있다. 다음 입장 때 늘어난 준비 집합으로 다시 부른다
            if (response.ReasonCode == ContractCodes.RejectReason.NOT_ALL_READY)
            {
                return;
            }

            _startRejected = true;
            _warn?.Invoke($"[Session] start 영구 거절로 출정을 닫는다, session={_sessionId}, reason={response.ReasonCode}");
            SetTerminal(ESortieTerminalReason.StartRejected);
        }

        // 401(임대 만료), 403은 서버 임대를 다시 받으면 통과할 수 있다. 400과 409(사유 있는 거절)만 영구로 본다
        private static bool IsPermanentStartRejection(in BackendResponse response)
        {
            return (response.StatusCode == STATUS_BAD_REQUEST) || (response.StatusCode == STATUS_CONFLICT);
        }

        // start 영수증을 받은 순간 출정 비밀 salt를 만든다. 개발 출정은 준비 시간이 끝난 순간이다
        private void CommitStart()
        {
            _startCommitted = true;
            _lootSalt       = LootRoller.CreateSalt();
        }

        private void OnLeaseTransferResponse(ref LeaseSlot slot, in BackendResponse response)
        {
            if ((response.IsSucceeded) && (response.Payload is LeaseTransferResponse transfer)
                && (transfer.ReasonCode == ContractCodes.RejectReason.NONE))
            {
                slot.State = ELeaseTransferState.Done;
                _startDirty = true;
                return;
            }

            bool permanent = ((response.IsSucceeded) && (response.Payload is LeaseTransferResponse))
                || ((response.Outcome == EBackendOutcome.Rejected)
                    && ((response.StatusCode == STATUS_BAD_REQUEST) || (response.StatusCode == STATUS_CONFLICT)));
            if (!permanent)
            {
                slot.State   = ELeaseTransferState.Waiting;
                slot.RetryAt = _serverTime + Math.Max(Constants.Network.LEASE_TRANSFER_RETRY_SECONDS, response.RetryAfterSeconds);
                return;
            }

            // 계정 임대가 다른 서버에 있거나 출정과 맞지 않는다. 이 사람은 이 던전에 있으면 안 되므로 내보내고 명단을 닫는다.
            // 시작 전 이탈이 생겼으므로 start는 보내지 않고 예약 만료가 티켓을 돌려준다
            slot.State = ELeaseTransferState.Rejected;
            int participantId = slot.ParticipantId;
            _warn?.Invoke($"[Session] 접속 임대 이관 거절로 참가자를 내보낸다, session={_sessionId}, participantId={participantId}, " +
                $"status={response.StatusCode}, reason={response.ReasonCode}");
            _roster.TryClose(participantId, ESessionEndReason.Abandon, _roster.LastServerTime);
            ParticipantEvicted?.Invoke(participantId);
        }

        private ERejectReason ResolveSession(string sortieId)
        {
            bool   hasSortie = !string.IsNullOrEmpty(sortieId);
            string sessionId = hasSortie ? sortieId : _localSessionId;
            if ((!hasSortie) && (!_allowLocalSession))
            {
                return ERejectReason.SessionMismatch;
            }

            if ((hasSortie) && (!SessionContractIds.IsApiId(sortieId)))
            {
                return ERejectReason.SessionMismatch;
            }

            if (_sessionId != null)
            {
                return string.Equals(_sessionId, sessionId, StringComparison.Ordinal)
                    ? ERejectReason.None
                    : ERejectReason.SessionMismatch;
            }

            bool usesBackend = (hasSortie) && (_gateway != null);
            if ((!usesBackend) && (!_allowLocalSession))
            {
                return ERejectReason.BackendUnavailable;
            }

            _sessionId   = sessionId;
            _usesBackend = usesBackend;
            return ERejectReason.None;
        }

        private void TickLobby(double serverTime)
        {
            if ((_sessionId == null) || (_roster.Count == 0) || (_startRejected) || (IsTerminal))
            {
                return;
            }

            if (_startCommitted)
            {
                BeginSession(serverTime);
                return;
            }

            if (!_usesBackend)
            {
                // 개발 출정은 첫 입장자가 보인 틱부터 준비 시간을 센다. 그 사이 들어온 참가자까지 시작 인원이 된다
                if (!_hasLocalStartTime)
                {
                    _hasLocalStartTime = true;
                    _localStartTime    = serverTime + _config.ReadyCountdownSeconds;
                }

                if (serverTime >= _localStartTime)
                {
                    CommitStart();
                    BeginSession(serverTime);
                }

                return;
            }

            if (_roster.HasDeparted)
            {
                if (!_lobbyBlocked)
                {
                    _lobbyBlocked = true;
                    _warn?.Invoke($"[Session] 시작 전 이탈자가 있어 start를 보내지 않는다, 예약 만료를 기다린다, session={_sessionId}");
                }

                return;
            }

            if (!TickLeaseTransfers(serverTime))
            {
                return;
            }

            if ((_startRequestId != 0) || (_absentCount > 0))
            {
                return;
            }

            bool retryDue = (_startRetryPending) && (serverTime >= _nextStartTime);
            if ((!_startDirty) && (!retryDue))
            {
                return;
            }

            SendStart();
        }

        // 보낼 시각이 된 이관을 보내고, 모두 확정됐으면 참이다
        private bool TickLeaseTransfers(double serverTime)
        {
            bool allDone = true;
            for (int i = 0; i < _leaseCount; ++i)
            {
                ref LeaseSlot slot = ref _leases[i];
                if (slot.State == ELeaseTransferState.Done)
                {
                    continue;
                }

                allDone = false;
                if ((slot.State == ELeaseTransferState.Waiting) && (serverTime >= slot.RetryAt))
                {
                    SendLeaseTransfer(ref slot);
                }
            }

            return allDone;
        }

        // 입장 확정 뒤 계정의 접속 임대를 마을에서 이 던전으로 옮긴다. newLeaseId는 출정과 참가자에서 만들어 재시도에도 같다
        private void SendLeaseTransfer(ref LeaseSlot slot)
        {
            string path = string.Format(CultureInfo.InvariantCulture, GameServerRoutes.LEASE_TRANSFER_FORMAT, _sessionId);
            BackendRequest request = BackendRequest.Post(path, new LeaseTransferRequest(slot.ApiParticipantId, slot.NewLeaseId),
                typeof(LeaseTransferResponse), slot.NewLeaseId, Constants.Network.API_TIMEOUT_SECONDS);
            slot.State     = ELeaseTransferState.InFlight;
            slot.RequestId = _gateway.Send(request, this);
        }

        private void AddLeaseSlot(int participantId, string apiParticipantId)
        {
            ref LeaseSlot slot = ref _leases[_leaseCount];
            slot.ParticipantId    = participantId;
            slot.ApiParticipantId = apiParticipantId;
            slot.NewLeaseId       = SessionContractIds.CreateLeaseTransferId(_sessionId, apiParticipantId);
            slot.RequestId        = 0;
            slot.RetryAt          = 0.0;
            slot.State            = ELeaseTransferState.Waiting;
            ++_leaseCount;
        }

        // 입장자 전원의 API 참가자 ID를 정렬해 보낸다. 같은 집합이면 operationId와 본문이 같아 재시도해도 한 번만 시작한다
        private void SendStart()
        {
            int count = 0;
            for (int i = 0; i < _roster.Count; ++i)
            {
                if (!SessionContractIds.TryGetApiParticipantId(_roster.GetAt(i).ParticipantKey, out string participantId))
                {
                    _startRejected = true;
                    _warn?.Invoke($"[Session] 참가자 키가 API 형식이 아니라 start를 보내지 않는다, session={_sessionId}");
                    SetTerminal(ESortieTerminalReason.StartRejected);
                    return;
                }

                _readyIds[count] = participantId;
                ++count;
            }

            Array.Sort(_readyIds, 0, count, StringComparer.Ordinal);
            var ready = new string[count];
            Array.Copy(_readyIds, ready, count);

            string operationId = SessionContractIds.CreateStartOperationId(_sessionId, ready, count);
            string path        = string.Format(CultureInfo.InvariantCulture, GameServerRoutes.START_FORMAT, _sessionId);
            BackendRequest request = BackendRequest.Post(path, new SessionStartRequest(operationId, ready), typeof(OperationResult),
                operationId, Constants.Network.API_TIMEOUT_SECONDS);

            _startDirty        = false;
            _startRetryPending = false;
            _startRequestId    = _gateway.Send(request, this);
        }

        private void ScheduleStartRetry(double retryAfterSeconds)
        {
            _startRetryPending = true;
            _nextStartTime     = _serverTime + Math.Max(Constants.Session.START_RETRY_SECONDS, retryAfterSeconds);
        }

        // start 영수증 뒤 첫 틱에서 한 번 부른다. 시작 인원과 인원별 배율은 이 시점에 고정되고 이후 이탈해도 바뀌지 않는다.
        // 영수증을 받은 뒤에는 API 쪽 출정이 이미 시작돼 티켓이 소모됐다. 여기서 못 열면 되돌릴 방법이 없는 결함이라
        // 오류 종료 코드로 프로세스를 내리고, API는 heartbeat 부재로 장애 취소를 처리한다
        private void BeginSession(double serverTime)
        {
            IBackendEventSink sink = CreateSink();
            if (sink == null)
            {
                _startRejected = true;
                ReportInvariant("저장 경로를 만들지 못했다");
                return;
            }

            _events = new SessionEventQueue(_sessionId, sink);

            // TODO(민성): 웨이브 디렉터가 생기면 일반전은 CreateNormal로 만들고 source ID와 직업 효과를 Begin 전에 등록한다.
            // 지금은 웨이브 없는 세션(개발 보스 종료만 제공)으로 연다
            var session = new DungeonSession(_sessionId, _roster, _events, _config, null, null, _lootSalt);
            if (!session.Begin(serverTime))
            {
                session.Dispose();
                _events        = null;
                _startRejected = true;
                ReportInvariant($"시작 조건이 맞지 않아 세션을 열지 못했다, count={_roster.Count}, active={_roster.ActiveCount}");
                return;
            }

            _session    = session;
            _settlement = new SessionSettlement(_sessionId, _usesBackend ? _gateway : null, _warn);

            // start를 보낸 뒤 끊긴 참가자는 시작 시각부터 재접속 유예를 센다
            for (int i = 0; i < _absentCount; ++i)
            {
                _session.TryDisconnect(_absentIds[i], serverTime);
            }

            _absentCount = 0;
        }

        private void ReportInvariant(string detail)
        {
            _warn?.Invoke($"[Session] 불변식 위반, start 확정 뒤 세션을 열지 못해 프로세스를 내린다, session={_sessionId}, {detail}");
            SetTerminal(ESortieTerminalReason.InvariantViolation);
        }

        private IBackendEventSink CreateSink()
        {
            if (_eventSink != null)
            {
                return _eventSink;
            }

            if (_usesBackend)
            {
                return new HttpBackendEventSink(_gateway, _warn);
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return new DevBackendEventSink();
#else
            _warn?.Invoke("[Session] 출시 빌드에 개발 저장 경로가 없다");
            return null;
#endif
        }

        // 세션이 끝난 뒤 정산이나 장애 취소 저장이 마무리됐는지 본다
        private void UpdateTerminal()
        {
            if ((IsTerminal) || (_session == null) || (!_session.IsEnded))
            {
                return;
            }

            if (_session.EndReason == ESessionEndReason.ServiceCancelled)
            {
                // 확정 거절로 멈춘 큐는 더 저장하지 않는다. 그 밖에는 종료 기록이 저장된 뒤 내린다
                if ((_session.HasTerminalStorage) || (_session.CanReturn))
                {
                    SetTerminal(ESortieTerminalReason.ServiceCancelled);
                }

                return;
            }

            if (_session.HasTerminalStorage)
            {
                // 종료 이벤트 저장 전에 큐가 멈췄다. 결과를 확정할 수 없어 API 장애 취소가 맡는다
                SetTerminal(ESortieTerminalReason.ServiceCancelled);
                return;
            }

            if (_settlement.State == ESessionSettlementState.Settled)
            {
                SetTerminal(ESortieTerminalReason.Settled);
            }
            else if (_settlement.State == ESessionSettlementState.Abandoned)
            {
                SetTerminal(ESortieTerminalReason.SettlementAbandoned);
            }
        }

        private void SetTerminal(ESortieTerminalReason reason)
        {
            if (IsTerminal)
            {
                return;
            }

            _terminalReason = reason;
        }

        private bool MarkLobbyAbsent(int participantId)
        {
            if ((!_roster.TryGet(participantId, out ParticipantSnapshot participant))
                || (participant.ConnectionState == EParticipantConnectionState.Departed))
            {
                return false;
            }

            for (int i = 0; i < _absentCount; ++i)
            {
                if (_absentIds[i] == participantId)
                {
                    return true;
                }
            }

            _absentIds[_absentCount] = participantId;
            ++_absentCount;
            return true;
        }

        private bool ClearLobbyAbsent(int participantId)
        {
            if ((!_roster.TryGet(participantId, out ParticipantSnapshot participant))
                || (participant.ConnectionState == EParticipantConnectionState.Departed))
            {
                return false;
            }

            for (int i = 0; i < _absentCount; ++i)
            {
                if (_absentIds[i] != participantId)
                {
                    continue;
                }

                --_absentCount;
                _absentIds[i]           = _absentIds[_absentCount];
                _absentIds[_absentCount] = 0;
                _startDirty             = true;
                break;
            }

            return true;
        }

        private void OnParticipantClosed(int participantId, ESessionEndReason reason, double serverTime)
        {
            _registry.Close(participantId);
        }
    }
}
