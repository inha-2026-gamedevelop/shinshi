// System
using System;

namespace Shinshi.AI
{
    // 발동 체력 비율과 각 단계 시간과 피해 비율은 밸런스 값이라 실행 코드에 두지 않고 호출하는 쪽(데이터)이 정한다.
    // 기획서가 "초기값"이나 "제안값"이라 적은 수치가 많아 모두 시험 전 값이다. 이지와 하드를 가르는 규칙은 주어지지 않아 같은 설정을 쓴다
    /// <summary> 월백 색 결계 기믹의 설정, 발동 체력과 중앙 집결과 지점 시간과 광역 공격과 연결 거리 피해 </summary>
    public sealed class WolbaekColorWardSpec
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly float   _triggerHealthRate;
        private readonly float   _gatherRadius;
        private readonly float   _gatherHoldSeconds;
        private readonly float   _wardRadius;
        private readonly int     _stageCount;
        private readonly float   _searchSeconds;
        private readonly float   _telegraphSeconds;
        private readonly int     _strikeCount;
        private readonly float   _strikeIntervalSeconds;
        private readonly float   _strikeDamageRate;
        private readonly float   _strikePushDistance;
        private readonly float   _maxSiteTravel;
        private readonly float   _minWardSeparation;
        private readonly float   _linkWarnDistance;
        private readonly float   _linkDamageDistance;
        private readonly float   _linkTickSeconds;
        private readonly float[] _linkTierUpperDistances;
        private readonly float[] _linkTierDamageRates;
        private readonly float   _rejoinGraceSeconds;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 체력 비율이 이 값 이하(경계 포함)가 되면 발동한다, 그 전에는 이 값에서 멈춘다 </summary>
        public float TriggerHealthRate => _triggerHealthRate;

        /// <summary> m, 중앙 공동 결계의 반경 </summary>
        public float GatherRadius => _gatherRadius;

        /// <summary> 초, 필수 참가자 전원이 중앙 결계 안에 연속으로 머물러야 하는 시간 </summary>
        public float GatherHoldSeconds => _gatherHoldSeconds;

        /// <summary> m, 색 결계의 보호 반경 </summary>
        public float WardRadius => _wardRadius;

        /// <summary> 새 위치에 결계가 나타나는 횟수 </summary>
        public int StageCount => _stageCount;

        /// <summary> 초, 결계가 나타난 뒤 첫 광역 공격까지, 자기 색 결계를 찾아 이동하는 시간이다 </summary>
        public float SearchSeconds => _searchSeconds;

        /// <summary> 초, 광역 공격 판정 앞의 예고 시간, 탐색 시간의 마지막 구간이기도 하다 </summary>
        public float TelegraphSeconds => _telegraphSeconds;

        /// <summary> 한 지점의 광역 공격 횟수 </summary>
        public int StrikeCount => _strikeCount;

        /// <summary> 초, 광역 공격 사이의 간격 </summary>
        public float StrikeIntervalSeconds => _strikeIntervalSeconds;

        /// <summary> 플레이어 최대 체력 대비 방어 전 피해 비율 </summary>
        public float StrikeDamageRate => _strikeDamageRate;

        /// <summary> m, 결계 밖에서 맞은 대상이 월백 바깥쪽으로 밀리는 거리 </summary>
        public float StrikePushDistance => _strikePushDistance;

        /// <summary> m, 같은 색 결계의 이전 위치에서 새 위치까지의 최대 거리, 첫 지점은 중앙 결계에서 잰다 </summary>
        public float MaxSiteTravel => _maxSiteTravel;

        /// <summary> m, 빨강과 파랑 결계 중심 사이의 최소 거리 </summary>
        public float MinWardSeparation => _minWardSeparation;

        /// <summary> m, 같은 색 두 명을 잇는 띠가 빨갛게 경고하기 시작하는 거리 </summary>
        public float LinkWarnDistance => _linkWarnDistance;

        /// <summary> m, 같은 색 두 명 사이가 이 거리를 넘으면 거리 피해가 시작된다, 이 거리 이하는 피해가 없다 </summary>
        public float LinkDamageDistance => _linkDamageDistance;

        /// <summary> 초, 거리를 넘은 채 온전히 지속돼야 첫 피해가 나는 시간이자 이후 피해 간격이다 </summary>
        public float LinkTickSeconds => _linkTickSeconds;

        /// <summary> 초, 복귀자가 같은 색 동료에게 합류할 수 있는 유예 시간 </summary>
        public float RejoinGraceSeconds => _rejoinGraceSeconds;

        /****************************************
        *                Methods
        ****************************************/

