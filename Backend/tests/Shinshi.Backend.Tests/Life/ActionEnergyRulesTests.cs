// System
using System;

// Third Party
using Xunit;

// Project
using Shinshi.Data.Life;

namespace Shinshi.Backend.Tests.Life
{
    /// <summary> 행동력 회복 경계(299초, 300초), 상한, 상한에서 쓸 때 기준 시각 초기화, 남은 진행분 보존을 검사한다 </summary>
    public sealed class ActionEnergyRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly DateTimeOffset G_Anchor = new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);

        /****************************************
        *                Methods
        ****************************************/

        [Theory]
        [InlineData(299, 10)]
        [InlineData(300, 11)]
        [InlineData(599, 11)]
        [InlineData(600, 12)]
        public void Recover_IntervalBoundary_AddsOnlyCompletedIntervals(int elapsedSeconds, long expected)
        {
            ActionEnergyState state = ActionEnergyRules.Recover(10, G_Anchor, G_Anchor.AddSeconds(elapsedSeconds));

            Assert.Equal(expected, state.Value);
        }

        [Fact]
        public void Recover_LongOffline_StopsAtCapAndHasNoNextRecovery()
        {
            ActionEnergyState state = ActionEnergyRules.Recover(0, G_Anchor, G_Anchor.AddDays(3));

            Assert.Equal(LifeContent.ACTION_MAX, state.Value);
            Assert.Null(ActionEnergyRules.NextRecoverAt(state));
        }

        [Fact]
        public void Recover_ClockBehindAnchor_DoesNotRecoverOrGoNegative()
        {
            ActionEnergyState state = ActionEnergyRules.Recover(5, G_Anchor, G_Anchor.AddSeconds(-1000));

            Assert.Equal(5L, state.Value);
            Assert.Equal(G_Anchor.AddSeconds(LifeContent.ACTION_RECOVER_INTERVAL_SECONDS),
                ActionEnergyRules.NextRecoverAt(state));
        }

        [Fact]
        public void TryConsume_BelowCap_AdvancesAnchorByUsedIntervalsOnly()
        {
            DateTimeOffset now = G_Anchor.AddSeconds(450);

            bool consumed = ActionEnergyRules.TryConsume(50, G_Anchor, now, LifeContent.ACTION_COST_PER_GATHER,
                out ActionEnergyState after);

            Assert.True(consumed);
            Assert.Equal(49L, after.Value);
            Assert.Equal(G_Anchor.AddSeconds(LifeContent.ACTION_RECOVER_INTERVAL_SECONDS), after.Anchor);

            // 남은 150초 진행분이 보존돼 150초 뒤 다음 회복이 온다
            Assert.Equal(now.AddSeconds(150), ActionEnergyRules.NextRecoverAt(ActionEnergyRules.Recover(after.Value,
                after.Anchor, now)));
        }

        [Fact]
        public void TryConsume_AtCap_ResetsAnchorToNow()
        {
            DateTimeOffset now = G_Anchor.AddSeconds(10000);

            bool consumed = ActionEnergyRules.TryConsume(99, G_Anchor, now, LifeContent.ACTION_COST_PER_GATHER,
                out ActionEnergyState after);

            Assert.True(consumed);
            Assert.Equal(LifeContent.ACTION_MAX - LifeContent.ACTION_COST_PER_GATHER, after.Value);
            Assert.Equal(now, after.Anchor);
        }

        [Fact]
        public void TryConsume_ReachesCapExactlyThroughRecovery_ResetsAnchorToNow()
        {
            // 98 + 2 구간 = 100. 상한에 닿은 뒤 경과한 시간은 다음 회복 진행분이 아니다
            DateTimeOffset now = G_Anchor.AddSeconds(2 * LifeContent.ACTION_RECOVER_INTERVAL_SECONDS + 200);

            bool consumed = ActionEnergyRules.TryConsume(98, G_Anchor, now, LifeContent.ACTION_COST_PER_GATHER,
                out ActionEnergyState after);

            Assert.True(consumed);
            Assert.Equal(98L, after.Value);
            Assert.Equal(now, after.Anchor);
        }

        [Fact]
        public void TryConsume_Insufficient_ReturnsFalseWithRecoveredValue()
        {
            bool consumed = ActionEnergyRules.TryConsume(1, G_Anchor, G_Anchor.AddSeconds(299),
                LifeContent.ACTION_COST_PER_GATHER, out ActionEnergyState after);

            Assert.False(consumed);
            Assert.Equal(1L, after.Value);
        }
    }
}
