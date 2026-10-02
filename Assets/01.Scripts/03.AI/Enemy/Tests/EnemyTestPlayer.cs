// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    /// <summary> 일반 적 시험용 플레이어 대역, 받은 피해를 그대로 반영하고 호출 수를 센다 </summary>
    public sealed class EnemyTestPlayer : IDamageable
    {
        /****************************************
        *              Properties
        ****************************************/

        public int                   CombatId        { get; }
        public ECombatFaction        Faction         => ECombatFaction.Player;
        public EParticipantLifeState LifeState       { get; set; } = EParticipantLifeState.Alive;
        public bool                  IsSummon        { get; set; }
        public int                   OwnerCombatId   { get; set; } = CombatIds.NONE;
        public Vector3               Position        { get; set; }
        public Vector3               Forward         { get; set; } = Vector3.forward;
        public float                 BodyRadius      => 0f;
        public float                 Health          { get; private set; }
        public float                 MaxHealth       { get; }
        public float                 Defense         { get; }
        public bool                  IsInvulnerable  => false;
        public bool                  IsGuardingFront => false;
        public int                   DamageCalls     { get; private set; }

        /****************************************
        *                Methods
        ****************************************/

        public EnemyTestPlayer(int combatId, Vector3 position, float maxHealth, float defense)
        {
            CombatId  = combatId;
            Position  = position;
            MaxHealth = maxHealth;
            Health    = maxHealth;
            Defense   = defense;
        }

        public void ApplyDamage(float appliedAmount, bool isLethal, double serverTime)
        {
            ++DamageCalls;
            Health -= appliedAmount;

            if (!isLethal)
            {
                return;
            }

            Health    = 0f;
            LifeState = EParticipantLifeState.Dead;
        }
    }
}
