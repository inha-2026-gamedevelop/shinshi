// System
using System;

namespace Shinshi.Combat
{
    /// <summary> 생명력 흡수 회복량 계산, 1초 구간마다 최대 생명 비율로 상한을 둔다 </summary>
    public static class LifeStealRules
    {
        // 클라이언트는 이 계산을 알 필요가 없어 서버 폴더에 둔다. 체력은 기존 복제 값으로만 전달된다

        /****************************************
        *                Fields
        ****************************************/

        // 아직 어떤 구간도 시작하지 않은 상태, 첫 회복이 새 구간을 연다
        public const long NO_WINDOW = long.MinValue;

        // 상한이 "초당" 비율이라 구간 길이는 1초로 고정한다
        private const double WINDOW_SECONDS = 1.0;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 이번 타격으로 회복할 양, 구간 누계를 갱신한다. 조건이 맞지 않으면 0이고 누계는 그대로다 </summary>
        public static float Grant(float appliedDamage, float rate, float health, float maxHealth, float capPerSecond,
            double serverTime, ref long window, ref float windowHealed)
        {
            // NaN은 비교가 모두 거짓이라 이 형태로 같이 걸러진다
            if ((!(appliedDamage > 0f)) || (!(rate > 0f)) || (!(maxHealth > 0f)) || (!(capPerSecond > 0f))
                || (float.IsInfinity(appliedDamage)) || (float.IsInfinity(rate)) || (float.IsInfinity(maxHealth))
                || (float.IsInfinity(capPerSecond)) || (double.IsNaN(serverTime)) || (double.IsInfinity(serverTime)))
            {
                return 0f;
            }

            // 시각이 되감겨 이전 구간 번호가 나와도 다른 구간이면 새로 시작한다
            long current = (long)Math.Floor(serverTime / WINDOW_SECONDS);
            if ((current != window) || (!(windowHealed >= 0f)))
            {
                window       = current;
                windowHealed = 0f;
            }

            float budget  = (maxHealth * capPerSecond) - windowHealed;
            float missing = maxHealth - health;
            float grant   = Math.Min(appliedDamage * rate, Math.Min(budget, missing));
            if (!(grant > 0f))
            {
                return 0f;
            }

            // 누계에는 실제로 오른 양만 더한다. 가득 찬 체력에서 버려진 몫이 상한을 먼저 채우지 않는다
            windowHealed += grant;
            return grant;
        }
    }
}
