// System
using System;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Combat
{
    /// <summary> 한 시전이 만들 효과의 구성과 일정을 한 번에 풀어 둔 값, 필요한 효과 칸과 마지막 일정과 실제 방출이 이 값 하나를 읽는다 </summary>
    public readonly struct SkillExecutionPlan
    {
        // 칸 수와 마지막 일정은 기존 계산과 같은 결과를 낸다. 방출하지 않는 모양(버프, 반격)의 칸 예약과
        // 방출하지 않는 후속 일정의 꼬리 포함도 시점 보존을 위해 그대로 둔다

        /****************************************
        *                Fields
        ****************************************/

        private readonly bool  _reservesNothing;
        private readonly bool  _isProjectile;
        private readonly int   _projectileCount;
        private readonly int   _volleyCount;
        private readonly float _repeatInterval;
        private readonly bool  _hasPrimaryArea;
        private readonly bool  _hasSecondHitCoefficient;
        private readonly float _secondHitDelay;
        private readonly bool  _hasFollowUpStrike;
        private readonly bool  _hasShockwave;
        private readonly bool  _hasFollowUpProjectile;
        private readonly float _coreDelay;
        private readonly bool  _hasDurationEffect;

        /****************************************
        *              Properties
        ****************************************/

        public bool  IsProjectile           => _isProjectile;
        public int   ProjectileCount        => _projectileCount;
        public int   VolleyCount            => _volleyCount;
        public float CoreDelay              => _coreDelay;
        public bool  HasFollowUpStrike      => _hasFollowUpStrike;
        public bool  HasShockwave           => _hasShockwave;
        public bool  HasFollowUpProjectile  => _hasFollowUpProjectile;
        public bool  HasDurationEffect      => _hasDurationEffect;
        public float SecondHitDelay         => _secondHitDelay;

        // 첫 타격 판정이 있는 범위 기술에서만 두 번째 타격을 방출한다
        public bool  EmitsSecondHit         => (_hasPrimaryArea) && (_hasSecondHitCoefficient);

        // 후속 투사체가 기대는 마지막 기준 일정, 투사체형은 마지막 발사 순번이고 범위형은 두 번째 타격 시각이다
        public float FollowUpProjectileBase => _isProjectile ? ((_volleyCount - 1) * _repeatInterval)
            : (EmitsSecondHit ? _secondHitDelay : 0f);

        /// <summary> 확정 전에 비워 둬야 하는 효과 칸 수 </summary>
        public int SlotCount
        {
            get
            {
                if (_reservesNothing)
                {
                    return 0;
                }
                if (_isProjectile)
                {
                    return (_projectileCount * _volleyCount) + (_hasFollowUpProjectile ? 1 : 0);
                }
                int slots = 0;
                if (_hasPrimaryArea)
                {
                    if ((_hasFollowUpStrike) || (_hasShockwave) || (_hasFollowUpProjectile))
                    {
                        ++slots;
                    }
                    if (_hasSecondHitCoefficient)
                    {
                        ++slots;
                    }
                }
                if (_hasDurationEffect)
                {
                    ++slots;
                }
                return slots;
            }
        }

        /// <summary> 동작 시작 뒤 아직 나가지 않았을 수 있는 마지막 타격까지의 시간(초), 이 시간 안은 같은 동작이다 </summary>
        public float ScheduledTail
        {
            get
            {
                float lastHit = (_volleyCount > 1) ? ((_volleyCount - 1) * _repeatInterval) : 0f;
                if (_hasSecondHitCoefficient)
                {
                    lastHit = Math.Max(lastHit, _secondHitDelay);
                }
                float tail = lastHit;
                if (_hasFollowUpStrike)
                {
                    tail = Math.Max(tail, _coreDelay);
                }
                else if (_hasFollowUpProjectile)
                {
                    tail = Math.Max(tail, lastHit + _coreDelay);
                }
                return tail;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public SkillExecutionPlan(SkillCatalogSO.SkillDefinition skill, in SkillModifiers modifiers)
        {
            SkillCoreVariant core = modifiers.Core;
            ESkillShape      area = skill.Area.Shape;

            _reservesNothing         = (area == ESkillShape.Summon) || (area == ESkillShape.Line);
            _isProjectile            = TalismanModifierRules.GetShape(skill, in modifiers) == ESkillShape.Projectile;
            _projectileCount         = TalismanModifierRules.GetProjectileCount(skill, in modifiers);
            _volleyCount             = Math.Max(1, skill.Effect.RepeatCount);
            _repeatInterval          = skill.Effect.RepeatInterval;
            _hasPrimaryArea          = skill.Effect.DamageCoefficient > 0f;
            _hasSecondHitCoefficient = skill.Effect.SecondHitCoefficient > 0f;
            _secondHitDelay          = skill.Effect.SecondHitDelay;
            _hasFollowUpStrike       = core.Is(ETalismanEffectKind.FollowUpStrike);
            _hasShockwave            = core.Is(ETalismanEffectKind.Shockwave);
            _hasFollowUpProjectile   = core.Is(ETalismanEffectKind.FollowUpProjectile);
            _coreDelay               = core.Delay;
            _hasDurationEffect       = skill.Effect.Duration > 0f;
        }

        /// <summary> 투사체 발사 순번 하나가 첫 발사에서 떨어진 시간(초) </summary>
        public float GetShotOffset(int shot)
        {
            return shot * _repeatInterval;
        }
    }
}
