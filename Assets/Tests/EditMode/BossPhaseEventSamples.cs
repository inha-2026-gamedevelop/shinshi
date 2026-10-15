// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    // 복원 기록 시험과 직렬화 시험이 같은 값으로 쓰고 같은 기준으로 비교하도록 한곳에 둔다.
    // 필드마다 서로 다른 값을 넣어, 쓰기와 읽기의 순서가 한 칸이라도 어긋나면 비교가 실패하게 한다
    /// <summary> 무철과 월백 하드 2페이즈 사건 값의 시험용 표본과 필드별 비교 </summary>
    public static class BossPhaseEventSamples
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int    BOSS_ID      = 3;
        public const int    TARGET_ID    = 5;
        public const int    STRUCTURE_ID = 41;
        public const int    DASH_COUNT   = 3;
        public const int    STAGE_COUNT  = 4;
        public const int    LINE_COUNT   = 5;
        public const int    ZONE_COUNT   = 6;
        public const int    SHOT_COUNT   = 8;
        public const int    STEP_COUNT   = 3;
        public const int    CIRCLE_COUNT = 13;
        public const string SEQUENCE_ID  = "mucheol_ph2_p05";
        public const string DASH_ID      = "mucheol_ph2_p06";
        public const double START_TIME   = 10.25;
        public const float  DASH_LENGTH  = 8f;

        private const float  FLOAT_EPSILON = 0.0001f;
        private const double TIME_EPSILON  = 0.000001;

        private static readonly Vector3 ORIGIN    = new Vector3(1f, 0.5f, -2f);
        private static readonly Vector3 DIRECTION = new Vector3(0.6f, 0f, 0.8f);

        /****************************************
        *                Samples
        ****************************************/

        // 세 타가 부채꼴, 직사각형, 원형으로 모두 다르다
        public static BossStrikeChainStart MakeStrikeChain(int castId, EBossStrikeChainKind kind = EBossStrikeChainKind.Sequence)
        {
            return new BossStrikeChainStart(BOSS_ID, castId, SEQUENCE_ID, kind, ORIGIN, DIRECTION, START_TIME, 0.9f,
                new[] { EBossTelegraphShape.Fan, EBossTelegraphShape.Line, EBossTelegraphShape.Circle },
                new[] { 5f, 6f, 7f }, new[] { 60f, 0f, 360f }, new[] { 0f, 3f, 0f }, new[] { -30f, 30f, 0f },
                new[] { 0f, 0.45f, 1f });
        }

        public static BossMultiDashStart MakeMultiDash(int castId)
        {
            return new BossMultiDashStart(BOSS_ID, castId, DASH_ID, Vector3.zero, Vector3.forward, DASH_LENGTH, 2.5f, START_TIME,
                0.8f, DASH_COUNT);
        }

        public static BossMultiDashReaim MakeReaim(int castId, int dashIndex)
        {
            return new BossMultiDashReaim(castId, dashIndex, new Vector3(0f, 0f, 8f), Vector3.right, 6.5f, 3f, 12.5, 0.6f);
        }

        public static BossBattlefieldStart MakeBattlefield(int castId)
        {
            return new BossBattlefieldStart(BOSS_ID, castId, ORIGIN, DIRECTION, 44f, 315f, START_TIME, 4f);
        }

        public static BossChargeOutcome MakeCharge(int castId)
        {
            return new BossChargeOutcome(BOSS_ID, castId, true, new Vector3(4f, 0f, 9f), 12.5f, STRUCTURE_ID, 2.5f, 13.75);
        }

        public static BossStarStageStart MakeStarStage(int castId)
        {
            var shape = new BossStarShape(Vector3.one, 20f, Vector3.forward, 14f, 5, 2, 1.5f, -20f);
            return new BossStarStageStart(BOSS_ID, castId, castId + 100, shape, 36f, STAGE_COUNT, START_TIME, 2f, 0.75f);
        }

        public static BossGridBeamStart MakeGridBeam(int castId)
        {
            return new BossGridBeamStart(BOSS_ID, castId, Vector3.one, 20f, LINE_COUNT, 2f, 7f, START_TIME, 1.5f, 0.4f, 0.9f);
        }

        public static BossChainFanStart MakeChainFan(int castId)
        {
            return new BossChainFanStart(BOSS_ID, castId, ORIGIN, DIRECTION, 18f, ZONE_COUNT, 45f, START_TIME, 0.5f, 1.2f,
                0.35f, 3f);
        }

        public static BossTrackingAreaStart MakeTrackingArea(int castId, int areaId)
        {
            return new BossTrackingAreaStart(BOSS_ID, castId, areaId, TARGET_ID, new Vector3(2f, 0f, 3f), 3.5f, START_TIME, 1.5f,
                0.5f);
        }

        public static BossBarrageStart MakeBarrage(int castId)
        {
            return new BossBarrageStart(BOSS_ID, castId, ORIGIN, START_TIME, SHOT_COUNT, 0.6f, 0.25f, 1.2f);
        }

        public static BossBarrageShot MakeShot(int castId, int shotIndex)
        {
            return new BossBarrageShot(castId, shotIndex, shotIndex + 2, new Vector3(0f, 0f, 1f + shotIndex), 20f + shotIndex,
                START_TIME + shotIndex);
        }

        public static BossCircleBurstStart MakeCircleBurst(int castId, int circleCount = CIRCLE_COUNT)
        {
            var centers = new Vector3[circleCount];
            var radii   = new float[circleCount];
            for (int i = 0; i < circleCount; ++i)
            {
                centers[i] = new Vector3(i, 0f, -i);
                radii[i]   = 2f + (0.25f * i);
            }

            return new BossCircleBurstStart(BOSS_ID, castId, centers, radii, START_TIME, 1.8f);
        }

        /****************************************
        *               Comparisons
        ****************************************/

        public static void AssertSame(in BossStrikeChainStart expected, in BossStrikeChainStart actual)
        {
            Assert.AreEqual(expected.BossId, actual.BossId);
            Assert.AreEqual(expected.CastId, actual.CastId);
            Assert.AreEqual(expected.PatternId, actual.PatternId);
            Assert.AreEqual(expected.Kind, actual.Kind);
            Assert.AreEqual(expected.Origin, actual.Origin);
            Assert.AreEqual(expected.Direction, actual.Direction);
            Assert.AreEqual(expected.StartServerTime, actual.StartServerTime, TIME_EPSILON);
            Assert.AreEqual(expected.TelegraphSeconds, actual.TelegraphSeconds, FLOAT_EPSILON);
            Assert.AreEqual(expected.StepCount, actual.StepCount);
            CollectionAssert.AreEqual(expected.Shapes, actual.Shapes);
            AssertSame(expected.Ranges, actual.Ranges);
            AssertSame(expected.ArcAngles, actual.ArcAngles);
            AssertSame(expected.Widths, actual.Widths);
            AssertSame(expected.AimOffsetDegrees, actual.AimOffsetDegrees);
            AssertSame(expected.DelaySeconds, actual.DelaySeconds);
        }

        public static void AssertSame(in BossMultiDashStart expected, in BossMultiDashStart actual)
        {
            Assert.AreEqual(expected.BossId, actual.BossId);
            Assert.AreEqual(expected.CastId, actual.CastId);
            Assert.AreEqual(expected.PatternId, actual.PatternId);
            Assert.AreEqual(expected.Origin, actual.Origin);
            Assert.AreEqual(expected.Direction, actual.Direction);
            Assert.AreEqual(expected.Length, actual.Length, FLOAT_EPSILON);
            Assert.AreEqual(expected.Width, actual.Width, FLOAT_EPSILON);
            Assert.AreEqual(expected.StartServerTime, actual.StartServerTime, TIME_EPSILON);
            Assert.AreEqual(expected.TelegraphSeconds, actual.TelegraphSeconds, FLOAT_EPSILON);
            Assert.AreEqual(expected.DashCount, actual.DashCount);
        }

        public static void AssertSame(in BossMultiDashReaim expected, in BossMultiDashReaim actual)
        {
            Assert.AreEqual(expected.CastId, actual.CastId);
            Assert.AreEqual(expected.DashIndex, actual.DashIndex);
            Assert.AreEqual(expected.Origin, actual.Origin);
            Assert.AreEqual(expected.Direction, actual.Direction);
            Assert.AreEqual(expected.Length, actual.Length, FLOAT_EPSILON);
            Assert.AreEqual(expected.Width, actual.Width, FLOAT_EPSILON);
            Assert.AreEqual(expected.StartServerTime, actual.StartServerTime, TIME_EPSILON);
            Assert.AreEqual(expected.ReaimSeconds, actual.ReaimSeconds, FLOAT_EPSILON);
        }

        public static void AssertSame(in BossMultiDashSnapshot expected, in BossMultiDashSnapshot actual)
        {
            AssertSame(expected.Start, actual.Start);
            Assert.AreEqual(expected.DashIndex, actual.DashIndex);
            Assert.AreEqual(expected.Origin, actual.Origin);
            Assert.AreEqual(expected.Direction, actual.Direction);
            Assert.AreEqual(expected.Length, actual.Length, FLOAT_EPSILON);
            Assert.AreEqual(expected.Width, actual.Width, FLOAT_EPSILON);
            Assert.AreEqual(expected.IsDirectionLocked, actual.IsDirectionLocked);
            Assert.AreEqual(expected.TelegraphStartServerTime, actual.TelegraphStartServerTime, TIME_EPSILON);
            Assert.AreEqual(expected.TelegraphSeconds, actual.TelegraphSeconds, FLOAT_EPSILON);
            Assert.AreEqual(expected.HasDashStarted, actual.HasDashStarted);
            Assert.AreEqual(expected.DashStartServerTime, actual.DashStartServerTime, TIME_EPSILON);
            Assert.AreEqual(expected.DashDistance, actual.DashDistance, FLOAT_EPSILON);
            Assert.AreEqual(expected.DashSeconds, actual.DashSeconds, FLOAT_EPSILON);
            Assert.AreEqual(expected.DashSpeed, actual.DashSpeed, FLOAT_EPSILON);
            Assert.AreEqual(expected.StoppedDistance, actual.StoppedDistance, FLOAT_EPSILON);
            Assert.AreEqual(expected.StoppedByWall, actual.StoppedByWall);
            Assert.AreEqual(expected.IsTruncated, actual.IsTruncated);
        }

        public static void AssertSame(in BossBattlefieldStart expected, in BossBattlefieldStart actual)
        {
            Assert.AreEqual(expected.BossId, actual.BossId);
            Assert.AreEqual(expected.CastId, actual.CastId);
            Assert.AreEqual(expected.Origin, actual.Origin);
            Assert.AreEqual(expected.Direction, actual.Direction);
            Assert.AreEqual(expected.Radius, actual.Radius, FLOAT_EPSILON);
            Assert.AreEqual(expected.ArcAngle, actual.ArcAngle, FLOAT_EPSILON);
            Assert.AreEqual(expected.StartServerTime, actual.StartServerTime, TIME_EPSILON);
            Assert.AreEqual(expected.TelegraphSeconds, actual.TelegraphSeconds, FLOAT_EPSILON);
        }

        public static void AssertSame(in BossChargeOutcome expected, in BossChargeOutcome actual)
        {
            Assert.AreEqual(expected.BossId, actual.BossId);
            Assert.AreEqual(expected.CastId, actual.CastId);
            Assert.AreEqual(expected.IsCollision, actual.IsCollision);
            Assert.AreEqual(expected.Position, actual.Position);
            Assert.AreEqual(expected.TravelDistance, actual.TravelDistance, FLOAT_EPSILON);
            Assert.AreEqual(expected.StructureId, actual.StructureId);
            Assert.AreEqual(expected.RecoverySeconds, actual.RecoverySeconds, FLOAT_EPSILON);
            Assert.AreEqual(expected.ServerTime, actual.ServerTime, TIME_EPSILON);
        }

        public static void AssertSame(in BossStarStageStart expected, in BossStarStageStart actual)
        {
            Assert.AreEqual(expected.BossId, actual.BossId);
            Assert.AreEqual(expected.CastId, actual.CastId);
            Assert.AreEqual(expected.StarId, actual.StarId);
            Assert.AreEqual(expected.Shape.ArenaCenter, actual.Shape.ArenaCenter);
            Assert.AreEqual(expected.Shape.ArenaRadius, actual.Shape.ArenaRadius, FLOAT_EPSILON);
            Assert.AreEqual(expected.Shape.BaseDirection, actual.Shape.BaseDirection);
            Assert.AreEqual(expected.Shape.RingRadius, actual.Shape.RingRadius, FLOAT_EPSILON);
            Assert.AreEqual(expected.Shape.VertexCount, actual.Shape.VertexCount);
            Assert.AreEqual(expected.Shape.ConnectStep, actual.Shape.ConnectStep);
            Assert.AreEqual(expected.Shape.BeamWidth, actual.Shape.BeamWidth, FLOAT_EPSILON);
            Assert.AreEqual(expected.Shape.SignedDegreesPerSecond, actual.Shape.SignedDegreesPerSecond, FLOAT_EPSILON);
            Assert.AreEqual(expected.StepDegrees, actual.StepDegrees, FLOAT_EPSILON);
            Assert.AreEqual(expected.StageCount, actual.StageCount);
            Assert.AreEqual(expected.CastStartServerTime, actual.CastStartServerTime, TIME_EPSILON);
            Assert.AreEqual(expected.FirstStrikeSeconds, actual.FirstStrikeSeconds, FLOAT_EPSILON);
            Assert.AreEqual(expected.StrikeIntervalSeconds, actual.StrikeIntervalSeconds, FLOAT_EPSILON);
        }

        public static void AssertSame(in BossGridBeamStart expected, in BossGridBeamStart actual)
        {
            Assert.AreEqual(expected.BossId, actual.BossId);
            Assert.AreEqual(expected.CastId, actual.CastId);
            Assert.AreEqual(expected.ArenaCenter, actual.ArenaCenter);
            Assert.AreEqual(expected.ArenaRadius, actual.ArenaRadius, FLOAT_EPSILON);
            Assert.AreEqual(expected.LineCount, actual.LineCount);
            Assert.AreEqual(expected.LineWidth, actual.LineWidth, FLOAT_EPSILON);
            Assert.AreEqual(expected.LineSpacing, actual.LineSpacing, FLOAT_EPSILON);
            Assert.AreEqual(expected.CastStartServerTime, actual.CastStartServerTime, TIME_EPSILON);
            Assert.AreEqual(expected.FirstStrikeSeconds, actual.FirstStrikeSeconds, FLOAT_EPSILON);
            Assert.AreEqual(expected.StrikeIntervalSeconds, actual.StrikeIntervalSeconds, FLOAT_EPSILON);
            Assert.AreEqual(expected.AxisGapSeconds, actual.AxisGapSeconds, FLOAT_EPSILON);
        }

        public static void AssertSame(in BossChainFanStart expected, in BossChainFanStart actual)
        {
            Assert.AreEqual(expected.BossId, actual.BossId);
            Assert.AreEqual(expected.CastId, actual.CastId);
            Assert.AreEqual(expected.Origin, actual.Origin);
            Assert.AreEqual(expected.SafeDirection, actual.SafeDirection);
            Assert.AreEqual(expected.Range, actual.Range, FLOAT_EPSILON);
            Assert.AreEqual(expected.ZoneCount, actual.ZoneCount);
            Assert.AreEqual(expected.ZoneArc, actual.ZoneArc, FLOAT_EPSILON);
            Assert.AreEqual(expected.CastStartServerTime, actual.CastStartServerTime, TIME_EPSILON);
            Assert.AreEqual(expected.SafeRevealSeconds, actual.SafeRevealSeconds, FLOAT_EPSILON);
            Assert.AreEqual(expected.FirstBlastSeconds, actual.FirstBlastSeconds, FLOAT_EPSILON);
            Assert.AreEqual(expected.BlastIntervalSeconds, actual.BlastIntervalSeconds, FLOAT_EPSILON);
            Assert.AreEqual(expected.LingerSeconds, actual.LingerSeconds, FLOAT_EPSILON);
        }

        public static void AssertSame(in BossTrackingAreaStart expected, in BossTrackingAreaStart actual)
        {
            Assert.AreEqual(expected.BossId, actual.BossId);
            Assert.AreEqual(expected.CastId, actual.CastId);
            Assert.AreEqual(expected.AreaId, actual.AreaId);
            Assert.AreEqual(expected.TargetCombatId, actual.TargetCombatId);
            Assert.AreEqual(expected.Center, actual.Center);
            Assert.AreEqual(expected.Radius, actual.Radius, FLOAT_EPSILON);
            Assert.AreEqual(expected.StartServerTime, actual.StartServerTime, TIME_EPSILON);
            Assert.AreEqual(expected.FollowSeconds, actual.FollowSeconds, FLOAT_EPSILON);
            Assert.AreEqual(expected.FixedSeconds, actual.FixedSeconds, FLOAT_EPSILON);
        }

        public static void AssertSame(in BossBarrageStart expected, in BossBarrageStart actual)
        {
            Assert.AreEqual(expected.BossId, actual.BossId);
            Assert.AreEqual(expected.CastId, actual.CastId);
            Assert.AreEqual(expected.Origin, actual.Origin);
            Assert.AreEqual(expected.CastStartServerTime, actual.CastStartServerTime, TIME_EPSILON);
            Assert.AreEqual(expected.ShotCount, actual.ShotCount);
            Assert.AreEqual(expected.TelegraphSeconds, actual.TelegraphSeconds, FLOAT_EPSILON);
            Assert.AreEqual(expected.ShotIntervalSeconds, actual.ShotIntervalSeconds, FLOAT_EPSILON);
            Assert.AreEqual(expected.Width, actual.Width, FLOAT_EPSILON);
        }

        public static void AssertSame(in BossBarrageShot expected, in BossBarrageShot actual)
        {
            Assert.AreEqual(expected.CastId, actual.CastId);
            Assert.AreEqual(expected.ShotIndex, actual.ShotIndex);
            Assert.AreEqual(expected.SectorIndex, actual.SectorIndex);
            Assert.AreEqual(expected.Direction, actual.Direction);
            Assert.AreEqual(expected.PathLength, actual.PathLength, FLOAT_EPSILON);
            Assert.AreEqual(expected.ScheduledServerTime, actual.ScheduledServerTime, TIME_EPSILON);
        }

        public static void AssertSame(in BossBarrageSnapshot expected, in BossBarrageSnapshot actual)
        {
            AssertSame(expected.Start, actual.Start);
            Assert.AreEqual(expected.FiredCount, actual.FiredCount);
            Assert.AreEqual(expected.Shots.Length, actual.Shots.Length);
            for (int i = 0; i < expected.Shots.Length; ++i)
            {
                AssertSame(in expected.Shots[i], in actual.Shots[i]);
            }
        }

        public static void AssertSame(in BossCircleBurstStart expected, in BossCircleBurstStart actual)
        {
            Assert.AreEqual(expected.BossId, actual.BossId);
            Assert.AreEqual(expected.CastId, actual.CastId);
            Assert.AreEqual(expected.CastStartServerTime, actual.CastStartServerTime, TIME_EPSILON);
            Assert.AreEqual(expected.StrikeSeconds, actual.StrikeSeconds, FLOAT_EPSILON);
            Assert.AreEqual(expected.CircleCount, actual.CircleCount);
            CollectionAssert.AreEqual(expected.Centers, actual.Centers);
            AssertSame(expected.Radii, actual.Radii);
        }

        private static void AssertSame(IReadOnlyList<float> expected, IReadOnlyList<float> actual)
        {
            Assert.AreEqual(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; ++i)
            {
                Assert.AreEqual(expected[i], actual[i], FLOAT_EPSILON);
            }
        }
    }
}
