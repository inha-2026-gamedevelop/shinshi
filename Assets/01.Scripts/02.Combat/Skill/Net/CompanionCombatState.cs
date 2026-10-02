// Unity
using UnityEngine;

// 서드파티
using Mirror;

namespace Shinshi.Combat
{
    /// <summary> 동료 프리팹의 복제 상태, 수명과 타깃 판정은 서버 실행 객체가 한다 </summary>
    public class CompanionCombatState : NetworkBehaviour
    {
        // 서버 스포너가 소환 때 서버 소유(연결 권한 없음)로 스폰하고 복귀, 재호출, 소유자 정리 때 내린다. Command가 없어
        // 클라이언트는 이 개체로 요청을 보낼 수 없다. 위치는 같은 프리팹의 NetworkTransform이 원격 보간으로 복제하므로
        // SyncVar로 다시 보내지 않고, 방향은 플레이어처럼 시각 피벗만 돌리도록 yaw 하나로 보낸다

        /****************************************
        *                Fields
        ****************************************/

        [SyncVar] private int    _combatId;
        [SyncVar] private int    _ownerCombatId;
        [SyncVar] private int    _targetCombatId;
        [SyncVar] private byte   _slot;
        [SyncVar] private bool   _active;
        [SyncVar] private double _expiresAt;
        [SyncVar] private float  _health;
        [SyncVar] private float  _maxHealth;
        [SyncVar] private ushort _facingYaw;

        /****************************************
        *              Properties
        ****************************************/

        public int     CombatId                         => _combatId;

        // 소유 플레이어의 PlayerCombatState.CombatId와 같다. 재접속으로 소유자 번호가 바뀌면 서버가 다음 틱에 고친다
        public int     OwnerCombatId                    => _ownerCombatId;
        public int     TargetCombatId                   => _targetCombatId;
        public byte    Slot                             => _slot;
        public bool    IsActive                         => _active;
        public bool    IsSummon                         => true;
        public bool    ExcludedFromBossParticipantCount => true;
        public double  ExpiresAt                        => _expiresAt;
        public float   Health                           => _health;
        public float   MaxHealth                        => _maxHealth;
        public Vector3 Position                         => transform.position;
        public ushort  FacingYaw                        => _facingYaw;

        /****************************************
        *                Server
        ****************************************/

        // SyncVar는 같은 값이면 더럽히지 않으므로 매 틱 불러도 바뀐 값만 전송된다
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
            _facingYaw      = facingYaw;
            transform.position = position;
        }
    }
}
