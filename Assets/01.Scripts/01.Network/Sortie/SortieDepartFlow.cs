// System
using System;
using System.Collections;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    // 순서: 마을 서버가 보낸 내 던전 입장권(SortieTicketMessage) 확인 -> 마을 연결 끊기 -> 던전 서버 접속, 입장 토큰으로 인증.
    // 인증 메시지는 로비 -> 마을 접속과 같은 JoinRequestMessage(프로토콜, 콘텐츠 해시, 일회용 토큰)이고 인증기가 보낸다.
    // 마을 임대가 던전으로 옮겨지기 전에는 ALREADY_CONNECTED로 거절될 수 있다. 그 재시도는 클라이언트 인증기가 맡는다.
    // 입장권 만료는 던전 서버와 API가 판정한다(클라이언트 시계는 어긋날 수 있어 여기서 거절하지 않는다).
    // 입장권 확인 실패는 마을 연결을 그대로 두고, 던전 접속 단계 실패는 연결이 없는 상태로 끝난다. 그때 호출자가
    // LobbyFlow.ReturnToCharacterSelect로 보낸다(SortieReturnFlow와 같은 규칙). 한 번에 하나만 돌린다.
    // 던전 씬(DungeonForest)은 던전 서버가 접속 직후 안내해 클라이언트가 불러온다(ShinshiNetworkManager.ConfigureServerRole)
    /// <summary> 마을에서 배정 던전으로 옮기는 클라이언트 흐름, 화면 표시는 하지 않고 결과 사유만 알린다 </summary>
    public sealed class SortieDepartFlow
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float CONNECT_TIMEOUT_SECONDS = 20f;

        // 던전 접속의 이미 접속 중 재시도까지 기다린다
        private const float JOIN_WAIT_SECONDS = CONNECT_TIMEOUT_SECONDS
            + (Constants.Network.JOIN_ALREADY_CONNECTED_RETRY_INTERVAL_SECONDS * Constants.Network.JOIN_ALREADY_CONNECTED_MAX_RETRIES);

        private readonly ShinshiNetworkManager      _manager;
        private readonly ShinshiClientAuthenticator _authenticator;

        private ERejectReason _joinRejected = ERejectReason.None;
        private bool          _retryPending = false;
        private bool          _isRunning    = false;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsRunning => _isRunning;

        /****************************************
        *                Methods
        ****************************************/

        public SortieDepartFlow(ShinshiNetworkManager manager, ShinshiClientAuthenticator authenticator)
        {
            if ((manager == null) || (authenticator == null))
            {
                throw new ArgumentNullException((manager == null) ? nameof(manager) : nameof(authenticator));
            }

            _manager       = manager;
            _authenticator = authenticator;
        }

        /// <summary> 입장권으로 마을을 끊고 던전에 접속한다, done은 None(던전 인증 성공)이나 거절 사유로 한 번 불린다 </summary>
        public IEnumerator Run(SortieTicketMessage ticket, Action<ERejectReason> done)
        {
            if (_isRunning)
            {
                done?.Invoke(ERejectReason.PendingCommit);
                yield break;
            }

            if (!IsValidDungeonTicket(in ticket))
            {
                done?.Invoke(ERejectReason.SessionMismatch);
                yield break;
            }

            _isRunning = true;
            ERejectReason result = ERejectReason.BackendUnavailable;
            _authenticator.Rejected           += OnJoinRejected;
            _authenticator.JoinRetryScheduled += OnJoinRetryScheduled;
            try
            {
                var outcome = new ERejectReason[1];
                yield return ConnectDungeon(ticket, outcome);
                result = outcome[0];
            }
            finally
            {
                _authenticator.Rejected           -= OnJoinRejected;
                _authenticator.JoinRetryScheduled -= OnJoinRetryScheduled;
                _manager.IsTransferringServer      = false;
                _isRunning                         = false;
            }

            done?.Invoke(result);
        }

        /// <summary> 마을 서버가 보낸 입장권이 접속할 수 있는 던전을 가리키는지 본다 </summary>
        public static bool IsValidDungeonTicket(in SortieTicketMessage ticket)
        {
            return (!string.IsNullOrEmpty(ticket.SessionId)) && (!string.IsNullOrEmpty(ticket.JoinToken))
                && (Uri.CheckHostName(ticket.Address ?? string.Empty) != UriHostNameType.Unknown)
                && (ticket.Port > 0) && (ticket.Port <= ushort.MaxValue);
        }

        /// <summary> 입장권을 받았을 때 흐름을 시작할지, 이미 옮기는 중이거나 마을에 연결돼 있지 않으면 거짓 </summary>
        public static bool CanBegin(bool isRunning, bool isTransferringServer, bool isClientConnected)
        {
            return (!isRunning) && (!isTransferringServer) && (isClientConnected);
        }

        // 결과를 담을 칸을 받아 코루틴 안에서 채운다(코루틴은 out 인자를 쓸 수 없다)
        private IEnumerator ConnectDungeon(SortieTicketMessage ticket, ERejectReason[] outcome)
        {
            if (!(Transport.active is PortTransport portTransport))
            {
                outcome[0] = ERejectReason.BackendUnavailable;
                yield break;
            }

            // 마을 씬에서 연결을 끊어도 캐릭터 선택으로 돌아가지 않게 서버 이동 중으로 표시한다
            _manager.IsTransferringServer = true;
            _authenticator.CancelJoinRetry();
            if (NetworkClient.active)
            {
                _manager.StopClient();
            }

            // 끊김 처리가 다음 프레임에 끝나므로 한 번 넘긴다
            yield return null;

            _joinRejected = ERejectReason.None;
            _retryPending = false;
            _manager.ConfigureServerRole(EServerRole.Dungeon);
            _manager.networkAddress = ticket.Address;
            portTransport.Port      = (ushort)ticket.Port;
            _authenticator.SetJoinToken(ticket.JoinToken);
            _manager.StartClient();

            float deadline = Time.realtimeSinceStartup + JOIN_WAIT_SECONDS;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (IsAuthenticated())
                {
                    outcome[0] = ERejectReason.None;
                    yield break;
                }

                if (_joinRejected != ERejectReason.None)
                {
                    break;
                }

                // 재접속 예약 사이에는 클라이언트가 잠시 내려가 있다. 다시 올라오면 예약이 소비된 것이다
                if (NetworkClient.active)
                {
                    _retryPending = false;
                }
                else if (!_retryPending)
                {
                    break;
                }

                yield return null;
            }

            _authenticator.CancelJoinRetry();
            if (NetworkClient.active)
            {
                _manager.StopClient();
            }

            outcome[0] = (_joinRejected != ERejectReason.None) ? _joinRejected : ERejectReason.BackendUnavailable;
        }

        private void OnJoinRejected(ERejectReason reason)
        {
            _joinRejected = reason;
        }

        private void OnJoinRetryScheduled(int retry)
        {
            _retryPending = true;
        }

        private static bool IsAuthenticated()
        {
            return (NetworkClient.isConnected) && (NetworkClient.connection != null) && (NetworkClient.connection.isAuthenticated);
        }
    }
}
