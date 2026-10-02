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

        /// <summary> 예고부터 회복까지 한 번의 시전 번호, 장판 한 개를 가리키는 키로도 쓴다. 모든 회차의 피해 요청이 같은 값을 쓴다 </summary>
        public int CastInstanceId => _castInstanceId;

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

        public BossAreaSpec(int castInstanceId, int attackerId, string definitionId, Vector3 center, float radius,
            float durationSeconds, float tickInterval, float rawDamage, EDamageFlags flags, double activateServerTime,
            BossDamageGate gate)
        {
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
