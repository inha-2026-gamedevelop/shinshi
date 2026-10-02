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

        private readonly float                 _health;
        private readonly EParticipantLifeState _lifeState;
        private readonly double                _deathTime;
        private readonly double                _nextAttackTime;
        private readonly uint                  _lastSequence;
        private readonly int                   _violationCount;
        private readonly double                _violationWindowStart;
        private readonly double                _nextDodgeTime;

        /****************************************
        *              Properties
        ****************************************/

        public float                 Health               => _health;
        public EParticipantLifeState LifeState            => _lifeState;
        public double                DeathTime            => _deathTime;
        public double                NextAttackTime       => _nextAttackTime;
        public uint                  LastSequence         => _lastSequence;
        public int                   ViolationCount       => _violationCount;
        public double                ViolationWindowStart => _violationWindowStart;
        public double                NextDodgeTime        => _nextDodgeTime;

        /****************************************
        *                Methods
        ****************************************/

        public ParticipantCombatSnapshot(float health, EParticipantLifeState lifeState, double deathTime, double nextAttackTime,
            uint lastSequence, int violationCount, double violationWindowStart, double nextDodgeTime = 0.0)
        {
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
