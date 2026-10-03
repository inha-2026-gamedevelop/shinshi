// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.AI
{
    // 중심은 예고 시작 순간 서버가 고정한 위치이고 장판이 사라질 때까지 바뀌지 않는다
    /// <summary> 장판 한 개의 활성 입력, 패턴이 만들어 장판 관리자에 넘긴다 </summary>
    public readonly struct BossAreaSpec
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int            _castInstanceId;
        private readonly int            _areaId;
        private readonly bool           _skipMissedRounds;
        private readonly int            _attackerId;
        private readonly string         _definitionId;
        private readonly Vector3        _center;
        private readonly float          _radius;
        private readonly float          _durationSeconds;
        private readonly float          _tickInterval;
        private readonly float          _rawDamage;
        private readonly EDamageFlags   _flags;
        private readonly double         _activateServerTime;
        private readonly BossDamageGate _gate;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 예고부터 회복까지 한 번의 시전 번호, 한 시전이 장판 여러 개를 설치할 수 있어 그 장판들이 함께 쓰는 묶음 키다. 모든 회차의 피해 요청이 같은 값을 쓴다 </summary>
        public int CastInstanceId => _castInstanceId;

        /// <summary> 장판 한 개를 가리키는 키, 슬롯과 알림과 클라이언트 표현이 이 번호로 장판을 구분한다. 단일 장판 시전은 시전 번호와 같다 </summary>
        public int AreaId => _areaId;

        /// <summary> 참이면 지연된 틱에서 가장 최근 도래한 회차만 현재 위치로 판정하고 이전 회차는 건너뛴다, 아니면 놓친 회차를 모두 처리한다 </summary>
        public bool SkipMissedRounds => _skipMissedRounds;

        public int     AttackerId   => _attackerId;
        public string  DefinitionId => _definitionId;
        public Vector3 Center       => _center;

        /// <summary> m, 수평 거리 이하(경계 포함)의 대상이 범위 안이다 </summary>
        public float   Radius       => _radius;

        /// <summary> 활성 뒤 유지 시간(초), 이 시각에 사라지며 그때 추가 타격은 없다 </summary>
        public float   DurationSeconds => _durationSeconds;

        /// <summary> 타격 간격(초), 활성 순간이 첫 타격이다 </summary>
        public float   TickInterval => _tickInterval;

        /// <summary> 회당 방어 전 피해량 </summary>
        public float   RawDamage    => _rawDamage;

        public EDamageFlags Flags   => _flags;

        /// <summary> 활성된 서버 시각, 틱 시각이 아니라 FSM의 단계 경계라 틱이 거칠어도 정확하다 </summary>
        public double  ActivateServerTime => _activateServerTime;

        /// <summary> 피해는 이 게이트로만 낸다, 사망과 전투 종료로 닫히면 나가지 않는다 </summary>
        public BossDamageGate Gate  => _gate;

        /****************************************
        *                Methods
        ****************************************/

        // 장판 번호를 따로 주지 않으면 시전 번호가 장판 번호이고 놓친 회차를 모두 처리한다(1페이즈 P05의 단일 장판)
        public BossAreaSpec(int castInstanceId, int attackerId, string definitionId, Vector3 center, float radius,
            float durationSeconds, float tickInterval, float rawDamage, EDamageFlags flags, double activateServerTime,
            BossDamageGate gate)
            : this(castInstanceId, castInstanceId, attackerId, definitionId, center, radius, durationSeconds, tickInterval,
                rawDamage, flags, activateServerTime, gate, false)
        {
        }

        public BossAreaSpec(int castInstanceId, int areaId, int attackerId, string definitionId, Vector3 center, float radius,
            float durationSeconds, float tickInterval, float rawDamage, EDamageFlags flags, double activateServerTime,
            BossDamageGate gate, bool skipMissedRounds)
        {
            _areaId             = areaId;
            _skipMissedRounds   = skipMissedRounds;
            _castInstanceId     = castInstanceId;
            _attackerId         = attackerId;
            _definitionId       = definitionId;
            _center             = center;
            _radius             = radius;
            _durationSeconds    = durationSeconds;
            _tickInterval       = tickInterval;
            _rawDamage          = rawDamage;
            _flags              = flags;
            _activateServerTime = activateServerTime;
            _gate               = gate;
        }
    }
}
