// System
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Systems;

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
        //
        // 출정 계약(SortieDescriptor: 종류, 맵, 전투, 보스, 난이도)도 첫 입장 토큰에서 고정한다. 이후 토큰이 출정 ID나 계약이
        // 다르면 SessionMismatch다. 일반전 계약은 웨이브 진행기 주입(WaveFactory)이 있어야 일반전으로 열고, 없으면 경고를 남기고
        // 웨이브 없는 개발 세션으로 연다. 보스전은 계약의 보스와 난이도로 보스, 보상 표, 경매 재료를 고른다.
        //
        // 준비 판정: 인증과 임대 이관만으로는 시작하지 않는다. 인증된 연결이 서버 플레이어 개체(AddPlayer)를 만들어
        // OnParticipantAttached로 알릴 때까지 그 참가자는 로딩 중이다. 로딩 중인 참가자가 있으면 start를 보내지 않고,
        // 인증 뒤 LOADING_TIMEOUT_SECONDS 안에 준비되지 않으면 연결을 끊는다. 준비 전에 끊기면 개체가 없어 전투 쪽 이탈 통지가
        // 오지 않으므로 OnConnectionLost가 같은 정리(OnParticipantDetached)를 한다. 연결 신호가 없는 단위 테스트는 기본값(끔)을 쓴다
        //
        // 포기는 메시지 처리기가 받아 두고 다음 틱 BeginTick 뒤에 처리한다. 틱 중간에 참가 집합이 바뀌지 않게 하기 위해서다.
        // 출정 상태(SortieStatusMessage)는 값이 바뀐 틱 끝과 (재)접속 때 그 참가자에게만 보낸다

        /****************************************
        *                Fields
        ****************************************/

        private const string GUID_FORMAT          = "D";
        private const int    STATUS_BAD_REQUEST   = 400;
        private const int    STATUS_CONFLICT      = 409;

        // 체크포인트 버퍼 처음 용량, 참가자 4명 기준 수백 바이트라 넉넉하다
        private const int    CHECKPOINT_INITIAL_BYTES = 1024;

        private enum ELeaseTransferState
        {
            Waiting  = 0,
            InFlight = 1,
            Done     = 2,
            Rejected = 3
        }

        // 참가자마다 하나, 명단 순서로 둔다. 같은 작업 ID 재전송에는 처음 결과를 다시 보낸다
        private struct AbandonSlot
        {
            public Guid          OperationId;
            public bool          Pending;
            public bool          HasResult;
            public ERejectReason Result;
        }

        private struct StatusSlot
        {
            public bool                Sent;
            public SortieStatusMessage Last;
        }

        // 인증됐지만 플레이어 개체가 아직 없는 참가자, 마감은 다음 틱에서 서버 시각으로 정한다
        private struct LoadingSlot
        {
            public int    ParticipantId;
            public bool   Awaiting;
            public double Deadline;
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
        private readonly LoadingSlot[]         _loading;
        private readonly int[]                 _absentIds;
        private readonly int[]                 _admittedIds;
        private readonly EPlayerClass[]        _admittedClasses;
        private readonly AbandonSlot[]         _abandons;
        private readonly StatusSlot[]          _statuses;
        private readonly SessionCheckpointState _checkpoint;
        private readonly MemoryStream          _checkpointStream;
        private readonly BinaryWriter          _checkpointWriter;

        private string                _sessionId          = null;
        private bool                  _usesBackend        = false;
        private SessionEventQueue     _events             = null;
        private DungeonSession        _session            = null;
        private SessionSettlement     _settlement         = null;
        private PartyAuctionDirector  _auction            = null;
        private string                _auctionItemId      = null;
        private float                 _auctionChance      = 0f;
        private byte[]                _lootSalt           = null;
        private double                _serverTime         = 0.0;
        private double                _localStartTime     = 0.0;
        private double                _nextStartTime      = 0.0;
        private int                   _startRequestId     = 0;
        private int                   _leaseCount         = 0;
        private int                   _absentCount        = 0;
        private int                   _loadingCount       = 0;
        private int                   _awaitingCount      = 0;
        private bool                  _hasLocalStartTime  = false;
        private bool                  _startDirty         = false;
        private bool                  _startRetryPending  = false;
        private bool                  _startCommitted     = false;
        private bool                  _startRejected      = false;
        private bool                  _lobbyBlocked       = false;
        private bool                  _inTick             = false;
        private bool                  _disposed           = false;
        private ESortieTerminalReason _terminalReason     = ESortieTerminalReason.None;
        private SortieDescriptor      _descriptor;
        private int                   _admittedCount      = 0;
        private WaveConfigSO          _waves              = null;
        private RewardCatalogSO       _rewards            = null;
        private TalismanCatalogSO     _talismans          = null;
        private string                _defaultBossId      = null;
        private Func<string, bool>    _isKnownBoss        = null;
        private bool                  _auctionReadmitOpen = false;
        private double                _nextCheckpointTime = 0.0;
        private long                  _checkpointSeq      = 0;
        private int                   _checkpointRequestId = 0;

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

        // 인증됐지만 플레이어 개체가 아직 없는 참가자 수
        public int  AwaitingPlayerCount => _awaitingCount;

        /// <summary> 켜면 인증된 연결이 플레이어 개체를 만들 때까지 시작하지 않고 시간 초과로 끊는다, 서버 부팅이 켠다 </summary>
        public bool TracksPlayerReadiness { get; set; } = false;

        /// <summary> None이 아니면 이 출정은 끝났고 프로세스를 재활용한다 </summary>
        public ESortieTerminalReason TerminalReason => _terminalReason;

        // 보스 성공 정산 뒤에만 있다. 끝나야 출정 프로세스가 재활용된다
        public PartyAuctionDirector  Auction        => _auction;

        // 서버 부트스트랩이 Mirror 연결 구현을 넣는다. 없으면 접속자가 없는 것으로 보고 경매 없이 출정을 닫는다
        public IAuctionAudience      AuctionAudience { get; set; }
        public bool                  IsTerminal     => _terminalReason != ESortieTerminalReason.None;

        // 보존 기록 만료로 명단을 닫는 것은 시작 뒤만이다. 시작 전 끊김의 유예 상한은 예약 만료다
        public bool ClosesParticipantOnRetentionExpiry => HasStarted;

        // 초, 안전 복귀 지점이 없을 때 부활을 다시 평가하는 간격
        public float SafePointRetryInterval => _config.SafePointRetryInterval;

        // m, 안전 복귀 지점과 살아 있는 적 사이 최소 수평 거리
        public float SafePointEnemyClearance => _config.SafePointEnemyClearance;

        // 첫 입장 토큰에서 고정한 출정 계약, 첫 입장 전과 개발 토큰은 계약 없음이다
        public SortieDescriptor Sortie => _descriptor;

        // 이 출정에서 열 보스, 보스전 계약이면 계약 값이고 아니면 ConfigureContent의 기본 보스다(없으면 null)
        public string           BossId         => SortieContentRules.ResolveBossId(in _descriptor, _defaultBossId);
        public EBossDifficulty  BossDifficulty => _descriptor.BossDifficulty;

        // 일반전 웨이브 진행기 주입 창구(적 담당 구현). 서버 부트스트랩이 출정 시작 전에 넣는다
        public IWaveSessionFactory WaveFactory      { get; set; }

        // 출정 상태와 포기 결과를 보낼 곳, 없으면 보내지 않는다
        public ISortieAudience     SortieAudience   { get; set; }

        // 체크포인트에 실을 전투 값(전투 런타임), 없으면 명단과 진행 값만 싣는다
        public ICheckpointSource   CheckpointSource { get; set; }

        // 출정 종료 직전에 미적용 물약 되돌림을 넣는 곳(전투 런타임), 출정 시작 전에 넣는다. 없으면 부르지 않는다
        public ISessionEndingListener SessionEndingListener { get; set; }

        /// <summary> 보스전 기록기의 시작, 종료와 경매 재입장 통지를 받을 곳, 출정 시작 전에 넣는다 </summary>
        public ICombatMeterSink CombatMeterSink { get; set; }

        // 보낸 진단 체크포인트 수와 응답 대기 여부
        public long CheckpointSequence   => _checkpointSeq;
        public bool IsCheckpointInFlight => _checkpointRequestId != 0;

        /// <summary> 접속 임대 이관이 확정 거절된 참가자, 인증된 연결을 끊어야 한다. 메인 스레드에서 통지한다 </summary>
        public event Action<int> ParticipantEvicted;

        /// <summary> 인증 뒤 제한 시간 안에 플레이어 개체가 생기지 않은 참가자, 연결을 끊어야 한다. 메인 스레드에서 통지한다 </summary>
        public event Action<int> ParticipantLoadTimedOut;

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
            _loading           = new LoadingSlot[Constants.Session.MAX_PARTY_SIZE];
            _absentIds         = new int[Constants.Session.MAX_PARTY_SIZE];
            _admittedIds       = new int[Constants.Session.MAX_PARTY_SIZE];
            _admittedClasses   = new EPlayerClass[Constants.Session.MAX_PARTY_SIZE];
            _abandons          = new AbandonSlot[Constants.Session.MAX_PARTY_SIZE];
            _statuses          = new StatusSlot[Constants.Session.MAX_PARTY_SIZE];
            _checkpoint        = new SessionCheckpointState(Constants.Session.MAX_PARTY_SIZE);
            _checkpointStream  = new MemoryStream(CHECKPOINT_INITIAL_BYTES);
            _checkpointWriter  = new BinaryWriter(_checkpointStream, Encoding.UTF8, true);
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

            ERejectReason reason = ResolveSession(in claims);
            if (reason != ERejectReason.None)
            {
                return reason;
            }

            int participantId = binding.ParticipantId;
            if (binding.IsAuctionOnly)
            {
                return AdmitAuctionOnly(participantId);
            }

            if (_roster.TryGet(participantId, out ParticipantSnapshot existing))
            {
                if (existing.ConnectionState == EParticipantConnectionState.Departed)
                {
                    return ERejectReason.AlreadySettled;
                }

                BeginAwaitingPlayer(participantId);
                return ERejectReason.None;
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

            _admittedIds[_admittedCount]     = participantId;
            _admittedClasses[_admittedCount] = binding.PlayerClass;
            ++_admittedCount;
            _startDirty = true;
            BeginAwaitingPlayer(participantId);
            return ERejectReason.None;
        }

        // 출정이 끝나 닫힌 원래 참가자가 경매 중에 돌아왔다. 출정 종료로 닫힌(Success) 사람만 받고 개인 이탈자는 거절한다
        private ERejectReason AdmitAuctionOnly(int participantId)
        {
            if ((_auction == null) || (_auction.IsFinished) || (!_registry.AllowsAuctionReadmission(participantId))
                || (!_roster.TryGet(participantId, out ParticipantSnapshot participant))
                || (participant.ConnectionState != EParticipantConnectionState.Departed)
                || (participant.CloseReason != ESessionEndReason.Success))
            {
                return ERejectReason.AlreadySettled;
            }

            return ERejectReason.None;
        }

        /// <summary> 경매 전용으로 다시 들어온 연결이 준비되면 부른다, 경매 상태와 출정 상태, 확정된 미터 결과를 그 참가자에게 보낸다 </summary>
        public void OnAuctionOnlyJoined(int participantId)
        {
            _auction?.SendSnapshot(participantId);
            SendStatusTo(participantId);
            CombatMeterSink?.ResendFinal(participantId);
        }

        /// <summary> 출정 계약에 쓸 GameDB 원본을 넣는다, 첫 입장 전에 한 번 부른다. defaultBossId는 보스전 계약이 없을 때(개발) 연다 </summary>
        public void ConfigureContent(WaveConfigSO waves, RewardCatalogSO rewards, TalismanCatalogSO talismans, string defaultBossId,
            Func<string, bool> isKnownBoss)
        {
            if (_sessionId != null)
            {
                throw new InvalidOperationException("첫 입장 뒤에는 출정 원본을 바꾸지 않는다");
            }

            _waves         = waves;
            _rewards       = rewards;
            _talismans     = talismans;
            _defaultBossId = defaultBossId;
            _isKnownBoss   = isKnownBoss;
        }

        /// <summary> 인증된 연결의 포기 요청을 받아 둔다, 다음 틱 BeginTick 뒤 처리하고 결과는 그 참가자에게 보낸다 </summary>
        public void RequestAbandon(int participantId, Guid operationId)
        {
            int index = IndexOfRoster(participantId);
            if ((operationId == Guid.Empty) || (index < 0) || (_disposed))
            {
                SendAbandonResult(participantId, operationId, ERejectReason.SessionMismatch);
                return;
            }

            ref AbandonSlot slot = ref _abandons[index];
            if (slot.OperationId == operationId)
            {
                // 같은 요청의 재전송이다. 처리했으면 처음 결과를 다시 보내고, 처리 전이면 처리 때 한 번 답한다
                if (slot.HasResult)
                {
                    SendAbandonResult(participantId, operationId, slot.Result);
                }

                return;
            }

            if (slot.Pending)
            {
                SendAbandonResult(participantId, operationId, ERejectReason.PendingCommit);
                return;
            }

            slot.OperationId = operationId;
            slot.Pending     = true;
            slot.HasResult   = false;
            slot.Result      = ERejectReason.None;
        }

        /// <summary> 이 참가자에게 지금 출정 상태를 보낸다, 접속, 재접속 때 부른다 </summary>
        public void SendStatusTo(int participantId)
        {
            int index = IndexOfRoster(participantId);
            if (index < 0)
            {
                return;
            }

            SortieStatusMessage status = BuildStatus(index);
            _statuses[index].Sent = true;
            _statuses[index].Last = status;
            SortieAudience?.SendStatus(participantId, in status);
        }

        /// <summary> 일반전 웨이브를 진행한다, 전투 런타임이 플레이어와 보스 피해 뒤 생사 보고 앞에서 부른다 </summary>
        public void TickWave()
        {
            if (_inTick)
            {
                _session.TickWave();
            }
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
            TickLoadingTimeouts(serverTime);
            if (_session == null)
            {
                TickLobby(serverTime);
                if (_session == null)
                {
                    AnswerPendingAbandons(ERejectReason.WrongPhase);
                    return false;
                }
            }

            if (!_session.BeginTick(serverTime))
            {
                return false;
            }

            _inTick = true;
            ProcessAbandons(serverTime);
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
            TickAuction();
            UpdateTerminal();
            TickCheckpoint();
            PublishStatuses();
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
            EndAwaitingPlayer(participantId, true);
            if (_session == null)
            {
                return ClearLobbyAbsent(participantId);
            }

            return (IsSessionRunning) && (_session.TryReconnect(participantId, serverTime));
        }

        /// <summary> 인증된 연결이 끊겼을 때 부른다, 플레이어 개체를 만들기 전이면 개체가 없어 전투 쪽 통지가 없으므로 여기서 이탈로 처리한다 </summary>
        public bool OnConnectionLost(int participantId)
        {
            if (!EndAwaitingPlayer(participantId, false))
            {
                return false;
            }

            return OnParticipantDetached(participantId, _serverTime);
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

        /// <summary> 보스 기믹이 최종 성공을 내준 틱에 부른다, 같은 틱 전원 사망보다 앞선다. BeginTick과 CompleteTick 사이에서만 받는다 </summary>
        public bool ReportBossFinalSuccess()
        {
            return (_inTick) && (_session.ReportBossFinalSuccess());
        }

        /// <summary> 보스 기믹이 실패한 틱에 부른다, 사유는 Timeout과 AllDead만 받는다. BeginTick과 CompleteTick 사이에서만 받는다 </summary>
        public bool ReportBossFightFailed(ESessionEndReason reason)
        {
            return (_inTick) && (_session.ReportBossFightFailed(reason));
        }

        // 보스를 열 수 없는 하드 출정을 보스 없이 진행하지 않고 닫는다. 서버 틱 안에서 그 틱의 서버 시각으로 부르면 받는다(세션이
        // 같은 틱 시각만 허용). 서비스 장애 취소는 정산을 시작하지 않으므로 환급은 백엔드 복구 작업이 한다
        /// <summary> 진행 중인 세션을 서비스 취소(ServiceCancelled)로 끝낸다, 진행 중이 아니거나 시각이 틀리면 거짓 </summary>
        public bool CancelSortieService(double serverTime)
        {
            return (IsSessionRunning) && (_session.CancelService(serverTime));
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

            // 진단용이라 재시도하지 않는다. 다음 주기가 새 상태로 대신한다
            if (response.RequestId == _checkpointRequestId)
            {
                _checkpointRequestId = 0;
                if (!response.IsSucceeded)
                {
                    _warn?.Invoke($"[Session] 체크포인트 저장 실패, 다음 주기에 새로 보낸다, session={_sessionId}, " +
                        $"seq={_checkpointSeq}, outcome={response.Outcome}, status={response.StatusCode}, reason={response.ReasonCode}");
                }

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
            _checkpointWriter.Dispose();
            _checkpointStream.Dispose();
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

        private ERejectReason ResolveSession(in JoinTokenClaims claims)
        {
            string sortieId  = claims.SortieId;
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

            // 같은 출정이라도 계약이 다르면 다른 예약에서 나온 토큰이다
            if (_sessionId != null)
            {
                return (string.Equals(_sessionId, sessionId, StringComparison.Ordinal)) && (_descriptor.Matches(claims.Sortie))
                    ? ERejectReason.None
                    : ERejectReason.SessionMismatch;
            }

            bool usesBackend = (hasSortie) && (_gateway != null);
            if ((!usesBackend) && (!_allowLocalSession))
            {
                return ERejectReason.BackendUnavailable;
            }

            // 이 서버 콘텐츠에 없는 보스로는 전투를 열 수 없다. 출정을 고정하지 않고 거절해 예약 만료가 티켓을 돌려주게 둔다
            SortieDescriptor sortie = claims.Sortie;
            if ((sortie.IsBoss) && (_isKnownBoss != null) && (!_isKnownBoss(sortie.BossId)))
            {
                _warn?.Invoke($"[Session] 출정 계약의 보스가 서버 콘텐츠에 없어 입장을 거절한다, sortie={sessionId}, boss={sortie.BossId}");
                return ERejectReason.SessionMismatch;
            }

            _sessionId   = sessionId;
            _usesBackend = usesBackend;
            _descriptor  = sortie;
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

                if ((serverTime >= _localStartTime) && (_awaitingCount == 0))
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

            // 인증과 임대 이관이 끝나도 플레이어 개체가 없는 참가자가 있으면 기다린다. 준비되면 _startDirty로 다시 시도한다
            if ((_startRequestId != 0) || (_absentCount > 0) || (_awaitingCount > 0))
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

            DungeonSession session = CreateSession();
            if (session == null)
            {
                _events        = null;
                _startRejected = true;
                ReportInvariant("일반전 원본을 등록하지 못해 세션을 열지 못했다");
                return;
            }

            session.SetEndingListener(SessionEndingListener);
            session.SetCombatMeterSink(CombatMeterSink);
            if (!session.Begin(serverTime))
            {
                session.Dispose();
                _events        = null;
                _startRejected = true;
                ReportInvariant($"시작 조건이 맞지 않아 세션을 열지 못했다, count={_roster.Count}, active={_roster.ActiveCount}");
                return;
            }

            _session            = session;
            _settlement         = new SessionSettlement(_sessionId, _usesBackend ? _gateway : null, _warn);
            _nextCheckpointTime = serverTime + Constants.Network.CHECKPOINT_INTERVAL_SECONDS;

            // start를 보낸 뒤 끊긴 참가자는 시작 시각부터 재접속 유예를 센다
            for (int i = 0; i < _absentCount; ++i)
            {
                _session.TryDisconnect(_absentIds[i], serverTime);
            }

            _absentCount = 0;
        }

        // 일반전 계약이고 웨이브 진행기 주입과 원본이 있으면 일반전, 아니면 보스전(또는 개발 세션)으로 연다
        private DungeonSession CreateSession()
        {
            if (_descriptor.IsNormal)
            {
                if ((WaveFactory != null) && (_waves != null) && (_rewards != null) && (_talismans != null))
                {
                    return CreateNormalSession();
                }

                // 일반전 계약에는 보스 보상(엽전)을 넣지 않는다. API가 일반전 정산의 엽전을 거절하기 때문이다
                _warn?.Invoke($"[Session] 일반전 웨이브 구현이 없어 웨이브 없는 개발 세션으로 연다, session={_sessionId}");
                return new DungeonSession(_sessionId, _roster, _events, _config, null, null, _lootSalt);
            }

            var boss = new DungeonSession(_sessionId, _roster, _events, _config, null, null, _lootSalt);
            ConfigureBossRewards(boss);
            return boss;
        }

        // 배치 계획 전체의 source ID와 참가자별 직업 드롭을 Begin 전에 등록한다. 하나라도 빠지면 처치나 개인 드롭이 버려지므로 열지 않는다
        private DungeonSession CreateNormalSession()
        {
            int count   = _roster.Count;
            var context = new WaveSessionContext(_sessionId, _descriptor.MapId, _descriptor.BattleId, count,
                _config.GetEnemyHealthScale(count), _config.GetDamageScale(count), _waves);
            IWaveSession wave = WaveFactory.Create(in context);
            if (wave == null)
            {
                return null;
            }

            DungeonSession session = DungeonSession.CreateNormal(_sessionId, _roster, _events, _config, _waves, _rewards, wave,
                _lootSalt);
            if (GameDB.IsInitialized && !session.ConfigureExperience(GameDB.CharacterLevel.ToSettings(), _descriptor.MapId))
            {
                session.Dispose();
                return null;
            }
            if (SortieContentRules.RegisterWaveSources(session, _waves, _rewards,
                GameDB.IsInitialized ? GameDB.Enemies : null) != _waves.CountTotalEnemies())
            {
                session.Dispose();
                return null;
            }

            // 시작 때 한 번이라 효과 목록을 새로 만든다
            var effects = new List<string>();
            for (int i = 0; i < _admittedCount; ++i)
            {
                if (!_roster.TryGet(_admittedIds[i], out _))
                {
                    continue;
                }

                SortieContentRules.CollectClassEffects(_talismans, _admittedClasses[i], effects);
                if (!session.RegisterParticipantLoot(_admittedIds[i], effects))
                {
                    session.Dispose();
                    return null;
                }
            }

            return session;
        }

        // 보스와 난이도에 맞는 보상 표가 있으면 처치 보상과 경매 재료를 그것으로 정한다. 없으면 ConfigureBossAuction 값만 쓴다
        private void ConfigureBossRewards(DungeonSession session)
        {
            string bossId = BossId;
            if ((_rewards == null) || (bossId == null))
            {
                return;
            }

            if (!SortieContentRules.TryFindBossReward(_rewards.BossRewards, bossId, BossDifficulty,
                out RewardCatalogSO.BossRewardSpec spec))
            {
                _warn?.Invoke($"[Session] 보스 보상 표가 없어 기본 보상과 경매 없이 연다, boss={bossId}, difficulty={BossDifficulty}");
                return;
            }

            session.RegisterBossReward(spec);
            ConfigureBossAuction(spec.AuctionItemId, spec.AuctionChance);
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

        /// <summary> 보스 처치 성공 때 쓸 파티 경매 희귀 재료와 출현 확률, 출정 시작 전에 한 번 넣는다 </summary>
        public void ConfigureBossAuction(string itemDefinitionId, float chance)
        {
            if ((!string.IsNullOrEmpty(itemDefinitionId)) && ((!(chance >= 0f)) || (!(chance <= 1f))))
            {
                throw new ArgumentOutOfRangeException(nameof(chance), "출현 확률은 0에서 1 사이다");
            }

            _auctionItemId = itemDefinitionId;
            _auctionChance = chance;
        }

        /// <summary> 인증된 연결의 경매 입찰을 넘긴다, 경매가 없으면 마감으로 돌려준다 </summary>
        public void HandleAuctionBid(int participantId, long amount)
        {
            if (_auction == null)
            {
                AuctionAudience?.SendBidResult(participantId, new AuctionBidResultMessage(amount, ERejectReason.Closed));
                return;
            }

            _auction.HandleBid(participantId, amount);
        }

        // API는 보스 성공 정산 뒤 출정을 Auction으로 두므로 품목이 없어도 경매 진행기가 finish로 닫아야 한다
        private void TickAuction()
        {
            if ((_auction == null) && (_settlement.State == ESessionSettlementState.Settled) && (_session.IsBossSession)
                && (_session.EndReason == ESessionEndReason.Success))
            {
                StartAuction();
            }

            _auction?.Tick(_serverTime);

            // 경매가 끝나면 닫힌 참가자의 재입장을 다시 막는다
            if ((_auctionReadmitOpen) && (_auction != null) && (_auction.IsFinished))
            {
                _registry.RevokeAuctionReadmission();
                _auctionReadmitOpen = false;
            }
        }

        private void StartAuction()
        {
            _auction = new PartyAuctionDirector(_sessionId, _config, _usesBackend ? _gateway : null,
                AuctionAudience ?? new DisconnectedAuctionAudience(), UnixMilliseconds, _warn);
            for (int i = 0; i < _roster.Count; ++i)
            {
                // 출정 진행 중에 떠난 사람(포기, 유예 만료)은 경매 대상이 아니다. 출정 종료로 닫힌 사람만 넣고 경매 동안 재입장을 허용한다
                ParticipantSnapshot participant = _roster.GetAt(i);
                if (participant.CloseReason != ESessionEndReason.Success)
                {
                    continue;
                }

                string apiId = participant.ParticipantKey;
                if ((_usesBackend) && (!SessionContractIds.TryGetApiParticipantId(participant.ParticipantKey, out apiId)))
                {
                    continue;
                }

                if (_auction.AddParticipant(participant.ParticipantId, apiId))
                {
                    _registry.AllowAuctionReadmission(participant.ParticipantId);
                    _auctionReadmitOpen = true;
                }
            }

            // 희귀 재료는 파티가 한 번의 출현 판정을 공유한다. 개발 출정은 start 영수증이 없어 salt를 여기서 만든다
            bool appears = (!string.IsNullOrEmpty(_auctionItemId))
                && LootRoller.RollBossAuction(_lootSalt ?? LootRoller.CreateSalt(), _sessionId, _auctionChance);
            _auction.Begin(_serverTime, appears ? _auctionItemId : null);
        }

        private static long UnixMilliseconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
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
                // 보스 성공은 파티 경매와 출정 닫기(finish)까지 끝나야 내린다
                if ((_auction != null) && (!_auction.IsFinished))
                {
                    return;
                }

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

        private int IndexOfLoading(int participantId)
        {
            for (int i = 0; i < _loadingCount; ++i)
            {
                if (_loading[i].ParticipantId == participantId)
                {
                    return i;
                }
            }

            return -1;
        }

        // 인증된 연결이 생겼다. 마감 시각은 다음 틱에서 서버 시각으로 정한다
        private void BeginAwaitingPlayer(int participantId)
        {
            if (!TracksPlayerReadiness)
            {
                return;
            }

            int index = IndexOfLoading(participantId);
            if (index < 0)
            {
                if (_loadingCount >= _loading.Length)
                {
                    return;
                }

                index = _loadingCount;
                ++_loadingCount;
                _loading[index].ParticipantId = participantId;
            }

            ref LoadingSlot slot = ref _loading[index];
            if (!slot.Awaiting)
            {
                slot.Awaiting = true;
                ++_awaitingCount;
            }

            slot.Deadline = double.NaN;
        }

        // 로딩 중이던 참가자를 푼다, 로딩 중이 아니었으면 거짓이다. 준비돼서 풀면 시작 조건을 다시 판정하게 한다
        private bool EndAwaitingPlayer(int participantId, bool ready)
        {
            int index = IndexOfLoading(participantId);
            if ((index < 0) || (!_loading[index].Awaiting))
            {
                return false;
            }

            _loading[index].Awaiting = false;
            --_awaitingCount;
            if (ready)
            {
                _startDirty = true;
            }

            return true;
        }

        // 마감을 넘긴 로딩 참가자의 연결을 끊게 알린다, 끊김이 오면 OnConnectionLost가 정리한다. 마감은 한 번만 알린다
        private void TickLoadingTimeouts(double serverTime)
        {
            if (_awaitingCount == 0)
            {
                return;
            }

            for (int i = 0; i < _loadingCount; ++i)
            {
                ref LoadingSlot slot = ref _loading[i];
                if (!slot.Awaiting)
                {
                    continue;
                }

                if (double.IsNaN(slot.Deadline))
                {
                    slot.Deadline = serverTime + Constants.Network.LOADING_TIMEOUT_SECONDS;
                    continue;
                }

                if (serverTime < slot.Deadline)
                {
                    continue;
                }

                slot.Deadline = double.PositiveInfinity;
                _warn?.Invoke($"[Session] 인증 뒤 플레이어 개체가 제한 시간 안에 생기지 않아 연결을 끊는다, session={_sessionId}, " +
                    $"participantId={slot.ParticipantId}");
                ParticipantLoadTimedOut?.Invoke(slot.ParticipantId);
            }
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

        // BeginTick 뒤, 피해 처리 전에 받아 둔 포기를 처리한다. 마지막 참가자가 포기하면 같은 틱 CompleteTick이 출정을 끝낸다
        private void ProcessAbandons(double serverTime)
        {
            for (int i = 0; i < _roster.Count; ++i)
            {
                ref AbandonSlot slot = ref _abandons[i];
                if (!slot.Pending)
                {
                    continue;
                }

                int participantId = _roster.GetAt(i).ParticipantId;
                ERejectReason reason;
                if (!_session.IsRunning)
                {
                    reason = ERejectReason.SessionTerminal;
                }
                else
                {
                    reason = _session.TryAbandon(participantId, serverTime) ? ERejectReason.None : ERejectReason.AlreadySettled;
                }

                CompleteAbandon(ref slot, participantId, reason);
            }
        }

        // 시작 전 포기는 받지 않는다. 시작 전 이탈은 start를 막고 예약 만료로 티켓을 돌려주는 경로라 사용자 포기로 열지 않는다
        private void AnswerPendingAbandons(ERejectReason reason)
        {
            for (int i = 0; i < _roster.Count; ++i)
            {
                ref AbandonSlot slot = ref _abandons[i];
                if (slot.Pending)
                {
                    CompleteAbandon(ref slot, _roster.GetAt(i).ParticipantId, reason);
                }
            }
        }

        private void CompleteAbandon(ref AbandonSlot slot, int participantId, ERejectReason reason)
        {
            slot.Pending   = false;
            slot.HasResult = true;
            slot.Result    = reason;
            SendAbandonResult(participantId, slot.OperationId, reason);
        }

        private void SendAbandonResult(int participantId, Guid operationId, ERejectReason reason)
        {
            var message = new SortieAbandonResultMessage(operationId, reason);
            SortieAudience?.SendAbandonResult(participantId, in message);
        }

        // 틱 끝에서 바뀐 참가자에게만 보낸다. 참가자는 4명 이하라 매 틱 비교해도 할당이 없다
        private void PublishStatuses()
        {
            if (SortieAudience == null)
            {
                return;
            }

            for (int i = 0; i < _roster.Count; ++i)
            {
                SortieStatusMessage status = BuildStatus(i);
                ref StatusSlot slot = ref _statuses[i];
                if ((slot.Sent) && (slot.Last.SameAs(in status)))
                {
                    continue;
                }

                slot.Sent = true;
                slot.Last = status;
                SortieAudience.SendStatus(_roster.GetAt(i).ParticipantId, in status);
            }
        }

        private SortieStatusMessage BuildStatus(int rosterIndex)
        {
            ParticipantSnapshot participant    = _roster.GetAt(rosterIndex);
            bool                ended          = (_session != null) && (_session.IsEnded);
            ESessionEndReason   endReason      = ended ? _session.EndReason : ESessionEndReason.Success;
            bool                personal       = (_session != null) && (_session.HasDeparture(participant.ParticipantId));
            ESessionEndReason   personalReason = personal ? participant.CloseReason : ESessionEndReason.Success;

            // 개인 이탈자는 이탈 기록 저장(API가 같은 트랜잭션에서 계정 임대를 푼다) 뒤 마을로 갈 수 있다
            bool canReturn = personal ? _session.IsDepartureStored(participant.ParticipantId) : CanReturnAfterEnd();
            return new SortieStatusMessage(CurrentPhase(), endReason, personal, personalReason, canReturn, CurrentSettlementState());
        }

        // API는 정산 확정 때 참가자 계정 임대를 푼다. 보스 성공은 경매 동안 던전이 임대를 쥐고 경매 닫기(finish)에서 푼다.
        // 장애 취소와 정산 확정 거절은 API 복구 작업이 풀어 기다릴 신호가 없으므로 종료 저장 뒤 허용하고 입장권 재시도에 맡긴다
        private bool CanReturnAfterEnd()
        {
            if ((_session == null) || (_settlement == null) || (!_session.CanReturn))
            {
                return false;
            }

            if ((_session.EndReason == ESessionEndReason.ServiceCancelled) || (_settlement.State == ESessionSettlementState.Abandoned))
            {
                return true;
            }

            if (_settlement.State != ESessionSettlementState.Settled)
            {
                return false;
            }

            if (_auction != null)
            {
                return _auction.IsFinished;
            }

            // 정산 응답은 틱 사이(Pump)에 오므로 경매가 다음 틱에 시작되기 전일 수 있다
            return (!_session.IsBossSession) || (_session.EndReason != ESessionEndReason.Success);
        }

        private ESortiePhase CurrentPhase()
        {
            if (IsTerminal)
            {
                return ESortiePhase.Closed;
            }

            if (_session == null)
            {
                return ESortiePhase.Waiting;
            }

            if (_session.IsRunning)
            {
                return ESortiePhase.Running;
            }

            return ((_auction != null) && (!_auction.IsFinished)) ? ESortiePhase.Auction : ESortiePhase.Ended;
        }

        private ESortieSettlementState CurrentSettlementState()
        {
            if ((_settlement == null) || (_session == null) || (!_session.IsEnded))
            {
                return ESortieSettlementState.None;
            }

            switch (_settlement.State)
            {
                case ESessionSettlementState.Settled:
                    return ESortieSettlementState.Settled;
                case ESessionSettlementState.Abandoned:
                    return ESortieSettlementState.Deferred;
                default:
                    return ESortieSettlementState.Pending;
            }
        }

        // 진행 중에만 30초마다 보낸다. 보내는 중이면 이번 주기는 건너뛰고 다음 주기가 새 상태로 대신한다(재시도 없음)
        private void TickCheckpoint()
        {
            if ((!_usesBackend) || (_session == null) || (!_session.IsRunning) || (_serverTime < _nextCheckpointTime))
            {
                return;
            }

            _nextCheckpointTime = _serverTime + Constants.Network.CHECKPOINT_INTERVAL_SECONDS;
            if (_checkpointRequestId != 0)
            {
                return;
            }

            SendCheckpoint();
        }

        // 30초에 한 번이라 base64 문자열과 요청 DTO 할당을 허용한다. 바이트 버퍼와 쓰기 개체는 재사용한다
        private void SendCheckpoint()
        {
            FillCheckpoint();
            _checkpointStream.SetLength(0);
            SessionCheckpointCodec.Write(_checkpointWriter, _checkpoint);
            _checkpointWriter.Flush();
            int length = (int)_checkpointStream.Length;
            if (length > SessionCheckpointCodec.MAX_STATE_BYTES)
            {
                _warn?.Invoke($"[Session] 체크포인트가 저장 상한을 넘어 보내지 않는다, session={_sessionId}, bytes={length}");
                return;
            }

            ++_checkpointSeq;
            string state = Convert.ToBase64String(_checkpointStream.GetBuffer(), 0, length);
            string path  = string.Format(CultureInfo.InvariantCulture, GameServerRoutes.CHECKPOINTS_FORMAT, _sessionId);
            var    body  = new SessionCheckpointRequest(_checkpointSeq, _events.LastAcknowledgedSequence,
                SessionCheckpointCodec.SCHEMA_VERSION, state);
            _checkpointRequestId = _gateway.Send(BackendRequest.Post(path, body, null, null, Constants.Network.API_TIMEOUT_SECONDS),
                this);
        }

        private void FillCheckpoint()
        {
            _checkpoint.ServerTime       = _serverTime;
            _checkpoint.ElapsedSeconds   = _serverTime - _session.StartTime;
            _checkpoint.StartingCount    = _session.StartingCount;
            _checkpoint.KillSequence     = _session.KillSequence;
            _checkpoint.RemainingRevives = _session.RemainingRevives;

            _checkpoint.HasWave         = _session.TryGetWaveProgress(out WaveProgressSnapshot progress);
            _checkpoint.WaveIndex       = progress.WaveIndex;
            _checkpoint.WaveCount       = progress.WaveCount;
            _checkpoint.RemainingInWave = progress.RemainingInWave;
            _checkpoint.WaveElapsed     = progress.ElapsedSeconds;

            float bossHealthRate = 0f;
            int   bossPhase      = 0;
            _checkpoint.HasBoss        = (CheckpointSource != null) && (CheckpointSource.TryGetBossVitals(out bossHealthRate, out bossPhase));
            _checkpoint.BossHealthRate = bossHealthRate;
            _checkpoint.BossPhase      = bossPhase;

            _checkpoint.ClearParticipants();
            for (int i = 0; i < _roster.Count; ++i)
            {
                ParticipantSnapshot participant = _roster.GetAt(i);
                CheckpointVitals    vitals      = default;
                bool hasVitals = (CheckpointSource != null)
                    && (CheckpointSource.TryGetParticipantVitals(participant.ParticipantId, out vitals));
                _checkpoint.TryAdd(new CheckpointParticipant(participant.ParticipantKey, participant.ConnectionState,
                    participant.LifeState, participant.CloseReason, participant.GraceUntil, hasVitals, in vitals));
            }
        }

        private int IndexOfRoster(int participantId)
        {
            for (int i = 0; i < _roster.Count; ++i)
            {
                if (_roster.GetAt(i).ParticipantId == participantId)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
