// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;
using Shinshi.Systems;

namespace Shinshi.UI
{
    /// <summary> 친구 창과 SocialClient를 잇는다. 열 때와 열린 동안 30초마다 목록을 받고, 요청, 수락, 거절, 취소, 삭제를 보낸다 </summary>
    public sealed class FriendsBinder : MonoBehaviour
    {
        // 관계 판정과 상한은 마을 서버와 API가 한다. 여기는 버튼 활성과 안내 글자만 정한다.
        // 던전에서는 관리 요청을 보내지 않고 마지막 목록과 안내만 보인다(채팅, 알림은 던전에서도 받는다)

        /****************************************
        *                Fields
        ****************************************/

        private const int    BUTTON_FIRST       = 0;
        private const int    BUTTON_SECOND      = 1;
        private const int    BUTTON_THIRD       = 2;
        private const string WHISPER_LABEL      = "귓속말";
        private const string PARTY_LABEL        = "파티 초대";
        private const string REMOVE_LABEL       = "삭제";
        private const string ACCEPT_LABEL       = "수락";
        private const string DECLINE_LABEL      = "거절";
        private const string CANCEL_LABEL       = "취소";
        private const string EMPTY_FRIENDS      = "아직 친구가 없습니다. 이름을 입력해 친구 요청을 보내 보세요.";
        private const string EMPTY_INCOMING     = "받은 친구 요청이 없습니다.";
        private const string EMPTY_OUTGOING     = "보낸 친구 요청이 없습니다.";
        private const string REMOVE_CONFIRM     = "{0}님을 친구에서 삭제할까요?";
        private const string DECLINE_CONFIRM    = "{0}님의 친구 요청을 거절할까요?";
        private const string PARTY_INVITED_TEXT = "{0}님에게 파티 초대를 보냈습니다.";

        [SerializeField] private FriendsView        _view   = null;
        [SerializeField] private HotkeyWindowToggle _toggle = null; // 창의 닫기 버튼도 단축키와 같은 닫기 경로를 쓴다
        [SerializeField] private HudChatView        _chat   = null; // 귓속말 버튼이 입력 칸에 "/w 이름 "을 채운다

        private readonly List<SocialFriendEntry>  _friends  = new List<SocialFriendEntry>(SocialLimits.MAX_SNAPSHOT_FRIENDS);
        private readonly List<SocialRequestEntry> _incoming = new List<SocialRequestEntry>(SocialLimits.MAX_SNAPSHOT_REQUESTS);
        private readonly List<SocialRequestEntry> _outgoing = new List<SocialRequestEntry>(SocialLimits.MAX_SNAPSHOT_REQUESTS);

        private SocialSnapshotMessage _snapshot;
        private bool                  _hasSnapshot;
        private bool                  _previewMode;
        private bool                  _wasOpen;
        private bool                  _isDirty;
        private bool                  _awaitingRefresh;
        private int                   _tab;
        private int                   _page;
        private float                 _nextRefresh;
        private string                _status = string.Empty;
        private Action                _confirmAction;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            SocialClient.SnapshotChanged += OnSnapshot;
            SocialClient.ResultReceived  += OnResult;
            SocialClient.StateChanged    += OnStateChanged;
            TownPartyClient.RoomChanged  += OnRoomChanged;
            if (_view == null)
            {
                return;
            }

            _view.CloseRequested   += OnCloseRequested;
            _view.RequestSubmitted += OnRequestSubmitted;
            _view.TabSelected      += OnTabSelected;
            _view.RowAction        += OnRowAction;
            _view.PageChanged      += OnPageChanged;
            _view.ConfirmAccepted  += OnConfirmAccepted;
            _view.ConfirmCancelled += OnConfirmCancelled;
        }

        private void OnDisable()
        {
            SocialClient.SnapshotChanged -= OnSnapshot;
            SocialClient.ResultReceived  -= OnResult;
            SocialClient.StateChanged    -= OnStateChanged;
            TownPartyClient.RoomChanged  -= OnRoomChanged;
            if (_view == null)
            {
                return;
            }

            _view.CloseRequested   -= OnCloseRequested;
            _view.RequestSubmitted -= OnRequestSubmitted;
            _view.TabSelected      -= OnTabSelected;
            _view.RowAction        -= OnRowAction;
            _view.PageChanged      -= OnPageChanged;
            _view.ConfirmAccepted  -= OnConfirmAccepted;
            _view.ConfirmCancelled -= OnConfirmCancelled;
        }

