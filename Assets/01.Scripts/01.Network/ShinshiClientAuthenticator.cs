// System
using System;
using System.Collections;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Network
{
    /// <summary> 클라이언트 쪽 인증, 연결 직후 프로토콜과 콘텐츠 해시와 접속 토큰을 보내고 서버 결과를 받는다 </summary>
    public sealed class ShinshiClientAuthenticator : NetworkAuthenticator
    {
        // 서버 인증기는 서버 어셈블리에 있어 클라이언트 빌드와 씬에 둘 수 없다. 이 컴포넌트가 씬, 클라이언트의 기본 인증기이고
        // 서버 부트스트랩이 서버를 열기 전에 매니저의 인증기를 서버 인증기로 바꾼다.
        // 바뀌지 않은 채 서버로 쓰이면 모든 연결을 거절한다(인증 없이 스폰되는 것보다 접속 불가가 안전하다)

        /****************************************
        *                Fields
        ****************************************/

        // 해시 계산은 GameDB 전체를 훑으므로 프로세스에서 한 번만 한다
        private static string _contentHash;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // 같은 프로세스에서 다시 접속하면 같은 참가자로 보이도록 한 번 만든 개발 토큰을 계속 쓴다
        private static string _devJoinToken;
#endif

        private string         _joinToken;
        private WaitForSeconds _waitJoinRetry;
        private Coroutine      _joinRetry;
        private int            _alreadyConnectedRetries = 0;
        private bool           _isRetryConnect          = false;

        /****************************************
        *              Properties
        ****************************************/

        public ERejectReason LastRejectReason { get; private set; } = ERejectReason.None;
        public int           ParticipantId    { get; private set; } = 0;

        // 서버가 인증을 거절했다. UI가 사유별 안내를 띄운다. 이미 접속 중 거절은 재시도를 다 쓴 뒤에만 낸다
        public event Action<ERejectReason> Rejected;

        // 이미 접속 중으로 거절돼 잠시 뒤 같은 토큰으로 다시 접속한다. 인자는 몇 번째 재시도인지, UI가 접속 중 표시를 유지한다
        public event Action<int> JoinRetryScheduled;

        /****************************************
        *                Methods
        ****************************************/

        // 도메인 리로드를 끈 에디터에서 이전 플레이의 값이 남지 않게 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            _contentHash = null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _devJoinToken = null;
#endif
        }

        /// <summary> 다음 접속에 보낼 일회용 접속 토큰, StartClient 전에 넣는다 </summary>
        public void SetJoinToken(string joinToken)
        {
            CancelJoinRetry();
            _joinToken = joinToken;
        }

        /// <summary> 예약된 재접속을 취소한다, 사용자가 접속을 그만두거나 다른 서버로 갈 때 부른다 </summary>
        public void CancelJoinRetry()
        {
            if (_joinRetry != null)
            {
                StopCoroutine(_joinRetry);
                _joinRetry = null;
            }

            _isRetryConnect = false;
        }

        private void OnDisable()
        {
            CancelJoinRetry();
        }

        /****************************************
        *                Server
        ****************************************/

        public override void OnServerAuthenticate(NetworkConnectionToClient conn)
        {
            Debug.LogError("[Auth] 서버 인증기가 설치되지 않아 접속을 거절한다. 서버는 ServerBootstrap으로 시작한다");
            ServerReject(conn);
        }

        /****************************************
        *                Client
        ****************************************/

        public override void OnStartClient()
        {
            NetworkClient.RegisterHandler<JoinResponseMessage>(OnJoinResponse, false);
        }

        public override void OnStopClient()
        {
            NetworkClient.UnregisterHandler<JoinResponseMessage>();
        }

        public override void OnClientAuthenticate()
        {
            LastRejectReason = ERejectReason.None;
            ParticipantId    = 0;

            // 사용자가 새로 시작한 접속이면 재시도 횟수를 처음부터 센다
            if (!_isRetryConnect)
            {
                _alreadyConnectedRetries = 0;
            }

            _isRetryConnect = false;

            string joinToken = ResolveJoinToken();
            if (string.IsNullOrEmpty(joinToken))
            {
                Debug.LogError("[Auth] 접속 토큰이 없어 접속하지 않는다");
                ClientReject();
                return;
            }

            string contentHash = ResolveContentHash();
            if (contentHash == null)
            {
                ClientReject();
                return;
            }

            NetworkClient.Send(new JoinRequestMessage(Constants.Network.PROTOCOL_VERSION, contentHash, joinToken));
        }

        // 서버는 인증 전 연결의 일반 메시지를 받지 않고 연결을 끊는다. 일반 요청은 인증이 끝난 연결에만 보낸다
        /// <summary> 연결돼 있고 서버 인증까지 끝났으면 참 </summary>
        public static bool IsAuthenticatedConnection()
        {
            return (NetworkClient.isConnected) && (NetworkClient.connection != null) && (NetworkClient.connection.isAuthenticated);
        }

        private void OnJoinResponse(JoinResponseMessage message)
        {
            if (message.IsAccepted)
            {
                _alreadyConnectedRetries = 0;
                ParticipantId            = message.ParticipantId;
                ClientAccept();
                return;
            }

            LastRejectReason = message.Reason;
            if (JoinRetryRules.ShouldRetry(message.Reason, _alreadyConnectedRetries))
            {
                ++_alreadyConnectedRetries;
                Debug.LogWarning($"[Auth] 이미 접속 중으로 거절돼 잠시 뒤 다시 접속한다, retry={_alreadyConnectedRetries}/" +
                    $"{Constants.Network.JOIN_ALREADY_CONNECTED_MAX_RETRIES}");
                JoinRetryScheduled?.Invoke(_alreadyConnectedRetries);
                ClientReject();
                ScheduleJoinRetry();
                return;
            }

            Debug.LogWarning($"[Auth] 서버가 접속을 거절했다, reason={message.Reason}");
            Rejected?.Invoke(message.Reason);
            ClientReject();
        }

        private void ScheduleJoinRetry()
        {
            if (_waitJoinRetry == null)
            {
                _waitJoinRetry = new WaitForSeconds(Constants.Network.JOIN_ALREADY_CONNECTED_RETRY_INTERVAL_SECONDS);
            }

            if (_joinRetry != null)
            {
                StopCoroutine(_joinRetry);
            }

            _joinRetry = StartCoroutine(CoRetryJoin());
        }

        // 거절로 클라이언트가 내려간 뒤 같은 주소와 토큰으로 다시 연다. 서버는 이전 교환 결과를 잠시 기억해 같은 토큰을 받는다
        private IEnumerator CoRetryJoin()
        {
            yield return _waitJoinRetry;
            _joinRetry = null;

            NetworkManager manager = NetworkManager.singleton;
            if ((manager == null) || (NetworkClient.active) || (NetworkServer.active))
            {
                yield break;
            }

            _isRetryConnect = true;
            try
            {
                manager.StartClient();
            }
            catch (Exception exception)
            {
                _isRetryConnect = false;
                Debug.LogError($"[Auth] 재접속 시작 실패 ({exception.GetType().Name})");
                Rejected?.Invoke(ERejectReason.AlreadyConnected);
            }
        }

        private string ResolveJoinToken()
        {
            if (!string.IsNullOrEmpty(_joinToken))
            {
                return _joinToken;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_devJoinToken == null)
            {
                _devJoinToken = Constants.Network.DEV_JOIN_TOKEN_PREFIX + Guid.NewGuid().ToString("N");
            }

            return _devJoinToken;
#else
            return null;
#endif
        }

        private static string ResolveContentHash()
        {
            if (_contentHash != null)
            {
                return _contentHash;
            }

            if (!GameDB.IsInitialized)
            {
                Debug.LogError("[Auth] GameDB 초기화 전이라 콘텐츠 해시를 만들 수 없다");
                return null;
            }

            try
            {
                _contentHash = GameDBContentHash.Compute();
            }
            catch (Exception exception)
            {
                // 데이터 값이 예외 메시지에 들어갈 수 있어 타입만 남긴다
                Debug.LogError($"[Auth] 콘텐츠 해시 계산 실패 ({exception.GetType().Name})");
                return null;
            }

            return _contentHash;
        }
    }
}