        // linkTierUpperDistances는 거리 구간의 위쪽 경계(초과~이하)이고 linkTierDamageRates는 구간별 최대 체력 대비 비율이다.
        // 비율은 경계 수보다 하나 많으며 마지막은 마지막 경계를 넘는 거리에 쓴다
        public WolbaekColorWardSpec(float triggerHealthRate, float gatherRadius, float gatherHoldSeconds, float wardRadius,
            int stageCount, float searchSeconds, float telegraphSeconds, int strikeCount, float strikeIntervalSeconds,
            float strikeDamageRate, float strikePushDistance, float maxSiteTravel, float minWardSeparation,
            float linkWarnDistance, float linkDamageDistance, float linkTickSeconds, float[] linkTierUpperDistances,
            float[] linkTierDamageRates, float rejoinGraceSeconds)
        {
            if ((!IsFinite(triggerHealthRate)) || (triggerHealthRate <= 0f) || (triggerHealthRate > 1f))
            {
                throw new ArgumentOutOfRangeException(nameof(triggerHealthRate), "발동 체력 비율은 0 초과 1 이하다");
            }

            RequirePositive(gatherRadius, nameof(gatherRadius), "중앙 결계 반경은 0보다 큰 유한값이다");
            RequirePositive(gatherHoldSeconds, nameof(gatherHoldSeconds), "연속 집결 시간은 0보다 큰 유한값이다");
            RequirePositive(wardRadius, nameof(wardRadius), "색 결계 반경은 0보다 큰 유한값이다");

            if (stageCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stageCount), "결계 지점 수는 1 이상이다");
            }

            RequirePositive(searchSeconds, nameof(searchSeconds), "탐색 시간은 0보다 큰 유한값이다");
            RequirePositive(telegraphSeconds, nameof(telegraphSeconds), "예고 시간은 0보다 큰 유한값이다");

            if (telegraphSeconds > searchSeconds)
            {
                throw new ArgumentOutOfRangeException(nameof(telegraphSeconds), "예고 시간은 탐색 시간 이하다");
            }

            if (strikeCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(strikeCount), "광역 공격 횟수는 1 이상이다");
            }

            RequirePositive(strikeIntervalSeconds, nameof(strikeIntervalSeconds), "광역 공격 간격은 0보다 큰 유한값이다");

            if (telegraphSeconds > strikeIntervalSeconds)
            {
                throw new ArgumentOutOfRangeException(nameof(telegraphSeconds), "예고 시간은 광역 공격 간격 이하다");
            }

            RequirePositive(strikeDamageRate, nameof(strikeDamageRate), "광역 공격 피해 비율은 0보다 큰 유한값이다");

            if ((!IsFinite(strikePushDistance)) || (strikePushDistance < 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(strikePushDistance), "밀려남 거리는 0 이상의 유한값이다");
            }

            RequirePositive(maxSiteTravel, nameof(maxSiteTravel), "결계 이동 최대 거리는 0보다 큰 유한값이다");
            RequirePositive(minWardSeparation, nameof(minWardSeparation), "두 색 결계 최소 거리는 0보다 큰 유한값이다");
            RequirePositive(linkWarnDistance, nameof(linkWarnDistance), "연결 띠 경고 거리는 0보다 큰 유한값이다");
            RequirePositive(linkDamageDistance, nameof(linkDamageDistance), "연결 거리 피해 시작 거리는 0보다 큰 유한값이다");

            if (linkWarnDistance > linkDamageDistance)
            {
                throw new ArgumentOutOfRangeException(nameof(linkWarnDistance), "경고 거리는 피해 시작 거리 이하다");
            }

            RequirePositive(linkTickSeconds, nameof(linkTickSeconds), "연결 거리 피해 간격은 0보다 큰 유한값이다");

            if ((!IsFinite(rejoinGraceSeconds)) || (rejoinGraceSeconds < 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(rejoinGraceSeconds), "합류 유예 시간은 0 이상의 유한값이다");
            }

            if ((linkTierUpperDistances == null) || (linkTierDamageRates == null)
                || (linkTierDamageRates.Length != (linkTierUpperDistances.Length + 1)))
            {
                throw new ArgumentException("연결 거리 피해 비율은 구간 경계보다 하나 많아야 한다", nameof(linkTierDamageRates));
            }

            float previous = linkDamageDistance;
            for (int i = 0; i < linkTierUpperDistances.Length; ++i)
            {
                if ((!IsFinite(linkTierUpperDistances[i])) || (!(linkTierUpperDistances[i] > previous)))
                {
                    throw new ArgumentOutOfRangeException(nameof(linkTierUpperDistances), "구간 경계는 피해 시작 거리보다 크고 오름차순이다");
                }

                previous = linkTierUpperDistances[i];
            }

            for (int i = 0; i < linkTierDamageRates.Length; ++i)
            {
                RequirePositive(linkTierDamageRates[i], nameof(linkTierDamageRates), "연결 거리 피해 비율은 0보다 큰 유한값이다");
            }

            _triggerHealthRate      = triggerHealthRate;
            _gatherRadius           = gatherRadius;
            _gatherHoldSeconds      = gatherHoldSeconds;
            _wardRadius             = wardRadius;
            _stageCount             = stageCount;
            _searchSeconds          = searchSeconds;
            _telegraphSeconds       = telegraphSeconds;
            _strikeCount            = strikeCount;
            _strikeIntervalSeconds  = strikeIntervalSeconds;
            _strikeDamageRate       = strikeDamageRate;
            _strikePushDistance     = strikePushDistance;
            _maxSiteTravel          = maxSiteTravel;
            _minWardSeparation      = minWardSeparation;
            _linkWarnDistance       = linkWarnDistance;
            _linkDamageDistance     = linkDamageDistance;
            _linkTickSeconds        = linkTickSeconds;
            _linkTierUpperDistances = (float[])linkTierUpperDistances.Clone();
            _linkTierDamageRates    = (float[])linkTierDamageRates.Clone();
            _rejoinGraceSeconds     = rejoinGraceSeconds;
        }

        /// <summary> 연결 거리에 맞는 최대 체력 대비 피해 비율, 피해 시작 거리 이하는 0이다. 경계 거리는 아래 구간에 속한다 </summary>
        public float GetLinkDamageRate(float distance)
        {
            if (!(distance > _linkDamageDistance))
            {
                return 0f;
            }

            for (int i = 0; i < _linkTierUpperDistances.Length; ++i)
            {
                if (distance <= _linkTierUpperDistances[i])
                {
                    return _linkTierDamageRates[i];
                }
            }

            return _linkTierDamageRates[_linkTierDamageRates.Length - 1];
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
