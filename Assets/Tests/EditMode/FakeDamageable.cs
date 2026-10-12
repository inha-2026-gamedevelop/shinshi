// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    /// <summary> 서버 피해 판정 테스트용 전투 대상, 받은 피해를 그대로 반영한다 </summary>
    public sealed class FakeDamageable : IDamageable, ILifeStealSource
    {
        /****************************************
        *              Properties
        ****************************************/

        public int                   CombatId        { get; }
        public ECombatFaction        Faction         { get; }
        public EParticipantLifeState LifeState       { get; set; } = EParticipantLifeState.Alive;
        public bool                  IsSummon        { get; set; }
        public int                   OwnerCombatId   { get; set; } = CombatIds.NONE;
        public Vector3               Position        { get; set; } = Vector3.zero;
        public Vector3               Forward         { get; set; } = Vector3.forward;
        public float                 BodyRadius      { get; set; } = 0f;
        public float                 Health          { get; set; }
        public float                 MaxHealth       { get; set; }
        public float                 Defense         { get; set; }
        public bool                  IsInvulnerable  { get; set; }
        public bool                  IsGuardingFront { get; set; }
        public double                DeathTime       { get; private set; }
        public int                   DamageCalls     { get; private set; }

        // 0이면 흡수가 없는 대상이다. 상한 없이 비율만 적용해 판정기가 넘긴 값을 확인한다
        public float                 LifeStealRate   { get; set; }
        public int                   LifeStealCalls  { get; private set; }
        public float                 LastLifeStealDamage { get; private set; }

        /****************************************
        *                Methods
        ****************************************/

        public FakeDamageable(int combatId, ECombatFaction faction, float maxHealth, float defense)
        {
            CombatId  = combatId;
            Faction   = faction;
            MaxHealth = maxHealth;
            Health    = maxHealth;
            Defense   = defense;
        }

        public float ReceiveLifeSteal(float appliedDamage, double serverTime)
        {
            ++LifeStealCalls;
            LastLifeStealDamage = appliedDamage;
            if ((LifeState != EParticipantLifeState.Alive) || (!(LifeStealRate > 0f)))
            {
                return 0f;
            }

            float healed = appliedDamage * LifeStealRate;
            Health += healed;
            return healed;
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
            DeathTime = serverTime;
        }
    }
}
