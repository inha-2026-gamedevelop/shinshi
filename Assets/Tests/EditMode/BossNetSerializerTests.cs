// System
using System;
using System.IO;

// Unity
using UnityEngine;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Common;

using Samples = Shinshi.Tests.BossPhaseEventSamples;

namespace Shinshi.Tests
{
    public class BossNetSerializerTests
    {
        // 무철과 월백 하드 2페이즈 사건 값이 쓰고 읽은 뒤에도 필드마다 같은지, 쓴 만큼만 읽는지 본다.
        // 길이가 어긋나거나 상한을 넘는 목록은 서버가 보내지 않아야 하므로 쓰기에서 막고, 받은 쪽은 프로토콜 불일치로 보고 읽기를 멈춘다

        /****************************************
        *                Fields
        ****************************************/

        private const int    CAST_ID       = 11;
        private const int    AREA_ID       = 21;
        private const int    SHOWN_SHOTS   = 3;
        private const int    SHORT_COUNT   = 2;    // 표본 타 목록(세 타)보다 하나 짧다
        private const int    LIST_COUNT    = 5;    // 타 목록의 형태를 뺀 값 목록 수
        private const double TIME_EPSILON  = 0.000001;

        /****************************************
        *            Mucheol Tests
        ****************************************/

        [TestCase(EBossStrikeChainKind.Combo)]
        [TestCase(EBossStrikeChainKind.Sequence)]
        public void StrikeChainStart_WriteRead_KeepsEveryStep(EBossStrikeChainKind kind)
        {
            BossStrikeChainStart source = Samples.MakeStrikeChain(CAST_ID, kind);

            BossStrikeChainStart copy = RoundTrip(writer => writer.WriteBossStrikeChainStart(source),
                reader => reader.ReadBossStrikeChainStart());

            Samples.AssertSame(in source, in copy);
            Assert.AreEqual(Samples.STEP_COUNT, copy.StepCount);
        }

        [Test]
        public void StrikeChainSnapshot_WriteRead_KeepsProgress()
        {
            var source = new BossStrikeChainSnapshot
            {
                Start             = Samples.MakeStrikeChain(CAST_ID),
                Direction         = Vector3.left,
                IsDirectionLocked = true,
                StruckCount       = 2
            };

            BossStrikeChainSnapshot copy = RoundTrip(writer => writer.WriteBossStrikeChainSnapshot(source),
                reader => reader.ReadBossStrikeChainSnapshot());

            Samples.AssertSame(source.Start, copy.Start);
            Assert.AreEqual(Vector3.left, copy.Direction);
            Assert.IsTrue(copy.IsDirectionLocked);
            Assert.AreEqual(2, copy.StruckCount);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void StrikeChainStart_WriteListShorterThanShapes_Throws(int shortList)
        {
            var lists = new float[LIST_COUNT][];
            for (int i = 0; i < LIST_COUNT; ++i)
            {
                lists[i] = new float[(i == shortList) ? SHORT_COUNT : Samples.STEP_COUNT];
            }

            var start = new BossStrikeChainStart(Samples.BOSS_ID, CAST_ID, Samples.SEQUENCE_ID, EBossStrikeChainKind.Sequence,
                Vector3.zero, Vector3.forward, Samples.START_TIME, 1f, new EBossTelegraphShape[Samples.STEP_COUNT], lists[0],
                lists[1], lists[2], lists[3], lists[4]);

            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                Assert.Throws<InvalidDataException>(() => writer.WriteBossStrikeChainStart(start));
            }
        }

        [Test]
        public void StrikeChainStart_WriteMissingList_Throws()
        {
            var start = new BossStrikeChainStart(Samples.BOSS_ID, CAST_ID, Samples.SEQUENCE_ID, EBossStrikeChainKind.Combo,
                Vector3.zero, Vector3.forward, Samples.START_TIME, 1f, new EBossTelegraphShape[Samples.STEP_COUNT],
                new float[Samples.STEP_COUNT], null, new float[Samples.STEP_COUNT], new float[Samples.STEP_COUNT],
                new float[Samples.STEP_COUNT]);

            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                Assert.Throws<InvalidDataException>(() => writer.WriteBossStrikeChainStart(start));
            }
        }

