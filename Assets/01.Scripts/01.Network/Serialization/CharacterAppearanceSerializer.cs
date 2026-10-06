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
        // 필드 순서를 바꾸면 서버와 클라가 다른 값을 읽는다. 바꿀 때는 Constants.Network.PROTOCOL_VERSION을 올린다

        // 존재 여부 1바이트를 포함해 nonnull v1 메시지는 44바이트로 고정이고, v2는 faceId/outfitId 2바이트만
        // 뒤에 붙어 46바이트, v3는 facialHair/bustSize 2바이트가 더 붙어 48바이트가 된다. 이 레이아웃을 깨는 내부 보초 바이트를 넣지 않는다 - 과거 request_hash와
        // 와이어 수신 쪽 모두 이 바이트 수 그대로를 기대한다

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

            if (!IsSupportedVersion(value.Version))
            {
                throw new ArgumentException("지원하지 않는 외형 버전은 보내지 않는다", nameof(value));
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
            if (value.Version >= CharacterAppearance.VERSION_2)
            {
                writer.WriteByte((byte)value.FaceId);
                writer.WriteByte((byte)value.OutfitId);
            }

            if (value.Version == CharacterAppearance.VERSION_3)
            {
                writer.WriteByte((byte)value.FacialHair);
                writer.WriteByte((byte)value.BustSize);
            }
        }

        // 지원 범위 밖 version은 뒤따르는 필드 수를 알 수 없으므로 그 자리에서 거절한다. 기본 외형으로 조용히
        // 넘기지 않고 예외로 알린다. 패킷이 중간에 잘리면 NetworkReader가 바이트 부족으로 예외를 던진다
        public static CharacterAppearance ReadCharacterAppearance(this NetworkReader reader)
        {
            if (!reader.ReadBool())
            {
                return null;
            }

            int version = reader.ReadByte();
            if (!IsSupportedVersion(version))
            {
                throw new InvalidOperationException($"지원하지 않는 외형 버전이다: {version}");
            }

            int   bodyType  = reader.ReadByte();
            int[] face      = ReadSliders(reader);
            int[] body      = ReadSliders(reader);
            int   skinTone  = reader.ReadByte();
            int   eyeColor  = reader.ReadByte();
            int   browColor = reader.ReadByte();
            int   lipColor  = reader.ReadByte();
            int   hairId    = reader.ReadUShort();
            int   hairColor = reader.ReadByte();

            int faceId     = CharacterAppearance.VALUE_MIN;
            int outfitId   = CharacterAppearance.VALUE_MIN;
            int facialHair = CharacterAppearance.VALUE_MIN;
            int bustSize   = CharacterAppearance.VALUE_MIN;
            if (version >= CharacterAppearance.VERSION_2)
            {
                faceId   = reader.ReadByte();
                outfitId = reader.ReadByte();
            }

            if (version == CharacterAppearance.VERSION_3)
            {
                facialHair = reader.ReadByte();
                bustSize   = reader.ReadByte();
            }

            return new CharacterAppearance(version, bodyType, face, body, skinTone,
                eyeColor, browColor, lipColor, hairId, hairColor, faceId, outfitId, facialHair, bustSize);
        }

        private static bool IsSupportedVersion(int version)
        {
            return (version == CharacterAppearance.VERSION_1) || (version == CharacterAppearance.VERSION_2)
                || (version == CharacterAppearance.VERSION_3);
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
