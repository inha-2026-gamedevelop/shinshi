// System
using System;

namespace Shinshi.AI
{
    // 기획서가 정한 값(4초 예고, 중앙 반경 8m, 생존자 1/2/3/4명에 노란 여우불 2/3/4/4개, 체력 8,000, 방어력 0, 4초 추적, 2초 뒤 폭발,
    // 반경 2m, 최대 체력 60%, 부활 보호 3초)과 기획이 정하지 않은 값(노란 여우불 몸체 반경, 묶음 사이 간격)을 구분한다.
    // 뒤의 값은 입력 계약이며 시험값은 개발 카탈로그에만 둔다. 밸런스 값이라 실행 코드에 두지 않고 호출하는 쪽(데이터)이 정한다.
    // 노란 여우불 체력과 방어력은 플레이 테스트 전 초기값이다
    /// <summary> 월백 하드 0% 최후의 영역 기믹의 설정 </summary>
    public sealed class WolbaekFinalAreaSpec
    {
        /****************************************
        *              Constants
        ****************************************/

        /// <summary> 노란 여우불 수 표의 길이, 생존자 1명부터 4명까지다 </summary>
        public const int YELLOW_COUNT_TABLE_LENGTH = 4;

        /****************************************
        *                Fields
        ****************************************/

        private readonly float _collapseSeconds;
        private readonly float _centralRadius;
        private readonly int[] _yellowCounts;
        private readonly float _yellowMaxHealth;
        private readonly float _yellowDefense;
        private readonly float _yellowBodyRadius;
        private readonly float _redTrackSeconds;
        private readonly float _redFuseSeconds;
        private readonly float _redBlastRadius;
        private readonly float _redSingleDamageRate;
        private readonly float _bundleGapSeconds;
        private readonly float _reviveProtectionSeconds;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 초, 중앙만 남는다는 예고부터 외곽 바닥을 제거하기까지 </summary>
        public float CollapseSeconds => _collapseSeconds;

        /// <summary> m, 바닥 제거 뒤에도 남는 중앙 반경 </summary>
        public float CentralRadius => _centralRadius;

        /// <summary> 노란 여우불 한 개의 체력 </summary>
        public float YellowMaxHealth => _yellowMaxHealth;

        /// <summary> 노란 여우불의 방어력 </summary>
        public float YellowDefense => _yellowDefense;

        /// <summary> m, 노란 여우불의 피격 몸체 반경 </summary>
        public float YellowBodyRadius => _yellowBodyRadius;

        /// <summary> 초, 빨간 여우불 묶음이 시작된 뒤 낙하 위치를 따라가는 시간 </summary>
        public float RedTrackSeconds => _redTrackSeconds;

        /// <summary> 초, 낙하 위치가 고정된 뒤 폭발까지의 시간 </summary>
        public float RedFuseSeconds => _redFuseSeconds;

        /// <summary> m, 빨간 여우불 폭발 반경 </summary>
        public float RedBlastRadius => _redBlastRadius;

        /// <summary> 플레이어 최대 체력 대비 방어 전 단일 적중 피해 비율, 방어와 반격으로 막지 못한다 </summary>
        public float RedSingleDamageRate => _redSingleDamageRate;

        /// <summary> 초, 한 묶음의 폭발 뒤 다음 묶음이 시작되기까지, 0이면 폭발 직후 이어서 시작한다 </summary>
        public float BundleGapSeconds => _bundleGapSeconds;

        /// <summary> 초, 이 기믹 중 부활한 참가자가 빨간 폭발 피해와 중복 적중 즉사를 받지 않는 시간, 추락은 막지 못한다 </summary>
        public float ReviveProtectionSeconds => _reviveProtectionSeconds;

        /****************************************
        *                Methods
        ****************************************/

