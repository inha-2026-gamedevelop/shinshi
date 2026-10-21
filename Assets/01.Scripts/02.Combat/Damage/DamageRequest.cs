// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 서버가 적중을 확정한 한 대상에 대한 피해 요청, 시전과 동작과 대상 번호를 나눠 중복 적중과 동작당 공명 1회를 가린다 </summary>
    public readonly struct DamageRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int          _attackerId;
        private readonly int          _hitTargetId;
        private readonly int          _castInstanceId;
        private readonly int          _actionInstanceId;
        private readonly float        _rawDamage;
        private readonly EDamageFlags _flags;
        private readonly Vector3      _sourcePosition;
        private readonly string       _sourceDefinitionId;
        private readonly Vector3      _travelDirection;

        /****************************************
        *              Properties
        ****************************************/

        // 피해를 낸 대상의 CombatId. 소환물이 때리면 소환물 번호이고 주인은 대상 조회로 찾는다
        public int AttackerId => _attackerId;

        public int HitTargetId => _hitTargetId;

        // 기술 한 번 시전, 또는 보스 패턴 한 번의 예고부터 회복까지
        public int CastInstanceId => _castInstanceId;

        // 시전 안의 공격 동작 하나. 한 동작이 여러 대상을 맞혀도 같은 번호이고, 지속 피해는 틱마다 새 번호를 받는다
        public int ActionInstanceId => _actionInstanceId;

        // 방어 감소 전 피해량, 방어 감소는 IDamageResolver 구현이 한다
        // 플레이어 치명타는 공통 서버 판정기가 대상별로 판정한다. 시작 인원 배율은 적의 요청을 감싸는 판정기가 적용한다
        public float RawDamage => _rawDamage;

        public EDamageFlags Flags => _flags;

        // 피해가 날아온 위치, 후방 판정 기준. 돌진과 중심 폭발은 공격자 위치, 투사체는 투사체 위치, 장판은 장판 중심
        public Vector3 SourcePosition => _sourcePosition;

        // 기술 또는 보스 패턴의 정의 ID, 판정 기록과 공명 규칙 조회에 쓴다
        public string SourceDefinitionId => _sourceDefinitionId;

        // 투사체가 맞는 순간 날아가던 방향, 근접과 범위 공격은 영벡터다. 맞은 쪽을 가르는 판정은 이 값이 있으면 SourcePosition보다 먼저 쓴다
        public Vector3 TravelDirection => _travelDirection;

        // Enum.HasFlag는 박싱하므로 비트 연산으로 검사한다
        public bool IsUnblockable   => (_flags & EDamageFlags.Unblockable) != 0;
        public bool GrantsResonance => (_flags & EDamageFlags.NoResonance) == 0;
        public bool IsDamageOverTime => (_flags & EDamageFlags.DamageOverTime) != 0;

        public bool HasValidIds => (_attackerId != CombatIds.NONE) && (_hitTargetId != CombatIds.NONE)
            && (_castInstanceId != CombatIds.NONE) && (_actionInstanceId != CombatIds.NONE);

        /****************************************
        *                Methods
        ****************************************/

        public DamageRequest(int attackerId, int hitTargetId, int castInstanceId, int actionInstanceId,
            float rawDamage, EDamageFlags flags, Vector3 sourcePosition, string sourceDefinitionId,
            Vector3 travelDirection = default)
        {
            _attackerId         = attackerId;
            _hitTargetId        = hitTargetId;
            _castInstanceId     = castInstanceId;
            _actionInstanceId   = actionInstanceId;
            _rawDamage          = rawDamage;
            _flags              = flags;
            _sourcePosition     = sourcePosition;
            _sourceDefinitionId = sourceDefinitionId;
            _travelDirection    = travelDirection;
        }

        /// <summary> 같은 동작의 다른 대상 요청을 만든다, 범위 공격이 대상마다 요청을 낼 때 쓴다 </summary>
        public DamageRequest WithTarget(int hitTargetId)
        {
            return new DamageRequest(_attackerId, hitTargetId, _castInstanceId, _actionInstanceId,
                _rawDamage, _flags, _sourcePosition, _sourceDefinitionId, _travelDirection);
        }
    }
}
