// Unity
using UnityEngine;

namespace Shinshi.Combat
{
    /// <summary> 피해와 자원은 보내지 않고 기존 공통 헤더와 조준 의도만 보낸다 </summary>
    public readonly struct PlayerSkillInput
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly PlayerInputHeader _header;
        private readonly ESkillRequestKind _kind;
        private readonly byte              _slot;
        private readonly ushort            _aimYaw;
        private readonly Vector3           _point;

        /****************************************
        *              Properties
        ****************************************/

        public PlayerInputHeader Header => _header;
        public ESkillRequestKind Kind   => _kind;
        public byte              Slot   => _slot;
        public ushort            AimYaw => _aimYaw;
        public Vector3           Point  => _point;

        /****************************************
        *                Methods
        ****************************************/

        public PlayerSkillInput(PlayerInputHeader header, ESkillRequestKind kind, byte slot, ushort aimYaw, Vector3 point)
        {
            _header = header;
            _kind   = kind;
            _slot   = slot;
            _aimYaw = aimYaw;
            _point  = point;
        }
    }
}
