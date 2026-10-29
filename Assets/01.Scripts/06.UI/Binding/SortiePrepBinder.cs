// System
using System;
using System.Collections.Generic;
using System.Globalization;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Network;
using Shinshi.Systems;

namespace Shinshi.UI
{
    /// <summary> 출정 준비 창을 마을 파티 방에 묶는다. 단축키로 열면 방이 없을 때 보스전 방을 만든다 </summary>
    // 서버는 방장까지 모두 준비해야 출정을 받는다. 방장의 출정 버튼은 자기 준비를 먼저 보내고 전원이 준비되면 출정을 보낸다.
    // 난이도는 방을 만들 때 정해져 서버에 바꾸는 요청이 없다. 혼자 있는 방장만 방을 나갔다가 다른 난이도로 다시 만든다
    public sealed class SortiePrepBinder : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float  REFRESH_INTERVAL_SECONDS = 0.25f;
        private const string ROOM_NAME_FORMAT         = "{0}의 파티";
        private const string DEFAULT_ROOM_NAME        = "파티";
        private const float  INVITE_TIMEOUT_SECONDS   = 8f;
        private const string INVITE_TIMEOUT_TEXT      = "초대 응답이 없다. 잠시 뒤 다시 시도해라.";

        [SerializeField] private HotkeyRouter   _router = null;
        [SerializeField] private GameObject     _window = null;
        [SerializeField] private SortiePrepView _view   = null;

        private readonly List<SortieMemberData> _members = new List<SortieMemberData>(Constants.Session.MAX_PARTY_SIZE);

        private Func<bool> _onToggle = null;
        private Func<bool> _onClose  = null;

        private EBossDifficulty _difficulty          = EBossDifficulty.Easy;
        private bool            _wasInRoom           = false;
        private bool            _startAfterReady     = false;
        private bool            _recreatePending     = false;
        private string          _recreateBattleId    = null;
        private bool            _createPending       = false;
        private float           _countdownReceivedAt = 0f;
        private float           _nextRefreshAt       = 0f;
        private readonly PartyInviteTracker _invite  = new PartyInviteTracker();

        /****************************************
        *              Properties
        ****************************************/

