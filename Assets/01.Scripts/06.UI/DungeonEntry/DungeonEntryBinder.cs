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
    /// <summary> 마을 NPC 상호작용으로 입장 창을 열고, 고른 곳으로 파티 방을 만들어 출정 준비 창에 넘긴다 </summary>
    // 입장은 새 경로를 만들지 않고 기존 출정 흐름(파티 방 -> 준비 -> 출정 -> 마을 서버 예약 -> 던전 서버 접속)을 쓴다.
    // 이 바인더는 방 생성 요청(TownPartyClient.CreateRoom)만 보낸다. 지역, 보스전 존재, 체험판 포함, 계정 해금, 입장권은
    // 마을 서버가 방을 만들 때와 준비, 출정 때 판정하고 거절하면 그 사유를 이 창에 보인다.
    // 이미 파티 방에 있으면 같은 곳은 준비 창만 열고, 혼자인 방은 나갔다가 새로 만들고, 파티원이 있는 방은 건드리지 않는다
    // TODO(진욱/기웅): 신시 안내(U18)가 생기기 전까지는 마을에 들어와 첫 성장 스냅샷을 받은 순간 안내 완료를 보낸다.
    // 보내지 않으면 숲(region_forest) 해금 조건(안내 완료)을 채울 길이 없어 숲 파티의 준비와 예약이 모두 거절된다
    public sealed class DungeonEntryBinder : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string ROOM_NAME_FORMAT  = "{0}의 파티";
        private const string DEFAULT_ROOM_NAME = "파티";

        private const string NOTICE_NOT_IN_TOWN   = "마을 서버에 접속한 상태에서만 입장할 수 있습니다";
        private const string NOTICE_NO_CATALOG    = "입장 목록을 불러오지 못했습니다";
        private const string NOTICE_IN_OTHER_ROOM = "다른 파티 방에 있습니다. 출정 준비 창에서 방을 나간 뒤 선택하세요";

        [SerializeField] private HotkeyRouter           _router     = null;
        [SerializeField] private DungeonEntryView       _view       = null;
        [SerializeField] private NpcInteractionTrigger  _npc        = null;
        [SerializeField] private SortiePrepBinder       _sortiePrep = null;

        private readonly List<DungeonEntryData> _entries = new List<DungeonEntryData>();

        private Func<bool> _onClose = null;

        private int               _selected   = -1;
        private DungeonEntryData? _pending    = null;
        private bool              _awaitLeave = false;
        private bool              _introSent  = false;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsOpen => (_view != null) && (_view.IsOpen);

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _onClose = OnCloseHotkey;
        }

        private void OnEnable()
        {
            if (_npc != null)
            {
                _npc.Interacted += OnInteracted;
                _npc.NearChanged += OnNearChanged;
            }

            if (_view != null)
            {
                _view.Closed        += Close;
                _view.EntrySelected += Select;
                _view.EnterClicked  += OnEnterClicked;
            }

            TownPartyClient.RoomChanged     += OnRoomChanged;
            TownPartyClient.ResultReceived  += OnPartyResult;
            TownGrowthClient.SnapshotChanged += OnGrowthSnapshot;
            TownProgressClient.IntroResultReceived += OnIntroResult;
        }

        private void OnDisable()
        {
            if (_npc != null)
            {
                _npc.Interacted -= OnInteracted;
                _npc.NearChanged -= OnNearChanged;
            }

            if (_view != null)
            {
                _view.Closed        -= Close;
                _view.EntrySelected -= Select;
                _view.EnterClicked  -= OnEnterClicked;
            }

            TownPartyClient.RoomChanged     -= OnRoomChanged;
            TownPartyClient.ResultReceived  -= OnPartyResult;
            TownGrowthClient.SnapshotChanged -= OnGrowthSnapshot;
            TownProgressClient.IntroResultReceived -= OnIntroResult;
            if (_router != null)
            {
                _router.Unregister(EHotkeyAction.CloseWindow, _onClose);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void Open()
        {
            if (_view == null)
            {
                return;
            }

            _pending    = null;
            _awaitLeave = false;
            SendIntroOnce();
            RebuildEntries();
            _view.Show();
            if (_router != null)
            {
                _router.Register(EHotkeyAction.CloseWindow, _onClose);
            }
        }

        public void Close()
        {
            _pending    = null;
            _awaitLeave = false;
            if (_view != null)
            {
                _view.Hide();
            }

            if (_router != null)
            {
                _router.Unregister(EHotkeyAction.CloseWindow, _onClose);
            }
        }

        private void OnInteracted()
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        // 멀어지면 창도 닫는다. 창만 남은 채 걸어가는 일을 막는다
        private void OnNearChanged(bool near)
        {
            if ((!near) && (IsOpen))
            {
                Close();
            }
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

        private void RebuildEntries()
        {
            if (GameDB.IsInitialized)
            {
                DungeonEntryRules.Build(GameDB.Progression, _entries);
            }
            else
            {
                _entries.Clear();
            }

            if (_entries.Count > _view.RowCapacity)
            {
                Debug.LogWarning($"[DungeonEntry] 입장 항목 {_entries.Count}개가 창의 줄 {_view.RowCapacity}개보다 많아 뒤는 보이지 않는다", this);
            }

            _view.SetEntries(_entries);
            if (_entries.Count == 0)
            {
                _selected = -1;
                _view.SetSelected(-1);
                _view.SetNotice(NOTICE_NO_CATALOG);
                return;
            }

            Select(FirstAvailable());
        }

        // 처음에는 입장할 수 있는 첫 항목을 고른다, 없으면 첫 항목이다
        private int FirstAvailable()
        {
            int visible = Math.Min(_entries.Count, _view.RowCapacity);
            for (int i = 0; i < visible; ++i)
            {
                if (_entries[i].IsAvailable)
                {
                    return i;
                }
            }

            return 0;
        }

        private void Select(int index)
        {
            if ((index < 0) || (index >= _entries.Count))
            {
                return;
            }

            _selected = index;
            _view.SetSelected(index);
            _view.SetDetail(_entries[index]);
        }

        private void OnEnterClicked()
        {
            if ((_selected < 0) || (_selected >= _entries.Count))
            {
                return;
            }

            DungeonEntryData entry = _entries[_selected];
            if (!entry.IsAvailable)
            {
                _view.SetNotice(entry.UnavailableReason);
                return;
            }

            if (!ClientRoles.IsTown())
            {
                _view.SetNotice(NOTICE_NOT_IN_TOWN);
                return;
            }

            PartyRoomStateMessage room = TownPartyClient.Room;
            if (!room.InRoom)
            {
                RequestCreate(entry);
                return;
            }

            if ((room.Mode == entry.Mode) && (string.Equals(room.ContentId, entry.ContentId, StringComparison.Ordinal)))
            {
                OpenSortiePrep();
                return;
            }

            // 파티원이 있거나 출정 중인 방은 여기서 떠나지 않는다
            bool alone = (room.Members.Length == 1) && (room.Phase == EPartyPhase.Open);
            if (!alone)
            {
                _view.SetNotice(NOTICE_IN_OTHER_ROOM);
                return;
            }

            _pending    = entry;
            _awaitLeave = true;
            if (!TownPartyClient.LeaveRoom())
            {
                ClearPending(RejectText.Describe(ERejectReason.BackendUnavailable));
            }
        }

        private void RequestCreate(DungeonEntryData entry)
        {
            _pending    = entry;
            _awaitLeave = false;

            string localName = ClientRoles.LocalCharacterName();
            string roomName  = string.IsNullOrEmpty(localName)
                ? DEFAULT_ROOM_NAME
                : string.Format(CultureInfo.InvariantCulture, ROOM_NAME_FORMAT, localName);
            if (!TownPartyClient.CreateRoom(roomName, entry.Mode, entry.ContentId, null))
            {
                ClearPending(RejectText.Describe(ERejectReason.BackendUnavailable));
            }
        }

        // 방을 떠난 알림이 오면 보류한 항목으로 새 방을 만들고, 그 방이 오면 출정 준비 창으로 넘긴다
        private void OnRoomChanged(PartyRoomStateMessage room)
        {
            if (!_pending.HasValue)
            {
                return;
            }

            DungeonEntryData entry = _pending.Value;
            if ((!room.InRoom) && (_awaitLeave))
            {
                RequestCreate(entry);
                return;
            }

            if ((room.InRoom) && (room.Mode == entry.Mode) && (string.Equals(room.ContentId, entry.ContentId, StringComparison.Ordinal)))
            {
                OpenSortiePrep();
            }
        }

        private void OnPartyResult(PartyResultMessage result)
        {
            if ((result.IsAccepted) || (!_pending.HasValue))
            {
                return;
            }

            ClearPending(RejectText.Describe(result.Reason));
        }

        // 첫 스냅샷이 오면 서버가 이 연결의 계정을 마을 참가자로 묶은 뒤다. 이미 기록된 계정도 API가 성공으로 받는다
        private void OnGrowthSnapshot(GrowthSnapshotMessage snapshot)
        {
            SendIntroOnce();
        }

        private void SendIntroOnce()
        {
            if ((_introSent) || (!ClientRoles.IsTown()))
            {
                return;
            }

            _introSent = TownProgressClient.CompleteIntro() != Guid.Empty;
        }

        // 거절되면 다음 스냅샷이나 창을 열 때 다시 보낸다
        private void OnIntroResult(IntroCompleteResultMessage result)
        {
            if (result.IsAccepted)
            {
                return;
            }

            Debug.LogWarning($"[DungeonEntry] 신시 안내 완료 기록이 거절됐다, reason={result.Reason}", this);
            _introSent = false;
        }

        private void ClearPending(string notice)
        {
            _pending    = null;
            _awaitLeave = false;
            if ((IsOpen) && (_view != null))
            {
                _view.SetNotice(notice);
            }
        }

        private void OpenSortiePrep()
        {
            Close();
            if ((_sortiePrep != null) && (!_sortiePrep.IsOpen))
            {
                _sortiePrep.Open();
            }
        }
    }
}
