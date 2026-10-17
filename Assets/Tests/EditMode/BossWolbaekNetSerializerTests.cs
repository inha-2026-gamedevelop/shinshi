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

using Samples = Shinshi.Tests.BossWolbaekGimmickSamples;

namespace Shinshi.Tests
{
    public class BossWolbaekNetSerializerTests
    {
        // 월백 기믹 사건 값과 복원 묶음이 쓰고 읽은 뒤에도 필드마다 같은지, 쓴 만큼만 읽는지 본다.
        // 상한을 넘는 목록은 쓰기에서 막고, 받은 쪽은 상한을 넘거나 범위를 벗어난 열거형을 프로토콜 불일치로 보고 읽기를 멈춘다

        /****************************************
        *                Fields
        ****************************************/

        private const int    CAST_ID      = 31;
        private const int    ATTACK_ID    = 4;
        private const double TIME_EPSILON = 0.000001;
        private const byte   BAD_ENUM     = 200;

        /****************************************
        *            Talisman Tests
        ****************************************/

        [Test]
        public void TalismanStart_WriteRead_KeepsEveryStructure()
        {
            WolbaekTalismanStart source = Samples.Talisman(CAST_ID);

            WolbaekTalismanStart copy = RoundTrip(writer => writer.WriteWolbaekTalismanStart(source),
                reader => reader.ReadWolbaekTalismanStart());

            Assert.AreEqual(source.CastInstanceId, copy.CastInstanceId);
            Assert.AreEqual(source.BossCombatId, copy.BossCombatId);
            Assert.AreEqual(source.TransitionEndServerTime, copy.TransitionEndServerTime, TIME_EPSILON);
            Assert.AreEqual(source.Center, copy.Center);
            Assert.AreEqual(source.Structures.Count, copy.Structures.Count);
            for (int i = 0; i < source.Structures.Count; ++i)
            {
                Assert.AreEqual(source.Structures[i].Color, copy.Structures[i].Color);
                Assert.AreEqual(source.Structures[i].SymbolId, copy.Structures[i].SymbolId);
                Assert.AreEqual(source.Structures[i].Center, copy.Structures[i].Center);
                Assert.AreEqual(source.Structures[i].AssignedCombatId, copy.Structures[i].AssignedCombatId);
            }
        }

        [Test]
        public void TalismanAttack_LineAndRing_KeepKindAndShape()
        {
            WolbaekTalismanAttack line = Samples.LineAttack(ATTACK_ID, Samples.START);
            WolbaekTalismanAttack ring = Samples.RingAttack(ATTACK_ID + 1, Samples.START);

            WolbaekTalismanAttack lineCopy = RoundTrip(writer => writer.WriteWolbaekTalismanAttack(line),
                reader => reader.ReadWolbaekTalismanAttack());
            WolbaekTalismanAttack ringCopy = RoundTrip(writer => writer.WriteWolbaekTalismanAttack(ring),
                reader => reader.ReadWolbaekTalismanAttack());

            Assert.AreEqual(EWolbaekTalismanAttackKind.Line, lineCopy.Kind);
            Assert.AreEqual(line.Direction, lineCopy.Direction);
            Assert.AreEqual(line.Length, lineCopy.Length);
            Assert.AreEqual(line.TargetCombatId, lineCopy.TargetCombatId);
            Assert.AreEqual(EWolbaekTalismanAttackKind.Ring, ringCopy.Kind);
            Assert.AreEqual(ring.InnerRadius, ringCopy.InnerRadius);
            Assert.AreEqual(ring.OuterRadius, ringCopy.OuterRadius);
            Assert.AreEqual(ring.StrikeServerTime, ringCopy.StrikeServerTime, TIME_EPSILON);
        }

