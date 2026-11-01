// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 연결 객체가 사라져도 남은 장판과 동료가 파괴된 Transform을 읽지 않게 한다 </summary>
    public sealed class SkillCasterAnchor : ISkillCaster, ICriticalDamageSource, ILifeStealSource, IHitImmunityTarget, IShieldTarget
    {
        /****************************************
        *                Fields
        ****************************************/

        private ISkillCaster          _attached;
        private int                   _combatId;
        private Vector3               _position;
        private Vector3               _forward;
        private float                 _attack;
        private float                 _critChance;
        private float                 _health;
        private float                 _maxHealth;
        private float                 _defense;
        private EParticipantLifeState _lifeState;

        /****************************************
        *              Properties
        ****************************************/

        public int                   CombatId        => (_attached != null) ? _attached.CombatId : _combatId;
        public ECombatFaction        Faction         => ECombatFaction.Player;
        public EParticipantLifeState LifeState       => (_attached != null) ? _attached.LifeState : _lifeState;
        public bool                  IsSummon        => false;
        public int                   OwnerCombatId   => CombatIds.NONE;
        public Vector3               Position        => (_attached != null) ? _attached.Position : _position;
        public Vector3               Forward         => (_attached != null) ? _attached.Forward : _forward;
        public float                 BodyRadius      => (_attached != null) ? _attached.BodyRadius : Constants.Combat.PLAYER_BODY_RADIUS;
        public float                 AttackPower     => (_attached != null) ? _attached.AttackPower : _attack;
        public float                 CritChance      => (_attached != null)
            ? ((_attached is ICriticalDamageSource critical) ? critical.CritChance : 0f) : _critChance;
        public float                 Health          => (_attached != null) ? _attached.Health : _health;
        public float                 MaxHealth       => (_attached != null) ? _attached.MaxHealth : _maxHealth;
        public float                 Defense         => (_attached != null) ? _attached.Defense : _defense;
        public bool                  IsInvulnerable  => (_attached != null) && (_attached.IsInvulnerable);
        public bool                  IsGuardingFront => false;

        /****************************************
        *                Methods
        ****************************************/

        public SkillCasterAnchor(ISkillCaster owner) { _attached = owner; }

        public void Rebind(ISkillCaster owner) { _attached = owner; }

        public void Detach()
        {
            if (_attached == null)
            {
                return;
            }
            _combatId  = _attached.CombatId;
            _position  = _attached.Position;
            _forward   = _attached.Forward;
            _attack    = _attached.AttackPower;
            _critChance = CritChance;
            _health    = _attached.Health;
            _maxHealth = _attached.MaxHealth;
            _defense   = _attached.Defense;
            _lifeState = _attached.LifeState;
            _attached  = null;
        }

        public void SetSkillPosition(Vector3 position)
        {
            if (_attached != null)
            {
                _attached.SetSkillPosition(position);
            }
        }

        public void SetSkillFacing(Vector3 direction)
        {
            if (_attached != null)
            {
                _attached.SetSkillFacing(direction);
            }
        }

        // 연결이 끊긴 동안에는 받을 개체가 없어 버린다. 재접속은 새 기술 시전으로만 면역을 다시 얻는다
        public void GrantHitImmunity(double untilServerTime)
        {
            if (_attached is IHitImmunityTarget target)
            {
                target.GrantHitImmunity(untilServerTime);
            }
        }

        // 면역과 같이 연결이 끊긴 동안에는 받을 개체가 없어 버린다
        public void GrantShield(float amount, double untilServerTime)
        {
            if (_attached is IShieldTarget target)
            {
                target.GrantShield(amount, untilServerTime);
            }
        }

        // 치명 확률은 보존값을 쓰지만 회복은 받을 개체가 있을 때만 넘긴다
        public float ReceiveLifeSteal(float appliedDamage, double serverTime)
        {
            return (_attached is ILifeStealSource source) ? source.ReceiveLifeSteal(appliedDamage, serverTime) : 0f;
        }

        public void ApplyDamage(float appliedAmount, bool isLethal, double serverTime)
        {
            if (_attached != null)
            {
                _attached.ApplyDamage(appliedAmount, isLethal, serverTime);
                return;
            }
            _health = isLethal ? 0f : Mathf.Max(0f, _health - appliedAmount);
            if (isLethal)
            {
                _lifeState = EParticipantLifeState.Dead;
            }
        }
    }
}
