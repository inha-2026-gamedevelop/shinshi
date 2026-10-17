// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    // 친구, 길드 창(UI 담당)은 이 창구의 이벤트만 구독하고 요청 메서드로 보낸다. 관계 판정과 상한 검사는 마을 서버와 API가 한다.
    // 서버는 연결마다 쓰기를 동시에 1건만 받으므로 여기서도 결과가 올 때까지 다음 쓰기를 보내지 않는다(Guid.Empty를 돌려준다).
    // 결과를 모르면(BackendUnavailable, 시간 초과) 스냅샷을 다시 받아 실제 상태를 본다. 같은 동작을 다시 눌러도 API가 상태 기준으로
    // 멱등 처리하며, 길드 생성만 상태로 구분되지 않아 같은 이름이면 앞선 RequestId를 다시 쓴다.
    // 스냅샷, 쓰기 잠금, 결과 불명 표시는 연결마다 비운다. 전달 ID 중복 기억은 DeliveryDedupe가 대화 기록과 같은 수명으로 따로 갖는다
    /// <summary> 클라이언트의 친구, 길드 요청 송신과 스냅샷, 결과, 알림 수신 창구 </summary>
    public static class SocialClient
    {
        /****************************************
        *                Fields
        ****************************************/

        private static SocialSnapshotMessage _snapshot;
        private static bool                  _hasSnapshot;
        private static bool                  _snapshotFailed;

        private static Guid          _pendingRequestId = Guid.Empty;
        private static ESocialAction _pendingAction    = ESocialAction.Snapshot;
        private static double        _pendingDeadline  = 0.0;

        private static string _lastCreateName      = null;
        private static Guid   _lastCreateRequestId = Guid.Empty;

        private static double _lastSnapshotRequest = double.NegativeInfinity;
        private static bool   _snapshotWanted      = false;
        private static double _lastAckSend         = double.NegativeInfinity;

        /****************************************
        *              Properties
        ****************************************/

        public static SocialSnapshotMessage Snapshot    => _snapshot;
        public static bool                  HasSnapshot => _hasSnapshot;

        // 결과를 기다리는 쓰기가 있으면 모든 소셜 창이 쓰기 버튼을 잠근다
        public static bool          HasPendingWrite  => _pendingRequestId != Guid.Empty;
        public static ESocialAction PendingAction    => _pendingAction;
        public static Guid          PendingRequestId => _pendingRequestId;

        // 서버가 스냅샷 조회도 실패했다고 알렸다. 마지막 스냅샷은 보이되 쓰기를 잠그고, 새 스냅샷을 받으면 풀린다
        public static bool IsSnapshotFailed => _snapshotFailed;

        // 받은 친구 요청 수와 받은 길드 초대, 내 길드 가입 신청(길드장만) 수. HUD 메뉴의 빨간 점에 쓴다
        public static int FriendBadgeCount => _hasSnapshot ? _snapshot.Incoming.Length : 0;
        public static int GuildBadgeCount  => _hasSnapshot ? (_snapshot.GuildInvites.Length + _snapshot.GuildApplications.Length) : 0;

        public static event Action<SocialSnapshotMessage> SnapshotChanged;
        public static event Action<SocialResultMessage>   ResultReceived;
        public static event Action<SocialNoticeMessage>   NoticeReceived;

        // 쓰기 잠금, 조회 실패, 연결 초기화로 버튼 상태가 바뀔 때
        public static event Action                        StateChanged;

        /****************************************
        *                Methods
        ****************************************/

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            ClearConnectionState();
            SnapshotChanged = null;
            ResultReceived  = null;
            NoticeReceived  = null;
            StateChanged    = null;
        }

        /// <summary> 클라이언트가 시작할 때마다 부른다, 이전 연결의 상태를 비운다 </summary>
        public static void RegisterHandlers()
        {
            ResetConnection();
            NetworkClient.ReplaceHandler<SocialResultMessage>(OnResult);
            NetworkClient.ReplaceHandler<SocialSnapshotMessage>(OnSnapshot);
            NetworkClient.ReplaceHandler<SocialNoticeMessage>(OnNotice);
        }

        // 서버를 옮기면 이전 서버의 쓰기 결과와 확인 대기는 새 서버가 모른다. 던전 이동, 재접속마다 비운다
        /// <summary> 연결이 끝날 때 부른다. 스냅샷, 쓰기 잠금, 확인 대기를 비우고 전달 ID 중복 기억은 남긴다 </summary>
        public static void ResetConnection()
        {
            ClearConnectionState();
            DeliveryDedupe.ClearPendingAcks();
            StateChanged?.Invoke();
        }

        /// <summary> 목록을 다시 받는다. 서버 제한(2초 1건) 안이면 보내지 않고 다음 주기에 보낸다 </summary>
        public static bool RequestSnapshot()
        {
            return RequestSnapshot(Time.realtimeSinceStartupAsDouble);
        }

        public static bool RequestSnapshot(double now)
        {
            if (!IsTownConnection())
            {
                return false;
            }

            if ((now - _lastSnapshotRequest) < Constants.Social.SNAPSHOT_REQUEST_WINDOW_SECONDS)
            {
                _snapshotWanted = true;
                return false;
            }

            _lastSnapshotRequest = now;
            _snapshotWanted      = false;
            NetworkClient.Send(new SocialRequestMessage(ESocialAction.Snapshot, Guid.NewGuid(), null, null, null, null, 0));
            return true;
        }

        public static Guid SendFriendRequest(string characterName)
        {
            return SendWrite(ESocialAction.FriendRequest, Guid.NewGuid(), characterName, null, null, null, 0);
        }

        public static Guid AcceptFriend(string characterId)
        {
            return SendWrite(ESocialAction.FriendAccept, Guid.NewGuid(), null, characterId, null, null, 0);
        }

        public static Guid DeclineFriend(string characterId)
        {
            return SendWrite(ESocialAction.FriendDecline, Guid.NewGuid(), null, characterId, null, null, 0);
        }

        public static Guid CancelFriendRequest(string characterId)
        {
            return SendWrite(ESocialAction.FriendCancel, Guid.NewGuid(), null, characterId, null, null, 0);
        }

        public static Guid RemoveFriend(string characterId)
        {
            return SendWrite(ESocialAction.FriendRemove, Guid.NewGuid(), null, characterId, null, null, 0);
        }

        // 결과를 모르는 생성 뒤 같은 이름으로 다시 누르면 같은 RequestId를 보내 API가 처음 만든 길드를 돌려주게 한다
        /// <summary> 길드를 만든다, 이름은 서버가 길드 이름 규칙으로 다시 정규화한다 </summary>
        public static Guid CreateGuild(string guildName)
        {
            bool sameAsLast = (_lastCreateRequestId != Guid.Empty)
                && (string.Equals(_lastCreateName, guildName, StringComparison.Ordinal));
            Guid requestId = sameAsLast ? _lastCreateRequestId : Guid.NewGuid();
            Guid sent      = SendWrite(ESocialAction.GuildCreate, requestId, guildName, null, null, null, 0);
            if (sent != Guid.Empty)
            {
                _lastCreateName      = guildName;
                _lastCreateRequestId = requestId;
            }

            return sent;
        }

        public static Guid ApplyGuild(string guildName)
        {
            return SendWrite(ESocialAction.GuildApply, Guid.NewGuid(), guildName, null, null, null, 0);
        }

        public static Guid AcceptApplication(string applicantId)
        {
            return SendWrite(ESocialAction.GuildApplicationAccept, Guid.NewGuid(), null, applicantId, MyGuildId(), null, 0);
        }

        public static Guid DeclineApplication(string applicantId)
        {
            return SendWrite(ESocialAction.GuildApplicationDecline, Guid.NewGuid(), null, applicantId, MyGuildId(), null, 0);
        }

        public static Guid InviteToGuild(string characterName)
        {
            return SendWrite(ESocialAction.GuildInvite, Guid.NewGuid(), characterName, null, MyGuildId(), null, 0);
        }

        public static Guid AcceptGuildInvite(string guildId)
        {
            return SendWrite(ESocialAction.GuildInviteAccept, Guid.NewGuid(), null, guildId, guildId, null, 0);
        }

        public static Guid DeclineGuildInvite(string guildId)
        {
            return SendWrite(ESocialAction.GuildInviteDecline, Guid.NewGuid(), null, guildId, guildId, null, 0);
        }

        /// <summary> 스냅샷에 보인 내 가입으로 탈퇴한다. 혼자 남은 길드장이면 길드가 해체된다 </summary>
        public static Guid LeaveGuild()
        {
            if ((!_hasSnapshot) || (!_snapshot.Guild.HasGuild))
            {
                return Guid.Empty;
            }

            SocialGuildView guild = _snapshot.Guild;
            return SendWrite(ESocialAction.GuildLeave, Guid.NewGuid(), null, null, guild.GuildId, guild.MyMembershipId, 0);
        }

        public static Guid KickMember(in SocialGuildMemberEntry member)
        {
            return SendWrite(ESocialAction.GuildKick, Guid.NewGuid(), null, member.CharacterId, MyGuildId(), member.MembershipId, 0);
        }

        /// <summary> 길드장을 넘긴다, 스냅샷의 길드장 버전을 기대 값으로 보낸다 </summary>
        public static Guid DelegateLeader(in SocialGuildMemberEntry member)
        {
            long version = _hasSnapshot ? _snapshot.Guild.LeaderVersion : 0;
            return SendWrite(ESocialAction.GuildDelegate, Guid.NewGuid(), null, member.CharacterId, MyGuildId(), member.MembershipId,
                version);
        }

        // 수신 확인 묶음 전송, 결과 대기 시간 초과, 미뤄 둔 스냅샷 요청을 처리한다. SocialClientDriver가 매 프레임 부른다
        /// <summary> 주기 처리, now는 실제 경과 시간(초) </summary>
        public static void Tick(double now)
        {
            if (!NetworkClient.isConnected)
            {
                return;
            }

            if ((_pendingRequestId != Guid.Empty) && (now >= _pendingDeadline))
            {
                // 결과가 오지 않으면 결과 불명으로 보고 잠금을 푼다. 스냅샷이 실제 상태를 알려 주고 같은 동작은 상태 기준으로 멱등이다
                ResultReceived?.Invoke(new SocialResultMessage(_pendingAction, _pendingRequestId, ERejectReason.BackendUnavailable));
                _pendingRequestId = Guid.Empty;
                StateChanged?.Invoke();
                RequestSnapshot(now);
            }

            if (_snapshotWanted)
            {
                RequestSnapshot(now);
            }

            FlushAcks(now);
        }

        /// <summary> 확인 대기가 있으면 간격과 서버 제한 안에서 한 묶음을 보낸다, 보냈으면 참 </summary>
        public static bool FlushAcks(double now)
        {
            int pending = DeliveryDedupe.PendingAckCount;
            if ((pending == 0) || (!NetworkClient.isConnected))
            {
                return false;
            }

            double elapsed     = now - _lastAckSend;
            double minInterval = Constants.Social.ACK_WINDOW_SECONDS / Constants.Social.ACK_MESSAGES_PER_WINDOW;
            bool   isFull      = pending >= Constants.Social.MAX_ACK_IDS_PER_MESSAGE;
            if ((elapsed < minInterval) || ((!isFull) && (elapsed < Constants.Social.DELIVERY_ACK_INTERVAL_SECONDS)))
            {
                return false;
            }

            long[] ids = DeliveryDedupe.TakeAcks(Constants.Social.MAX_ACK_IDS_PER_MESSAGE);
            if (ids == null)
            {
                return false;
            }

            _lastAckSend = now;
            NetworkClient.Send(new SocialDeliveryAckMessage(ids));
            return true;
        }

        private static Guid SendWrite(ESocialAction action, Guid requestId, string name, string targetId, string guildId,
            string membershipId, long expectedLeaderVersion)
        {
            if ((!IsTownConnection()) || (_pendingRequestId != Guid.Empty) || (_snapshotFailed))
            {
                return Guid.Empty;
            }

            _pendingRequestId = requestId;
            _pendingAction    = action;
            _pendingDeadline  = Time.realtimeSinceStartupAsDouble + Constants.Social.REQUEST_TIMEOUT_SECONDS;
            NetworkClient.Send(new SocialRequestMessage(action, requestId, name, targetId, guildId, membershipId,
                expectedLeaderVersion));
            StateChanged?.Invoke();
            return requestId;
        }

        // 관리 요청은 마을 서버만 받는다. 던전은 채팅과 알림만 받으므로 보내지 않는다
        /// <summary> 마을 서버에 접속해 있으면 참 </summary>
        public static bool IsTownConnection()
        {
            return (NetworkClient.isConnected) && (NetworkManager.singleton is ShinshiNetworkManager manager)
                && (manager.ServerRole == EServerRole.Town);
        }

        private static string MyGuildId()
        {
            return ((_hasSnapshot) && (_snapshot.Guild.HasGuild)) ? _snapshot.Guild.GuildId : null;
        }

        private static void ClearConnectionState()
        {
            _snapshot            = default;
            _hasSnapshot         = false;
            _snapshotFailed      = false;
            _pendingRequestId    = Guid.Empty;
            _pendingAction       = ESocialAction.Snapshot;
            _pendingDeadline     = 0.0;
            _lastCreateName      = null;
            _lastCreateRequestId = Guid.Empty;
            _lastSnapshotRequest = double.NegativeInfinity;
            _snapshotWanted      = false;
            _lastAckSend         = double.NegativeInfinity;
        }

        // 잠금을 먼저 풀어야 결과를 받은 창이 같은 처리 안에서 다음 요청을 보낼 수 있다
        private static void OnResult(SocialResultMessage message)
        {
            bool changed = false;
            if ((message.Action == ESocialAction.Snapshot) && (message.RequestId == Guid.Empty)
                && (message.Reason == ERejectReason.BackendUnavailable))
            {
                _snapshotFailed = true;
                changed         = true;
            }

            if ((_pendingRequestId != Guid.Empty) && (message.RequestId == _pendingRequestId))
            {
                _pendingRequestId = Guid.Empty;
                changed           = true;
            }

            // 확정되거나 확정 거절된 생성은 같은 번호를 다시 쓸 이유가 없다
            if ((message.Action == ESocialAction.GuildCreate) && (message.Reason != ERejectReason.BackendUnavailable))
            {
                _lastCreateName      = null;
                _lastCreateRequestId = Guid.Empty;
            }

            if (changed)
            {
                StateChanged?.Invoke();
            }

            ResultReceived?.Invoke(message);
        }

        private static void OnSnapshot(SocialSnapshotMessage message)
        {
            bool wasFailed = _snapshotFailed;
            _snapshot       = message;
            _hasSnapshot    = true;
            _snapshotFailed = false;
            if (wasFailed)
            {
                StateChanged?.Invoke();
            }

            SnapshotChanged?.Invoke(message);
        }

        // 알림은 목록이 바뀌었다는 표시라 스냅샷을 다시 받는다. 중복으로 버린 알림도 확인은 보낸다
        private static void OnNotice(SocialNoticeMessage message)
        {
            if (!DeliveryDedupe.Accept(message.DeliveryId))
            {
                return;
            }

            NoticeReceived?.Invoke(message);
            RequestSnapshot();
        }
    }
}