        [Test]
        public void TalismanRestore_WriteRead_KeepsSuccessTimeAndLastReset()
        {
            WolbaekTalismanStart  start  = Samples.Talisman(CAST_ID);
            WolbaekTalismanAttack attack = Samples.LineAttack(ATTACK_ID, Samples.START + 4.0);
            var source = new BossWolbaekTalismanRestore
            {
                Snapshot            = new WolbaekTalismanSnapshot(EWolbaekTalismanState.Active, in start, true, Samples.START + 3.5,
                    true, in attack, 0d),
                SuccessServerTime   = double.NaN,
                HasLastReset        = true,
                LastResetServerTime = Samples.START + 3.25,
                LastResetReason     = EWolbaekHoldReset.Died,
                LastResetCombatId   = Samples.MEMBER_B
            };

            BossWolbaekTalismanRestore copy = RoundTrip(writer => writer.WriteBossWolbaekTalismanRestore(source),
                reader => reader.ReadBossWolbaekTalismanRestore());

            Assert.AreEqual(EWolbaekTalismanState.Active, copy.Snapshot.State);
            Assert.IsTrue(copy.Snapshot.HasHold);
            Assert.AreEqual(ATTACK_ID, copy.Snapshot.Attack.AttackId);
            Assert.IsTrue(double.IsNaN(copy.SuccessServerTime), "모르는 성공 시각은 NaN 그대로 온다");
            Assert.IsTrue(copy.HasLastReset);
            Assert.AreEqual(EWolbaekHoldReset.Died, copy.LastResetReason);
            Assert.AreEqual(Samples.MEMBER_B, copy.LastResetCombatId);
            Assert.AreEqual(Samples.START + 3.25, copy.LastResetServerTime, TIME_EPSILON);
        }

        [Test]
        public void TalismanStart_TooManyStructures_WriteThrowsAndCannotWrite()
        {
            var structures = new WolbaekTalismanStructure[Constants.Network.BOSS_WOLBAEK_MAX_STRUCTURES + 1];
            var start = new WolbaekTalismanStart(Samples.BOSS_ID, CAST_ID, Samples.START, Samples.START, 1f, Vector3.zero, structures);

            Assert.IsFalse(BossWolbaekNetSerializer.CanWrite(in start));
            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                Assert.Throws<InvalidDataException>(() => writer.WriteWolbaekTalismanStart(start));
            }
        }

        [Test]
        public void TalismanStructure_ColorOutOfRange_ReadThrows()
        {
            AssertReadThrows(writer =>
            {
                writer.WriteInt(0);
                writer.WriteByte(BAD_ENUM);
                writer.WriteInt(0);
                writer.WriteVector3(Vector3.zero);
                writer.WriteFloat(0f);
                writer.WriteInt(0);
            }, reader => reader.ReadWolbaekTalismanStructure());
        }

        /****************************************
        *              Ward Tests
        ****************************************/

        [Test]
        public void WardRestore_WriteRead_KeepsStageGraceAndPendingStrike()
        {
            WolbaekWardStart  start  = Samples.Ward(CAST_ID);
            WolbaekWardStage  stage  = Samples.Stage(1, Samples.START + 10.0);
            WolbaekWardStrike strike = Samples.Strike(1, 2, Samples.START + 18.0);
            var source = new BossWolbaekWardRestore
            {
                Snapshot         = new WolbaekWardSnapshot(EWolbaekWardState.Strike, in start, false, 0d, in stage, 2,
                    new[] { 0d, Samples.START + 20.0 }),
                HasPendingStrike = true,
                PendingStrike    = strike
            };

            BossWolbaekWardRestore copy = RoundTrip(writer => writer.WriteBossWolbaekWardRestore(source),
                reader => reader.ReadBossWolbaekWardRestore());

            Assert.AreEqual(EWolbaekWardState.Strike, copy.Snapshot.State);
            Assert.AreEqual(Samples.PAIR, copy.Snapshot.Start.Members.Count);
            Assert.AreEqual(EWolbaekWardColor.Blue, copy.Snapshot.Start.Members[1].Color);
            Assert.AreEqual(1, copy.Snapshot.Stage.Index);
            Assert.AreEqual(stage.RedCenter, copy.Snapshot.Stage.RedCenter);
            Assert.AreEqual(2, copy.Snapshot.NextStrikeIndex);
            Assert.AreEqual(Samples.START + 20.0, copy.Snapshot.GraceEndServerTimes[1], TIME_EPSILON);
            Assert.IsTrue(copy.HasPendingStrike);
            Assert.AreEqual(2, copy.PendingStrike.StrikeIndex);
            Assert.AreEqual(strike.Origin, copy.PendingStrike.Origin);
        }

