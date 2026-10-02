// Unity
using UnityEngine;

// 서드파티
using Mirror;

namespace Shinshi.Combat
{
    /// <summary> 동료 프리팹의 복제 상태, 수명과 타깃 판정은 서버 실행 객체가 한다 </summary>
    public class CompanionCombatState : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [SyncVar] private int     _combatId;
        [SyncVar] private int     _ownerCombatId;
        [SyncVar] private int     _targetCombatId;
        [SyncVar] private byte    _slot;
        [SyncVar] private bool    _active;
        [SyncVar] private double  _expiresAt;
        [SyncVar] private float   _health;
        [SyncVar] private float   _maxHealth;
        [SyncVar] private Vector3 _position;
        [SyncVar] private ushort  _facingYaw;

        /****************************************
        *              Properties
        ****************************************/

        public int     CombatId                         => _combatId;
        public int     OwnerCombatId                    => _ownerCombatId;
        public int     TargetCombatId                   => _targetCombatId;
        public byte    Slot                             => _slot;
        public bool    IsActive                         => _active;
        public bool    IsSummon                         => true;
        public bool    ExcludedFromBossParticipantCount => true;
        public double  ExpiresAt                        => _expiresAt;
        public float   Health                           => _health;
        public float   MaxHealth                        => _maxHealth;
        public Vector3 Position                         => _position;
        public ushort  FacingYaw                        => _facingYaw;

        /****************************************
        *                Server
        ****************************************/

        [Server]
        public void SetCombatSnapshot(int combatId, int ownerCombatId, int targetCombatId, byte slot, bool active,
            double expiresAt, float health, float maxHealth, Vector3 position, ushort facingYaw)
        {
            _combatId       = combatId;
            _ownerCombatId  = ownerCombatId;
            _targetCombatId = targetCombatId;
            _slot           = slot;
            _active         = active;
            _expiresAt      = expiresAt;
            _health         = health;
            _maxHealth      = maxHealth;
            _position       = position;
            _facingYaw      = facingYaw;
        }
    }
}
