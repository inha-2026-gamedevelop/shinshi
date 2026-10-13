// System
using System;

namespace Shinshi.AI
{
    // 발동 체력과 단계 시간과 반경과 피해 비율은 밸런스 값이라 실행 코드에 두지 않고 호출하는 쪽(데이터)이 정한다.
    // 기획서가 정한 값(3초 준비, 4초 추적, 2초 탈출, 1초 정리, 성공 3회, 60초, 반경 1.5m, 최대 체력 25%, 6초 반격)과
    // 기획이 정하지 않은 값(핵 사이 최소 간격, 하드 휩쓸기의 범위와 각도)을 구분한다. 뒤의 값은 입력 계약이며 시험값은 개발 카탈로그에만 둔다.
    // 하드 휩쓸기(Sweep)는 하드에만 있고 이지는 비워 둔다. 다음 경계(NextBoundary)는 반격 중 체력이 더 내려가지 못하는 경계다.
    // 하드는 0%에 정확히 멈추고 사망을 보류해 0% 기믹에 넘기며, 이지는 비워 두어 반격 중에도 기존 처치 규칙을 따른다
    /// <summary> 월백 여우불 유도와 봉인 핵 기믹의 설정 </summary>
    public sealed class WolbaekFoxfireSealSpec
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float HIT_DIAMETER_FACTOR = 2f;

        /****************************************
        *                Fields
        ****************************************/

        private readonly float                _triggerHealthRate;
        private readonly float?               _nextBoundaryHealthRate;
        private readonly float                _prepareSeconds;
        private readonly float                _trackSeconds;
        private readonly float                _escapeSeconds;
        private readonly float                _cleanupSeconds;
        private readonly int                  _requiredSuccesses;
        private readonly float                _timeLimitSeconds;
        private readonly float                _blastRadius;
        private readonly float                _coreHitRadius;
        private readonly float                _blastDamageRate;
        private readonly float                _minCoreSeparation;
        private readonly float                _counterSeconds;
        private readonly WolbaekSealSweepSpec _sweep;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 체력 비율이 이 값 이하(경계 포함)가 되면 발동한다, 그 전에는 이 값에서 멈춘다 </summary>
        public float TriggerHealthRate => _triggerHealthRate;

        /// <summary> 반격 중 보스가 더 내려가지 못하는 다음 경계, 이지는 없다 </summary>
        public bool HasNextBoundary => _nextBoundaryHealthRate.HasValue;

        /// <summary> 다음 경계의 체력 비율, HasNextBoundary가 거짓이면 의미가 없다 </summary>
        public float NextBoundaryHealthRate => _nextBoundaryHealthRate ?? 0f;

        /// <summary> 초, 핵과 여우불이 나타난 뒤 추적이 시작되기까지 </summary>
        public float PrepareSeconds => _prepareSeconds;

        /// <summary> 초, 낙하 위치가 대상을 따라가는 시간 </summary>
        public float TrackSeconds => _trackSeconds;

        /// <summary> 초, 낙하 위치가 고정된 뒤 폭발까지 빠져나갈 시간 </summary>
        public float EscapeSeconds => _escapeSeconds;

        /// <summary> 초, 판정 뒤 다음 차례까지의 정리 시간 </summary>
        public float CleanupSeconds => _cleanupSeconds;

        /// <summary> 기믹을 통과하는 데 필요한 누적 성공 횟수 </summary>
        public int RequiredSuccesses => _requiredSuccesses;

        /// <summary> 초, 기믹 전체 제한 시간, 차례 사이 정리 시간을 포함한다 </summary>
        public float TimeLimitSeconds => _timeLimitSeconds;

        /// <summary> m, 여우불 폭발 반경 </summary>
        public float BlastRadius => _blastRadius;

        /// <summary> m, 낙하 중심에서 핵 중심까지 이 거리 이내(경계 포함)면 핵 파괴로 인정한다 </summary>
        public float CoreHitRadius => _coreHitRadius;

        /// <summary> 플레이어 최대 체력 대비 방어 전 폭발 피해 비율, 방어는 되고 밀려남은 없다 </summary>
        public float BlastDamageRate => _blastDamageRate;

        /// <summary> m, 한 차례의 핵 중심 사이 최소 간격, 한 발이 두 핵을 함께 맞히지 못하도록 핵 적중 반경의 두 배보다 크다 </summary>
        public float MinCoreSeparation => _minCoreSeparation;

        /// <summary> 초, 성공 뒤 보스가 피해를 받는 시간 </summary>
        public float CounterSeconds => _counterSeconds;

