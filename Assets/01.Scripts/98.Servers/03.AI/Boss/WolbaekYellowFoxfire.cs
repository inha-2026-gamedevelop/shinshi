// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.AI
{
    // 최후의 영역의 노란 여우불 피격 몸체다. 플레이어 공격은 이 몸체를 일반 적처럼 맞히고 DamageResolver가 ApplyDamage를 부른다.
    // 위치와 체력은 서버가 정하고 움직이지 않는다. 체력이 0이 되면 파괴로 보며 기믹이 다음 틱에 읽는다.
    // 기믹이 끝나거나 정리되면 봉인해 더 이상 피해를 받지 않는다
    /// <summary> 노란 여우불 하나의 서버 피격 몸체 </summary>
    public sealed class WolbaekYellowFoxfire : IDamageable
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int     _combatId;
        private readonly Vector3 _position;
        private readonly float   _maxHealth;
        private readonly float   _defense;
        private readonly float   _bodyRadius;

        private float                 _health;
        private EParticipantLifeState _lifeState = EParticipantLifeState.Alive;
        private double                _destroyedServerTime;
        private bool                  _isSealed;

        /****************************************
        *              Properties
        ****************************************/

        public int                   CombatId        => _combatId;
        public ECombatFaction        Faction         => ECombatFaction.Enemy;
        public EParticipantLifeState LifeState       => _lifeState;
        public bool                  IsSummon        => false;
        public int                   OwnerCombatId   => CombatIds.NONE;
        public Vector3               Position        => _position;
        public Vector3               Forward         => Vector3.forward;
        public float                 BodyRadius      => _bodyRadius;
        public float                 Health          => _health;
        public float                 MaxHealth       => _maxHealth;
        public float                 Defense         => _defense;
        public bool                  IsGuardingFront => false;

        /// <summary> 봉인된 뒤에는 피해와 공명을 받지 않는다 </summary>
        public bool IsInvulnerable => _isSealed;

        /// <summary> 파괴됐다 </summary>
        public bool IsDestroyed => _lifeState != EParticipantLifeState.Alive;

        /// <summary> 파괴된 서버 시각, 파괴 전에는 0이다 </summary>
        public double DestroyedServerTime => _destroyedServerTime;

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekYellowFoxfire(int combatId, Vector3 position, float maxHealth, float defense, float bodyRadius)
        {
            _combatId   = combatId;
            _position   = position;
            _maxHealth  = maxHealth;
            _health     = maxHealth;
            _defense    = defense;
            _bodyRadius = bodyRadius;
        }

        public void ApplyDamage(float appliedAmount, bool isLethal, double serverTime)
        {
            if ((_lifeState != EParticipantLifeState.Alive) || _isSealed)
            {
                return;
            }

            _health = isLethal ? 0f : Mathf.Max(0f, _health - appliedAmount);
            if (_health <= Constants.Combat.HEALTH_EPSILON)
            {
                _health              = 0f;
                _lifeState           = EParticipantLifeState.Dead;
                _destroyedServerTime = serverTime;
            }
        }

        /// <summary> 더 이상 피해를 받지 않게 한다, 기믹이 끝났거나 정리될 때 부른다 </summary>
        public void Seal()
        {
            _isSealed = true;
        }
    }
}
