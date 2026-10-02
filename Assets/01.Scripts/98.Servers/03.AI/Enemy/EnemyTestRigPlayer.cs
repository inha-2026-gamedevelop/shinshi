// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.AI
{
    /// <summary> 시험 구동기가 쓰는 플레이어 대역, 받은 피해를 그대로 반영하고 위치를 바로 바꿀 수 있다 </summary>
    public sealed class EnemyTestRigPlayer : IDamageable
    {
        /****************************************
        *              Properties
        ****************************************/

        public int                   CombatId        { get; }
        public ECombatFaction        Faction         => ECombatFaction.Player;
        public EParticipantLifeState LifeState       { get; private set; } = EParticipantLifeState.Alive;
        public bool                  IsSummon        => false;
        public int                   OwnerCombatId   => CombatIds.NONE;
        public Vector3               Position        { get; set; }
        public Vector3               Forward         => Vector3.forward;
        public float                 BodyRadius      => 0f;
        public float                 Health          { get; private set; }
        public float                 MaxHealth       { get; }
        public float                 Defense         => 0f;
        public bool                  IsInvulnerable  => false;
        public bool                  IsGuardingFront => false;

        /****************************************
        *                Methods
        ****************************************/

        public EnemyTestRigPlayer(int combatId, Vector3 position, float maxHealth)
        {
            CombatId  = combatId;
            Position  = position;
            MaxHealth = maxHealth;
            Health    = maxHealth;
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
    }
}
