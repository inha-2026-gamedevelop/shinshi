// System
using System;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Common;

using Samples = Shinshi.Tests.BossWolbaekGimmickSamples;

namespace Shinshi.Tests
{
    public class BossWolbaekGimmickLedgerTests
    {
        // 월백 기믹 복원 기록이 진행 중인 기믹만 남기고, 끝난 기믹과 지난 일회성 효과(판정, 폭발)는 늦은 접속에 넘기지 않는지 본다.
        // 서버 기록을 스폰 초기 상태로 쓰고 클라이언트 기록이 읽어 같은 복원을 재생하는지도 본다

        /****************************************
        *                Fields
        ****************************************/

        private const int    CAST_ID      = 41;
        private const int    ATTACK_ID    = 3;
        private const double TIME_EPSILON = 0.000001;

        private BossWolbaekGimmickLedger _ledger;
        private BossWolbaekEventRecorder _listener;

        [SetUp]
        public void SetUp()
        {
            _ledger   = new BossWolbaekGimmickLedger();
            _listener = new BossWolbaekEventRecorder();
        }

        /****************************************
        *               Talisman
        ****************************************/

        [Test]
        public void Talisman_StruckAttack_IsNotReplayedButHoldIs()
        {
            WolbaekTalismanStart  start  = Samples.Talisman(CAST_ID);
            WolbaekTalismanAttack attack = Samples.LineAttack(ATTACK_ID, Samples.START + 4.0);
            _ledger.OnTalismanStarted(in start);
            _ledger.OnTalismanHoldStarted(CAST_ID, Samples.START + 3.5);
            _ledger.OnTalismanAttackTelegraphed(CAST_ID, in attack);
            _ledger.OnTalismanAttackStruck(CAST_ID, ATTACK_ID, attack.StrikeServerTime);

            _ledger.Replay(_listener);

            Assert.AreEqual(1, _listener.RestoredTalismans.Count);
            WolbaekTalismanSnapshot snapshot = _listener.RestoredTalismans[0].Snapshot;
            Assert.AreEqual(EWolbaekTalismanState.Active, snapshot.State);
            Assert.IsTrue(snapshot.HasHold);
            Assert.AreEqual(Samples.START + 3.5, snapshot.HoldStartServerTime, TIME_EPSILON);
            Assert.IsFalse(snapshot.HasAttack, "판정이 난 공격은 늦은 접속에서 다시 그리지 않는다");
        }

        [Test]
        public void Talisman_Reset_KeepsLastResetAndSuccessKeepsSuccessTime()
        {
            WolbaekTalismanStart start = Samples.Talisman(CAST_ID);
            _ledger.OnTalismanStarted(in start);
            _ledger.OnTalismanHoldStarted(CAST_ID, Samples.START + 3.5);
            _ledger.OnTalismanHoldReset(CAST_ID, Samples.START + 4.0, EWolbaekHoldReset.Left, Samples.MEMBER_A);

            Assert.IsTrue(_ledger.Talisman.HasLastReset);
            Assert.IsFalse(_ledger.Talisman.Snapshot.HasHold);
            Assert.AreEqual(Samples.MEMBER_A, _ledger.Talisman.LastResetCombatId);

            _ledger.OnTalismanSucceeded(CAST_ID, Samples.START + 9.0, Samples.START + 15.0);

            Assert.AreEqual(EWolbaekTalismanState.Counter, _ledger.Talisman.Snapshot.State);
            Assert.AreEqual(Samples.START + 9.0, _ledger.Talisman.SuccessServerTime, TIME_EPSILON);
            Assert.AreEqual(Samples.START + 15.0, _ledger.Talisman.Snapshot.CounterEndServerTime, TIME_EPSILON);

            _ledger.OnTalismanCounterEnded(CAST_ID);

            Assert.IsFalse(_ledger.HasTalisman);
        }

        [Test]
        public void Talisman_OtherCastEvents_DoNotChangeTheRecord()
        {
            WolbaekTalismanStart start = Samples.Talisman(CAST_ID);
            _ledger.OnTalismanStarted(in start);

            _ledger.OnTalismanHoldStarted(CAST_ID + 1, Samples.START);
            _ledger.OnTalismanCleared(CAST_ID + 1);

            Assert.IsTrue(_ledger.HasTalisman);
            Assert.IsFalse(_ledger.Talisman.Snapshot.HasHold);
        }

        /****************************************
        *                 Ward
        ****************************************/