        [Test]
        public void WardStart_MemberCountOverLimit_ReadThrows()
        {
            AssertReadThrows(writer => writer.WriteByte((byte)(Constants.Network.BOSS_WOLBAEK_MAX_MEMBERS + 1)),
                reader => reader.ReadWolbaekWardStart());
        }

        /****************************************
        *              Seal Tests
        ****************************************/

        [Test]
        public void SealRestore_WriteRead_KeepsTurnPositionsAndCoreMask()
        {
            WolbaekSealStart start = Samples.Seal(CAST_ID);
            WolbaekSealTurn  turn  = Samples.Turn(1, Samples.START + 10.0);
            WolbaekSealSweep sweep = Samples.Sweep(1, 0, Samples.START + 12.0);
            var positions = new[] { new Vector3(float.NaN, float.NaN, float.NaN), new Vector3(3f, 0f, 4f) };
            var source = new BossWolbaekSealRestore
            {
                Snapshot          = new WolbaekSealSnapshot(EWolbaekSealState.Active, EWolbaekSealStep.Track, in start, in turn, 1,
                    positions, new[] { false, true }, false, true, in sweep, 0d),
                DestroyedCoreMask = 0b10
            };

            BossWolbaekSealRestore copy = RoundTrip(writer => writer.WriteBossWolbaekSealRestore(source),
                reader => reader.ReadBossWolbaekSealRestore());

            Assert.AreEqual(EWolbaekSealStep.Track, copy.Snapshot.Step);
            Assert.AreEqual(1, copy.Snapshot.Turn.Index);
            Assert.AreEqual(Samples.PAIR, copy.Snapshot.Turn.Cores.Count);
            Assert.AreEqual(Samples.MEMBER_B, copy.Snapshot.Turn.Foxfires[1].TargetCombatId);
            Assert.IsTrue(float.IsNaN(copy.Snapshot.FoxfirePositions[0].x), "모르는 위치는 NaN 그대로 온다");
            Assert.AreEqual(new Vector3(3f, 0f, 4f), copy.Snapshot.FoxfirePositions[1]);
            Assert.IsTrue(copy.Snapshot.FoxfireDetached[1]);
            Assert.IsTrue(copy.Snapshot.HasPendingSweep);
            Assert.AreEqual(sweep.HalfAngleDegrees, copy.Snapshot.PendingSweep.HalfAngleDegrees);
            Assert.AreEqual(0b10, copy.DestroyedCoreMask);
            Assert.IsFalse(BossWolbaekGimmickLedger.IsCoreDestroyed(copy.DestroyedCoreMask, 0));
            Assert.IsTrue(BossWolbaekGimmickLedger.IsCoreDestroyed(copy.DestroyedCoreMask, 1));
        }

        [Test]
        public void SealTurn_CoreCountOverLimit_CannotWrite()
        {
            var cores = new WolbaekSealCore[Constants.Network.BOSS_WOLBAEK_MAX_CORES + 1];
            var turn  = new WolbaekSealTurn(0, 0d, 0d, 0d, 0d, 0d, cores, Array.Empty<WolbaekSealFoxfire>());

            Assert.IsFalse(BossWolbaekNetSerializer.CanWrite(in turn));
        }

        [Test]
        public void SealSnapshot_StepOutOfRange_ReadThrows()
        {
            AssertReadThrows(writer =>
            {
                writer.WriteByte((byte)EWolbaekSealState.Active);
                writer.WriteByte(BAD_ENUM);
            }, reader => reader.ReadWolbaekSealSnapshot());
        }

        /****************************************
        *              Final Tests
        ****************************************/

