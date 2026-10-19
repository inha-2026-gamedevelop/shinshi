// System
using System;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 파티 초대 요청을 보내기 전에 걸러 내는 사전 점검 결과 </summary>
    public enum EInviteBlock
    {
        None,
        Pending,
        Offline,
        NoRoom,
        NotMember,
        WrongPhase,
        Full,
        EmptyName,
        Self
    }

    /// <summary> 파티 초대 사전 점검과 화면 문구 </summary>
    // 점검은 헛된 요청을 줄이는 용도다. 초대 성공 여부와 최종 판정은 항상 서버 응답이 정한다
    public static class PartyInviteRules
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string SENT_FORMAT = "{0}님에게 초대를 보냈다. 수락을 기다린다.";

        private const string PENDING_TEXT         = "초대 응답을 기다리는 중이다.";
        private const string OFFLINE_TEXT         = "마을 서버에 연결되어 있지 않다.";
        private const string NO_ROOM_TEXT         = "파티 방에 들어가 있어야 초대할 수 있다.";
        private const string NOT_MEMBER_TEXT      = "파티 방의 멤버만 초대할 수 있다.";
        private const string WRONG_PHASE_TEXT     = "출정 준비 중에만 초대할 수 있다.";
        private const string FULL_TEXT            = "파티 정원이 가득 찼다.";
        private const string EMPTY_NAME_TEXT      = "초대할 캐릭터 이름을 입력해라.";
        private const string SELF_TEXT            = "자기 자신은 초대할 수 없다.";
        private const string INVALID_TARGET_TEXT  = "접속 중인 캐릭터를 찾을 수 없다. 이름을 확인해라.";
        private const string INVITE_CAPACITY_TEXT = "정원이 찼거나 상대가 이미 초대를 받은 상태다.";

        /****************************************
        *                Methods
        ****************************************/

        public static string NormalizeName(string raw)
        {
            return (raw == null) ? string.Empty : raw.Trim();
        }

        /// <summary> 보낼 수 있으면 None, 아니면 막는 이유를 돌려준다 </summary>
        public static EInviteBlock Check(string name, string localName, bool connected, in PartyRoomStateMessage room, bool pending)
        {
            if (pending)
            {
                return EInviteBlock.Pending;
            }

            if (!connected)
            {
                return EInviteBlock.Offline;
            }

            if ((!room.InRoom) || (room.Members == null))
            {
                return EInviteBlock.NoRoom;
            }

            if (!IsMember(in room, localName))
            {
                return EInviteBlock.NotMember;
            }

            if (room.Phase != EPartyPhase.Open)
            {
                return EInviteBlock.WrongPhase;
            }

            if (room.Members.Length >= Constants.Session.MAX_PARTY_SIZE)
            {
                return EInviteBlock.Full;
            }

            string target = NormalizeName(name);
            if (target.Length == 0)
            {
                return EInviteBlock.EmptyName;
            }

            if (string.Equals(target, localName, StringComparison.OrdinalIgnoreCase))
            {
                return EInviteBlock.Self;
            }

            return EInviteBlock.None;
        }

        /// <summary> 초대 입력칸을 보여 줄 조건. 서버 정책대로 방 멤버면 누구나 쓴다 </summary>
        public static bool CanUse(in PartyRoomStateMessage room, string localName)
        {
            return (room.InRoom) && (room.Members != null) && (IsMember(in room, localName));
        }

        public static string Describe(EInviteBlock block)
        {
            switch (block)
            {
                case EInviteBlock.Pending:    return PENDING_TEXT;
                case EInviteBlock.Offline:    return OFFLINE_TEXT;
                case EInviteBlock.NoRoom:     return NO_ROOM_TEXT;
                case EInviteBlock.NotMember:  return NOT_MEMBER_TEXT;
                case EInviteBlock.WrongPhase: return WRONG_PHASE_TEXT;
                case EInviteBlock.Full:       return FULL_TEXT;
                case EInviteBlock.EmptyName:  return EMPTY_NAME_TEXT;
                case EInviteBlock.Self:       return SELF_TEXT;
                default:                      return string.Empty;
            }
        }

        /// <summary> 서버가 초대 요청을 거절한 사유를 초대 상황에 맞는 문구로 바꾼다 </summary>
        public static string DescribeReject(ERejectReason reason)
        {
            switch (reason)
            {
                case ERejectReason.InvalidTarget: return INVALID_TARGET_TEXT;
                case ERejectReason.NoCapacity:    return INVITE_CAPACITY_TEXT;
                default:                          return RejectText.Describe(reason);
            }
        }

        private static bool IsMember(in PartyRoomStateMessage room, string localName)
        {
            return SortiePrepRules.TryFindLocal(in room, localName, out PartyMemberView _);
        }
    }
}
