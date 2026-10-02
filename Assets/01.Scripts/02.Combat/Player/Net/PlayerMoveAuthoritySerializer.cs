// Unity
using UnityEngine;

// 서드파티
using Mirror;

namespace Shinshi.Combat
{
    /// <summary> 서버 확정 이동 상태의 Mirror 직렬화, Weaver가 SyncVar에 이 메서드를 쓴다 </summary>
    public static class PlayerMoveAuthoritySerializer
    {
        // 구조체를 private readonly 필드로 두어 Weaver가 자동 직렬화하지 못한다. 순서를 바꾸면 서버와 클라이언트가
        // 다른 값을 읽으므로 둘을 함께 배포한다

        /****************************************
        *                Methods
        ****************************************/

        public static void WritePlayerMoveAuthority(this NetworkWriter writer, PlayerMoveAuthority value)
        {
            // 기본값은 유효 표시만 보내고 끝낸다. 읽는 쪽 생성자는 항상 유효한 값을 만들기 때문이다
            writer.WriteBool(value.IsValid);
            if (!value.IsValid)
            {
                return;
            }

            Vector3 position = value.Position;
            writer.WriteFloat(position.x);
            writer.WriteFloat(position.y);
            writer.WriteFloat(position.z);
            writer.WriteVarUInt(value.MoveSequence);
            writer.WriteFloat(value.MoveElapsed);
            writer.WriteBool(value.IsMoving);
        }

        public static PlayerMoveAuthority ReadPlayerMoveAuthority(this NetworkReader reader)
        {
            if (!reader.ReadBool())
            {
                return default;
            }

            float x        = reader.ReadFloat();
            float y        = reader.ReadFloat();
            float z        = reader.ReadFloat();
            uint  sequence = reader.ReadVarUInt();
            float elapsed  = reader.ReadFloat();
            bool  isMoving = reader.ReadBool();
            return new PlayerMoveAuthority(new Vector3(x, y, z), sequence, elapsed, isMoving);
        }
    }
}
