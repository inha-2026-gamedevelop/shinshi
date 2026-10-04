// System
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 출시 서버의 접속 토큰 검증기, 일회용 토큰을 백엔드에서 참가자 정보로 교환한다 </summary>
    public sealed class ApiJoinTokenVerifier : IJoinTokenVerifier, IBackendResponseListener
    {
        // 토큰은 60초, 1회 교환, 참가자와 인스턴스에 묶인다. 교환은 재시도하지 않는다(응답을 잃으면 이미 쓴 토큰이 된다).
        // 인증 마감 안에 결과가 오지 않으면 결과를 버린다. 인증기는 그 사이 연결을 시간 초과로 닫았을 것이다.
        //
        // 이전 연결의 끊김을 서버가 늦게 알면(KCP 최대 10초) 새 연결은 이미 접속 중으로 거절되고, 클라이언트는 같은 토큰으로
        // 다시 접속한다. 토큰은 이미 교환돼 API로는 다시 바꿀 수 없으므로 교환 결과를 토큰 해시로 잠시 기억해 이 서버에서만
        // 다시 받아 준다. 입장에 성공하면 OnAdmitted에서 지워 같은 토큰으로 두 번 입장하지 못하게 한다

        /****************************************
        *                Fields
        ****************************************/

        private readonly BackendGateway                        _gateway;
        private readonly string                                _instanceId;
        private readonly EServerRole                           _serverRole;
        private readonly Func<double>                          _clock;
        private readonly Action<string>                        _warn;
        private readonly bool                                  _requireCombatLoadout;
        private readonly Dictionary<int, PendingExchange>      _pending     = new Dictionary<int, PendingExchange>();
        private readonly Dictionary<string, ExchangedClaims>   _exchanged   = new Dictionary<string, ExchangedClaims>(StringComparer.Ordinal);
        private readonly List<string>                          _expiredKeys = new List<string>();

        /****************************************
        *              Properties
        ****************************************/

        public int PendingCount    => _pending.Count;
        public int RememberedCount => _exchanged.Count;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> clock은 단조 증가 초, warn은 null이면 기록하지 않는다. 출시 빌드는 전투 준비 없는 던전 입장을 거절한다 </summary>
        public ApiJoinTokenVerifier(BackendGateway gateway, string instanceId, EServerRole serverRole, Func<double> clock,
            Action<string> warn)
            : this(gateway, instanceId, serverRole, clock, warn, !UnityEngine.Debug.isDebugBuild)
        {
        }

        /// <summary> requireCombatLoadout이면 combatLoadout이 없는 던전 교환 응답을 SessionMismatch로 거절한다 </summary>
        public ApiJoinTokenVerifier(BackendGateway gateway, string instanceId, EServerRole serverRole, Func<double> clock,
            Action<string> warn, bool requireCombatLoadout)
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
            _serverRole = serverRole;
            _clock      = clock ?? BackendGateway.MonotonicSeconds;
            _warn       = warn;

            _requireCombatLoadout = requireCombatLoadout;
        }

        public void Verify(int requestId, string joinToken, IJoinTokenVerifyListener listener)
        {
            if (listener == null)
            {
                throw new ArgumentNullException(nameof(listener));
            }

            if (string.IsNullOrEmpty(joinToken))
            {
                listener.OnJoinTokenVerified(requestId, ERejectReason.TokenRevoked, default);
                return;
            }

            double now = _clock();
            ForgetExpired(now);

            string tokenHash = HashToken(joinToken);
            if (_exchanged.TryGetValue(tokenHash, out ExchangedClaims remembered))
            {
                _exchanged[tokenHash] = remembered.WithAuthRequest(requestId);
                listener.OnJoinTokenVerified(requestId, ERejectReason.None, remembered.Claims);
                return;
            }

            // 인증기의 대기 상한보다 많이 쌓일 일은 없지만, 백엔드가 멈춘 동안 기억이 끝없이 늘지 않게 막는다
            if (_pending.Count >= Constants.Network.MAX_PENDING_AUTH_CONNECTIONS)
            {
                listener.OnJoinTokenVerified(requestId, ERejectReason.BackendUnavailable, default);
                return;
            }

            BackendRequest request = BackendRequest.Post(JoinApiRoutes.JOIN_TOKEN_EXCHANGE,
                new JoinTokenExchangeRequest(joinToken, _instanceId, (int)_serverRole), typeof(JoinTokenExchangeResponse), null,
                Constants.Network.JOIN_TOKEN_EXCHANGE_TIMEOUT_SECONDS);
            int backendRequestId = _gateway.Send(request, this);
            _pending[backendRequestId] = new PendingExchange(requestId, listener,
                now + Constants.Network.JOIN_TOKEN_VERIFY_DEADLINE_SECONDS, tokenHash);
        }

        public void OnAdmitted(int requestId)
        {
            string admittedKey = null;
            foreach (KeyValuePair<string, ExchangedClaims> pair in _exchanged)
            {
                if (pair.Value.LastAuthRequestId == requestId)
                {
                    admittedKey = pair.Key;
                    break;
                }
            }

            if (admittedKey != null)
            {
                _exchanged.Remove(admittedKey);
            }
        }

        // 게이트웨이가 메인 스레드의 Pump에서 부른다
        public void OnBackendResponse(in BackendResponse response)
        {
            if (!_pending.TryGetValue(response.RequestId, out PendingExchange pending))
            {
                return;
            }

            _pending.Remove(response.RequestId);
            double        now    = _clock();
            ERejectReason reason = ToClaims(in response, out JoinTokenClaims claims);

            // 늦게 왔어도 교환은 끝났으므로 기억해 둔다. 클라이언트가 같은 토큰으로 다시 오면 API를 다시 부르지 않고 받는다
            if (reason == ERejectReason.None)
            {
                Remember(pending.TokenHash, in claims, pending.AuthRequestId, now);
            }

            if (now > pending.Deadline)
            {
                _warn?.Invoke($"[Auth] 토큰 교환 결과가 인증 마감 뒤에 와서 버린다, requestId={pending.AuthRequestId}, " +
                    $"outcome={response.Outcome}, reason={reason}");
                return;
            }

            pending.Listener.OnJoinTokenVerified(pending.AuthRequestId, reason, claims);
        }

        private ERejectReason ToClaims(in BackendResponse response, out JoinTokenClaims claims)
        {
            claims = default;
            switch (response.Outcome)
            {
                case EBackendOutcome.Succeeded:
                    ERejectReason reason = JoinTokenClaimsRules.TryCreate(_serverRole, _instanceId,
                        response.Payload as JoinTokenExchangeResponse, _requireCombatLoadout, out claims);
                    if (reason != ERejectReason.None)
                    {
                        _warn?.Invoke($"[Auth] 토큰 교환 응답이 이 서버와 맞지 않아 거절한다, reason={reason}, instance={_instanceId}");
                    }

                    return reason;
                case EBackendOutcome.Rejected:
                    return JoinTokenClaimsRules.FromApiReasonCode(response.ReasonCode);
                case EBackendOutcome.Timeout:
                    // 인증기의 시간 초과와 같은 사유로 알린다. 토큰은 API에서 이미 소모됐을 수 있다
                    return ERejectReason.TokenExpired;
                default:
                    // 연결 실패, 5xx, 회로 열림, 계약 불일치. 클라이언트 탓이 아니므로 백엔드 일시 불가로 알린다
                    return ERejectReason.BackendUnavailable;
            }
        }

        private void Remember(string tokenHash, in JoinTokenClaims claims, int authRequestId, double now)
        {
            if ((!_exchanged.ContainsKey(tokenHash)) && (_exchanged.Count >= Constants.Network.MAX_JOIN_EXCHANGE_REUSE_ENTRIES))
            {
                return;
            }

            _exchanged[tokenHash] = new ExchangedClaims(claims, now + Constants.Network.JOIN_EXCHANGE_REUSE_SECONDS, authRequestId);
        }

        private void ForgetExpired(double now)
        {
            if (_exchanged.Count == 0)
            {
                return;
            }

            _expiredKeys.Clear();
            foreach (KeyValuePair<string, ExchangedClaims> pair in _exchanged)
            {
                if (now >= pair.Value.ExpiresAt)
                {
                    _expiredKeys.Add(pair.Key);
                }
            }

            for (int i = 0; i < _expiredKeys.Count; ++i)
            {
                _exchanged.Remove(_expiredKeys[i]);
            }

            _expiredKeys.Clear();
        }

        // 원문 토큰을 메모리 사전의 키로 두지 않는다. 덤프나 디버거에서 다시 쓸 수 있는 값이 남지 않게 한다
        private static string HashToken(string joinToken)
        {
            byte[] hash;
            using (SHA256 sha = SHA256.Create())
            {
                hash = sha.ComputeHash(Encoding.UTF8.GetBytes(joinToken));
            }

            var builder = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; ++i)
            {
                builder.Append(hash[i].ToString("x2"));
            }

            return builder.ToString();
        }

        /****************************************
        *               Records
        ****************************************/

        private readonly struct PendingExchange
        {
            private readonly int                      _authRequestId;
            private readonly IJoinTokenVerifyListener _listener;
            private readonly double                   _deadline;
            private readonly string                   _tokenHash;

            public int                      AuthRequestId => _authRequestId;
            public IJoinTokenVerifyListener Listener      => _listener;
            public double                   Deadline      => _deadline;
            public string                   TokenHash     => _tokenHash;

            public PendingExchange(int authRequestId, IJoinTokenVerifyListener listener, double deadline, string tokenHash)
            {
                _authRequestId = authRequestId;
                _listener      = listener;
                _deadline      = deadline;
                _tokenHash     = tokenHash;
            }
        }

        private readonly struct ExchangedClaims
        {
            private readonly JoinTokenClaims _claims;
            private readonly double          _expiresAt;
            private readonly int             _lastAuthRequestId;

            public JoinTokenClaims Claims            => _claims;
            public double          ExpiresAt         => _expiresAt;
            public int             LastAuthRequestId => _lastAuthRequestId;

            public ExchangedClaims(in JoinTokenClaims claims, double expiresAt, int lastAuthRequestId)
            {
                _claims            = claims;
                _expiresAt         = expiresAt;
                _lastAuthRequestId = lastAuthRequestId;
            }

            public ExchangedClaims WithAuthRequest(int authRequestId)
            {
                return new ExchangedClaims(_claims, _expiresAt, authRequestId);
            }
        }
    }
}