        [Test]
        public void Ward_ResolvedStrike_AdvancesNextIndexAndDropsThePendingStrike()
        {
            WolbaekWardStart  start  = Samples.Ward(CAST_ID);
            WolbaekWardStage  stage  = Samples.Stage(0, Samples.START + 10.0);
            WolbaekWardStrike first  = Samples.Strike(0, 0, Samples.START + 12.0);
            WolbaekWardStrike second = Samples.Strike(0, 1, Samples.START + 14.0);
            _ledger.OnWardStarted(in start);
            _ledger.OnGatherHoldStarted(CAST_ID, Samples.START + 1.0);
            _ledger.OnGatherCompleted(CAST_ID);
            _ledger.OnStageStarted(CAST_ID, in stage);
            _ledger.OnStrikeTelegraphed(CAST_ID, in first);
            _ledger.OnStrikeResolved(CAST_ID, 0, 0, first.StrikeServerTime);
            _ledger.OnStrikeTelegraphed(CAST_ID, in second);
            _ledger.OnGraceStarted(CAST_ID, Samples.MEMBER_B, Samples.START + 20.0);

            _ledger.Replay(_listener);

            BossWolbaekWardRestore restore = _listener.RestoredWards[0];
            Assert.AreEqual(EWolbaekWardState.Strike, restore.Snapshot.State);
            Assert.IsFalse(restore.Snapshot.HasHold);
            Assert.AreEqual(1, restore.Snapshot.NextStrikeIndex);
            Assert.IsTrue(restore.HasPendingStrike);
            Assert.AreEqual(1, restore.PendingStrike.StrikeIndex, "판정한 첫 공격이 아니라 진행 중인 두 번째 예고만 넘긴다");
            Assert.AreEqual(0d, restore.Snapshot.GraceEndServerTimes[0]);
            Assert.AreEqual(Samples.START + 20.0, restore.Snapshot.GraceEndServerTimes[1], TIME_EPSILON);
        }

        [Test]
        public void Ward_Succeeded_RemovesTheRecord()
        {
            WolbaekWardStart start = Samples.Ward(CAST_ID);
            _ledger.OnWardStarted(in start);

            _ledger.OnWardSucceeded(CAST_ID, 3);
            _ledger.Replay(_listener);

            Assert.IsFalse(_ledger.HasWard);
            Assert.AreEqual(0, _listener.RestoredWards.Count);
        }

        [Test]
        public void Ward_TooManyMembers_IsNotRecorded()
        {
            var members = new WolbaekWardMember[Constants.Network.BOSS_WOLBAEK_MAX_MEMBERS + 1];
            var start   = new WolbaekWardStart(Samples.BOSS_ID, CAST_ID, Samples.START, Vector3.zero, 5f, 8f, 3f, 6f, 9f, members);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("색 결계 명단"));
            _ledger.OnWardStarted(in start);

            Assert.IsFalse(_ledger.HasWard, "쓸 수 없는 값을 남기면 스폰 직렬화가 멈춘다");
        }

        /****************************************
        *                 Seal
        ****************************************/

        [Test]
        public void Seal_LockedPositionsAndCoreMask_AreKeptAndSweepIsDroppedOnResolve()
        {
            WolbaekSealStart start = Samples.Seal(CAST_ID);
            WolbaekSealTurn  turn  = Samples.Turn(0, Samples.START);
            WolbaekSealSweep sweep = Samples.Sweep(0, 0, Samples.START + 4.0);
            var centers = new[] { new Vector3(6f, 0f, 0f), new Vector3(-6f, 0f, 0f) };
            _ledger.OnSealStarted(in start);
            _ledger.OnSealTurnStarted(CAST_ID, in turn);
            _ledger.OnSealTrackingStarted(CAST_ID, 0, Samples.START + 3.0);
            _ledger.OnSealFoxfireDetached(CAST_ID, 0, 1, Samples.START + 3.5, new Vector3(2f, 0f, 2f));

            BossWolbaekSealRestore tracking = _ledger.Seal;
            Assert.AreEqual(EWolbaekSealStep.Track, tracking.Snapshot.Step);
            Assert.IsTrue(float.IsNaN(tracking.Snapshot.FoxfirePositions[0].x), "추적 중 위치는 사건이 없어 모른다");
            Assert.AreEqual(new Vector3(2f, 0f, 2f), tracking.Snapshot.FoxfirePositions[1]);
            Assert.IsTrue(tracking.Snapshot.FoxfireDetached[1]);

            _ledger.OnSealSweepTelegraphed(CAST_ID, in sweep);
            _ledger.OnSealFoxfiresLocked(CAST_ID, 0, Samples.START + 7.0, centers);
            Assert.IsTrue(_ledger.Seal.Snapshot.HasPendingSweep);
            Assert.IsTrue(_ledger.Seal.Snapshot.FoxfiresLocked);
            Assert.AreEqual(EWolbaekSealStep.Escape, _ledger.Seal.Snapshot.Step);

            _ledger.OnSealTurnResolved(CAST_ID, 0, Samples.START + 9.0, 1, 0b01);
            _ledger.Replay(_listener);

            BossWolbaekSealRestore restore = _listener.RestoredSeals[0];
            Assert.AreEqual(EWolbaekSealStep.Cleanup, restore.Snapshot.Step);
            Assert.AreEqual(1, restore.Snapshot.SuccessCount);
            Assert.AreEqual(0b01, restore.DestroyedCoreMask);
            Assert.IsFalse(restore.Snapshot.HasPendingSweep, "판정이 끝난 차례의 휩쓸기는 넘기지 않는다");
            Assert.AreEqual(centers[0], restore.Snapshot.FoxfirePositions[0]);
        }

