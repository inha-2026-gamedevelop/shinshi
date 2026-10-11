// System
using System.IO;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 마을 파티 메시지의 Mirror 직렬화, Weaver가 이 메서드를 쓴다 </summary>
    public static class PartyMessageSerializer
    {
        // 메시지는 private readonly 필드라 Weaver 자동 직렬화가 내용을 쓰지 못한다. 여기서 순서를 고정한다.
        // 방 목록과 방 상태는 요청이나 변경 때만 오가는 드문 메시지라 이름과 콘텐츠 ID 문자열을 그대로 보낸다.
        // 배열 길이는 읽기 전에 상한으로 막는다. 조작된 길이로 큰 배열을 만들게 두지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private const int MAX_ROOMS   = Constants.Town.MAX_LISTED_ROOMS;
        private const int MAX_MEMBERS = Constants.Session.MAX_PARTY_SIZE;

        /****************************************
        *                Methods
        ****************************************/

        public static void WritePartyRequestMessage(this NetworkWriter writer, PartyRequestMessage value)
        {
            writer.WriteByte((byte)value.Action);
            writer.WriteInt(value.RoomId);
            writer.WriteByte((byte)value.Mode);
            writer.WriteString(value.ContentId);
            writer.WriteString(value.Name);
            writer.WriteString(value.Password);
            writer.WriteBool(value.Ready);
            writer.WriteInt(value.SmallPotions);
            writer.WriteInt(value.LargePotions);
        }

        public static PartyRequestMessage ReadPartyRequestMessage(this NetworkReader reader)
        {
            var    action       = (EPartyAction)reader.ReadByte();
            int    roomId       = reader.ReadInt();
            var    mode         = (EPartyMode)reader.ReadByte();
            string contentId    = reader.ReadString();
            string name         = reader.ReadString();
            string password     = reader.ReadString();
            bool   ready        = reader.ReadBool();
            int    smallPotions = reader.ReadInt();
            int    largePotions = reader.ReadInt();
            return new PartyRequestMessage(action, roomId, mode, contentId, name, password, ready, smallPotions, largePotions);
        }

        public static void WritePartyResultMessage(this NetworkWriter writer, PartyResultMessage value)
        {
            writer.WriteByte((byte)value.Action);
            writer.WriteVarUInt((uint)value.Reason);
        }

        public static PartyResultMessage ReadPartyResultMessage(this NetworkReader reader)
        {
            var action = (EPartyAction)reader.ReadByte();
            var reason = (ERejectReason)reader.ReadVarUInt();
            return new PartyResultMessage(action, reason);
        }

        public static void WritePartyRoomListMessage(this NetworkWriter writer, PartyRoomListMessage value)
        {
            PartyRoomSummary[] rooms = value.Rooms;
            writer.WriteInt(rooms.Length);
            for (int i = 0; i < rooms.Length; ++i)
            {
                WriteSummary(writer, in rooms[i]);
            }
        }

        public static PartyRoomListMessage ReadPartyRoomListMessage(this NetworkReader reader)
        {
            int count = ReadCount(reader, MAX_ROOMS);
            var rooms = new PartyRoomSummary[count];
            for (int i = 0; i < count; ++i)
            {
                rooms[i] = ReadSummary(reader);
            }

            return new PartyRoomListMessage(rooms);
        }

        public static void WritePartyRoomStateMessage(this NetworkWriter writer, PartyRoomStateMessage value)
        {
            writer.WriteBool(value.InRoom);
            if (!value.InRoom)
            {
                return;
            }

            writer.WriteInt(value.RoomId);
            writer.WriteLong(value.Revision);
            writer.WriteString(value.Name);
            writer.WriteByte((byte)value.Mode);
            writer.WriteString(value.ContentId);
            writer.WriteSByte(value.Difficulty);
            writer.WriteBool(value.HasPassword);
            writer.WriteInt(value.LeaderParticipantId);
            writer.WriteByte((byte)value.Phase);
            writer.WriteFloat(value.CountdownRemaining);

            PartyMemberView[] members = value.Members;
            writer.WriteInt(members.Length);
            for (int i = 0; i < members.Length; ++i)
            {
                WriteMember(writer, in members[i]);
            }
        }

        public static PartyRoomStateMessage ReadPartyRoomStateMessage(this NetworkReader reader)
        {
            if (!reader.ReadBool())
            {
                return PartyRoomStateMessage.Left();
            }

            int    roomId     = reader.ReadInt();
            long   revision   = reader.ReadLong();
            string name       = reader.ReadString();
            var    mode       = (EPartyMode)reader.ReadByte();
            string contentId  = reader.ReadString();
            sbyte  difficulty = reader.ReadSByte();
            bool   hasPass    = reader.ReadBool();
            int    leaderId   = reader.ReadInt();
            var    phase      = (EPartyPhase)reader.ReadByte();
            float  countdown  = reader.ReadFloat();
            int    count      = ReadCount(reader, MAX_MEMBERS);
            var    members    = new PartyMemberView[count];
            for (int i = 0; i < count; ++i)
            {
                members[i] = ReadMember(reader);
            }

            return new PartyRoomStateMessage(true, roomId, revision, name, mode, contentId, difficulty, hasPass, leaderId, phase,
                countdown, members);
        }

        public static void WritePartyInviteMessage(this NetworkWriter writer, PartyInviteMessage value)
        {
            writer.WriteInt(value.RoomId);
            writer.WriteString(value.RoomName);
            writer.WriteString(value.InviterName);
            writer.WriteByte((byte)value.Mode);
            writer.WriteString(value.ContentId);
            writer.WriteFloat(value.ExpiresIn);
        }

        public static PartyInviteMessage ReadPartyInviteMessage(this NetworkReader reader)
        {
            int    roomId      = reader.ReadInt();
            string roomName    = reader.ReadString();
            string inviterName = reader.ReadString();
            var    mode        = (EPartyMode)reader.ReadByte();
            string contentId   = reader.ReadString();
            float  expiresIn   = reader.ReadFloat();
            return new PartyInviteMessage(roomId, roomName, inviterName, mode, contentId, expiresIn);
        }

        public static void WriteSortieTicketMessage(this NetworkWriter writer, SortieTicketMessage value)
        {
            writer.WriteString(value.SessionId);
            writer.WriteString(value.InstanceId);
            writer.WriteString(value.Address);
            writer.WriteInt(value.Port);
            writer.WriteString(value.JoinToken);
            writer.WriteLong(value.ExpiresAtUnixMs);
        }

        public static SortieTicketMessage ReadSortieTicketMessage(this NetworkReader reader)
        {
            string sessionId  = reader.ReadString();
            string instanceId = reader.ReadString();
            string address    = reader.ReadString();
            int    port       = reader.ReadInt();
            string joinToken  = reader.ReadString();
            long   expiresAt  = reader.ReadLong();
            return new SortieTicketMessage(sessionId, instanceId, address, port, joinToken, expiresAt);
        }

        /// <summary> 배열 길이를 읽고 0 이상 상한 이하인지 본다, 아니면 읽기를 멈춘다(Mirror가 연결을 끊는다) </summary>
        public static int ReadCount(NetworkReader reader, int max)
        {
            int count = reader.ReadInt();
            if ((count < 0) || (count > max))
            {
                throw new InvalidDataException("배열 길이가 계약 상한을 넘는다");
            }

            return count;
        }

        private static void WriteSummary(NetworkWriter writer, in PartyRoomSummary value)
        {
            writer.WriteInt(value.RoomId);
            writer.WriteString(value.Name);
            writer.WriteByte((byte)value.Mode);
            writer.WriteString(value.ContentId);
            writer.WriteSByte(value.Difficulty);
            writer.WriteByte((byte)value.Phase);
            writer.WriteByte(value.MemberCount);
            writer.WriteByte(value.ReadyCount);
            writer.WriteBool(value.HasPassword);
            writer.WriteByte(value.Warriors);
            writer.WriteByte(value.Sorcerers);
            writer.WriteByte(value.Summoners);
            writer.WriteByte(value.Swordsmen);
            writer.WriteByte(value.Mageknights);
            writer.WriteByte(value.Archers);
        }

        private static PartyRoomSummary ReadSummary(NetworkReader reader)
        {
            int    roomId      = reader.ReadInt();
            string name        = reader.ReadString();
            var    mode        = (EPartyMode)reader.ReadByte();
            string contentId   = reader.ReadString();
            sbyte  difficulty  = reader.ReadSByte();
            var    phase       = (EPartyPhase)reader.ReadByte();
            byte   memberCount = reader.ReadByte();
            byte   readyCount  = reader.ReadByte();
            bool   hasPassword = reader.ReadBool();
            byte   warriors    = reader.ReadByte();
            byte   sorcerers   = reader.ReadByte();
            byte   summoners   = reader.ReadByte();
            byte   swordsmen   = reader.ReadByte();
            byte   mageknights = reader.ReadByte();
            byte   archers     = reader.ReadByte();
            return new PartyRoomSummary(roomId, name, mode, contentId, difficulty, phase, memberCount, readyCount, hasPassword,
                warriors, sorcerers, summoners, swordsmen, mageknights, archers);
        }

        private static void WriteMember(NetworkWriter writer, in PartyMemberView value)
        {
            writer.WriteInt(value.ParticipantId);
            writer.WriteString(value.Name);
            writer.WriteByte((byte)value.PlayerClass);
            writer.WriteByte((byte)value.Ready);
            writer.WriteInt(value.SmallPotions);
            writer.WriteInt(value.LargePotions);
            writer.WriteLong(value.TotalExperience);
        }

        private static PartyMemberView ReadMember(NetworkReader reader)
        {
            int    participantId   = reader.ReadInt();
            string name            = reader.ReadString();
            var    playerClass     = (EPlayerClass)reader.ReadByte();
            var    ready           = (EPartyReadyState)reader.ReadByte();
            int    smallPotions    = reader.ReadInt();
            int    largePotions    = reader.ReadInt();
            long   totalExperience = reader.ReadLong();
            return new PartyMemberView(participantId, name, playerClass, ready, smallPotions, largePotions, totalExperience);
        }
    }
}
