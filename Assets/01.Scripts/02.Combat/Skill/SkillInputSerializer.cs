// Unity
using UnityEngine;

// 서드파티
using Mirror;

namespace Shinshi.Combat
{
    /// <summary> 불변 기술 입력의 필드 순서를 고정한 Mirror 직렬화 </summary>
    public static class SkillInputSerializer
    {
        /****************************************
        *                Methods
        ****************************************/

        public static void WritePlayerSkillInput(this NetworkWriter writer, PlayerSkillInput value)
        {
            writer.WritePlayerInputHeader(value.Header);
            writer.WriteByte((byte)value.Kind);
            writer.WriteByte(value.Slot);
            writer.WriteUShort(value.AimYaw);
            writer.WriteVector3(value.Point);
        }

        public static PlayerSkillInput ReadPlayerSkillInput(this NetworkReader reader)
        {
            PlayerInputHeader header = reader.ReadPlayerInputHeader();
            var kind = (ESkillRequestKind)reader.ReadByte();
            byte slot = reader.ReadByte();
            ushort aimYaw = reader.ReadUShort();
            Vector3 point = reader.ReadVector3();
            return new PlayerSkillInput(header, kind, slot, aimYaw, point);
        }
    }
}