        [Test]
        public void Seal_CounterThenCounterEnded_RestoresCounterThenNothing()
        {
            WolbaekSealStart start = Samples.Seal(CAST_ID);
            WolbaekSealTurn  turn  = Samples.Turn(0, Samples.START);
            _ledger.OnSealStarted(in start);
            _ledger.OnSealTurnStarted(CAST_ID, in turn);
            _ledger.OnSealSucceeded(CAST_ID, Samples.START + 30.0, Samples.START + 36.0);

            Assert.AreEqual(EWolbaekSealState.Counter, _ledger.Seal.Snapshot.State);
            Assert.AreEqual(Samples.START + 36.0, _ledger.Seal.Snapshot.CounterEndServerTime, TIME_EPSILON);

            _ledger.OnSealCounterEnded(CAST_ID);

            Assert.IsTrue(_ledger.IsEmpty);
        }

        /****************************************
        *                 Final
        ****************************************/

        [Test]
        public void Final_DestroyedYellowAndResolvedBundle_AreNotReplayedAsAlive()
        {
            WolbaekFinalStart  start  = Samples.Final(CAST_ID);
            WolbaekFinalBundle bundle = Samples.Bundle(0, Samples.START + 6.0);
            _ledger.OnFinalStarted(in start);
            _ledger.OnFinalOuterFloorRemoved(CAST_ID, Samples.START + 4.0);
            _ledger.OnFinalYellowsSpawned(CAST_ID, Samples.Yellows());
            _ledger.OnFinalYellowDestroyed(CAST_ID, 1, Samples.START + 5.0);
            _ledger.OnFinalBundleStarted(CAST_ID, in bundle);
            _ledger.OnFinalRedDetached(CAST_ID, 0, 0, Samples.START + 7.0, Vector3.one);

            BossWolbaekFinalRestore hunting = _ledger.Final;
            Assert.AreEqual(EWolbaekFinalStep.Hunt, hunting.Snapshot.Step);
            Assert.AreEqual(0f, hunting.Snapshot.YellowHealth[1], "부서진 노란 여우불은 체력 0으로 넘겨 다시 만들지 않는다");
            Assert.AreEqual(8000f, hunting.Snapshot.YellowHealth[0]);
            Assert.IsTrue(hunting.Snapshot.HasBundle);
            Assert.AreEqual(Vector3.one, hunting.Snapshot.RedPositions[0]);
            Assert.IsTrue(float.IsNaN(hunting.Snapshot.RedPositions[1].x));

            _ledger.OnFinalRedsLocked(CAST_ID, 0, Samples.START + 10.0, new[] { Vector3.left, Vector3.right });
            _ledger.OnFinalBundleResolved(CAST_ID, 0, Samples.START + 12.0);
            _ledger.Replay(_listener);

            Assert.IsFalse(_listener.RestoredFinals[0].Snapshot.HasBundle, "터진 묶음은 재접속에서 다시 터뜨리지 않는다");
        }

        [Test]
        public void Final_Succeeded_RemovesTheRecord()
        {
            WolbaekFinalStart start = Samples.Final(CAST_ID);
            _ledger.OnFinalStarted(in start);

            _ledger.OnFinalSucceeded(CAST_ID, Samples.START + 30.0);

            Assert.IsTrue(_ledger.IsEmpty);
        }

