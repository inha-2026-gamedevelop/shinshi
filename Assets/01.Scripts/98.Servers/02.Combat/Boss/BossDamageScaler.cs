// System
using System;

namespace Shinshi.Combat
{
    // 보스 패턴은 DamageRef x Href를 방어 전 피해로 낸다. 시작 인원 피해 배율은 패턴이 모르게 여기서 한 번만 곱한다
    /// <summary> 보스가 내는 피해에 시작 인원 피해 배율을 곱해 안쪽 판정에 넘긴다 </summary>
    public sealed class BossDamageScaler : IDamageResolver
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IDamageResolver _inner;
        private readonly float           _damageScale;

        /****************************************
        *                Methods
        ****************************************/

        public BossDamageScaler(IDamageResolver inner, float damageScale)
        {
            if (inner == null)
            {
                throw new ArgumentNullException(nameof(inner));
            }

            if ((!(damageScale > 0f)) || (float.IsInfinity(damageScale)))
            {
                throw new ArgumentOutOfRangeException(nameof(damageScale), "피해 배율은 0보다 큰 유한값이다");
            }

            _inner       = inner;
            _damageScale = damageScale;
        }

        public int IssueInstanceId()
        {
            return _inner.IssueInstanceId();
        }

        // 피해량만 바꾸고 번호와 위치는 그대로 넘긴다. 중복 적중과 반격 방어 판정이 원래 요청과 같아야 한다
        public DamageResult Apply(in DamageRequest request)
        {
            var scaled = new DamageRequest(request.AttackerId, request.HitTargetId, request.CastInstanceId,
                request.ActionInstanceId, request.RawDamage * _damageScale, request.Flags, request.SourcePosition,
                request.SourceDefinitionId);
            return _inner.Apply(in scaled);
        }
    }
}
