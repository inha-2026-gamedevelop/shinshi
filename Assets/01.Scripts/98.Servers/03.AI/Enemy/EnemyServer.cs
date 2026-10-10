// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

using EnemyDefinition = Shinshi.Data.EnemyCatalogSO.EnemyDefinition;

namespace Shinshi.AI
{
    // 체력, 생사, 위치만 가진다. 어디로 움직이고 언제 때릴지는 두뇌(MeleeEnemyBrain)가 정하고,
    // 화면 표현은 이 값을 읽는 View가 맡는다. 치명타는 BossServer와 같이 그 자리에서 Dead로 바꾼다
    /// <summary> 서버의 일반 적 개체, 피해를 받는 몸체 </summary>
    public sealed class EnemyServer : IDamageable
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int     _combatId;
        private readonly float   _maxHealth;
        private readonly float   _defense;
        private readonly float   _bodyRadius;
        private readonly float   _separationRadius;

        private Vector3               _position;
        private Vector3               _forward;
        private float                 _health;
        private EParticipantLifeState _lifeState = EParticipantLifeState.Alive;

        /****************************************
        *              Properties
        ****************************************/

        public int                   CombatId      => _combatId;
        public ECombatFaction        Faction       => ECombatFaction.Enemy;
        public EParticipantLifeState LifeState     => _lifeState;
        public bool                  IsSummon      => false;
        public int                   OwnerCombatId => CombatIds.NONE;
        public Vector3               Position      => _position;
        public Vector3               Forward       => _forward;
        public float                 BodyRadius    => _bodyRadius;

        // BodyRadius는 감지, 피격, 돌진의 벽 정지 거리 기준이고 이 값은 적끼리 벌어지는 간격 기준이다. 서로 바꿔 쓰지 않는다
        /// <summary> 적끼리 겹치지 않게 하는 이동용 반경, 0이면 간격 계산에 참여하지 않는다 </summary>
        public float SeparationRadius => _separationRadius;

        public float Health          => _health;
        public float MaxHealth       => _maxHealth;
        public float Defense         => _defense;
        public bool  IsInvulnerable  => false;
        public bool  IsGuardingFront => false;

        /****************************************
        *                Methods
        ****************************************/

        // referenceAttack은 전장 지역의 Aref다. 플레이어의 현재 능력치를 대신 넣지 않는다.
        // 몸체 반경은 기획에 값이 없어 호출자가 정한다. healthScale은 출정 시작 인원 체력 배율(SessionConfigSO)이고 기본 1이다.
        // separationRadius는 적끼리 간격용 반경이며 기본 0은 겹침 방지에 참여하지 않는 기존 동작이다
        public EnemyServer(EnemyDefinition definition, int combatId, Vector3 position, Vector3 forward,
            float referenceAttack, float bodyRadius, float healthScale = 1f, float separationRadius = 0f)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (combatId == CombatIds.NONE)
            {
                throw new ArgumentOutOfRangeException(nameof(combatId), "전투 번호가 없다");
            }

            if ((!(referenceAttack > 0f)) || (float.IsInfinity(referenceAttack)))
            {
                throw new ArgumentOutOfRangeException(nameof(referenceAttack), "지역 기준 공격력은 0보다 큰 유한값이다");
            }

            if ((!(healthScale > 0f)) || (float.IsInfinity(healthScale)))
            {
                throw new ArgumentOutOfRangeException(nameof(healthScale), "체력 배율은 0보다 큰 유한값이다");
            }

            if ((!(separationRadius >= 0f)) || (float.IsInfinity(separationRadius)))
            {
                throw new ArgumentOutOfRangeException(nameof(separationRadius), "간격 반경은 0 이상의 유한값이다");
            }

            _combatId         = combatId;
            _position         = position;
            _forward          = BossTelegraphRules.Flatten(forward, Vector3.forward);
            _maxHealth        = definition.HealthRef * referenceAttack * healthScale;
            _health           = _maxHealth;
            _defense          = definition.Defense;
            _bodyRadius       = bodyRadius;
            _separationRadius = separationRadius;
        }

        public void ApplyDamage(float appliedAmount, bool isLethal, double serverTime)
        {
            if (_lifeState != EParticipantLifeState.Alive)
            {
                return;
            }

            if (isLethal)
            {
                _health    = 0f;
                _lifeState = EParticipantLifeState.Dead;
                return;
            }

            _health = Mathf.Max(0f, _health - appliedAmount);
        }

        /// <summary> 두뇌가 이동 결과를 넣는다 </summary>
        public void SetPosition(Vector3 position)
        {
            _position = position;
        }

        /// <summary> 두뇌가 정면을 정한다, 수평 단위 벡터로 바꿔 저장한다 </summary>
        public void SetForward(Vector3 forward)
        {
            _forward = BossTelegraphRules.Flatten(forward, _forward);
        }
    }
}
