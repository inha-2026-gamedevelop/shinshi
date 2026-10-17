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
    /// <summary> 길드 창과 SocialClient를 잇는다. 길드 만들기, 가입 신청, 초대 수락과 거절, 신청 처리, 초대, 위임, 추방, 탈퇴를 보낸다 </summary>
    public sealed class GuildBinder : MonoBehaviour
    {
        // 길드원 판정, 정원, 길드장 검사는 마을 서버와 API가 한다. 여기는 길드장 전용 버튼을 보일지와 확인 글자만 정한다.
        // 탈퇴, 추방, 위임은 화면에 보인 가입 ID와 길드장 버전을 그대로 보내 늦게 도착한 재전송이 새 상태를 건드리지 못하게 한다

        /****************************************
        *                Fields
        ****************************************/

        private const int    BUTTON_FIRST     = 0;
        private const int    BUTTON_SECOND    = 1;
        private const int    BUTTON_THIRD     = 2;
        private const string INFO_SEPARATOR   = " ";
        private const string INVITES_TITLE    = "받은 길드 초대";
        private const string ACCEPT_LABEL     = "수락";
        private const string DECLINE_LABEL    = "거절";
        private const string DELEGATE_LABEL   = "위임";
        private const string KICK_LABEL       = "추방";
        private const string INVITER_FORMAT   = "초대: {0}";
        private const string EMPTY_INVITES    = "받은 길드 초대가 없습니다. 길드를 만들거나 이름으로 가입을 신청하세요.";
        private const string EMPTY_APPLY      = "가입 신청이 없습니다.";
        private const string EMPTY_MEMBERS    = "길드원 목록을 불러오는 중입니다.";
        private const string DECLINE_INVITE   = "{0} 길드의 초대를 거절할까요?";
        private const string DECLINE_APPLY    = "{0}님의 가입 신청을 거절할까요?";
        private const string DELEGATE_CONFIRM = "{0}님에게 길드장을 넘길까요?";
        private const string KICK_CONFIRM     = "{0}님을 길드에서 추방할까요?";

        [SerializeField] private GuildView          _view   = null;
        [SerializeField] private HotkeyWindowToggle _toggle = null; // 창의 닫기 버튼도 단축키와 같은 닫기 경로를 쓴다

        private readonly List<SocialGuildMemberEntry> _members      = new List<SocialGuildMemberEntry>(SocialLimits.MAX_SNAPSHOT_MEMBERS);
        private readonly List<SocialRequestEntry>     _applications = new List<SocialRequestEntry>(SocialLimits.MAX_SNAPSHOT_APPLICATIONS);
        private readonly List<SocialGuildInviteEntry> _invites      = new List<SocialGuildInviteEntry>(SocialLimits.MAX_SNAPSHOT_INVITES);

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
            if (_view == null)
            {
                return;
            }

            _view.CloseRequested   += OnCloseRequested;
            _view.CreateSubmitted  += OnCreateSubmitted;
            _view.ApplySubmitted   += OnApplySubmitted;
            _view.InviteSubmitted  += OnInviteSubmitted;
            _view.LeaveRequested   += OnLeaveRequested;
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
            if (_view == null)
            {
                return;
            }

            _view.CloseRequested   -= OnCloseRequested;
            _view.CreateSubmitted  -= OnCreateSubmitted;
            _view.ApplySubmitted   -= OnApplySubmitted;
            _view.InviteSubmitted  -= OnInviteSubmitted;
            _view.LeaveRequested   -= OnLeaveRequested;
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
            _tab         = Mathf.Clamp(tab, 0, GuildViewRules.TAB_COUNT - 1);
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
            _members.Clear();
            _members.AddRange(snapshot.Members);
            GuildViewRules.SortMembers(_members);
            _applications.Clear();
            _applications.AddRange(snapshot.GuildApplications);
            _invites.Clear();
            _invites.AddRange(snapshot.GuildInvites);
            if (!GuildViewRules.ShowsLeaderTools(snapshot))
            {
                _tab = GuildViewRules.TAB_MEMBERS;
            }
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

            if (!SocialViewRules.IsGuildAction(result.Action))
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

            if (result.IsAccepted)
            {
                _view.ClearInputs();
            }

            _isDirty = true;
        }

        private void OnStateChanged()
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
            if ((tab < 0) || (tab >= GuildViewRules.TAB_COUNT))
            {
                return;
            }

            if ((tab == GuildViewRules.TAB_APPLICATIONS) && ((!_hasSnapshot) || (!GuildViewRules.ShowsLeaderTools(_snapshot))))
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

        private void OnCreateSubmitted(string input)
        {
            if (!CanWrite())
            {
                return;
            }

            if (!GuildViewRules.TryNormalizeGuildName(input, out string name))
            {
                _status = SocialViewRules.INVALID_GUILD_TEXT;
                Apply();
                return;
            }

            Begin(SocialClient.CreateGuild(name));
        }

        private void OnApplySubmitted(string input)
        {
            if (!CanWrite())
            {
                return;
            }

            if (!GuildViewRules.TryNormalizeGuildName(input, out string name))
            {
                _status = SocialViewRules.INVALID_GUILD_TEXT;
                Apply();
                return;
            }

            Begin(SocialClient.ApplyGuild(name));
        }

        private void OnInviteSubmitted(string input)
        {
            if ((!CanWrite()) || (!GuildViewRules.ShowsLeaderTools(_snapshot)))
            {
                return;
            }

            if (!CharacterNameRules.TryNormalize(input, out string name, out _))
            {
                _status = SocialViewRules.INVALID_NAME_TEXT;
                Apply();
                return;
            }

            Begin(SocialClient.InviteToGuild(name));
        }

        private void OnLeaveRequested()
        {
            if ((!CanWrite()) || (!_snapshot.Guild.HasGuild))
            {
                return;
            }

            if (GuildViewRules.MustDelegateBeforeLeave(_snapshot))
            {
                _status = GuildViewRules.MustDelegateText();
                Apply();
                return;
            }

            Confirm(GuildViewRules.LeaveConfirmText(_snapshot), () => Begin(SocialClient.LeaveGuild()));
        }

        private void OnRowAction(int row, int button)
        {
            if (!CanWrite())
            {
                return;
            }

            int index = (_page * GuildViewRules.ROWS_PER_PAGE) + row;
            if (GuildViewRules.ModeOf(_snapshot) == EGuildWindowMode.NoGuild)
            {
                OnInviteAction(index, button);
                return;
            }

            if (_tab == GuildViewRules.TAB_APPLICATIONS)
            {
                OnApplicationAction(index, button);
                return;
            }

            OnMemberAction(index, button);
        }

        private void OnInviteAction(int index, int button)
        {
            if ((index < 0) || (index >= _invites.Count))
            {
                return;
            }

            SocialGuildInviteEntry invite  = _invites[index];
            string                 guildId = invite.GuildId;
            if (button == BUTTON_FIRST)
            {
                Begin(SocialClient.AcceptGuildInvite(guildId));
                return;
            }

            if (button == BUTTON_SECOND)
            {
                Confirm(string.Format(DECLINE_INVITE, invite.GuildName), () => Begin(SocialClient.DeclineGuildInvite(guildId)));
            }
        }

        private void OnApplicationAction(int index, int button)
        {
            if ((index < 0) || (index >= _applications.Count))
            {
                return;
            }

            SocialRequestEntry application = _applications[index];
            string             id          = application.CharacterId;
            if (button == BUTTON_FIRST)
            {
                Begin(SocialClient.AcceptApplication(id));
                return;
            }

            if (button == BUTTON_SECOND)
            {
                Confirm(string.Format(DECLINE_APPLY, application.Name), () => Begin(SocialClient.DeclineApplication(id)));
            }
        }

        private void OnMemberAction(int index, int button)
        {
            if ((index < 0) || (index >= _members.Count))
            {
                return;
            }

            SocialGuildMemberEntry member = _members[index];
            if (!GuildViewRules.CanManage(_snapshot, member))
            {
                return;
            }

            if (button == BUTTON_FIRST)
            {
                Confirm(string.Format(DELEGATE_CONFIRM, member.Name), () => Begin(SocialClient.DelegateLeader(member)));
                return;
            }

            if (button == BUTTON_SECOND)
            {
                Confirm(string.Format(KICK_CONFIRM, member.Name), () => Begin(SocialClient.KickMember(member)));
            }
        }

        private void Confirm(string text, Action action)
        {
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

        private int CurrentCount()
        {
            if ((!_hasSnapshot) || (GuildViewRules.ModeOf(_snapshot) == EGuildWindowMode.NoGuild))
            {
                return _invites.Count;
            }

            return (_tab == GuildViewRules.TAB_APPLICATIONS) ? _applications.Count : _members.Count;
        }

        private void Apply()
        {
            if (_view == null)
            {
                return;
            }

            bool             writable = CanWrite();
            EGuildWindowMode mode     = _hasSnapshot ? GuildViewRules.ModeOf(_snapshot) : EGuildWindowMode.NoGuild;
            bool             leader   = (_hasSnapshot) && (GuildViewRules.ShowsLeaderTools(_snapshot));
            string           count    = (mode == EGuildWindowMode.Guild)
                ? FriendsViewRules.CountText(_snapshot.Guild.MemberCount, SocialLimits.MAX_GUILD_MEMBERS)
                : string.Empty;
            _view.SetHeader(_hasSnapshot ? GuildViewRules.TitleText(_snapshot) : GuildViewRules.TitleText(default), count);
            _view.SetMode(mode, leader);
            _view.SetWritable(writable);
            _view.SetListTitle((mode == EGuildWindowMode.NoGuild) ? INVITES_TITLE : string.Empty);
            for (int i = 0; i < GuildViewRules.TAB_COUNT; ++i)
            {
                _view.SetTab(i, GuildViewRules.TabLabel(i, _applications.Count), i == _tab);
            }

            int total = CurrentCount();
            _page = FriendsViewRules.ClampPage(_page, total);
            int first = _page * GuildViewRules.ROWS_PER_PAGE;
            for (int row = 0; row < _view.RowCount; ++row)
            {
                ApplyRow(_view.Row(row), first + row, mode, writable);
            }

            _view.SetEmpty(EmptyText(mode, total));
            _view.SetPage(FriendsViewRules.PageText(_page, total), _page > 0, _page < (FriendsViewRules.PageCount(total) - 1));
            _view.SetStatus(StatusText());
        }

        private void ApplyRow(SocialRowView row, int index, EGuildWindowMode mode, bool writable)
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

            row.SetButton(BUTTON_THIRD, null, false);
            long now = _snapshot.ServerTimeUnixMs;
            if (mode == EGuildWindowMode.NoGuild)
            {
                SocialGuildInviteEntry invite  = _invites[index];
                string                 inviter = string.IsNullOrEmpty(invite.InviterName) ? SocialViewRules.UNKNOWN_NAME : invite.InviterName;
                row.SetTexts(invite.GuildName, string.Format(INVITER_FORMAT, inviter),
                    FriendsViewRules.RemainingText(invite.ExpiresAtUnixMs, now));
                row.SetPresence(false, EPresence.Offline);
                row.SetButton(BUTTON_FIRST, ACCEPT_LABEL, writable);
                row.SetButton(BUTTON_SECOND, DECLINE_LABEL, writable);
                return;
            }

            if (_tab == GuildViewRules.TAB_APPLICATIONS)
            {
                SocialRequestEntry application = _applications[index];
                row.SetTexts(application.Name, LobbyText.ClassName(application.ClassId),
                    FriendsViewRules.RemainingText(application.ExpiresAtUnixMs, now));
                row.SetPresence(false, EPresence.Offline);
                row.SetButton(BUTTON_FIRST, ACCEPT_LABEL, writable);
                row.SetButton(BUTTON_SECOND, DECLINE_LABEL, writable);
                return;
            }

            SocialGuildMemberEntry member = _members[index];
            bool                   manage = GuildViewRules.CanManage(_snapshot, member);
            string                 info   = FriendsViewRules.PresenceText(member.Presence) + INFO_SEPARATOR
                + GuildViewRules.RoleText(member.Role);
            row.SetTexts(member.Name, LobbyText.ClassName(member.ClassId), info.TrimEnd());
            row.SetPresence(true, member.Presence);
            row.SetButton(BUTTON_FIRST, manage ? DELEGATE_LABEL : null, writable);
            row.SetButton(BUTTON_SECOND, manage ? KICK_LABEL : null, writable);
        }

        private string EmptyText(EGuildWindowMode mode, int total)
        {
            if ((!_hasSnapshot) || (total > 0))
            {
                return string.Empty;
            }

            if (mode == EGuildWindowMode.NoGuild)
            {
                return EMPTY_INVITES;
            }

            return (_tab == GuildViewRules.TAB_APPLICATIONS) ? EMPTY_APPLY : EMPTY_MEMBERS;
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