        /****************************************
        *                 Warp
        ****************************************/

        [Test]
        public void Warp_Departure_IsReplayedUntilThatCastEnds()
        {
            _ledger.OnWarpDeparted(CAST_ID, new Vector3(3f, 0f, 4f), Samples.START);
            _ledger.OnCastEnded(CAST_ID + 1);
            _ledger.Replay(_listener);

            CollectionAssert.AreEqual(new[] { CAST_ID }, _listener.RestoredWarps);

            _ledger.OnCastEnded(CAST_ID);

            Assert.IsFalse(_ledger.HasWarp);
        }

        /****************************************
        *            Write And Read
        ****************************************/

        [Test]
        public void WriteRead_EveryGimmick_ClientReplaysTheSameState()
        {
            WolbaekTalismanStart talisman = Samples.Talisman(CAST_ID);
            WolbaekWardStart     ward     = Samples.Ward(CAST_ID + 1);
            WolbaekWardStage     stage    = Samples.Stage(2, Samples.START + 20.0);
            WolbaekSealStart     seal     = Samples.Seal(CAST_ID + 2);
            WolbaekSealTurn      turn     = Samples.Turn(1, Samples.START + 30.0);
            WolbaekFinalStart    final    = Samples.Final(CAST_ID + 3);
            WolbaekFinalBundle   bundle   = Samples.Bundle(1, Samples.START + 40.0);
            _ledger.OnTalismanStarted(in talisman);
            _ledger.OnTalismanSucceeded(CAST_ID, Samples.START + 9.0, Samples.START + 15.0);
            _ledger.OnWardStarted(in ward);
            _ledger.OnStageStarted(CAST_ID + 1, in stage);
            _ledger.OnSealStarted(in seal);
            _ledger.OnSealTurnStarted(CAST_ID + 2, in turn);
            _ledger.OnFinalStarted(in final);
            _ledger.OnFinalOuterFloorRemoved(CAST_ID + 3, Samples.START + 4.0);
            _ledger.OnFinalYellowsSpawned(CAST_ID + 3, Samples.Yellows());
            _ledger.OnFinalBundleStarted(CAST_ID + 3, in bundle);
            _ledger.OnWarpDeparted(CAST_ID + 4, Vector3.forward, Samples.START + 50.0);

            var client = new BossWolbaekGimmickLedger();
            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                _ledger.Write(writer);
                using (NetworkReaderPooled reader = NetworkReaderPool.Get(writer.ToArraySegment()))
                {
                    client.Read(reader);
                    Assert.AreEqual(0, reader.Remaining, "쓴 만큼 정확히 읽는다");
                }
            }

            client.Replay(_listener);

            Assert.AreEqual(Samples.START + 9.0, _listener.RestoredTalismans[0].SuccessServerTime, TIME_EPSILON);
            Assert.AreEqual(EWolbaekTalismanState.Counter, _listener.RestoredTalismans[0].Snapshot.State);
            Assert.AreEqual(2, _listener.RestoredWards[0].Snapshot.Stage.Index);
            Assert.AreEqual(1, _listener.RestoredSeals[0].Snapshot.Turn.Index);
            Assert.AreEqual(Samples.PAIR, _listener.RestoredSeals[0].Snapshot.FoxfirePositions.Count);
            Assert.AreEqual(Samples.YELLOWS, _listener.RestoredFinals[0].Snapshot.Yellows.Count);
            Assert.AreEqual(1, _listener.RestoredFinals[0].Snapshot.Bundle.Index);
            CollectionAssert.AreEqual(new[] { CAST_ID + 4 }, _listener.RestoredWarps);
            Assert.AreEqual(0, _listener.Names.Count, "복원은 사건으로 다시 보내지 않는다");
        }

        [Test]
        public void WriteRead_Empty_ReadsEmptyAndReplaysNothing()
        {
            var client = new BossWolbaekGimmickLedger();
            WolbaekTalismanStart stale = Samples.Talisman(CAST_ID);
            client.OnTalismanStarted(in stale);

            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                _ledger.Write(writer);
                using (NetworkReaderPooled reader = NetworkReaderPool.Get(writer.ToArraySegment()))
                {
                    client.Read(reader);
                }
            }

            client.Replay(_listener);

            Assert.IsTrue(client.IsEmpty, "읽기는 이전 기록을 버린다");
            Assert.AreEqual(0, _listener.RestoredTalismans.Count);
        }
    }
}
