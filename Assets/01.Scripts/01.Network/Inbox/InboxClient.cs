// System
using System;
using System.Collections;
using System.Globalization;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    // 우편함은 마을 서버를 거치지 않고 게임 세션 토큰으로 API를 직접 부른다(GET /v1/inbox, POST /v1/inbox/claim).
    // 우편함 화면(UI 담당)은 이 창구의 이벤트만 구독한다. 한 번에 요청 하나만 보내고, 진행 중이면 새 요청은 거짓을 돌려준다.
    // 수령은 현재 캐릭터(LobbyFlow.ActiveCharacterId)로 보낸다. 결과를 모르면(네트워크 실패) RetryClaim으로 같은 operationId를 다시 보낸다
    /// <summary> 클라이언트의 우편 목록 조회와 수령 창구 </summary>
    public static class InboxClient
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string OPERATION_ID_FORMAT = "D";

        private static InboxClientHost _host;
        private static LobbyApiClient  _api;
        private static bool            _busy;
        private static string          _lastClaimInboxId;
        private static string          _lastClaimOperationId;

        /****************************************
        *              Properties
        ****************************************/

        public static bool IsBusy => _busy;

        public static event Action<InboxListResult>  ListReceived;
        public static event Action<InboxClaimResult> ClaimCompleted;

        /****************************************
        *                Methods
        ****************************************/

        // 도메인 리로드를 끈 에디터에서 이전 플레이의 값이 남지 않게 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            _host                 = null;
            _api                  = null;
            _busy                 = false;
            _lastClaimInboxId     = null;
            _lastClaimOperationId = null;
            ListReceived          = null;
            ClaimCompleted        = null;
        }

        /// <summary> 받을 수 있는 우편 목록을 요청한다, 결과는 ListReceived로 온다 </summary>
        public static bool RequestList()
        {
            if (_busy)
            {
                return false;
            }

            Begin();
            _host.StartCoroutine(_api.SendGame<InboxListResponse>(ApiRoutes.INBOX, null, OnListReply));
            return true;
        }

        /// <summary> 우편 하나를 현재 캐릭터로 수령한다, 결과는 ClaimCompleted로 온다 </summary>
        public static bool Claim(string inboxId)
        {
            if ((_busy) || (string.IsNullOrEmpty(inboxId)))
            {
                return false;
            }

            _lastClaimInboxId     = inboxId;
            _lastClaimOperationId = Guid.NewGuid().ToString(OPERATION_ID_FORMAT);
            return SendClaim();
        }

        /// <summary> 결과를 모르는 마지막 수령을 같은 operationId로 다시 보낸다, API가 처음 결과를 돌려준다 </summary>
        public static bool RetryClaim()
        {
            if ((_busy) || (string.IsNullOrEmpty(_lastClaimOperationId)))
            {
                return false;
            }

            return SendClaim();
        }

        private static bool SendClaim()
        {
            Begin();
            var body = new InboxClaimRequest(_lastClaimOperationId, _lastClaimInboxId, LobbyFlow.ActiveCharacterId);
            _host.StartCoroutine(_api.SendGame<OperationResult>(ApiRoutes.INBOX_CLAIM, body, OnClaimReply));
            return true;
        }

        private static void Begin()
        {
            if (_host == null)
            {
                var go = new GameObject("InboxClientHost") { hideFlags = HideFlags.HideAndDontSave };
                UnityEngine.Object.DontDestroyOnLoad(go);
                _host = go.AddComponent<InboxClientHost>();
            }

            _api  = new LobbyApiClient();
            _busy = true;
        }

        private static void OnListReply(LobbyApiReply<InboxListResponse> reply)
        {
            _busy = false;
            InboxListResult result = reply.IsSuccess
                ? new InboxListResult(true, reply.Value.Entries ?? Array.Empty<InboxEntry>(), reply.Value.ServerTimeUnixMs, ERejectReason.None)
                : new InboxListResult(false, Array.Empty<InboxEntry>(), 0, InboxRejectRules.FromReply(reply.ReasonCode, reply.StatusCode));
            ListReceived?.Invoke(result);
        }

        private static void OnClaimReply(LobbyApiReply<OperationResult> reply)
        {
            _busy = false;
            string inboxId = _lastClaimInboxId;
            if ((reply.IsSuccess) && (reply.Value.Status == ContractCodes.OperationStatus.COMMITTED))
            {
                ClaimCompleted?.Invoke(new InboxClaimResult(inboxId, true, ERejectReason.None, false));
                return;
            }

            // 응답을 받지 못했거나 서버가 일시 실패를 돌려주면 결과를 모른다. 같은 operationId로 다시 보낼 수 있다
            bool unknown = (!reply.IsSuccess) && (InboxRejectRules.IsUnknownOutcome(reply.StatusCode, reply.ReasonCode));
            ERejectReason reason = unknown
                ? ERejectReason.BackendUnavailable
                : InboxRejectRules.FromReply(reply.ReasonCode, reply.StatusCode);
            ClaimCompleted?.Invoke(new InboxClaimResult(inboxId, false, reason, unknown));
        }

        /****************************************
        *               Records
        ****************************************/

        // 코루틴을 돌리기 위한 보이지 않는 부품
        private sealed class InboxClientHost : MonoBehaviour { }
    }
}
