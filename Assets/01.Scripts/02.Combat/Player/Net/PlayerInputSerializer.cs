// Unity
using UnityEngine;

// 서드파티
using Mirror;

namespace Shinshi.Combat
{
    /// <summary> 플레이어 입력의 Mirror 직렬화, Weaver가 Command 인자에 이 메서드를 쓴다 </summary>
    public static class PlayerInputSerializer
    {
        // 입력 구조체는 바꿀 수 없게 private readonly 필드로 두었다. Weaver는 public 필드만 자동 직렬화하므로
        // 직접 쓰지 않으면 빈 내용이 전송된다. 여기서 순서를 고정하고 작은 값은 가변 길이로 줄인다.
        // 순서를 바꾸면 서버와 클라이언트가 다른 값을 읽으므로 둘을 함께 배포한다(PROTOCOL_VERSION이 같아야 접속된다)

        /****************************************
        *                Methods
        ****************************************/

        public static void WritePlayerInputHeader(this NetworkWriter writer, PlayerInputHeader value)
        {
            writer.WriteVarUInt((uint)value.ProtocolVersion);
            writer.WriteInt(value.SessionEpoch);
            writer.WriteVarUInt((uint)value.ParticipantId);
            writer.WriteVarUInt(value.Sequence);
            writer.WriteDouble(value.EstimatedServerTime);
        }

        public static PlayerInputHeader ReadPlayerInputHeader(this NetworkReader reader)
        {
            int    protocolVersion = (int)reader.ReadVarUInt();
            int    sessionEpoch    = reader.ReadInt();
            int    participantId   = (int)reader.ReadVarUInt();
            uint   sequence        = reader.ReadVarUInt();
            double time            = reader.ReadDouble();
            return new PlayerInputHeader(protocolVersion, sessionEpoch, participantId, sequence, time);
        }

        // 마을 크기가 정해지지 않아 좌표를 양자화하지 않고 float 그대로 보낸다
        public static void WritePlayerMoveInput(this NetworkWriter writer, PlayerMoveInput value)
        {
            writer.WritePlayerInputHeader(value.Header);
            Vector3 destination = value.Destination;
            writer.WriteFloat(destination.x);
            writer.WriteFloat(destination.y);
            writer.WriteFloat(destination.z);
        }

        public static PlayerMoveInput ReadPlayerMoveInput(this NetworkReader reader)
        {
            PlayerInputHeader header = reader.ReadPlayerInputHeader();
            float x = reader.ReadFloat();
            float y = reader.ReadFloat();
            float z = reader.ReadFloat();
            return new PlayerMoveInput(header, new Vector3(x, y, z));
        }

        public static void WritePlayerAttackInput(this NetworkWriter writer, PlayerAttackInput value)
        {
            writer.WritePlayerInputHeader(value.Header);
            writer.WriteUShort(value.AimYaw);
        }

        public static PlayerAttackInput ReadPlayerAttackInput(this NetworkReader reader)
        {
            PlayerInputHeader header = reader.ReadPlayerInputHeader();
            ushort aimYaw = reader.ReadUShort();
            return new PlayerAttackInput(header, aimYaw);
        }

        public static void WritePlayerDodgeInput(this NetworkWriter writer, PlayerDodgeInput value)
        {
            writer.WritePlayerInputHeader(value.Header);
            writer.WriteUShort(value.AimYaw);
        }

        public static PlayerDodgeInput ReadPlayerDodgeInput(this NetworkReader reader)
        {
            PlayerInputHeader header = reader.ReadPlayerInputHeader();
            ushort aimYaw = reader.ReadUShort();
            return new PlayerDodgeInput(header, aimYaw);
        }
    }
}
