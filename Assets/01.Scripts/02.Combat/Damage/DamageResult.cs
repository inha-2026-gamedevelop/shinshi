// System
using System;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 피해 요청 한 건의 처리 결과, IDamageResolver 구현이 만든다 </summary>
    public readonly struct DamageResult
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly EDamageOutcome _outcome;
        private readonly float          _appliedAmount;
        private readonly bool           _isLethal;
        private readonly bool           _isRearHit;

        /****************************************
        *              Properties
        ****************************************/

        public EDamageOutcome Outcome => _outcome;

        // 실제로 줄어든 체력, 남은 체력을 넘는 과잉분은 넣지 않는다
        public float AppliedAmount => _appliedAmount;

        // 이 피해로 대상이 쓰러졌다
        public bool IsLethal => _isLethal;

        // 대상 정면 기준 후방에서 맞았다. 후방 피격 기절이 반격을 끊는 판정에 쓴다
        public bool IsRearHit => _isRearHit;

        public bool IsApplied => _outcome == EDamageOutcome.Applied;

        // 공명이 인정하는 적중. 빗나감, 막힘, 무적은 0이고 과잉 피해로 처치한 마무리는 1회로 센다
        public bool CountsAsHit => (IsApplied) && ((_appliedAmount > 0f) || (_isLethal));

        /****************************************
        *                Methods
        ****************************************/

        private DamageResult(EDamageOutcome outcome, float appliedAmount, bool isLethal, bool isRearHit)
        {
            _outcome       = outcome;
            _appliedAmount = appliedAmount;
            _isLethal      = isLethal;
            _isRearHit     = isRearHit;
        }

        public static DamageResult Applied(float appliedAmount, bool isLethal, bool isRearHit)
        {
            return new DamageResult(EDamageOutcome.Applied, appliedAmount, isLethal, isRearHit);
        }

        // 막힌 공격은 정면이었으므로 후방 피격이 아니다
        public static DamageResult Guarded()
        {
            return new DamageResult(EDamageOutcome.Guarded, 0f, false, false);
        }

        /// <summary> 체력이 변하지 않은 결과, Applied와 Guarded는 전용 생성 메서드를 쓴다 </summary>
        public static DamageResult Ignored(EDamageOutcome outcome)
        {
            if ((outcome == EDamageOutcome.Applied) || (outcome == EDamageOutcome.Guarded))
            {
                throw new ArgumentException("적용, 막힘 결과는 Applied, Guarded로 만든다", nameof(outcome));
            }

            return new DamageResult(outcome, 0f, false, false);
        }
    }
}
