// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Dev
{
    /// <summary> 타격감 시험 몬스터를 서버 판정이 읽는 전투 대상으로 감싼다. 죽음은 몬스터의 타격 횟수 규칙이 정하므로 체력은 죽지 않을 만큼 크게 둔다 </summary>
    public sealed class DevMonsterCombatTarget : IDamageable
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float DEFAULT_BODY_RADIUS = 0.5f; // m
        private const float VIRTUAL_HEALTH      = 1_000_000f;

        private readonly int                  _combatId;
        private readonly DevHitFeedbackMonster _monster;
        private readonly Collider             _collider;
        private readonly float                _bodyRadius;

        /****************************************
        *              Properties
        ****************************************/

        public DevHitFeedbackMonster Monster  => _monster;
        public Collider              Collider => _collider;

        public int                   CombatId       => _combatId;
        public ECombatFaction        Faction        => ECombatFaction.Enemy;
        public EParticipantLifeState LifeState      => ((_monster == null) || (_monster.IsDead)) ? EParticipantLifeState.Dead : EParticipantLifeState.Alive;
        public bool                  IsSummon       => false;
        public int                   OwnerCombatId  => CombatIds.NONE;
        public Vector3               Position       => (_monster != null) ? _monster.transform.position : Vector3.zero;
        public Vector3               Forward        => (_monster != null) ? _monster.transform.forward : Vector3.forward;
        public float                 BodyRadius     => _bodyRadius;
        public float                 Health         => VIRTUAL_HEALTH;
        public float                 MaxHealth      => VIRTUAL_HEALTH;
        public float                 Defense        => 0f;
        public bool                  IsInvulnerable => false;
        public bool                  IsGuardingFront => false;

        /****************************************
        *                Methods
        ****************************************/

        public DevMonsterCombatTarget(int combatId, DevHitFeedbackMonster monster, Collider collider)
        {
            _combatId = combatId;
            _monster  = monster;
            _collider = collider;

            Vector3 extents = (collider != null) ? collider.bounds.extents : new Vector3(DEFAULT_BODY_RADIUS, 0f, DEFAULT_BODY_RADIUS);
            _bodyRadius = Mathf.Max(DEFAULT_BODY_RADIUS * 0.5f, Mathf.Max(extents.x, extents.z));
        }

        public void ApplyDamage(float appliedAmount, bool isLethal, double serverTime)
        {
            // 체력은 가상이라 따로 줄이지 않는다. 피해 표현은 판정기 알림(DamageApplied)에서 한 번 재생한다
        }
    }
}
