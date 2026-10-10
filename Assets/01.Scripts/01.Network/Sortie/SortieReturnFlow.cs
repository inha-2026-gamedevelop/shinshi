// System
using System;
using System.Collections;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    // 순서: 마을 입장권(TOWN_JOIN_TICKET) -> 던전 연결 끊기 -> 마을 서버 접속. 던전 이탈 기록이 API에 저장되기 전에는 계정 임대가
    // 던전에 남아 입장권이 ALREADY_CONNECTED로 거절될 수 있어 2초 간격으로 몇 번 다시 묻는다(접속 재시도와 같은 규칙).
    // 마을 서버 접속의 이미 접속 중 재시도는 클라이언트 인증기가 맡는다.
    // 출정 화면(UI 담당)이 SortieClient.Current.CanReturn을 본 뒤 MonoBehaviour에서 Run을 코루틴으로 돌린다. 한 번에 하나만 돌린다.
    // 입장권 단계 실패는 던전 연결을 그대로 두고, 마을 접속 단계 실패는 연결이 없는 상태로 끝난다. 그때 호출자가
    // LobbyFlow.ReturnToCharacterSelect로 보낸다(화면 안내는 호출자 몫)
    /// <summary> 던전에서 마을로 돌아가는 클라이언트 흐름, 화면 표시는 하지 않고 결과 사유만 알린다 </summary>
    public sealed class SortieReturnFlow
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float CONNECT_TIMEOUT_SECONDS = 20f;

        // 마을 접속의 이미 접속 중 재시도까지 기다린다
        private const float JOIN_WAIT_SECONDS = CONNECT_TIMEOUT_SECONDS
            + (Constants.Network.JOIN_ALREADY_CONNECTED_RETRY_INTERVAL_SECONDS * Constants.Network.JOIN_ALREADY_CONNECTED_MAX_RETRIES);

        private readonly LobbyApiClient             _api;
        private readonly ShinshiNetworkManager      _manager;
        private readonly ShinshiClientAuthenticator _authenticator;

        private LobbyApiReply<JoinTicket> _reply        = null;
        private ERejectReason             _joinRejected = ERejectReason.None;
        private bool                      _retryPending = false;
        private bool                      _isRunning    = false;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsRunning => _isRunning;

        /****************************************
        *                Methods
        ****************************************/

        public SortieReturnFlow(LobbyApiClient api, ShinshiNetworkManager manager, ShinshiClientAuthenticator authenticator)
        {
            if ((api == null) || (manager == null) || (authenticator == null))
            {
                throw new ArgumentNullException((api == null) ? nameof(api) : (manager == null) ? nameof(manager) : nameof(authenticator));
            }

            _api           = api;
            _manager       = manager;
            _authenticator = authenticator;
        }

        /// <summary> 마을 입장권을 받아 던전을 끊고 마을에 접속한다, done은 None(마을 인증 성공)이나 거절 사유로 한 번 불린다 </summary>
        public IEnumerator Run(string characterId, Action<ERejectReason> done)
        {
            if (_isRunning)
            {
                done?.Invoke(ERejectReason.PendingCommit);
                yield break;
            }

            // 입장권 요청과 재시도 구간에 던전 연결이 끊겨도 캐릭터 선택으로 보내지 않게 처음부터 서버 이동 중으로 표시한다.
            // 실패하면 호출자가 연결 상태를 보고 캐릭터 선택으로 보낸다
            _isRunning = true;
            _manager.IsTransferringServer = true;
            ERejectReason result = ERejectReason.BackendUnavailable;
            _authenticator.Rejected           += OnJoinRejected;
            _authenticator.JoinRetryScheduled += OnJoinRetryScheduled;
            try
            {
                var outcome = new ERejectReason[1];
                yield return RequestTicket(characterId, outcome);
                result = outcome[0];
                if (result == ERejectReason.None)
                {
                    yield return ConnectTown(_reply.Value, outcome);
                    result = outcome[0];
                }
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

        /// <summary> 마을 입장권이 이 캐릭터의 마을 서버를 가리키는지 본다 </summary>
        public static bool IsValidTownTicket(JoinTicket ticket, string characterId)
        {
            return (ticket != null) && (ticket.ServerRole == ContractCodes.ServerRole.TOWN)
                && (string.Equals(ticket.CharacterId, characterId, StringComparison.Ordinal))
                && (!string.IsNullOrEmpty(ticket.JoinToken))
                && (Uri.CheckHostName(ticket.Address ?? string.Empty) != UriHostNameType.Unknown)
                && (ticket.Port > 0) && (ticket.Port <= ushort.MaxValue);
        }

        /// <summary> 입장권 실패 응답을 거절 사유로 바꾼다, 서버에 닿지 못했으면 BackendUnavailable </summary>
        public static ERejectReason ToRejectReason(long statusCode, int reasonCode)
        {
            if (statusCode == 0)
            {
                return ERejectReason.BackendUnavailable;
            }

            if ((reasonCode == ContractCodes.RejectReason.NONE) || (!Enum.IsDefined(typeof(ERejectReason), reasonCode)))
            {
                return ERejectReason.SessionMismatch;
            }

            return (ERejectReason)reasonCode;
        }

        // 결과를 담을 칸을 받아 코루틴 안에서 채운다(코루틴은 out 인자를 쓸 수 없다)
        private IEnumerator RequestTicket(string characterId, ERejectReason[] outcome)
        {
            WaitForSecondsRealtime wait = null;
            for (int retries = 0; ; ++retries)
            {
                _reply = null;
                yield return _api.SendGame<JoinTicket>(ApiRoutes.TOWN_JOIN_TICKET, new TownJoinTicketRequest(characterId), OnTicketReply);
                if ((_reply != null) && (_reply.IsSuccess))
                {
                    outcome[0] = IsValidTownTicket(_reply.Value, characterId) ? ERejectReason.None : ERejectReason.SessionMismatch;
                    yield break;
                }

                ERejectReason reason = (_reply != null) ? ToRejectReason(_reply.StatusCode, _reply.ReasonCode) : ERejectReason.BackendUnavailable;
                if (!JoinRetryRules.ShouldRetry(reason, retries))
                {
                    outcome[0] = reason;
                    yield break;
                }

                if (wait == null)
                {
                    wait = new WaitForSecondsRealtime(Constants.Network.JOIN_ALREADY_CONNECTED_RETRY_INTERVAL_SECONDS);
                }

                yield return wait;
            }
        }

        private IEnumerator ConnectTown(JoinTicket ticket, ERejectReason[] outcome)
        {
            if (!(Transport.active is PortTransport portTransport))
            {
                outcome[0] = ERejectReason.BackendUnavailable;
                yield break;
            }

            _authenticator.CancelJoinRetry();
            if (NetworkClient.active)
            {
                _manager.StopClient();
            }

            // 끊김 처리가 다음 프레임에 끝나므로 한 번 넘긴다
            yield return null;

            _joinRejected = ERejectReason.None;
            _retryPending = false;
            _manager.ConfigureServerRole(EServerRole.Town);
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

        private void OnTicketReply(LobbyApiReply<JoinTicket> reply)
        {
            _reply = reply;
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