        [Test]
        public void StrikeChainStart_WriteStepsOverLimit_Throws()
        {
            int count = Constants.Network.BOSS_STRIKE_CHAIN_MAX_STEPS + 1;
            var start = new BossStrikeChainStart(Samples.BOSS_ID, CAST_ID, Samples.SEQUENCE_ID, EBossStrikeChainKind.Sequence,
                Vector3.zero, Vector3.forward, Samples.START_TIME, 1f, new EBossTelegraphShape[count], new float[count],
                new float[count], new float[count], new float[count], new float[count]);

            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                Assert.Throws<InvalidDataException>(() => writer.WriteBossStrikeChainStart(start));
            }
        }

        [Test]
        public void StrikeChainStart_WriteStepsAtLimit_RoundTrips()
        {
            int count = Constants.Network.BOSS_STRIKE_CHAIN_MAX_STEPS;
            var source = new BossStrikeChainStart(Samples.BOSS_ID, CAST_ID, Samples.SEQUENCE_ID, EBossStrikeChainKind.Sequence,
                Vector3.zero, Vector3.forward, Samples.START_TIME, 1f, new EBossTelegraphShape[count], new float[count],
                new float[count], new float[count], new float[count], new float[count]);

            BossStrikeChainStart copy = RoundTrip(writer => writer.WriteBossStrikeChainStart(source),
                reader => reader.ReadBossStrikeChainStart());

            Assert.AreEqual(count, copy.StepCount, "상한과 같은 수는 받는다");
        }

        [Test]
        public void StrikeChainStart_ReadStepCountOverLimit_Throws()
        {
            AssertReadThrows(writer =>
            {
                WriteStrikeChainHeader(writer, (byte)EBossStrikeChainKind.Sequence);
                writer.WriteByte((byte)(Constants.Network.BOSS_STRIKE_CHAIN_MAX_STEPS + 1));
            }, reader => reader.ReadBossStrikeChainStart());
        }

        [Test]
        public void StrikeChainStart_ReadKindOutOfRange_Throws()
        {
            AssertReadThrows(writer =>
            {
                WriteStrikeChainHeader(writer, (byte)(EBossStrikeChainKind.Sequence + 1));
                writer.WriteByte(0);
            }, reader => reader.ReadBossStrikeChainStart());
        }

        // 타 목록의 형태는 한 번에 터지는 부채꼴, 원형, 직사각형뿐이다
        [TestCase(EBossTelegraphShape.Dash)]
        [TestCase(EBossTelegraphShape.Projectile)]
        public void StrikeChainStart_ReadShapeThatIsNotSingleStrike_Throws(EBossTelegraphShape shape)
        {
            AssertReadThrows(writer =>
            {
                WriteStrikeChainHeader(writer, (byte)EBossStrikeChainKind.Sequence);
                writer.WriteByte(1);
                writer.WriteByte((byte)shape);
            }, reader => reader.ReadBossStrikeChainStart());
        }

        [Test]
        public void MultiDashStart_WriteRead_KeepsEveryField()
        {
            BossMultiDashStart source = Samples.MakeMultiDash(CAST_ID);

            BossMultiDashStart copy = RoundTrip(writer => writer.WriteBossMultiDashStart(source),
                reader => reader.ReadBossMultiDashStart());

            Samples.AssertSame(in source, in copy);
        }

        [Test]
        public void MultiDashReaim_WriteRead_KeepsEveryField()
        {
            BossMultiDashReaim source = Samples.MakeReaim(CAST_ID, 1);

            BossMultiDashReaim copy = RoundTrip(writer => writer.WriteBossMultiDashReaim(source),
                reader => reader.ReadBossMultiDashReaim());

            Samples.AssertSame(in source, in copy);
        }

        [Test]
        public void MultiDashSnapshot_WriteRead_KeepsCurrentDash()
        {
            var source = new BossMultiDashSnapshot
            {
                Start                    = Samples.MakeMultiDash(CAST_ID),
                DashIndex                = 2,
                Origin                   = new Vector3(1f, 0f, 2f),
                Direction                = Vector3.right,
                Length                   = 6.5f,
                Width                    = 3f,
                IsDirectionLocked        = true,
                TelegraphStartServerTime = 12.5,
                TelegraphSeconds         = 0.6f,
                HasDashStarted           = true,
                DashStartServerTime      = 13.1,
                DashDistance             = 6f,
                DashSeconds              = 0.75f,
                DashSpeed                = 8f,
                StoppedDistance          = 4.25f,
                StoppedByWall            = true,
                IsTruncated              = true
            };

            BossMultiDashSnapshot copy = RoundTrip(writer => writer.WriteBossMultiDashSnapshot(source),
                reader => reader.ReadBossMultiDashSnapshot());

            Samples.AssertSame(in source, in copy);
        }

        [Test]
        public void BattlefieldStartAndSnapshot_WriteRead_KeepsEveryField()
        {
            var source = new BossBattlefieldSnapshot
            {
                Start            = Samples.MakeBattlefield(CAST_ID),
                IsStruck         = true,
                StruckServerTime = 14.25
            };

            BossBattlefieldStart start = RoundTrip(writer => writer.WriteBossBattlefieldStart(source.Start),
                reader => reader.ReadBossBattlefieldStart());
            BossBattlefieldSnapshot copy = RoundTrip(writer => writer.WriteBossBattlefieldSnapshot(source),
                reader => reader.ReadBossBattlefieldSnapshot());

            Samples.AssertSame(source.Start, in start);
            Samples.AssertSame(source.Start, copy.Start);
            Assert.IsTrue(copy.IsStruck);
            Assert.AreEqual(source.StruckServerTime, copy.StruckServerTime, TIME_EPSILON);
        }

        [Test]
        public void ChargeOutcome_WriteRead_KeepsEveryField()
        {
            BossChargeOutcome source = Samples.MakeCharge(CAST_ID);

            BossChargeOutcome copy = RoundTrip(writer => writer.WriteBossChargeOutcome(source),
                reader => reader.ReadBossChargeOutcome());

            Samples.AssertSame(in source, in copy);
        }

        /****************************************
        *          Hard Phase Tests
        ****************************************/

        [Test]
        public void StarStageStartAndSnapshot_WriteRead_KeepsEveryField()
        {
            var source = new BossStarStageSnapshot
            {
                Start       = Samples.MakeStarStage(CAST_ID),
                StruckCount = 3
            };

            BossStarStageStart start = RoundTrip(writer => writer.WriteBossStarStageStart(source.Start),
                reader => reader.ReadBossStarStageStart());
            BossStarStageSnapshot copy = RoundTrip(writer => writer.WriteBossStarStageSnapshot(source),
                reader => reader.ReadBossStarStageSnapshot());

            Samples.AssertSame(source.Start, in start);
            Samples.AssertSame(source.Start, copy.Start);
            Assert.AreEqual(3, copy.StruckCount);
        }

        [Test]
        public void GridBeamStartAndSnapshot_WriteRead_KeepsEveryField()
        {
            var source = new BossGridBeamSnapshot
            {
                Start            = Samples.MakeGridBeam(CAST_ID),
                TelegraphedCount = 7,
                StruckCount      = 4
            };

            BossGridBeamStart start = RoundTrip(writer => writer.WriteBossGridBeamStart(source.Start),
                reader => reader.ReadBossGridBeamStart());
            BossGridBeamSnapshot copy = RoundTrip(writer => writer.WriteBossGridBeamSnapshot(source),
                reader => reader.ReadBossGridBeamSnapshot());

            Samples.AssertSame(source.Start, in start);
            Samples.AssertSame(source.Start, copy.Start);
            Assert.AreEqual(7, copy.TelegraphedCount);
            Assert.AreEqual(4, copy.StruckCount);
        }

        [Test]
        public void ChainFanStartAndSnapshot_WriteRead_KeepsEveryField()
        {
            var source = new BossChainFanSnapshot
            {
                Start         = Samples.MakeChainFan(CAST_ID),
                BlastedCount  = 5,
                IsDangerEnded = true
            };

            BossChainFanStart start = RoundTrip(writer => writer.WriteBossChainFanStart(source.Start),
                reader => reader.ReadBossChainFanStart());
            BossChainFanSnapshot copy = RoundTrip(writer => writer.WriteBossChainFanSnapshot(source),
                reader => reader.ReadBossChainFanSnapshot());

            Samples.AssertSame(source.Start, in start);
            Samples.AssertSame(source.Start, copy.Start);
            Assert.AreEqual(5, copy.BlastedCount);
            Assert.IsTrue(copy.IsDangerEnded);
        }

        [Test]
        public void TrackingAreaStartAndSnapshot_WriteRead_KeepsEveryField()
        {
            var source = new BossTrackingAreaSnapshot
            {
                Start           = Samples.MakeTrackingArea(CAST_ID, AREA_ID),
                Center          = new Vector3(6f, 0f, 7f),
                IsFixed         = true,
                FixedServerTime = 11.75
            };

            BossTrackingAreaStart start = RoundTrip(writer => writer.WriteBossTrackingAreaStart(source.Start),
                reader => reader.ReadBossTrackingAreaStart());
            BossTrackingAreaSnapshot copy = RoundTrip(writer => writer.WriteBossTrackingAreaSnapshot(source),
                reader => reader.ReadBossTrackingAreaSnapshot());

            Samples.AssertSame(source.Start, in start);
            Samples.AssertSame(source.Start, copy.Start);
            Assert.AreEqual(source.Center, copy.Center);
            Assert.IsTrue(copy.IsFixed);
            Assert.AreEqual(source.FixedServerTime, copy.FixedServerTime, TIME_EPSILON);
        }

        [Test]
        public void BarrageStartAndShot_WriteRead_KeepsEveryField()
        {
            BossBarrageStart sourceStart = Samples.MakeBarrage(CAST_ID);
            BossBarrageShot  sourceShot  = Samples.MakeShot(CAST_ID, 4);

            BossBarrageStart start = RoundTrip(writer => writer.WriteBossBarrageStart(sourceStart),
                reader => reader.ReadBossBarrageStart());
            BossBarrageShot shot = RoundTrip(writer => writer.WriteBossBarrageShot(sourceShot),
                reader => reader.ReadBossBarrageShot());

            Samples.AssertSame(in sourceStart, in start);
            Samples.AssertSame(in sourceShot, in shot);
        }

        [Test]
        public void BarrageSnapshot_WriteRead_KeepsShotsInOrder()
        {
            BossBarrageSnapshot source = MakeBarrageSnapshot(SHOWN_SHOTS);

            BossBarrageSnapshot copy = RoundTrip(writer => writer.WriteBossBarrageSnapshot(source),
                reader => reader.ReadBossBarrageSnapshot());

            Samples.AssertSame(in source, in copy);
            Assert.AreEqual(SHOWN_SHOTS, copy.Shots.Length);
        }

        [Test]
        public void BarrageSnapshot_WriteNullShots_ReadsEmptyList()
        {
            var source = new BossBarrageSnapshot
            {
                Start      = Samples.MakeBarrage(CAST_ID),
                FiredCount = 0,
                Shots      = null
            };

            BossBarrageSnapshot copy = RoundTrip(writer => writer.WriteBossBarrageSnapshot(source),
                reader => reader.ReadBossBarrageSnapshot());

            Assert.IsNotNull(copy.Shots, "받는 쪽은 빈 배열로 읽어 목록을 바로 돌 수 있다");
            Assert.AreEqual(0, copy.Shots.Length);
        }

        [Test]
        public void BarrageSnapshot_ShotsAtLimitAndOver_AcceptsLimitAndThrowsOver()
        {
            BossBarrageSnapshot atLimit = MakeBarrageSnapshot(Constants.Network.BOSS_BARRAGE_MAX_SHOTS);
            BossBarrageSnapshot over    = MakeBarrageSnapshot(Constants.Network.BOSS_BARRAGE_MAX_SHOTS + 1);

            BossBarrageSnapshot copy = RoundTrip(writer => writer.WriteBossBarrageSnapshot(atLimit),
                reader => reader.ReadBossBarrageSnapshot());
            Assert.AreEqual(Constants.Network.BOSS_BARRAGE_MAX_SHOTS, copy.Shots.Length);

            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                Assert.Throws<InvalidDataException>(() => writer.WriteBossBarrageSnapshot(over));
            }
        }

        [Test]
        public void BarrageSnapshot_ReadShotCountOverLimit_Throws()
        {
            AssertReadThrows(writer =>
            {
                writer.WriteBossBarrageStart(Samples.MakeBarrage(CAST_ID));
                writer.WriteInt(0);
                writer.WriteByte((byte)(Constants.Network.BOSS_BARRAGE_MAX_SHOTS + 1));
            }, reader => reader.ReadBossBarrageSnapshot());
        }

        [Test]
        public void CircleBurstStartAndSnapshot_WriteRead_KeepsEveryCircle()
        {
            var source = new BossCircleBurstSnapshot
            {
                Start    = Samples.MakeCircleBurst(CAST_ID),
                IsStruck = true
            };

            BossCircleBurstStart start = RoundTrip(writer => writer.WriteBossCircleBurstStart(source.Start),
                reader => reader.ReadBossCircleBurstStart());
            BossCircleBurstSnapshot copy = RoundTrip(writer => writer.WriteBossCircleBurstSnapshot(source),
                reader => reader.ReadBossCircleBurstSnapshot());

            Samples.AssertSame(source.Start, in start);
            Samples.AssertSame(source.Start, copy.Start);
            Assert.AreEqual(Samples.CIRCLE_COUNT, copy.Start.CircleCount);
            Assert.IsTrue(copy.IsStruck);
        }

        [Test]
        public void CircleBurstStart_CirclesAtLimitAndOver_AcceptsLimitAndThrowsOver()
        {
            BossCircleBurstStart atLimit = Samples.MakeCircleBurst(CAST_ID, Constants.Network.BOSS_CIRCLE_BURST_MAX_CIRCLES);
            BossCircleBurstStart over    = Samples.MakeCircleBurst(CAST_ID, Constants.Network.BOSS_CIRCLE_BURST_MAX_CIRCLES + 1);

            BossCircleBurstStart copy = RoundTrip(writer => writer.WriteBossCircleBurstStart(atLimit),
                reader => reader.ReadBossCircleBurstStart());
            Samples.AssertSame(in atLimit, in copy);

            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                Assert.Throws<InvalidDataException>(() => writer.WriteBossCircleBurstStart(over));
            }
        }

        [Test]
        public void CircleBurstStart_WriteRadiiCountDiffersFromCenters_Throws()
        {
            var shortRadii = new BossCircleBurstStart(Samples.BOSS_ID, CAST_ID, new Vector3[Samples.CIRCLE_COUNT],
                new float[Samples.CIRCLE_COUNT - 1], Samples.START_TIME, 1f);
            var noRadii = new BossCircleBurstStart(Samples.BOSS_ID, CAST_ID, new Vector3[Samples.CIRCLE_COUNT], null,
                Samples.START_TIME, 1f);

            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                Assert.Throws<InvalidDataException>(() => writer.WriteBossCircleBurstStart(shortRadii));
                Assert.Throws<InvalidDataException>(() => writer.WriteBossCircleBurstStart(noRadii));
            }
        }

        [Test]
        public void CircleBurstStart_ReadCircleCountOverLimit_Throws()
        {
            AssertReadThrows(writer =>
            {
                writer.WriteInt(Samples.BOSS_ID);
                writer.WriteInt(CAST_ID);
                writer.WriteDouble(Samples.START_TIME);
                writer.WriteFloat(1f);
                writer.WriteByte((byte)(Constants.Network.BOSS_CIRCLE_BURST_MAX_CIRCLES + 1));
            }, reader => reader.ReadBossCircleBurstStart());
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static BossBarrageSnapshot MakeBarrageSnapshot(int shotCount)
        {
            var shots = new BossBarrageShot[shotCount];
            for (int i = 0; i < shotCount; ++i)
            {
                shots[i] = Samples.MakeShot(CAST_ID, i);
            }

            return new BossBarrageSnapshot
            {
                Start      = Samples.MakeBarrage(CAST_ID),
                FiredCount = shotCount / 2,
                Shots      = shots
            };
        }

        // 타 목록 시작 값의 타 수 앞까지를 직렬화 순서대로 쓴다
        private static void WriteStrikeChainHeader(NetworkWriter writer, byte kind)
        {
            writer.WriteInt(Samples.BOSS_ID);
            writer.WriteInt(CAST_ID);
            writer.WriteString(Samples.SEQUENCE_ID);
            writer.WriteByte(kind);
            writer.WriteVector3(Vector3.zero);
            writer.WriteVector3(Vector3.forward);
            writer.WriteDouble(Samples.START_TIME);
            writer.WriteFloat(1f);
        }

        private static T RoundTrip<T>(Action<NetworkWriter> write, Func<NetworkReader, T> read)
        {
            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                write(writer);
                using (NetworkReaderPooled reader = NetworkReaderPool.Get(writer.ToArraySegment()))
                {
                    T value = read(reader);
                    Assert.AreEqual(0, reader.Remaining, "쓴 만큼 정확히 읽는다");
                    return value;
                }
            }
        }

        private static void AssertReadThrows<T>(Action<NetworkWriter> write, Func<NetworkReader, T> read)
        {
            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                write(writer);
                using (NetworkReaderPooled reader = NetworkReaderPool.Get(writer.ToArraySegment()))
                {
                    Assert.Throws<InvalidDataException>(() => read(reader));
                }
            }
        }
    }
}