        public bool IsOpen => (_window != null) && (_window.activeSelf);

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _onToggle = OnToggleHotkey;
            _onClose  = OnCloseHotkey;
        }

        private void OnEnable()
        {
            if (_router != null)
            {
                _router.Register(EHotkeyAction.SortiePrep, _onToggle);
            }

            TownPartyClient.RoomChanged    += OnRoomChanged;
            TownPartyClient.ResultReceived += OnPartyResult;
            if (_view != null)
            {
                _view.ReadyClicked      += OnReadyClicked;
                _view.StartClicked      += OnStartClicked;
                _view.LeaveClicked      += OnLeaveClicked;
                _view.DifficultyChanged += OnDifficultyChanged;
                _view.InviteClicked     += OnInviteClicked;
            }
        }

        private void OnDisable()
        {
            if (_router != null)
            {
                _router.Unregister(EHotkeyAction.SortiePrep, _onToggle);
                _router.Unregister(EHotkeyAction.CloseWindow, _onClose);
            }

            ResetInvite();
            TownPartyClient.RoomChanged    -= OnRoomChanged;
            TownPartyClient.ResultReceived -= OnPartyResult;
            if (_view != null)
            {
                _view.ReadyClicked      -= OnReadyClicked;
                _view.StartClicked      -= OnStartClicked;
                _view.LeaveClicked      -= OnLeaveClicked;
                _view.DifficultyChanged -= OnDifficultyChanged;
                _view.InviteClicked     -= OnInviteClicked;
            }
        }

        // 카운트다운 숫자만 주기적으로 다시 쓴다
        private void Update()
        {
            ExpireInvite();
            if ((!IsOpen) || (Time.unscaledTime < _nextRefreshAt))
            {
                return;
            }

            _nextRefreshAt = Time.unscaledTime + REFRESH_INTERVAL_SECONDS;
            ApplyCountdown(TownPartyClient.Room);
        }

        /****************************************
        *                Methods
        ****************************************/

        public void Open()
        {
            if ((_window == null) || (_view == null))
            {
                return;
            }

            bool wasOpen = _window.activeSelf;
            _window.SetActive(true);
            if (!wasOpen)
            {
                UiSound.Play(EUiSoundGroup.Common, EUiSoundAction.Open);
            }
            if (_router != null)
            {
                _router.Register(EHotkeyAction.CloseWindow, _onClose);
            }

            if (!TownPartyClient.Room.InRoom)
            {
                CreateRoom(_difficulty);
            }

            Refresh();
        }

        public void Close()
        {
            ResetInvite();
            if ((_window != null) && (_window.activeSelf))
            {
                _window.SetActive(false);
                UiSound.Play(EUiSoundGroup.Common, EUiSoundAction.Close);
            }

            if (_router != null)
            {
                _router.Unregister(EHotkeyAction.CloseWindow, _onClose);
            }
        }

        private bool OnToggleHotkey()
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }

            return true;
        }

        private bool OnCloseHotkey()
        {
            if (!IsOpen)
            {
                _router.Unregister(EHotkeyAction.CloseWindow, _onClose);
                return false;
            }

            Close();
            return true;
        }

        // battleId가 있으면 그 보스전으로, 없으면 난이도의 첫 선택 가능 보스전으로 방을 만든다. 실패하면 대기를 풀고 화면을 갱신한다
        private bool CreateRoom(EBossDifficulty difficulty, string battleId = null)
        {
            if (_createPending)
            {
                return true;
            }

            ProgressionCatalogSO.BossBattleDefinition battle = null;
            if (!ClientRoles.IsTown())
            {
                FailCreate("마을에서만 출정을 준비할 수 있다.");
                return false;
            }

            bool found = GameDB.IsInitialized
                && ((string.IsNullOrEmpty(battleId))
                    ? SortiePrepRules.TrySelectBattle(GameDB.Progression, difficulty, out battle)
                    : (GameDB.Progression.TryGetBattle(battleId, out battle)));
            if (!found)
            {
                FailCreate("열 수 있는 보스전이 없다.");
                return false;
            }

            string localName = ClientRoles.LocalCharacterName();
            string roomName  = string.IsNullOrEmpty(localName)
                ? DEFAULT_ROOM_NAME
                : string.Format(CultureInfo.InvariantCulture, ROOM_NAME_FORMAT, localName);
            if (!TownPartyClient.CreateRoom(roomName, EPartyMode.Boss, battle.DefinitionId, null))
            {
                _view.SetNotice(ERejectReason.BackendUnavailable);
                FailCreate(null);
                return false;
            }

            _createPending = true;
            _view.SetNotice("파티 방을 만드는 중이다.");
            Refresh();
            return true;
        }

        private void FailCreate(string notice)
        {
            _createPending    = false;
            _recreatePending  = false;
            _recreateBattleId = null;
            if (notice != null)
            {
                _view.SetNotice(notice);
            }

            Refresh();
        }

        private void OnRoomChanged(PartyRoomStateMessage room)
        {
            SortieContentMemory.Remember(in room);
            if ((room.InRoom) && (room.Phase == EPartyPhase.Countdown))
            {
                _countdownReceivedAt = Time.unscaledTime;
            }

            // 초대를 받아 들어간 방은 창을 띄워 보여 준다
            if ((room.InRoom) && (!_wasInRoom) && (!IsOpen))
            {
                Open();
            }

            if ((!room.InRoom) && (_recreatePending))
            {
                string battleId = _recreateBattleId;
                _recreatePending  = false;
                _recreateBattleId = null;
                CreateRoom(_difficulty, battleId);
            }

            if ((room.InRoom) && (_createPending))
            {
                _createPending = false;
                if (_view != null)
                {
                    _view.SetNotice(string.Empty);
                }
            }

            if (!room.InRoom)
            {
                ResetInvite();
            }

            _wasInRoom = room.InRoom;
            TryStartAfterReady(in room);
            Refresh();
        }

        private void Refresh()
        {
            if ((!IsOpen) || (_view == null))
            {
                return;
            }

            PartyRoomStateMessage room = TownPartyClient.Room;
            SortiePrepRules.BuildMembers(in room, _members);
            _view.SetMembers(_members);
            bool pending = _createPending || _recreatePending;
            _view.SetRoomControls(room.InRoom, pending);
            if (!room.InRoom)
            {
                ResetInvite();
                _view.SetInviteState(false, false);
                _view.SetLocalState(false, false);
                _view.SetStartEnabled(false);
                _view.SetCountdown(0);
                if (pending)
                {
                    _view.SetNotice(_recreatePending ? "난이도를 바꾸려고 방을 다시 만드는 중이다." : "파티 방을 만드는 중이다.");
                }

                return;
            }

            // 카오스 던전(일반전) 방은 난이도가 없어 난이도 선택을 숨긴다
            _difficulty = SortiePrepRules.ToDifficulty(room.Difficulty);
            _view.SetDungeon(DungeonEntryRules.ContentName(GameDB.IsInitialized ? GameDB.Progression : null, room.Mode, room.ContentId),
                room.Name, null);
            _view.SetDifficulty(_difficulty);
            _view.SetDifficultyVisible(room.Mode == EPartyMode.Boss);

            bool isLeader = false;
            bool isReady  = false;
            if (SortiePrepRules.TryFindLocal(in room, ClientRoles.LocalCharacterName(), out PartyMemberView local))
            {
                isLeader = local.ParticipantId == room.LeaderParticipantId;
                isReady  = local.Ready == EPartyReadyState.Ready;
            }

            _view.SetLocalState(isLeader, isReady);
            _view.SetInviteState(PartyInviteRules.CanUse(in room, ClientRoles.LocalCharacterName()), !_invite.IsPending);
            _view.SetStartEnabled((isLeader) && (room.Phase == EPartyPhase.Open) && (SortiePrepRules.AreGuestsReady(in room)));
            ApplyCountdown(room);
            ApplyPhaseNotice(room.Phase);
        }

        private void ApplyCountdown(in PartyRoomStateMessage room)
        {
            if ((_view == null) || (!room.InRoom))
            {
                return;
            }

            if (room.Phase != EPartyPhase.Countdown)
            {
                _view.SetCountdown(0);
                return;
            }

            float remaining = room.CountdownRemaining - (Time.unscaledTime - _countdownReceivedAt);
            _view.SetCountdown(Mathf.Max(0, Mathf.CeilToInt(remaining)));
        }

        private void ApplyPhaseNotice(EPartyPhase phase)
        {
            switch (phase)
            {
                case EPartyPhase.Reserving:
                    _view.SetNotice("던전 자리를 예약하는 중이다.");
                    break;
                case EPartyPhase.Queued:
                    _view.SetNotice("던전 자리가 나기를 기다리는 중이다.");
                    break;
                case EPartyPhase.Departing:
                    _view.SetNotice("던전으로 이동하는 중이다.");
                    break;
                default:
                    break;
            }
        }

        private void OnReadyClicked()
        {
            PartyRoomStateMessage room = TownPartyClient.Room;
            bool isReady = (SortiePrepRules.TryFindLocal(in room, ClientRoles.LocalCharacterName(), out PartyMemberView local))
                && (local.Ready == EPartyReadyState.Ready);
            SendReady(!isReady);
        }

        // 카운트다운이나 대기열이면 취소한다. 모집 중이면 자기 준비를 보내고 전원이 준비되면 출정을 보낸다
        private void OnStartClicked()
        {
            PartyRoomStateMessage room = TownPartyClient.Room;
            if (!room.InRoom)
            {
                return;
            }

            if ((room.Phase == EPartyPhase.Countdown) || (room.Phase == EPartyPhase.Queued))
            {
                TownPartyClient.CancelSortie();
                return;
            }

            if (SortiePrepRules.AreAllReady(in room))
            {
                TownPartyClient.StartSortie();
                return;
            }

            _startAfterReady = true;
            SendReady(true);
        }

        private void TryStartAfterReady(in PartyRoomStateMessage room)
        {
            if (!_startAfterReady)
            {
                return;
            }

            if ((!room.InRoom) || (room.Phase != EPartyPhase.Open))
            {
                _startAfterReady = false;
                return;
            }

            if (SortiePrepRules.AreAllReady(in room))
            {
                _startAfterReady = false;
                TownPartyClient.StartSortie();
            }
        }

        private void OnLeaveClicked()
        {
            _startAfterReady  = false;
            _recreatePending  = false;
            _recreateBattleId = null;
            ResetInvite();
            if (TownPartyClient.Room.InRoom)
            {
                TownPartyClient.LeaveRoom();
            }

            Close();
        }

        // 서버에 난이도 변경 요청이 없어 혼자 있는 방장만 방을 다시 만든다. 그 밖에는 방의 난이도로 되돌린다
        private void OnDifficultyChanged(EBossDifficulty difficulty)
        {
            if ((_createPending) || (_recreatePending))
            {
                _view.SetDifficulty(_difficulty);
                return;
            }

            PartyRoomStateMessage room = TownPartyClient.Room;
            if (!room.InRoom)
            {
                EBossDifficulty previous = _difficulty;
                _difficulty = difficulty;
                if (!CreateRoom(difficulty))
                {
                    _difficulty = previous;
                    _view.SetDifficulty(previous);
                }

                return;
            }

            ProgressionCatalogSO.BossBattleDefinition sibling = null;
            bool canChange = (room.Mode == EPartyMode.Boss) && (room.Phase == EPartyPhase.Open) && (room.Members.Length == 1);
            if (!canChange)
            {
                _view.SetDifficulty(SortiePrepRules.ToDifficulty(room.Difficulty));
                _view.SetNotice("파티원이 있거나 출정 중에는 난이도를 바꿀 수 없다.");
                return;
            }

            if ((!GameDB.IsInitialized) || (!SortiePrepRules.TryFindSibling(GameDB.Progression, room.ContentId, difficulty, out sibling)))
            {
                _view.SetDifficulty(SortiePrepRules.ToDifficulty(room.Difficulty));
                _view.SetNotice("이 보스는 선택한 난이도로 지금 열 수 없다.");
                return;
            }

            _difficulty       = difficulty;
            _recreatePending  = true;
            _recreateBattleId = sibling.DefinitionId;
            TownPartyClient.LeaveRoom();
        }

        // 서버에 초대를 보내고 응답이 올 때까지 다시 보내지 못하게 막는다. 성공 문구는 서버가 받아들인 응답에서만 만든다
        private void OnInviteClicked(string rawName)
        {
            if (_view == null)
            {
                return;
            }

            string name = PartyInviteRules.NormalizeName(rawName);
            PartyRoomStateMessage room = TownPartyClient.Room;
            EInviteBlock block = PartyInviteRules.Check(name, ClientRoles.LocalCharacterName(), ClientRoles.IsTown(), in room, _invite.IsPending);
            if (block != EInviteBlock.None)
            {
                _view.SetNotice(PartyInviteRules.Describe(block));
                return;
            }

            // 응답이 보내는 도중이나 직후에 와도 놓치지 않도록 ID를 먼저 발급해 대기 상태를 걸고 보낸다
            uint requestId = TownPartyClient.ReserveRequestId();
            _invite.Begin(requestId, name, Time.unscaledTime);
            if (!TownPartyClient.InviteWithId(name, requestId))
            {
                _invite.Reset();
                _view.SetNotice(PartyInviteRules.Describe(EInviteBlock.Offline));
                return;
            }

            _view.SetNotice(PartyInviteRules.Describe(EInviteBlock.Pending));
            Refresh();
        }

        private void ExpireInvite()
        {
            if (!_invite.IsExpired(Time.unscaledTime, INVITE_TIMEOUT_SECONDS))
            {
                return;
            }

            ResetInvite();
            if (_view != null)
            {
                _view.SetNotice(INVITE_TIMEOUT_TEXT);
            }

            Refresh();
        }

        private void ResetInvite()
        {
            _invite.Reset();
        }

        // 이 창이 지금 기다리는 요청 ID와 정확히 같은 응답만 받는다. 다른 화면이 보낸 초대나 닫기, 시간 초과 뒤 늦은 응답은 무시한다
        private void OnInviteResult(PartyResultMessage result)
        {
            EInviteOutcome outcome = _invite.Resolve(in result, out string target);
            if ((outcome == EInviteOutcome.Ignored) || (_view == null))
            {
                return;
            }

            if (outcome == EInviteOutcome.Accepted)
            {
                _view.SetNotice(string.Format(CultureInfo.InvariantCulture, PartyInviteRules.SENT_FORMAT, target));
                _view.ClearInviteInput();
            }
            else
            {
                _view.SetNotice(PartyInviteRules.DescribeReject(result.Reason));
            }

            Refresh();
        }

        private void OnPartyResult(PartyResultMessage result)
        {
            if (result.Action == EPartyAction.Invite)
            {
                OnInviteResult(result);
                return;
            }

            if ((result.IsAccepted) || (_view == null))
            {
                return;
            }

            _startAfterReady = false;
            _createPending    = false;
            _recreatePending  = false;
            _recreateBattleId = null;
            _view.SetNotice(result.Reason);
            Refresh();
        }

        // 지참 물약은 가진 만큼 모두 보낸다. 준비 판정과 상한은 서버가 맡는다
        private void SendReady(bool ready)
        {
            int small = 0;
            int large = 0;
            if ((ready) && (GameDB.IsInitialized) && (TownLifeClient.HasSnapshot))
            {
                small = CountPotion(GameDB.Life, EPotionSize.Small);
                large = CountPotion(GameDB.Life, EPotionSize.Large);
            }

            if (!TownPartyClient.SetReady(ready, small, large))
            {
                _view.SetNotice(ERejectReason.BackendUnavailable);
            }
        }

        private static int CountPotion(LifeConfigSO life, EPotionSize size)
        {
            LifeConfigSO.PotionDefinition definition = life.GetPotion(size);
            if (definition == null)
            {
                return 0;
            }

            long total = 0;
            TownItemEntry[] potions = TownLifeClient.Snapshot.Potions;
            for (int i = 0; i < potions.Length; ++i)
            {
                if (potions[i].DefinitionId == definition.DefinitionId)
                {
                    total += potions[i].Quantity - potions[i].Reserved;
                }
            }

            return (int)Math.Max(0L, Math.Min(total, int.MaxValue));
        }
    }
}
