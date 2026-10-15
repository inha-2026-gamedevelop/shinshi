// System
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

        /****************************************
        *              Properties
        ****************************************/

        public ServerSessionRegistry Sessions => _sessions;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            _accountLeases?.Tick();
            if (_pending.Count == 0)
            {
                return;
            }

            _due.Clear();
            if (_pending.CollectDue(Time.unscaledTimeAsDouble, _due) == 0)
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

        /// <summary> 역할별 주소 묶음당 인증 대기 상한, 던전이 더 좁다 </summary>
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
        }

        public override void OnServerAuthenticate(NetworkConnectionToClient conn)
        {
            if (!_pending.TryAdd(conn.connectionId, conn.address,
                Time.unscaledTimeAsDouble + Constants.Network.AUTH_REQUEST_TIMEOUT_SECONDS))
            {
                // 주소는 개인 정보라 로그에 남기지 않는다
                Debug.LogWarning($"[Auth] 인증 대기 상한(전체 {_pending.Capacity}, 주소별 {_pending.MaxPerAddress})을 넘어 연결을 바로 끊는다, " +
                    $"connId={conn.connectionId}");
                ServerReject(conn);
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
            if ((_sessions == null) || (!(conn.authenticationData is SessionBinding binding)))
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
            if ((sessions == null) || (binding == null))
            {
                return;
            }

            sessions.Forget(binding);

            // 던전으로 떠나는 참가자의 임대는 던전이 이관 경로로 옮긴다. 여기서 풀면 이관이 거절된다
            // 해제가 끝내 실패해도 계정 임대는 heartbeat 만료로 풀린다(백엔드 계약)
            if ((binding.AccountLeaseId != null) && (!binding.IsDeparting))
            {
                accountLeases?.Release(binding.AccountLeaseId);
            }
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
            _pending.TryBeginVerify(conn.connectionId, requestId, Time.unscaledTimeAsDouble + Constants.Network.AUTH_TIMEOUT_SECONDS);
            _verifier.Verify(requestId, message.JoinToken, this);
        }

        // 검증기가 메인 스레드에서 부른다. 그 사이 연결이 끊겼거나 시간 초과로 닫혔을 수 있어 현재 상태를 다시 본다
        public void OnJoinTokenVerified(int requestId, ERejectReason reason, in JoinTokenClaims claims)
        {
            if (!_pending.TryFindVerifying(requestId, out int connectionId))
            {
                return;
            }

            if (!NetworkServer.connections.TryGetValue(connectionId, out NetworkConnectionToClient conn))
            {
                _pending.Remove(connectionId);
                return;
            }

            if (reason != ERejectReason.None)
            {
                Reject(conn, reason);
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

            // 같은 임대로 다시 들어왔으면 이전 연결 종료가 걸어 둔 해제를 아직 보내지 않았을 때 거둔다
            if (binding.AccountLeaseId != null)
            {
                _accountLeases?.Cancel(binding.AccountLeaseId);
            }

            _pending.Remove(connectionId);
            conn.authenticationData = binding;
            conn.Send(new JoinResponseMessage(ERejectReason.None, binding.ParticipantId));
            ServerAccept(conn);

            // 입장한 토큰은 검증기가 다시 받아 주지 않게 한다. 이미 접속 중으로 거절된 토큰만 재시도를 위해 남는다
            _verifier.OnAdmitted(requestId);
        }

        // 거절 응답이 전송 배치에 실려 나간 뒤 Update가 끊는다. 바로 끊으면 응답이 버려져 클라이언트가 사유를 모른다
        private void Reject(NetworkConnectionToClient conn, ERejectReason reason)
        {
            Debug.LogWarning($"[Auth] 접속 거절, connId={conn.connectionId}, reason={reason}");
            conn.isAuthenticated = false;
            conn.Send(new JoinResponseMessage(reason, 0));
            if (!_pending.MarkClosing(conn.connectionId, Time.unscaledTimeAsDouble + Constants.Network.AUTH_REJECT_DISCONNECT_DELAY_SECONDS))
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
