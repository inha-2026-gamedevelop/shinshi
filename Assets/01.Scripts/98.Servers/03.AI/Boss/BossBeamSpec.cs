// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.AI
{
    // 기하는 예고 시작 순간 서버가 고정한 값이고 빔이 사라질 때까지 바뀌지 않는다. 대상 판정은 BossBeamGeometry.Contains로만 한다
    /// <summary> 빔 한 줄의 활성 입력, 패턴이 만들어 빔 관리자에 넘긴다 </summary>
    public readonly struct BossBeamSpec
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int            _castInstanceId;
        private readonly int            _beamId;
        private readonly int            _attackerId;
        private readonly string         _definitionId;
        private readonly BossBeamShape  _shape;
        private readonly float          _durationSeconds;
        private readonly float          _tickInterval;
        private readonly float          _rawDamage;
        private readonly EDamageFlags   _flags;
        private readonly double         _activateServerTime;
        private readonly BossDamageGate _gate;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 한 시전의 번호, 그 시전이 설치한 빔들이 함께 쓰는 묶음 키다. 모든 회차의 피해 요청이 같은 값을 쓴다 </summary>
        public int CastInstanceId => _castInstanceId;

        /// <summary> 빔 한 줄을 가리키는 키, 슬롯과 알림과 클라이언트 표현이 이 번호로 빔을 구분한다 </summary>
        public int BeamId => _beamId;

        public int            AttackerId   => _attackerId;
        public string         DefinitionId => _definitionId;
        public BossBeamShape  Shape        => _shape;

        /// <summary> 활성 뒤 유지 시간(초), 이 시각에 사라지며 그때 추가 타격은 없다 </summary>
        public float          DurationSeconds => _durationSeconds;

        /// <summary> 타격 간격(초), 활성 순간이 첫 타격이다 </summary>
        public float          TickInterval => _tickInterval;

        /// <summary> 회당 방어 전 피해량 </summary>
        public float          RawDamage    => _rawDamage;

        public EDamageFlags   Flags        => _flags;

        /// <summary> 활성된 서버 시각, 틱 시각이 아니라 FSM의 단계 경계라 틱이 거칠어도 정확하다 </summary>
        public double         ActivateServerTime => _activateServerTime;

        /// <summary> 피해는 이 게이트로만 낸다, 사망과 전투 종료로 닫히면 나가지 않는다 </summary>
        public BossDamageGate Gate         => _gate;

        /****************************************
        *                Methods
        ****************************************/

        public BossBeamSpec(int castInstanceId, int beamId, int attackerId, string definitionId, in BossBeamShape shape,
            float durationSeconds, float tickInterval, float rawDamage, EDamageFlags flags, double activateServerTime,
            BossDamageGate gate)
        {
            _castInstanceId     = castInstanceId;
            _beamId             = beamId;
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