        [Test]
        public void FinalRestore_WriteRead_KeepsYellowsBundleAndLockedReds()
        {
            WolbaekFinalStart  start   = Samples.Final(CAST_ID);
            WolbaekFinalBundle bundle  = Samples.Bundle(2, Samples.START + 20.0);
            WolbaekFinalYellow[] yellows = Samples.Yellows();
            var source = new BossWolbaekFinalRestore
            {
                Snapshot = new WolbaekFinalSnapshot(EWolbaekFinalStep.Hunt, in start, true, yellows, new[] { 8000f, 0f, 8000f },
                    true, in bundle, new[] { Vector3.one, Vector3.left }, new[] { false, false }, true)
            };

            BossWolbaekFinalRestore copy = RoundTrip(writer => writer.WriteBossWolbaekFinalRestore(source),
                reader => reader.ReadBossWolbaekFinalRestore());

            Assert.AreEqual(EWolbaekFinalStep.Hunt, copy.Snapshot.Step);
            Assert.IsTrue(copy.Snapshot.OuterFloorRemoved);
            Assert.AreEqual(Samples.YELLOWS, copy.Snapshot.Yellows.Count);
            Assert.AreEqual(yellows[2].CombatId, copy.Snapshot.Yellows[2].CombatId);
            Assert.AreEqual(0f, copy.Snapshot.YellowHealth[1]);
            Assert.IsTrue(copy.Snapshot.HasBundle);
            Assert.AreEqual(2, copy.Snapshot.Bundle.Index);
            Assert.AreEqual(Samples.MEMBER_B, copy.Snapshot.Bundle.Reds[1].TargetCombatId);
            Assert.AreEqual(Vector3.left, copy.Snapshot.RedPositions[1]);
            Assert.IsTrue(copy.Snapshot.RedsLocked);
        }

        [Test]
        public void FinalYellowArray_WriteRead_KeepsEveryYellowAndRejectsOverLimit()
        {
            WolbaekFinalYellow[] source = Samples.Yellows();

            WolbaekFinalYellow[] copy = RoundTrip(writer => writer.WriteWolbaekFinalYellowArray(source),
                reader => reader.ReadWolbaekFinalYellowArray());

            Assert.AreEqual(source.Length, copy.Length);
            Assert.AreEqual(source[1].Position, copy[1].Position);
            Assert.IsFalse(BossWolbaekNetSerializer.CanWrite(new WolbaekFinalYellow[Constants.Network.BOSS_WOLBAEK_MAX_YELLOWS + 1]));
            AssertReadThrows(writer => writer.WriteByte((byte)(Constants.Network.BOSS_WOLBAEK_MAX_YELLOWS + 1)),
                reader => reader.ReadWolbaekFinalYellowArray());
        }

        [Test]
        public void FinalBundle_RedCountOverLimit_ReadThrows()
        {
            AssertReadThrows(writer =>
            {
                writer.WriteInt(0);
                writer.WriteDouble(0d);
                writer.WriteDouble(0d);
                writer.WriteDouble(0d);
                writer.WriteByte((byte)(Constants.Network.BOSS_WOLBAEK_MAX_REDS + 1));
            }, reader => reader.ReadWolbaekFinalBundle());
        }

        /****************************************
        *            Grid Beam Tests
        ****************************************/

        [Test]
        public void GridBeamStart_WriteRead_KeepsFirstLineId()
        {
            var source = new BossGridBeamStart(Samples.BOSS_ID, CAST_ID, Vector3.one, 18f, 12, 3f, 3f, Samples.START, 1f, 0.5f, 1.5f,
                777);

            BossGridBeamStart copy = RoundTrip(writer => writer.WriteBossGridBeamStart(source),
                reader => reader.ReadBossGridBeamStart());

            Assert.AreEqual(777, copy.FirstLineId);
            Assert.AreEqual(source.AxisGapSeconds, copy.AxisGapSeconds);
        }

        /****************************************
        *               Helpers
        ****************************************/

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