        // yellowCounts는 생존자 1명부터 4명까지의 노란 여우불 수이고 1명 이상이며 줄어들지 않는다
        public WolbaekFinalAreaSpec(float collapseSeconds, float centralRadius, int[] yellowCounts, float yellowMaxHealth,
            float yellowDefense, float yellowBodyRadius, float redTrackSeconds, float redFuseSeconds, float redBlastRadius,
            float redSingleDamageRate, float bundleGapSeconds, float reviveProtectionSeconds)
        {
            RequirePositive(collapseSeconds, nameof(collapseSeconds), "바닥 제거 예고 시간은 0보다 큰 유한값이다");
            RequirePositive(centralRadius, nameof(centralRadius), "중앙 반경은 0보다 큰 유한값이다");

            if ((yellowCounts == null) || (yellowCounts.Length != YELLOW_COUNT_TABLE_LENGTH))
            {
                throw new ArgumentException("노란 여우불 수 표는 생존자 1명부터 4명까지 네 칸이다", nameof(yellowCounts));
            }

            for (int i = 0; i < yellowCounts.Length; ++i)
            {
                if (yellowCounts[i] <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(yellowCounts), "노란 여우불 수는 1 이상이다");
                }

                if ((i > 0) && (yellowCounts[i] < yellowCounts[i - 1]))
                {
                    throw new ArgumentOutOfRangeException(nameof(yellowCounts), "생존자가 늘면 노란 여우불 수가 줄지 않는다");
                }
            }

            RequirePositive(yellowMaxHealth, nameof(yellowMaxHealth), "노란 여우불 체력은 0보다 큰 유한값이다");
            RequireNonNegative(yellowDefense, nameof(yellowDefense), "노란 여우불 방어력은 0 이상의 유한값이다");
            RequirePositive(yellowBodyRadius, nameof(yellowBodyRadius), "노란 여우불 몸체 반경은 0보다 큰 유한값이다");
            RequirePositive(redTrackSeconds, nameof(redTrackSeconds), "빨간 여우불 추적 시간은 0보다 큰 유한값이다");
            RequirePositive(redFuseSeconds, nameof(redFuseSeconds), "빨간 여우불 폭발 대기 시간은 0보다 큰 유한값이다");
            RequirePositive(redBlastRadius, nameof(redBlastRadius), "빨간 폭발 반경은 0보다 큰 유한값이다");
            RequirePositive(redSingleDamageRate, nameof(redSingleDamageRate), "빨간 폭발 피해 비율은 0보다 큰 유한값이다");
            RequireNonNegative(bundleGapSeconds, nameof(bundleGapSeconds), "묶음 사이 간격은 0 이상의 유한값이다");
            RequireNonNegative(reviveProtectionSeconds, nameof(reviveProtectionSeconds), "부활 보호 시간은 0 이상의 유한값이다");

            _collapseSeconds         = collapseSeconds;
            _centralRadius           = centralRadius;
            _yellowCounts            = (int[])yellowCounts.Clone();
            _yellowMaxHealth         = yellowMaxHealth;
            _yellowDefense           = yellowDefense;
            _yellowBodyRadius        = yellowBodyRadius;
            _redTrackSeconds         = redTrackSeconds;
            _redFuseSeconds          = redFuseSeconds;
            _redBlastRadius          = redBlastRadius;
            _redSingleDamageRate     = redSingleDamageRate;
            _bundleGapSeconds        = bundleGapSeconds;
            _reviveProtectionSeconds = reviveProtectionSeconds;
        }

        /// <summary> 생존자 수에 맞는 노란 여우불 수, 4명 이상은 4명의 값이고 0명 이하는 0이다 </summary>
        public int GetYellowCount(int livingCount)
        {
            if (livingCount <= 0)
            {
                return 0;
            }

            int index = Math.Min(livingCount, YELLOW_COUNT_TABLE_LENGTH) - 1;
            return _yellowCounts[index];
        }

        private static void RequirePositive(float value, string name, string message)
        {
            if ((!IsFinite(value)) || (value <= 0f))
            {
                throw new ArgumentOutOfRangeException(name, message);
            }
        }

        private static void RequireNonNegative(float value, string name, string message)
        {
            if ((!IsFinite(value)) || (value < 0f))
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
