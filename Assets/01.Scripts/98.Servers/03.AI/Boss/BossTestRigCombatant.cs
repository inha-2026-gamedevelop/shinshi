// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.AI
{
    // 무철 시험 구동기가 플레이어와 동료와 소환물 대역으로 함께 쓴다. EnemyTestRigPlayer는 방어력이 0으로 고정이고 체력을 되돌릴 수 없어
    // 방어력과 체력이 피해에 미치는 영향을 보려는 시험에 쓰지 못해 따로 둔다. 다른 시험 구동기가 쓰는 그 클래스는 고치지 않는다
    /// <summary> 시험 구동기용 피해 대상 대역, 방어력과 소속과 주인을 정할 수 있고 위치와 체력을 바로 바꿀 수 있다 </summary>
    public sealed class BossTestRigCombatant : IDamageable
    {
        /****************************************
        *              Properties
        ****************************************/

        public int                   CombatId        { get; }
        public ECombatFaction        Faction         => ECombatFaction.Player;
        public EParticipantLifeState LifeState       { get; private set; } = EParticipantLifeState.Alive;
        public bool                  IsSummon        { get; }
        public int                   OwnerCombatId   { get; }
        public Vector3               Position        { get; set; }
        public Vector3               Forward         => Vector3.forward;
        public float                 BodyRadius      { get; }
        public float                 Health          { get; private set; }
        public float                 MaxHealth       { get; }
        public float                 Defense         { get; }
        public bool                  IsInvulnerable  => false;
        public bool                  IsGuardingFront => false;

        /****************************************
        *                Methods
        ****************************************/

        public BossTestRigCombatant(int combatId, Vector3 position, float maxHealth, float defense, float bodyRadius,
            bool isSummon = false, int ownerCombatId = CombatIds.NONE)
        {
            CombatId      = combatId;
            Position      = position;
            MaxHealth     = maxHealth;
            Health        = maxHealth;
            Defense       = defense;
            BodyRadius    = bodyRadius;
            IsSummon      = isSummon;
            OwnerCombatId = ownerCombatId;
        }

        public void ApplyDamage(float appliedAmount, bool isLethal, double serverTime)
        {
            if (isLethal)
            {
                Health    = 0f;
                LifeState = EParticipantLifeState.Dead;
                return;
            }

            Health = Mathf.Max(0f, Health - appliedAmount);
        }

        /// <summary> 체력을 가득 채우고 되살린다, 시험을 이어 가려는 용도다 </summary>
        public void Revive()
        {
            Health    = MaxHealth;
            LifeState = EParticipantLifeState.Alive;
        }

        /// <summary> 체력을 지정한 값으로 맞춘다, 최대 체력을 넘지 않고 0 이하이면 쓰러진 것으로 둔다 </summary>
        public void SetHealth(float health)
        {
            Health    = Mathf.Clamp(health, 0f, MaxHealth);
            LifeState = (Health > 0f) ? EParticipantLifeState.Alive : EParticipantLifeState.Dead;
        }
    }
}
