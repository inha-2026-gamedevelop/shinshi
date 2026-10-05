// System
using System;

namespace Shinshi.Data.Life
{
    /// <summary> 계정 행동력 회복과 소비. 입력 시각은 모두 DB 시각이며 클라이언트 시각을 받지 않는다 </summary>
    public static class ActionEnergyRules
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 저장 값과 기준 시각에서 now까지의 회복을 반영한다. 상한을 넘는 회복은 쌓지 않는다.
        /// 저장 값을 바꾸지 않으므로 조회에서 써도 다음 소비의 결과가 달라지지 않는다 </summary>
        public static ActionEnergyState Recover(long storedValue, DateTimeOffset anchor, DateTimeOffset now)
        {
            long value = Math.Clamp(storedValue, 0, LifeContent.ACTION_MAX);
            if (value >= LifeContent.ACTION_MAX)
            {
                return new ActionEnergyState(LifeContent.ACTION_MAX, anchor, 0);
            }

            long intervals = CountIntervals(anchor, now);
            long needed = DivideRoundUp(LifeContent.ACTION_MAX - value, LifeContent.ACTION_RECOVER_AMOUNT);
            if (intervals >= needed)
            {
                return new ActionEnergyState(LifeContent.ACTION_MAX, anchor, intervals);
            }

            return new ActionEnergyState(value + (intervals * LifeContent.ACTION_RECOVER_AMOUNT), anchor, intervals);
        }

        /// <summary> 회복을 반영한 뒤 cost만큼 쓴다. 모자라면 false이고 상태는 그대로다.
        /// 상한에서 쓰면 기준 시각을 now로, 아니면 회복에 쓴 구간만큼만 앞으로 옮겨 남은 진행분을 보존한다 </summary>
        public static bool TryConsume(long storedValue, DateTimeOffset anchor, DateTimeOffset now, long cost,
            out ActionEnergyState after)
        {
            if (cost <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cost));
            }

            ActionEnergyState recovered = Recover(storedValue, anchor, now);
            if (recovered.Value < cost)
            {
                after = recovered;
                return false;
            }

            // 상한에 닿은 동안은 회복이 멈춰 있었으므로 그 사이 경과를 다음 회복 진행분으로 넘기지 않는다
            DateTimeOffset nextAnchor = recovered.Value >= LifeContent.ACTION_MAX
                ? now
                : anchor + TimeSpan.FromTicks(LifeContent.ActionRecoverInterval.Ticks * recovered.Intervals);
            after = new ActionEnergyState(recovered.Value - cost, nextAnchor, 0);
            return true;
        }

        /// <summary> 다음 회복 시각. 상한이면 null이다 </summary>
        public static DateTimeOffset? NextRecoverAt(ActionEnergyState recovered)
        {
            if (recovered.Value >= LifeContent.ACTION_MAX)
            {
                return null;
            }

            return recovered.Anchor + TimeSpan.FromTicks(LifeContent.ActionRecoverInterval.Ticks * (recovered.Intervals + 1));
        }

        // 기준 시각보다 앞선 now(시계 역행)는 회복 없음으로 본다
        private static long CountIntervals(DateTimeOffset anchor, DateTimeOffset now)
        {
            long elapsed = (now - anchor).Ticks;
            return elapsed <= 0 ? 0 : elapsed / LifeContent.ActionRecoverInterval.Ticks;
        }

        private static long DivideRoundUp(long value, long divisor)
        {
            return (value + divisor - 1) / divisor;
        }
    }

    /// <summary> 회복을 반영한 행동력. Intervals는 Anchor 뒤로 지난 회복 구간 수다 </summary>
    public readonly struct ActionEnergyState
    {
        /****************************************
        *              Properties
        ****************************************/

        public long           Value     { get; }
        public DateTimeOffset Anchor    { get; }
        public long           Intervals { get; }

        /****************************************
        *                Methods
        ****************************************/

        public ActionEnergyState(long value, DateTimeOffset anchor, long intervals)
        {
            Value     = value;
            Anchor    = anchor;
            Intervals = intervals;
        }
    }
}
