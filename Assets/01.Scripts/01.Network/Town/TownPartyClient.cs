// System
using System;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 파티 화면(UI 담당)은 이 창구의 이벤트만 구독하고 요청 메서드로 보낸다. 판정은 모두 마을 서버가 한다.
    // 요청 메서드는 연결되지 않았으면 거짓을 돌려주고 아무것도 보내지 않는다
    /// <summary> 클라이언트의 마을 파티 요청 송신과 결과 수신 창구 </summary>
    public static class TownPartyClient
    {
        /****************************************
        *                Fields
        ****************************************/

        private static PartyRoomStateMessage _room;

        // 연결을 다시 맺어도 되돌리지 않는다. 이전 연결의 늦은 응답이 새 요청의 ID와 겹치지 않게 한다
        private static uint _lastRequestId = 0;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 마지막으로 받은 내 방 상태, 방이 없으면 InRoom이 거짓이다 </summary>
        public static PartyRoomStateMessage Room => _room;

        public static event Action<PartyRoomListMessage>  RoomListReceived;
        public static event Action<PartyRoomStateMessage> RoomChanged;
        public static event Action<PartyInviteMessage>    InviteReceived;
        public static event Action<PartyResultMessage>    ResultReceived;

        // 받으면 ShinshiNetworkManager가 SortieDepartFlow로 마을을 끊고 던전에 접속한다. 결과는 매니저의 SortieDepartFinished로 온다
        public static event Action<SortieTicketMessage>   SortieTicketReceived;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 클라이언트가 시작할 때 한 번 부른다, 이전 연결의 상태를 비운다 </summary>
        public static void RegisterHandlers()
        {
            _room = PartyRoomStateMessage.Left();
            NetworkClient.ReplaceHandler<PartyRoomListMessage>(OnRoomList);
            NetworkClient.ReplaceHandler<PartyRoomStateMessage>(OnRoomState);
            NetworkClient.ReplaceHandler<PartyInviteMessage>(OnInvite);
            NetworkClient.ReplaceHandler<PartyResultMessage>(OnResult);
            NetworkClient.ReplaceHandler<SortieTicketMessage>(OnSortieTicket);
        }

        public static bool RequestRoomList()
        {
            return Send(new PartyRequestMessage(EPartyAction.ListRooms, 0, EPartyMode.Normal, null, null, null, false, 0, 0));
        }

        /// <summary> contentId는 일반전이면 지역 ID, 보스전이면 보스전 정의 ID. 비밀번호가 없으면 null </summary>
        public static bool CreateRoom(string roomName, EPartyMode mode, string contentId, string password)
        {
            return Send(new PartyRequestMessage(EPartyAction.CreateRoom, 0, mode, contentId, roomName, password, false, 0, 0));
        }

        public static bool JoinRoom(int roomId, string password)
        {
            return Send(new PartyRequestMessage(EPartyAction.JoinRoom, roomId, EPartyMode.Normal, null, null, password, false, 0, 0));
        }

        public static bool LeaveRoom()
        {
            return Send(new PartyRequestMessage(EPartyAction.LeaveRoom, 0, EPartyMode.Normal, null, null, null, false, 0, 0));
        }

        /// <summary> 준비하면 서버가 티켓과 지참 물약을 확인한다, 결과는 방 상태의 준비 표시로 온다 </summary>
        public static bool SetReady(bool ready, int smallPotions, int largePotions)
        {
            return Send(new PartyRequestMessage(EPartyAction.SetReady, 0, EPartyMode.Normal, null, null, null, ready, smallPotions,
                largePotions));
        }

        public static bool Invite(string characterName)
        {
            return Invite(characterName, out uint _);
        }

        /// <summary> 응답과 맞춰 볼 0이 아닌 요청 ID를 발급해 보낸다. 보내지 못해도 발급한 ID를 돌려준다 </summary>
        public static bool Invite(string characterName, out uint requestId)
        {
            requestId = ReserveRequestId();
            return InviteWithId(characterName, requestId);
        }

        /// <summary> 단조 증가하는 0이 아닌 요청 ID를 발급한다. 화면이 응답 전에 대기 상태를 먼저 걸 때 쓴다 </summary>
        public static uint ReserveRequestId()
        {
            ++_lastRequestId;
            if (_lastRequestId == 0)
            {
                ++_lastRequestId;
            }

            return _lastRequestId;
        }

        /// <summary> ReserveRequestId로 받은 ID를 실어 초대를 보낸다 </summary>
        public static bool InviteWithId(string characterName, uint requestId)
        {
            return Send(new PartyRequestMessage(EPartyAction.Invite, 0, EPartyMode.Normal, null, characterName, null, false, 0, 0,
                requestId));
        }

        public static bool AcceptInvite(int roomId)
        {
            return Send(new PartyRequestMessage(EPartyAction.AcceptInvite, roomId, EPartyMode.Normal, null, null, null, false, 0, 0));
        }

        public static bool DeclineInvite(int roomId)
        {
            return Send(new PartyRequestMessage(EPartyAction.DeclineInvite, roomId, EPartyMode.Normal, null, null, null, false, 0,
                0));
        }

        /// <summary> 방장만, 전원 준비면 카운트다운 뒤 예약한다 </summary>
        public static bool StartSortie()
        {
            return Send(new PartyRequestMessage(EPartyAction.StartSortie, 0, EPartyMode.Normal, null, null, null, false, 0, 0));
        }

        /// <summary> 방장만, 카운트다운이나 대기열에서 빠져 모집으로 돌아간다 </summary>
        public static bool CancelSortie()
        {
            return Send(new PartyRequestMessage(EPartyAction.CancelSortie, 0, EPartyMode.Normal, null, null, null, false, 0, 0));
        }

        private static bool Send(PartyRequestMessage message)
        {
            if (!NetworkClient.isConnected)
            {
                return false;
            }

            NetworkClient.Send(message);
            return true;
        }

        private static void OnRoomList(PartyRoomListMessage message)
        {
            RoomListReceived?.Invoke(message);
        }

        private static void OnRoomState(PartyRoomStateMessage message)
        {
            _room = message;
            RoomChanged?.Invoke(message);
        }

        private static void OnInvite(PartyInviteMessage message)
        {
            InviteReceived?.Invoke(message);
        }

        private static void OnResult(PartyResultMessage message)
        {
            ResultReceived?.Invoke(message);
        }

        private static void OnSortieTicket(SortieTicketMessage message)
        {
            SortieTicketReceived?.Invoke(message);
        }
    }
}