        /// <summary> 하드 휩쓸기 설정, 이지는 null이다 </summary>
        public WolbaekSealSweepSpec Sweep => _sweep;

        /****************************************
        *                Methods
        ****************************************/

        // nextBoundaryHealthRate가 null이면 반격 중 다음 경계를 두지 않는다. 값이 있으면 0 이상이고 발동 비율보다 낮다
        public WolbaekFoxfireSealSpec(float triggerHealthRate, float? nextBoundaryHealthRate, float prepareSeconds,
            float trackSeconds, float escapeSeconds, float cleanupSeconds, int requiredSuccesses, float timeLimitSeconds,
            float blastRadius, float coreHitRadius, float blastDamageRate, float minCoreSeparation, float counterSeconds,
            WolbaekSealSweepSpec sweep)
        {
            if ((!IsFinite(triggerHealthRate)) || (triggerHealthRate <= 0f) || (triggerHealthRate > 1f))
            {
                throw new ArgumentOutOfRangeException(nameof(triggerHealthRate), "발동 체력 비율은 0 초과 1 이하다");
            }

            if (nextBoundaryHealthRate.HasValue)
            {
                float next = nextBoundaryHealthRate.Value;
                if ((!IsFinite(next)) || (next < 0f) || (next >= triggerHealthRate))
                {
                    throw new ArgumentOutOfRangeException(nameof(nextBoundaryHealthRate), "다음 경계 비율은 0 이상이고 발동 비율보다 낮다");
                }
            }

            RequirePositive(prepareSeconds, nameof(prepareSeconds), "준비 시간은 0보다 큰 유한값이다");
            RequirePositive(trackSeconds, nameof(trackSeconds), "추적 시간은 0보다 큰 유한값이다");
            RequirePositive(escapeSeconds, nameof(escapeSeconds), "탈출 시간은 0보다 큰 유한값이다");

            if ((!IsFinite(cleanupSeconds)) || (cleanupSeconds < 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(cleanupSeconds), "정리 시간은 0 이상의 유한값이다");
            }

            if (requiredSuccesses <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(requiredSuccesses), "필요한 성공 횟수는 1 이상이다");
            }

            RequirePositive(timeLimitSeconds, nameof(timeLimitSeconds), "전체 제한 시간은 0보다 큰 유한값이다");
            RequirePositive(blastRadius, nameof(blastRadius), "폭발 반경은 0보다 큰 유한값이다");
            RequirePositive(coreHitRadius, nameof(coreHitRadius), "핵 적중 반경은 0보다 큰 유한값이다");
            RequirePositive(blastDamageRate, nameof(blastDamageRate), "폭발 피해 비율은 0보다 큰 유한값이다");

            if ((!IsFinite(minCoreSeparation)) || (!(minCoreSeparation > (coreHitRadius * HIT_DIAMETER_FACTOR))))
            {
                throw new ArgumentOutOfRangeException(nameof(minCoreSeparation), "핵 사이 최소 간격은 핵 적중 반경의 두 배보다 크다");
            }

            if ((!IsFinite(counterSeconds)) || (counterSeconds < 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(counterSeconds), "반격 시간은 0 이상의 유한값이다");
            }

            if ((sweep != null) && (sweep.HitOffsetsSeconds[sweep.Count - 1] >= trackSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(sweep), "휩쓸기는 추적 시간 안에 모두 판정돼야 한다");
            }

            _triggerHealthRate      = triggerHealthRate;
            _nextBoundaryHealthRate = nextBoundaryHealthRate;
            _prepareSeconds         = prepareSeconds;
            _trackSeconds           = trackSeconds;
            _escapeSeconds          = escapeSeconds;
            _cleanupSeconds         = cleanupSeconds;
            _requiredSuccesses      = requiredSuccesses;
            _timeLimitSeconds       = timeLimitSeconds;
            _blastRadius            = blastRadius;
            _coreHitRadius          = coreHitRadius;
            _blastDamageRate        = blastDamageRate;
            _minCoreSeparation      = minCoreSeparation;
            _counterSeconds         = counterSeconds;
            _sweep                  = sweep;
        }

        private static void RequirePositive(float value, string name, string message)
        {
            if ((!IsFinite(value)) || (value <= 0f))
            {
                throw new ArgumentOutOfRangeException(name, message);
            }
        }

        private static bool IsFinite(float value)
        {
            return (!float.IsNaN(value)) && (!float.IsInfinity(value));
        }
    }
}
