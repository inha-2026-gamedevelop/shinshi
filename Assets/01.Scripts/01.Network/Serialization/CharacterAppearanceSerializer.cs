// System
using System;
using System.Collections.Generic;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 캐릭터 외형의 Mirror 직렬화, 서버가 접속 때 한 번 보낸다 </summary>
    public static class CharacterAppearanceSerializer
    {
        // Contracts DTO는 백엔드와 같이 쓰려고 private 필드로 두어 Weaver가 자동 직렬화하지 못한다.
        // public 필드 스냅샷을 따로 만드는 대신 여기서 직접 읽고 쓴다.
        // 필드 순서를 바꾸면 서버와 클라가 다른 값을 읽는다. 바꿀 때는 CharacterAppearance.CURRENT_VERSION을 올린다

        /****************************************
        *                Methods
        ****************************************/

        // 검증하지 않은 값을 바이트로 잘라 보내면 다른 외형으로 보이므로 보내기 전에 막는다
        public static void WriteCharacterAppearance(this NetworkWriter writer, CharacterAppearance value)
        {
            if (value == null)
            {
                writer.WriteBool(false);
                return;
            }

            if (!value.IsWellFormed())
            {
                throw new ArgumentException("검증되지 않은 외형은 보내지 않는다", nameof(value));
            }

            writer.WriteBool(true);
            writer.WriteByte((byte)value.Version);
            writer.WriteByte((byte)value.BodyType);
            WriteSliders(writer, value.Face);
            WriteSliders(writer, value.Body);
            writer.WriteByte((byte)value.SkinTone);
            writer.WriteByte((byte)value.EyeColor);
            writer.WriteByte((byte)value.BrowColor);
            writer.WriteByte((byte)value.LipColor);
            writer.WriteUShort((ushort)value.HairId);
            writer.WriteByte((byte)value.HairColor);
        }

        // 모르는 version 값도 그대로 돌려주고 적용 쪽이 기본 외형으로 표시한다. 다만 길이를 앞에 붙이는 것은 슬라이더 배열뿐이라
        // 고정 필드를 더하거나 빼는 버전 변경은 읽을 수 없고 뒤따르는 데이터까지 어긋난다. 그런 변경은 서버와 클라이언트를 함께 배포한다
        public static CharacterAppearance ReadCharacterAppearance(this NetworkReader reader)
        {
            if (!reader.ReadBool())
            {
                return null;
            }

            int   version   = reader.ReadByte();
            int   bodyType  = reader.ReadByte();
            int[] face      = ReadSliders(reader);
            int[] body      = ReadSliders(reader);
            int   skinTone  = reader.ReadByte();
            int   eyeColor  = reader.ReadByte();
            int   browColor = reader.ReadByte();
            int   lipColor  = reader.ReadByte();
            int   hairId    = reader.ReadUShort();
            int   hairColor = reader.ReadByte();

            return new CharacterAppearance(version, bodyType, face, body, skinTone,
                eyeColor, browColor, lipColor, hairId, hairColor);
        }

        // 개수를 1바이트로 보내 다른 version의 길이도 읽을 수 있게 한다
        private static void WriteSliders(NetworkWriter writer, IReadOnlyList<int> sliders)
        {
            writer.WriteByte((byte)sliders.Count);
            for (int i = 0; i < sliders.Count; ++i)
            {
                writer.WriteByte((byte)sliders[i]);
            }
        }

        private static int[] ReadSliders(NetworkReader reader)
        {
            int   count   = reader.ReadByte();
            int[] sliders = new int[count];
            for (int i = 0; i < count; ++i)
            {
                sliders[i] = reader.ReadByte();
            }

            return sliders;
        }
    }
}
