// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 연결이 끊긴 참가자의 서버 전투 상태, 재접속 유예 동안 보존했다가 같은 참가자가 돌아오면 그대로 되돌린다 </summary>
    public readonly struct ParticipantCombatSnapshot
    {
        // 복귀 시 무료 회복, 부활, 쿨다운 초기화가 없어야 하므로 체력, 생사, 다음 공격 시각을 모두 담는다.
        // 위반 누적을 담지 않으면 끊고 다시 붙는 것으로 입력 위반 기록을 지울 수 있다

        /****************************************
        *                Fields
        ****************************************/

        private readonly float                                 _health;
        private readonly EParticipantLifeState                 _lifeState;
        private readonly double                                _deathTime;
        private readonly double                                _nextAttackTime;
        private readonly uint                                  _lastSequence;
        private readonly int                                   _violationCount;
        private readonly double                                _violationWindowStart;
        private readonly PlayerInputGate.RateViolationSnapshot _rateViolations;
        private readonly PlayerInputGate.TokenBucketSnapshot   _tokenBucket;
        private readonly double                                _nextDodgeTime;
        private readonly Vector3                               _deathPosition;
        private readonly bool                                  _hasDeathPosition;
        private readonly long                                  _lifeStealWindow;
        private readonly float                                 _lifeStealWindowHealed;

        /****************************************
        *              Properties
        ****************************************/

        // 담지 않으면 끊고 다시 붙는 것으로 같은 구간의 흡수 회복 상한을 다시 채울 수 있다
        public long                  LifeStealWindow       => _lifeStealWindow;
        public float                 LifeStealWindowHealed => _lifeStealWindowHealed;


        public float                 Health               => _health;
        public EParticipantLifeState LifeState            => _lifeState;
        public double                DeathTime            => _deathTime;
        public double                NextAttackTime       => _nextAttackTime;
        public uint                  LastSequence         => _lastSequence;
        public int                   ViolationCount       => _violationCount;
        public double                ViolationWindowStart => _violationWindowStart;
        public double                NextDodgeTime        => _nextDodgeTime;

        // 재접속으로 새로 만든 개체는 스폰 위치에서 시작하므로 일반전 부활 위치를 따로 보존한다
        public Vector3               DeathPosition        => _deathPosition;
        public bool                  HasDeathPosition     => _hasDeathPosition;

        /****************************************
        *                Methods
        ****************************************/

        public ParticipantCombatSnapshot(float health, EParticipantLifeState lifeState, double deathTime, double nextAttackTime,
            uint lastSequence, int violationCount, double violationWindowStart, double nextDodgeTime = 0.0,
            Vector3 deathPosition = default, bool hasDeathPosition = false,
            PlayerInputGate.RateViolationSnapshot rateViolations = default,
            PlayerInputGate.TokenBucketSnapshot tokenBucket = default,
            long lifeStealWindow = LifeStealRules.NO_WINDOW, float lifeStealWindowHealed = 0f)
        {
            _deathPosition        = deathPosition;
            _hasDeathPosition     = hasDeathPosition;
            _lifeStealWindow       = lifeStealWindow;
            _lifeStealWindowHealed = lifeStealWindowHealed;
            _rateViolations       = rateViolations;
            _tokenBucket          = tokenBucket;
            _nextDodgeTime        = nextDodgeTime;
            _health               = health;
            _lifeState            = lifeState;
            _deathTime            = deathTime;
            _nextAttackTime       = nextAttackTime;
            _lastSequence         = lastSequence;
            _violationCount       = violationCount;
            _violationWindowStart = violationWindowStart;
        }
    }
}
