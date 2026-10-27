// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Dev
{
    /// <summary> 솔로 호스트 시험에서 서버 판정이 때릴 수 있는 죽지 않는 적 대역, 치명타 연출 등 맞는 쪽 반응을 보려고 둔다 </summary>
    public sealed class DevSoloDummyTarget : IDamageable
    {
        // 서버 레지스트리에 등록되는 판정 몸체만 가진다. 화면에 보이는 캡슐은 호스트가 따로 세우며 충돌체를 달지 않는다.
        // 체력은 매우 커서 사실상 죽지 않고, 치명 판정이 와도 체력만 채워 두고 사망으로 넘기지 않는다.
        // 이 클래스는 에디터 전용 어셈블리에 있어 빌드에 들어가지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private const float DUMMY_HEALTH = 1000000f;
        private const float DEFENSE      = 0f;

        private readonly int     _combatId;
        private readonly Vector3 _position;
        private readonly float   _bodyRadius;

        private float _health = DUMMY_HEALTH;

        /****************************************
        *              Properties
        ****************************************/

        public int                   CombatId       => _combatId;
        public ECombatFaction        Faction        => ECombatFaction.Enemy;
        public EParticipantLifeState LifeState      => EParticipantLifeState.Alive;
        public bool                  IsSummon       => false;
        public int                   OwnerCombatId  => CombatIds.NONE;
        public Vector3               Position       => _position;
        public Vector3               Forward        => Vector3.back;
        public float                 BodyRadius     => _bodyRadius;
        public float                 Health         => _health;
        public float                 MaxHealth      => DUMMY_HEALTH;
        public float                 Defense        => DEFENSE;
        public bool                  IsInvulnerable => false;
        public bool                  IsGuardingFront => false;

        /****************************************
        *                Methods
        ****************************************/

        public DevSoloDummyTarget(int combatId, Vector3 position, float bodyRadius)
        {
            _combatId   = combatId;
            _position   = position;
            _bodyRadius = bodyRadius;
        }

        public void ApplyDamage(float appliedAmount, bool isLethal, double serverTime)
        {
            _health = Mathf.Max(0f, _health - appliedAmount);
            if ((isLethal) || (_health < (DUMMY_HEALTH * 0.5f)))
            {
                _health = DUMMY_HEALTH;
            }
        }
    }
}
