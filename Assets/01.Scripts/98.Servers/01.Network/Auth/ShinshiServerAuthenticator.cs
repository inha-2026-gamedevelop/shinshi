// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 서버 인증기, 프로토콜과 콘텐츠 해시와 접속 토큰을 확인한 연결만 참가자와 결합해 받는다 </summary>
    public sealed class ShinshiServerAuthenticator : NetworkAuthenticator, IJoinTokenVerifyListener
    {
        // 서버 부트스트랩이 실행 중에 매니저 개체에 붙이고 Configure한 뒤 매니저의 인증기로 지정한다. 프리팹, 씬에 붙이지 않는다.
        // Mirror는 인증 전 연결의 일반 메시지(AddPlayer, Command)를 받지 않고 끊으므로 인증 전 스폰과 입력은 없다.
        // 인증 대기 연결은 상한과 마감 시각을 둬 연결만 열고 요청을 보내지 않는 공격으로 자리를 채우지 못하게 한다.
        // 요청 대기는 1초, 주소별 대기는 마을 4개, 던전 2개로 묶는다(IPv6는 /64 접두사 하나를 한 주소로 센다).
        // 던전은 받을 사람이 파티 인원뿐이라 대기 상한을 그 두 배로 줄인다.
        // 마을은 프로세스가 오래 살므로 끊긴 참가자의 키를 명단에서 지워(Forget) 누적되지 않게 하고, 계정 접속 임대를 푼다

        /****************************************
        *                Fields
        ****************************************/

        private readonly List<PendingAuthEntry> _due = new List<PendingAuthEntry>(Constants.Network.MAX_PENDING_AUTH_CONNECTIONS);

        // Configure 전에 연결이 와도 비어 있지 않게 마을 상한으로 만들어 둔다. Configure가 역할에 맞게 다시 만든다
        private PendingAuthTable _pending = new PendingAuthTable(Constants.Network.MAX_PENDING_AUTH_CONNECTIONS,
            Constants.Network.MAX_PENDING_AUTH_PER_ADDRESS);

        private IJoinTokenVerifier    _verifier;
        private ServerSessionRegistry _sessions;
        private IParticipantAdmission _admission;
        private AccountLeaseReleaser  _accountLeases;
        private string                _contentHash;
        private bool                  _forgetOnDisconnect = false;
        private int                   _lastRequestId      = 0;

        // 입장은 허가됐지만 서버 맵 전환이 끝나기를 기다리며 ServerAccept를 미룬 연결. 토큰 소비는 허가 직후 이미 확정했다
        private readonly List<HeldAdmission> _held      = new List<HeldAdmission>(Constants.Session.MAX_PARTY_SIZE);
        private readonly List<HeldAdmission> _heldBatch = new List<HeldAdmission>(Constants.Session.MAX_PARTY_SIZE);

        // 서버 맵 로드 중에는 Mirror가 받은 앱 메시지를 처리하지 않아(NetworkServer.isLoadingScene) 요청 대기, 검증 대기 마감을
        // 로드 한 번에 한해 상한(DUNGEON_MAP_LOAD_TIMEOUT_SECONDS)까지 미룬다. 매 프레임 밀지 않고 로드를 처음 본 시각에 한 번만 정한다
        private bool   _loadGraceActive = false;
        private double _loadGraceUntil  = 0.0;

        private struct HeldAdmission
        {
            public int            ConnectionId;
            public SessionBinding Binding;
        }

        /****************************************
        *              Properties
        ****************************************/

        public ServerSessionRegistry Sessions => _sessions;

        // 던전 서버가 계약에 맞춰 씬을 바꾸는 중이면 입장 확정을 미룬다. 단일 씬 개발 구성은 비워 둔다
        public IDungeonMapLoader MapLoader { get; set; }

        // 마감 판정에 쓰는 시계(초), 비우면 Time.unscaledTimeAsDouble. 시험이 시간을 주입한다
        public Func<double> Clock { get; set; }

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            double now = Now();
            SyncLoadGrace(now);
            _accountLeases?.Tick();
            TickHeld();
            if (_pending.Count == 0)
            {
                return;
            }

            _due.Clear();
            if (_pending.CollectDue(now, _due) == 0)
            {
                return;
            }

            for (int i = 0; i < _due.Count; ++i)
            {
                PendingAuthEntry entry = _due[i];
                if (!NetworkServer.connections.TryGetValue(entry.ConnectionId, out NetworkConnectionToClient connection))
                {
                    continue;
                }

                // 거절 응답을 보낸 뒤이거나 요청 대기 1초 안에 아무것도 보내지 않았다. 응답 없이 끊는다
                if ((entry.Stage == EPendingAuthStage.Closing) || (entry.Stage == EPendingAuthStage.Waiting))
                {
                    ServerReject(connection);
                    continue;
                }

                // 맵 전환을 기다리던 연결이 마감을 넘겼다. 토큰 소비는 허가 때 이미 확정했고, 결합은 연결 종료 처리가 푼다
                DropHeld(entry.ConnectionId);

                // 토큰 검증이 마감 안에 끝나지 않았다
                Reject(connection, ERejectReason.TokenExpired);
            }

            _due.Clear();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 서버를 열기 전에 한 번 부른다, verifier가 null이면 모든 접속을 거절한다. admission은 없으면 명단 결합만 본다 </summary>
        public void Configure(IJoinTokenVerifier verifier, ServerSessionRegistry sessions, string contentHash,
            IParticipantAdmission admission, EServerRole role)
        {
            Configure(verifier, sessions, contentHash, admission, role, null);
        }

        /// <summary> accountLeases는 마을에서 백엔드가 있을 때만 넣는다, 없으면 끊긴 계정의 임대는 heartbeat 만료로 풀린다 </summary>
        public void Configure(IJoinTokenVerifier verifier, ServerSessionRegistry sessions, string contentHash,
            IParticipantAdmission admission, EServerRole role, AccountLeaseReleaser accountLeases)
        {
            _verifier           = verifier;
            _sessions           = sessions;
            _contentHash        = contentHash;
            _admission          = admission;
            _forgetOnDisconnect = role == EServerRole.Town;
            _accountLeases      = _forgetOnDisconnect ? accountLeases : null;
            _pending            = new PendingAuthTable(MaxPendingFor(role), MaxPendingPerAddressFor(role));
        }

        /// <summary> 역할별 인증 대기 상한, 던전은 파티 최대 인원의 배수다 </summary>
        public static int MaxPendingFor(EServerRole role)
        {
            return (role == EServerRole.Dungeon)
                ? Constants.Session.MAX_PARTY_SIZE * Constants.Network.DUNGEON_PENDING_AUTH_PER_PARTICIPANT
                : Constants.Network.MAX_PENDING_AUTH_CONNECTIONS;
        }

        /// <summary> 역할별 주소 묶음당 인증 대기 상한, 던전은 같은 NAT 뒤의 한 파티를 허용한다 </summary>
        public static int MaxPendingPerAddressFor(EServerRole role)
        {
            return (role == EServerRole.Dungeon)
                ? Constants.Network.DUNGEON_MAX_PENDING_AUTH_PER_ADDRESS
                : Constants.Network.MAX_PENDING_AUTH_PER_ADDRESS;
        }

        /// <summary> 인증된 참가자의 연결을 찾아 끊는다, 접속 임대 이관 거절처럼 드문 경로에서만 부른다 </summary>
        public void DisconnectParticipant(int participantId)
        {
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
            {
                if ((connection.authenticationData is SessionBinding binding) && (binding.ParticipantId == participantId))
                {
                    Debug.LogWarning($"[Auth] 출정에서 내보낸 참가자의 연결을 끊는다, participantId={participantId}");
                    connection.Disconnect();
                    return;
                }
            }
        }

        public override void OnStartServer()
        {
            if ((_sessions == null) || (string.IsNullOrEmpty(_contentHash)))
            {
                Debug.LogError("[Auth] 서버 인증기가 구성되지 않아 모든 접속을 거절한다");
            }
            else if (_verifier == null)
            {
                Debug.LogError("[Auth] 접속 토큰 검증기가 없어 모든 접속을 거절한다");
            }

            NetworkServer.RegisterHandler<JoinRequestMessage>(OnJoinRequest, false);
        }

        public override void OnStopServer()
        {
            NetworkServer.UnregisterHandler<JoinRequestMessage>();
            _pending.Clear();
            _held.Clear();
            _heldBatch.Clear();
            _loadGraceActive = false;
            _loadGraceUntil  = 0.0;
        }

        public override void OnServerAuthenticate(NetworkConnectionToClient conn)
        {
            double now = Now();

            // 로드 중에 들어온 연결도 로드가 끝나야 요청이 처리된다. 같은 로드 상한까지만 요청 대기를 미룬다. 표 상한은 그대로다
            bool loading = SyncLoadGrace(now);
            if (!_pending.TryAdd(conn.connectionId, conn.address, now + Constants.Network.AUTH_REQUEST_TIMEOUT_SECONDS))
            {
                // 주소는 개인 정보라 로그에 남기지 않는다
                Debug.LogWarning($"[Auth] 인증 대기 상한(전체 {_pending.Capacity}, 주소별 {_pending.MaxPerAddress})을 넘어 연결을 바로 끊는다, " +
                    $"connId={conn.connectionId}");
                ServerReject(conn);
                return;
            }

            if (loading)
            {
                _pending.TryGraceForLoad(conn.connectionId, _loadGraceUntil);
            }
        }

        /// <summary> 매니저의 서버 연결 종료 이벤트에서 부른다, 대기 항목과 참가자 접속 상태를 정리한다 </summary>
        public void HandleServerDisconnected(NetworkConnectionToClient conn)
        {
            if (conn == null)
            {
                return;
            }

            _pending.Remove(conn.connectionId);
            DropHeld(conn.connectionId);
            if ((_sessions == null) || (!(conn.authenticationData is SessionBinding binding)))
            {
                return;
            }

            ReleaseBinding(binding);
        }

        // 연결 종료와 사라진 연결 정리가 같은 방식으로 결합을 푼다
        private void ReleaseBinding(SessionBinding binding)
        {
            if ((_sessions == null) || (binding == null))
            {
                return;
            }

            if (_forgetOnDisconnect)
            {
                ForgetTownBinding(_sessions, binding, _accountLeases);
                return;
            }

            _sessions.Release(binding);
        }

        /// <summary> 마을 연결 종료 정리, 명단에서 키를 지우고 계정 접속 임대 해제를 보낸다 </summary>
        public static void ForgetTownBinding(ServerSessionRegistry sessions, SessionBinding binding,
            AccountLeaseReleaser accountLeases)
        {
            ForgetTownBinding(sessions, binding, accountLeases, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        /// <summary> nowUnixMs를 받는 판, 출정 입장권 기한 판정을 시계 없이 시험하려고 나눴다 </summary>
        public static void ForgetTownBinding(ServerSessionRegistry sessions, SessionBinding binding,
            AccountLeaseReleaser accountLeases, long nowUnixMs)
        {
            if ((sessions == null) || (binding == null))
            {
                return;
            }

            sessions.Forget(binding);

            // 던전으로 떠나는 참가자의 임대는 던전이 이관 경로로 옮긴다. 여기서 풀면 이관이 거절된다
            // 해제가 끝내 실패해도 계정 임대는 heartbeat 만료로 풀린다(백엔드 계약)
            // 입장권 만료와 여유가 지나도록 이관이 없으면 표시가 풀려 바로 푼다. 아직 기다리는 중이면 그 시각까지 해제를 미룬다.
            // 이관 없이 입장권만 받고 끊긴 경우 마을 heartbeat가 임대를 계속 연장하므로, 미루지 않으면 재로그인이 무기한 막힌다.
            // 미룬 해제는 원래 마을 인스턴스 소유일 때만 효력이 있어(백엔드 임대 ID와 인스턴스 일치 검사) 이관된 임대는 건드리지 않는다
            if (binding.AccountLeaseId == null)
            {
                return;
            }

            if (!binding.IsDepartingAt(nowUnixMs))
            {
                accountLeases?.Release(binding.AccountLeaseId);
                return;
            }

            accountLeases?.ReleaseAfter(binding.AccountLeaseId, binding.SecondsUntilDepartingExpires(nowUnixMs));
        }

        private void OnJoinRequest(NetworkConnectionToClient conn, JoinRequestMessage message)
        {
            // 대기 중이 아닌 연결(이미 인증, 검증 중, 거절 뒤)의 요청은 버린다
            if (!_pending.IsWaiting(conn.connectionId))
            {
                return;
            }

            if ((_sessions == null) || (_verifier == null))
            {
                Reject(conn, ERejectReason.Maintenance);
                return;
            }

            ERejectReason reason = JoinHandshakeRules.Check(message.ProtocolVersion, message.ContentHash, message.JoinToken,
                Constants.Network.PROTOCOL_VERSION, _contentHash);
            if (reason != ERejectReason.None)
            {
                Reject(conn, reason);
                return;
            }

            int requestId = NextRequestId();
            _pending.TryBeginVerify(conn.connectionId, requestId, Now() + Constants.Network.AUTH_TIMEOUT_SECONDS);
            _verifier.Verify(requestId, message.JoinToken, this);
        }

        // 검증기가 메인 스레드에서 부른다. 그 사이 연결이 끊겼거나 시간 초과로 닫혔을 수 있어 현재 상태를 다시 본다
        public void OnJoinTokenVerified(int requestId, ERejectReason reason, in JoinTokenClaims claims)
        {
            // 마감 시간 초과나 연결 끊김으로 이미 닫힌 요청이다. 성공 결과는 계정 임대를 잡았으므로 풀고 교환 기억도 지운다
            if (!_pending.TryFindVerifying(requestId, out int connectionId, out double deadline))
            {
                AbandonVerified(requestId, reason, in claims);
                return;
            }

            if (!NetworkServer.connections.TryGetValue(connectionId, out NetworkConnectionToClient conn))
            {
                _pending.Remove(connectionId);
                AbandonVerified(requestId, reason, in claims);
                return;
            }

            if (reason != ERejectReason.None)
            {
                Reject(conn, reason);
                return;
            }

            // 응답 Pump가 마감 처리(Update)보다 먼저 돌면 마감이 지난 요청이 아직 검증 중으로 남아 있다. 늦은 성공은 받지 않는다
            if (Now() >= deadline)
            {
                AbandonVerified(requestId, reason, in claims);
                Reject(conn, ERejectReason.TokenExpired);
                return;
            }

            reason = _sessions.TryAdmit(in claims, out SessionBinding binding);
            if (reason != ERejectReason.None)
            {
                // 이미 접속 중이면 검증기가 기억한 교환 결과로 곧 다시 들어오므로 임대를 남긴다. 그 밖의 거절은 이번 교환이 잡은
                // 계정 임대를 바로 푼다(남기면 만료 전까지 그 계정의 다른 로그인이 막힌다)
                if ((reason != ERejectReason.AlreadyConnected) && (claims.AccountLeaseId != null))
                {
                    _accountLeases?.Release(claims.AccountLeaseId);
                }

                Reject(conn, reason);
                return;
            }

            if (_admission != null)
            {
                reason = _admission.Admit(in claims, binding);
                if (reason != ERejectReason.None)
                {
                    // 접속 중 표시를 풀어야 같은 참가자가 조건을 맞춰 다시 들어올 수 있다
                    _sessions.Release(binding);
                    Reject(conn, reason);
                    return;
                }
            }

            // 허가가 끝났으면 미룰지와 무관하게 이 토큰은 쓴 것이다. 검증기가 기억한 교환 결과를 지워 다시 쓰지 못하게 한다.
            // 미룬 동안 같은 토큰의 중복 검증이 기억의 요청 번호를 덮어 쓰면 나중에 지우지 못하므로 지금 확정한다.
            // 이미 접속 중으로 거절된 토큰만 재시도를 위해 남는다
            _verifier?.OnAdmitted(requestId);

            // 서버가 첫 계약에 맞는 맵으로 바꾸는 중이면 확정을 미룬다. ServerAccept가 나가면 Mirror가 그 시점의 networkSceneName을
            // 보내므로 전환 전에 받으면 클라이언트가 잘못된 맵으로 간다. 결합과 명단은 유지하고 마감은 로드 유예 상한까지만 늘린다.
            // 늦게 온 같은 출정 참가자도 전환 중이면 같은 경로로 미뤄진다
            if (SyncLoadGrace(Now()))
            {
                conn.authenticationData = binding;
                _pending.TryGraceForLoad(connectionId, _loadGraceUntil);
                _held.Add(new HeldAdmission { ConnectionId = connectionId, Binding = binding });
                return;
            }

            CompleteAccept(conn, binding);
        }

        // 입장 확정, 결합을 연결에 싣고 응답과 ServerAccept를 보낸다
        private void CompleteAccept(NetworkConnectionToClient conn, SessionBinding binding)
        {
            // 같은 임대로 다시 들어왔으면 이전 연결 종료가 걸어 둔 해제를 아직 보내지 않았을 때 거둔다
            if (binding.AccountLeaseId != null)
            {
                _accountLeases?.Cancel(binding.AccountLeaseId);
            }

            _pending.Remove(conn.connectionId);
            conn.authenticationData = binding;
            conn.Send(new JoinResponseMessage(ERejectReason.None, binding.ParticipantId));
            ServerAccept(conn);
        }

        // 맵 전환이 끝났으면 미룬 입장을 모두 확정한다. 확정 중 다시 들어온 호출이 목록을 건드려도 안전하게 옮겨 담아 돈다
        private void TickHeld()
        {
            if ((_held.Count == 0) || ((MapLoader != null) && (MapLoader.IsLoading)))
            {
                return;
            }

            _heldBatch.Clear();
            _heldBatch.AddRange(_held);
            _held.Clear();

            double now = Now();
            for (int i = 0; i < _heldBatch.Count; ++i)
            {
                HeldAdmission held = _heldBatch[i];
                if (!NetworkServer.connections.TryGetValue(held.ConnectionId, out NetworkConnectionToClient conn))
                {
                    // 연결 종료 처리를 거치지 않고 사라진 연결, 결합을 풀어 참가자가 다시 들어올 수 있게 한다
                    _pending.Remove(held.ConnectionId);
                    ReleaseBinding(held.Binding);
                    continue;
                }

                // 메인 스레드가 오래 멈춰 로드 유예 상한을 넘겨 처음 깨어난 프레임은 로드 종료와 마감 초과가 겹친다.
                // 검증 대기가 아니거나(사라짐, 끊기 대기) 마감이 지났으면 승인하지 않는다. 토큰 소비는 허가 때 이미 확정했다.
                // 결합은 연결에 그대로 두고 명단과 던전 호스트의 정리는 실제 연결 종료 구독 체인(인증기 정리 뒤 호스트 통지)에 맡긴다.
                // 여기서 먼저 풀면 호스트가 결합 없이 끊김을 놓치고, 그 사이 재접속이 같은 결합을 쓰다 옛 종료가 새 연결을 풀 수 있다
                if ((!_pending.TryGetVerifyingDeadline(held.ConnectionId, out double deadline)) || (now >= deadline))
                {
                    if (!_pending.IsClosing(held.ConnectionId))
                    {
                        Reject(conn, ERejectReason.TokenExpired);
                    }

                    continue;
                }

                CompleteAccept(conn, held.Binding);
            }

            _heldBatch.Clear();
        }

        // 미룬 입장이 확정 없이 닫힐 때(끊김, 마감) 목록에서만 뺀다. 토큰 소비는 허가 때 이미 확정했다
        private void DropHeld(int connectionId)
        {
            for (int i = 0; i < _held.Count; ++i)
            {
                if (_held[i].ConnectionId != connectionId)
                {
                    continue;
                }

                _held.RemoveAt(i);
                return;
            }
        }

        // 서버 맵 로드를 처음 본 시각에 한 번 유예 상한을 정해 기존 대기 연결의 마감을 그 상한까지 미룬다. 로드가 끝나면 늘어난 항목만
        // 정상 마감(요청 1초, 검증 5초)으로 되돌린다. 끊기 대기는 유예하지 않는다. 로드 중이면 참을 돌려준다
        private bool SyncLoadGrace(double now)
        {
            if ((MapLoader != null) && (MapLoader.IsLoading))
            {
                if (!_loadGraceActive)
                {
                    _loadGraceActive = true;
                    _loadGraceUntil  = now + Constants.Network.DUNGEON_MAP_LOAD_TIMEOUT_SECONDS;
                    _pending.GraceAllForLoad(_loadGraceUntil);
                }

                return true;
            }

            if (_loadGraceActive)
            {
                _loadGraceActive = false;
                _pending.EndLoadGrace(now + Constants.Network.AUTH_REQUEST_TIMEOUT_SECONDS, now + Constants.Network.AUTH_TIMEOUT_SECONDS);
            }

            return false;
        }

        private double Now()
        {
            return (Clock != null) ? Clock() : Time.unscaledTimeAsDouble;
        }

        // 쓰지 못하고 버린 검증 결과 정리. 실패 결과는 잡은 임대가 없다. 새 교환마다 새 임대라 접속 중인 다른 연결의 임대와 겹치지 않는다
        private void AbandonVerified(int requestId, ERejectReason reason, in JoinTokenClaims claims)
        {
            _verifier?.OnAbandoned(requestId);
            if ((reason == ERejectReason.None) && (claims.AccountLeaseId != null))
            {
                _accountLeases?.Release(claims.AccountLeaseId);
            }
        }

        // 거절 응답이 전송 배치에 실려 나간 뒤 Update가 끊는다. 바로 끊으면 응답이 버려져 클라이언트가 사유를 모른다
        private void Reject(NetworkConnectionToClient conn, ERejectReason reason)
        {
            Debug.LogWarning($"[Auth] 접속 거절, connId={conn.connectionId}, reason={reason}");
            conn.isAuthenticated = false;
            conn.Send(new JoinResponseMessage(reason, 0));
            if (!_pending.MarkClosing(conn.connectionId, Now() + Constants.Network.AUTH_REJECT_DISCONNECT_DELAY_SECONDS))
            {
                ServerReject(conn);
            }
        }

        private int NextRequestId()
        {
            ++_lastRequestId;

            // 음수로 돌면 1부터 다시 쓴다. 0은 검증 전 값이다
            if (_lastRequestId <= 0)
            {
                _lastRequestId = 1;
            }

            return _lastRequestId;
        }
    }
}