        private void Update()
        {
            if (_view == null)
            {
                return;
            }

            bool isOpen = _view.IsOpen;
            if ((isOpen) && (!_wasOpen))
            {
                OnOpened();
            }

            _wasOpen = isOpen;
            if (!isOpen)
            {
                return;
            }

            if ((!_previewMode) && (Time.unscaledTime >= _nextRefresh))
            {
                _nextRefresh = Time.unscaledTime + Constants.Social.SNAPSHOT_REFRESH_SECONDS;
                SocialClient.RequestSnapshot();
            }

            if (_isDirty)
            {
                _isDirty = false;
                Apply();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary> 서버 없이 가짜 목록으로 창을 그린다, 요청은 보내지 않는다 </summary>
        public void ApplyPreview(SocialSnapshotMessage snapshot, int tab)
        {
            if ((Application.isPlaying) && (NetworkClient.isConnected))
            {
                return;
            }

            _previewMode = true;
            _status      = SocialViewRules.PREVIEW_TEXT;
            _tab         = Mathf.Clamp(tab, 0, FriendsViewRules.TAB_COUNT - 1);
            _page        = 0;
            UseSnapshot(snapshot);
            Apply();
        }
#endif

        private void OnOpened()
        {
            _confirmAction = null;
            _view.ShowConfirm(null);
            if (!_previewMode)
            {
                _status      = string.Empty;
                _nextRefresh = Time.unscaledTime + Constants.Social.SNAPSHOT_REFRESH_SECONDS;
                SocialClient.RequestSnapshot();
                if (SocialClient.HasSnapshot)
                {
                    UseSnapshot(SocialClient.Snapshot);
                }
            }

            _isDirty = true;
        }

        private void UseSnapshot(in SocialSnapshotMessage snapshot)
        {
            _snapshot    = snapshot;
            _hasSnapshot = true;
            _friends.Clear();
            _friends.AddRange(snapshot.Friends);
            FriendsViewRules.SortFriends(_friends);
            _incoming.Clear();
            _incoming.AddRange(snapshot.Incoming);
            _outgoing.Clear();
            _outgoing.AddRange(snapshot.Outgoing);
        }

        private void OnSnapshot(SocialSnapshotMessage snapshot)
        {
            if (_previewMode)
            {
                return;
            }

            UseSnapshot(snapshot);
            if (_awaitingRefresh)
            {
                _awaitingRefresh = false;
                _status          = SocialViewRules.REFRESHED_TEXT;
            }

            _isDirty = true;
        }

        private void OnResult(SocialResultMessage result)
        {
            if (_previewMode)
            {
                return;
            }

            if ((result.Action == ESocialAction.Snapshot) && (result.Reason == ERejectReason.BackendUnavailable))
            {
                _awaitingRefresh = false;
                _status          = SocialViewRules.SNAPSHOT_FAILED_TEXT;
                _isDirty         = true;
                return;
            }

            if (!SocialViewRules.IsFriendAction(result.Action))
            {
                return;
            }

            if (result.Reason == ERejectReason.BackendUnavailable)
            {
                _awaitingRefresh = true;
                _status          = SocialViewRules.UNKNOWN_TEXT;
            }
            else
            {
                _status = result.IsAccepted ? SocialViewRules.SuccessText(result.Action) : SocialViewRules.Describe(result.Reason);
            }

            if ((result.IsAccepted) && (result.Action == ESocialAction.FriendRequest))
            {
                _view.ClearNameInput();
            }

            _isDirty = true;
        }

        private void OnStateChanged()
        {
            _isDirty = true;
        }

        private void OnRoomChanged(PartyRoomStateMessage room)
        {
            _isDirty = true;
        }

        private void OnCloseRequested()
        {
            if (_toggle != null)
            {
                _toggle.RequestClose();
                return;
            }

            _view.SetOpen(false);
        }

        private void OnTabSelected(int tab)
        {
            if ((tab < 0) || (tab >= FriendsViewRules.TAB_COUNT))
            {
                return;
            }

            _tab  = tab;
            _page = 0;
            _confirmAction = null;
            _view.ShowConfirm(null);
            Apply();
        }

        private void OnPageChanged(int delta)
        {
            _page = FriendsViewRules.ClampPage(_page + delta, CurrentCount());
            Apply();
        }

        private void OnRequestSubmitted(string input)
        {
            if (!CanWrite())
            {
                return;
            }

            if (!CharacterNameRules.TryNormalize(input, out string name, out _))
            {
                _status = SocialViewRules.INVALID_NAME_TEXT;
                Apply();
                return;
            }

            Begin(SocialClient.SendFriendRequest(name));
        }

        private void OnRowAction(int row, int button)
        {
            int index = (_page * FriendsViewRules.ROWS_PER_PAGE) + row;
            switch (_tab)
            {
                case FriendsViewRules.TAB_FRIENDS:
                    OnFriendAction(index, button);
                    break;
                case FriendsViewRules.TAB_INCOMING:
                    OnIncomingAction(index, button);
                    break;
                default:
                    OnOutgoingAction(index, button);
                    break;
            }
        }

        private void OnFriendAction(int index, int button)
        {
            if ((index < 0) || (index >= _friends.Count))
            {
                return;
            }

            SocialFriendEntry friend = _friends[index];
            switch (button)
            {
                case BUTTON_FIRST:
                    if ((_chat != null) && (FriendsViewRules.CanWhisper(friend.Presence)))
                    {
                        _chat.BeginWhisper(friend.Name);
                    }

                    break;
                case BUTTON_SECOND:
                    if ((!_previewMode) && (CanPartyInvite(in friend)) && (TownPartyClient.Invite(friend.Name)))
                    {
                        _status = string.Format(PARTY_INVITED_TEXT, friend.Name);
                        Apply();
                    }

                    break;
                case BUTTON_THIRD:
                    string id = friend.CharacterId;
                    Confirm(string.Format(REMOVE_CONFIRM, friend.Name), () => Begin(SocialClient.RemoveFriend(id)));
                    break;
                default:
                    break;
            }
        }

        private void OnIncomingAction(int index, int button)
        {
            if ((index < 0) || (index >= _incoming.Count) || (!CanWrite()))
            {
                return;
            }

            SocialRequestEntry request = _incoming[index];
            string             id      = request.CharacterId;
            if (button == BUTTON_FIRST)
            {
                Begin(SocialClient.AcceptFriend(id));
                return;
            }

            if (button == BUTTON_SECOND)
            {
                Confirm(string.Format(DECLINE_CONFIRM, request.Name), () => Begin(SocialClient.DeclineFriend(id)));
            }
        }

        private void OnOutgoingAction(int index, int button)
        {
            if ((index < 0) || (index >= _outgoing.Count) || (button != BUTTON_FIRST) || (!CanWrite()))
            {
                return;
            }

            Begin(SocialClient.CancelFriendRequest(_outgoing[index].CharacterId));
        }

        private void Confirm(string text, Action action)
        {
            if (!CanWrite())
            {
                return;
            }

            _confirmAction = action;
            _view.ShowConfirm(text);
        }

        private void OnConfirmAccepted()
        {
            Action action = _confirmAction;
            _confirmAction = null;
            _view.ShowConfirm(null);
            if ((action != null) && (CanWrite()))
            {
                action();
            }
        }

        private void OnConfirmCancelled()
        {
            _confirmAction = null;
            _view.ShowConfirm(null);
        }

        private void Begin(Guid requestId)
        {
            if (requestId != Guid.Empty)
            {
                _status = SocialViewRules.WAITING_TEXT;
            }

            Apply();
        }

        private bool CanWrite()
        {
            return (!_previewMode) && (SocialClient.IsTownConnection()) && (!SocialClient.HasPendingWrite)
                && (!SocialClient.IsSnapshotFailed) && (_hasSnapshot);
        }

        private bool CanPartyInvite(in SocialFriendEntry friend)
        {
            PartyRoomStateMessage room = TownPartyClient.Room;
            return FriendsViewRules.CanPartyInvite(friend.Presence, friend.SameServer, room.InRoom, room.Phase);
        }

        private int CurrentCount()
        {
            switch (_tab)
            {
                case FriendsViewRules.TAB_INCOMING:
                    return _incoming.Count;
                case FriendsViewRules.TAB_OUTGOING:
                    return _outgoing.Count;
                default:
                    return _friends.Count;
            }
        }

        private void Apply()
        {
            if (_view == null)
            {
                return;
            }

            bool writable = CanWrite();
            _view.SetCount(FriendsViewRules.CountText(_friends.Count, SocialLimits.MAX_FRIENDS));
            _view.SetRequestInteractable(writable);
            for (int i = 0; i < FriendsViewRules.TAB_COUNT; ++i)
            {
                _view.SetTab(i, FriendsViewRules.TabLabel(i, _incoming.Count), i == _tab);
            }

            int count = CurrentCount();
            _page = FriendsViewRules.ClampPage(_page, count);
            int first = _page * FriendsViewRules.ROWS_PER_PAGE;
            for (int row = 0; row < _view.RowCount; ++row)
            {
                ApplyRow(_view.Row(row), first + row, writable);
            }

            _view.SetEmpty(EmptyText(count));
            _view.SetPage(FriendsViewRules.PageText(_page, count), _page > 0,
                _page < (FriendsViewRules.PageCount(count) - 1));
            _view.SetStatus(StatusText());
        }

        private void ApplyRow(SocialRowView row, int index, bool writable)
        {
            if (row == null)
            {
                return;
            }

            bool visible = (_hasSnapshot) && (index < CurrentCount());
            row.SetVisible(visible);
            if (!visible)
            {
                return;
            }

            long now = _snapshot.ServerTimeUnixMs;
            switch (_tab)
            {
                case FriendsViewRules.TAB_FRIENDS:
                    SocialFriendEntry friend = _friends[index];
                    row.SetTexts(friend.Name, LobbyText.ClassName(friend.ClassId), FriendsViewRules.PresenceText(friend.Presence));
                    row.SetPresence(true, friend.Presence);
                    row.SetButton(BUTTON_FIRST, WHISPER_LABEL, (_chat != null) && (FriendsViewRules.CanWhisper(friend.Presence)));
                    row.SetButton(BUTTON_SECOND, PARTY_LABEL, (!_previewMode) && (CanPartyInvite(in friend)));
                    row.SetButton(BUTTON_THIRD, REMOVE_LABEL, writable);
                    break;
                case FriendsViewRules.TAB_INCOMING:
                    SocialRequestEntry incoming = _incoming[index];
                    row.SetTexts(incoming.Name, LobbyText.ClassName(incoming.ClassId),
                        FriendsViewRules.RemainingText(incoming.ExpiresAtUnixMs, now));
                    row.SetPresence(false, EPresence.Offline);
                    row.SetButton(BUTTON_FIRST, ACCEPT_LABEL, writable);
                    row.SetButton(BUTTON_SECOND, DECLINE_LABEL, writable);
                    row.SetButton(BUTTON_THIRD, null, false);
                    break;
                default:
                    SocialRequestEntry outgoing = _outgoing[index];
                    row.SetTexts(outgoing.Name, LobbyText.ClassName(outgoing.ClassId),
                        FriendsViewRules.RemainingText(outgoing.ExpiresAtUnixMs, now));
                    row.SetPresence(false, EPresence.Offline);
                    row.SetButton(BUTTON_FIRST, CANCEL_LABEL, writable);
                    row.SetButton(BUTTON_SECOND, null, false);
                    row.SetButton(BUTTON_THIRD, null, false);
                    break;
            }
        }

        private string EmptyText(int count)
        {
            if ((!_hasSnapshot) || (count > 0))
            {
                return string.Empty;
            }

            switch (_tab)
            {
                case FriendsViewRules.TAB_INCOMING:
                    return EMPTY_INCOMING;
                case FriendsViewRules.TAB_OUTGOING:
                    return EMPTY_OUTGOING;
                default:
                    return EMPTY_FRIENDS;
            }
        }

        // 서버 상태 안내가 요청 결과 안내보다 앞선다. 연결이 없거나 던전이면 결과 안내 대신 그 이유를 보인다
        private string StatusText()
        {
            if (_previewMode)
            {
                return _status;
            }

            if (ClientRoles.IsDungeon())
            {
                return SocialViewRules.DUNGEON_TEXT;
            }

            if (!SocialClient.IsTownConnection())
            {
                return SocialViewRules.OFFLINE_TEXT;
            }

            if (SocialClient.IsSnapshotFailed)
            {
                return SocialViewRules.SNAPSHOT_FAILED_TEXT;
            }

            if (!_hasSnapshot)
            {
                return SocialViewRules.LOADING_TEXT;
            }

            return SocialClient.HasPendingWrite ? SocialViewRules.WAITING_TEXT : _status;
        }
    }
}
