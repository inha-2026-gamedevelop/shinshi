// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.AI
{
    // 배치와 회전 방향과 속도는 예고 시작 순간 서버가 고정한 값이고 구조가 사라질 때까지 바뀌지 않는다. 회전각은 활성 서버 시각에서 흐른 시간으로 계산한다
    /// <summary> 별형 빔 구조 하나의 활성 입력, 패턴이 만들어 별형 관리자에 넘긴다 </summary>
    public readonly struct BossStarSpec
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int            _castInstanceId;
        private readonly int            _starId;
        private readonly int            _attackerId;
        private readonly string         _definitionId;
        private readonly BossStarShape  _shape;
        private readonly float          _durationSeconds;
        private readonly float          _tickInterval;
        private readonly float          _rawDamage;
        private readonly EDamageFlags   _flags;
        private readonly double         _activateServerTime;
        private readonly BossDamageGate _gate;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 한 시전의 번호, 모든 회차의 피해 요청이 같은 값을 쓴다 </summary>
        public int CastInstanceId => _castInstanceId;

        /// <summary> 구조 하나를 가리키는 키, 슬롯과 알림과 클라이언트 표현이 이 번호로 구분한다 </summary>
        public int StarId => _starId;

        public int           AttackerId   => _attackerId;
        public string        DefinitionId => _definitionId;
        public BossStarShape Shape        => _shape;

        /// <summary> 활성 뒤 회전과 유지 시간(초), 이 시각에 사라지며 그때 추가 타격은 없다 </summary>
        public float DurationSeconds => _durationSeconds;

        /// <summary> 타격 간격(초), 활성 순간이 첫 타격이다 </summary>
        public float TickInterval => _tickInterval;

        /// <summary> 회당 방어 전 피해량 </summary>
        public float RawDamage => _rawDamage;

        public EDamageFlags Flags => _flags;

        /// <summary> 활성된 서버 시각, 틱 시각이 아니라 FSM의 단계 경계라 틱이 거칠어도 정확하다 </summary>
        public double ActivateServerTime => _activateServerTime;

        public BossDamageGate Gate => _gate;

        /****************************************
        *                Methods
        ****************************************/

        public BossStarSpec(int castInstanceId, int starId, int attackerId, string definitionId, in BossStarShape shape,
            float durationSeconds, float tickInterval, float rawDamage, EDamageFlags flags, double activateServerTime,
            BossDamageGate gate)
        {
            _castInstanceId     = castInstanceId;
            _starId             = starId;
            _attackerId         = attackerId;
            _definitionId       = definitionId;
            _shape              = shape;
            _durationSeconds    = durationSeconds;
            _tickInterval       = tickInterval;
            _rawDamage          = rawDamage;
            _flags              = flags;
            _activateServerTime = activateServerTime;
            _gate               = gate;
        }
    }
}
