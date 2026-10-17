// System
using System.IO;
using System.Text.RegularExpressions;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;

using Samples = Shinshi.Tests.BossPhaseEventSamples;

namespace Shinshi.Tests
{
    public class BossPhaseEffectLedgerTests
    {
        // 무철과 월백 하드 2페이즈 전용 공격의 복원 기록이 진행 중인 값만 남기는지, 종류마다 정해진 시점에 지우는지,
        // 스폰 초기 상태로 그대로 오가고 늦게 연결한 수신부에 복원으로 넘어가는지 본다

        /****************************************
        *                Fields
        ****************************************/

        private const int    CAST_ID       = 11;
        private const int    OTHER_ID      = 12;
        private const int    AREA_ID       = 21;
        private const int    OTHER_AREA_ID = 22;
        private const int    LINE_ID       = 31;
        private const int    PROJECTILE_ID = 51;
        private const int    FIRED_SHOTS   = 2;
        private const int    SHOWN_SHOTS   = 3;
        private const float  DASH_SECONDS  = 0.5f;
        private const float  DASH_SPEED    = 16f;
        private const float  STOP_DISTANCE = 5f;
        private const float  SIDE_OFFSET   = 1f;   // m, 옆으로 비껴 멈춰도 진행 방향 거리만 센다
        private const float  FAR_DISTANCE  = 20f;  // m, 돌진 거리보다 멀다
        private const float  AIM_LENGTH    = 6.5f;
        private const float  ROTATION      = 36f;
        private const double EVENT_TIME    = 11.5;
        private const double LATER_TIME    = 12.75;
        private const double STOP_TIME     = 11.9;
        private const int    STRUCTURE_ID  = 7;
        private const double GROGGY_START  = 14.0;
        private const double GROGGY_END    = 17.0;
        private const float  FLOAT_EPSILON = 0.0001f;
        private const double TIME_EPSILON  = 0.000001;

        // 추적 장판 수보다 앞에 쓰는 한 건짜리 기록 수(타 목록, 연속 돌진, 전장 공격, 돌진 결과, 돌진 유도 그로기, 별 단계, 격자, 연쇄 부채꼴)
        private const int SINGLE_RECORDS_BEFORE_AREAS = 8;

        private static readonly Vector3 MOVED_CENTER = new Vector3(4f, 0f, 5f);
        private static readonly Vector3 FIXED_CENTER = new Vector3(6f, 0f, 7f);

        /****************************************
        *            Mucheol Tests
        ****************************************/

        [Test]
        public void StrikeChain_AimLockHit_KeepsDirectionAndStruckCount()
        {
            var ledger = new BossPhaseEffectLedger();
            BossStrikeChainStart start = Samples.MakeStrikeChain(CAST_ID);
            ledger.OnStrikeChainStarted(in start);

            Assert.IsTrue(ledger.HasStrikeChain);
            Assert.AreEqual(start.Direction, ledger.StrikeChain.Direction, "조준이 오기 전에는 시작 방향이다");
            Assert.AreEqual(0, ledger.StrikeChain.StruckCount);

            ledger.OnStrikeChainAimChanged(EBossStrikeChainKind.Sequence, CAST_ID, Vector3.right, EVENT_TIME);
            Assert.AreEqual(Vector3.right, ledger.StrikeChain.Direction);
            Assert.IsFalse(ledger.StrikeChain.IsDirectionLocked);

            ledger.OnStrikeChainDirectionLocked(EBossStrikeChainKind.Sequence, CAST_ID, Vector3.left);
            ledger.OnStrikeChainAimChanged(EBossStrikeChainKind.Sequence, CAST_ID, Vector3.back, LATER_TIME);
            Assert.AreEqual(Vector3.left, ledger.StrikeChain.Direction, "고정 뒤에 늦게 온 조준은 무시한다");
            Assert.IsTrue(ledger.StrikeChain.IsDirectionLocked);

            ledger.OnStrikeChainHit(EBossStrikeChainKind.Sequence, CAST_ID, 1, EVENT_TIME);
            ledger.OnStrikeChainHit(EBossStrikeChainKind.Sequence, CAST_ID, 0, EVENT_TIME);
            Assert.AreEqual(2, ledger.StrikeChain.StruckCount, "순서가 뒤바뀌어 와도 확정된 타 수를 줄이지 않는다");
        }

        [Test]
        public void StrikeChain_HitBeforeLock_MarksDirectionLocked()
        {
            var ledger = new BossPhaseEffectLedger();
            BossStrikeChainStart start = Samples.MakeStrikeChain(CAST_ID, EBossStrikeChainKind.Combo);
            ledger.OnStrikeChainStarted(in start);

            ledger.OnStrikeChainHit(EBossStrikeChainKind.Combo, CAST_ID, 0, EVENT_TIME);

            Assert.AreEqual(1, ledger.StrikeChain.StruckCount);
            Assert.IsTrue(ledger.StrikeChain.IsDirectionLocked, "타가 확정됐으면 방향은 더 바뀌지 않는다");
        }

        [Test]
        public void StrikeChain_EventsForOtherCast_Ignored()
        {
            var ledger = new BossPhaseEffectLedger();
            BossStrikeChainStart start = Samples.MakeStrikeChain(CAST_ID);
            ledger.OnStrikeChainStarted(in start);

            ledger.OnStrikeChainAimChanged(EBossStrikeChainKind.Sequence, OTHER_ID, Vector3.right, EVENT_TIME);
            ledger.OnStrikeChainHit(EBossStrikeChainKind.Sequence, OTHER_ID, 2, EVENT_TIME);
            ledger.OnStrikeChainCancelled(EBossStrikeChainKind.Sequence, OTHER_ID);
            ledger.OnCastEnded(OTHER_ID);

            Assert.IsTrue(ledger.HasStrikeChain);
            Assert.AreEqual(start.Direction, ledger.StrikeChain.Direction);
            Assert.AreEqual(0, ledger.StrikeChain.StruckCount);
        }

        [Test]
        public void StrikeChain_CastEndedOrCancelled_Removes()
        {
            var ledger = new BossPhaseEffectLedger();
            BossStrikeChainStart start = Samples.MakeStrikeChain(CAST_ID);

            ledger.OnStrikeChainStarted(in start);
            ledger.OnCastEnded(CAST_ID);
            Assert.IsFalse(ledger.HasStrikeChain, "끝난 시전은 복원에 넣지 않는다");

            ledger.OnStrikeChainStarted(in start);
            ledger.OnStrikeChainCancelled(EBossStrikeChainKind.Sequence, CAST_ID);
            Assert.IsFalse(ledger.HasStrikeChain);
            Assert.IsTrue(ledger.IsEmpty);
        }

        [Test]
        public void MultiDash_DashStartedThenStopped_RecordsDistanceAlongDirection()
        {
            var ledger = new BossPhaseEffectLedger();
            BossMultiDashStart start = Samples.MakeMultiDash(CAST_ID);
            ledger.OnMultiDashStarted(in start);

            Assert.AreEqual(0, ledger.MultiDash.DashIndex);
            Assert.IsFalse(ledger.MultiDash.HasDashStarted);
            Assert.Less(ledger.MultiDash.StoppedDistance, 0f, "멈추기 전이면 음수다");

            ledger.OnMultiDashAimChanged(CAST_ID, 0, Vector3.forward, AIM_LENGTH, EVENT_TIME);
            Assert.AreEqual(AIM_LENGTH, ledger.MultiDash.Length, FLOAT_EPSILON);

            ledger.OnMultiDashDirectionLocked(CAST_ID, 0, Vector3.forward, Samples.DASH_LENGTH);
            ledger.OnMultiDashAimChanged(CAST_ID, 0, Vector3.right, AIM_LENGTH, LATER_TIME);
            Assert.AreEqual(Vector3.forward, ledger.MultiDash.Direction, "고정 뒤에 늦게 온 조준은 무시한다");
            Assert.AreEqual(Samples.DASH_LENGTH, ledger.MultiDash.Length, FLOAT_EPSILON);

            ledger.OnMultiDashDashStopped(CAST_ID, 0, new Vector3(0f, 0f, STOP_DISTANCE), false, STOP_TIME);
            Assert.Less(ledger.MultiDash.StoppedDistance, 0f, "달리기 전에 온 정지는 무시한다");
            Assert.IsTrue(double.IsNaN(ledger.MultiDash.StoppedServerTime));

            ledger.OnMultiDashDashStarted(CAST_ID, 0, Samples.DASH_LENGTH, DASH_SECONDS, DASH_SPEED, EVENT_TIME);
            Assert.IsTrue(ledger.MultiDash.HasDashStarted);
            Assert.AreEqual(EVENT_TIME, ledger.MultiDash.DashStartServerTime, TIME_EPSILON);
            Assert.AreEqual(Samples.DASH_LENGTH, ledger.MultiDash.DashDistance, FLOAT_EPSILON);
            Assert.AreEqual(DASH_SECONDS, ledger.MultiDash.DashSeconds, FLOAT_EPSILON);
            Assert.AreEqual(DASH_SPEED, ledger.MultiDash.DashSpeed, FLOAT_EPSILON);

            ledger.OnMultiDashDashStopped(CAST_ID, 0, new Vector3(SIDE_OFFSET, 0f, STOP_DISTANCE), true, STOP_TIME);
            Assert.AreEqual(STOP_DISTANCE, ledger.MultiDash.StoppedDistance, FLOAT_EPSILON);
            Assert.IsTrue(ledger.MultiDash.StoppedByWall);
            Assert.AreEqual(STOP_TIME, ledger.MultiDash.StoppedServerTime, TIME_EPSILON);

            ledger.OnMultiDashDashStopped(CAST_ID, 0, new Vector3(0f, 0f, FAR_DISTANCE), false, LATER_TIME);
            Assert.AreEqual(Samples.DASH_LENGTH, ledger.MultiDash.StoppedDistance, FLOAT_EPSILON, "정지 거리는 돌진 거리를 넘지 않는다");
        }

        [Test]
        public void MultiDash_ReaimStarted_ReplacesCurrentDashAndIgnoresEarlierDash()
        {
            var ledger = new BossPhaseEffectLedger();
            BossMultiDashStart start = Samples.MakeMultiDash(CAST_ID);
            ledger.OnMultiDashStarted(in start);
            ledger.OnMultiDashDashStarted(CAST_ID, 0, Samples.DASH_LENGTH, DASH_SECONDS, DASH_SPEED, EVENT_TIME);
            ledger.OnMultiDashDashStopped(CAST_ID, 0, new Vector3(0f, 0f, STOP_DISTANCE), true, STOP_TIME);

            BossMultiDashReaim reaim = Samples.MakeReaim(CAST_ID, 1);
            ledger.OnMultiDashReaimStarted(in reaim);

            BossMultiDashSnapshot current = ledger.MultiDash;
            Assert.AreEqual(1, current.DashIndex);
            Assert.AreEqual(reaim.Origin, current.Origin);
            Assert.AreEqual(reaim.Direction, current.Direction);
            Assert.AreEqual(reaim.Length, current.Length, FLOAT_EPSILON);
            Assert.AreEqual(reaim.Width, current.Width, FLOAT_EPSILON);
            Assert.AreEqual(reaim.StartServerTime, current.TelegraphStartServerTime, TIME_EPSILON);
            Assert.AreEqual(reaim.ReaimSeconds, current.TelegraphSeconds, FLOAT_EPSILON);
            Assert.IsFalse(current.IsDirectionLocked);
            Assert.IsFalse(current.HasDashStarted, "새 돌진은 아직 달리지 않았다");
            Assert.Less(current.StoppedDistance, 0f);
            Assert.IsFalse(current.StoppedByWall);
            Assert.IsTrue(double.IsNaN(current.StoppedServerTime), "새 돌진은 정지 시각이 없다");
            Samples.AssertSame(in start, current.Start);

            ledger.OnMultiDashDashStarted(CAST_ID, 0, Samples.DASH_LENGTH, DASH_SECONDS, DASH_SPEED, LATER_TIME);
            Assert.IsFalse(ledger.MultiDash.HasDashStarted, "앞 돌진에 늦게 온 사건은 지금 돌진을 바꾸지 못한다");

            BossMultiDashReaim otherCast = Samples.MakeReaim(OTHER_ID, 2);
            ledger.OnMultiDashReaimStarted(in otherCast);
            Assert.AreEqual(1, ledger.MultiDash.DashIndex, "다른 시전의 재조준은 무시한다");
        }

        [Test]
        public void MultiDash_TruncatedThenCastEnded_MarksAndRemoves()
        {
            var ledger = new BossPhaseEffectLedger();
            BossMultiDashStart start = Samples.MakeMultiDash(CAST_ID);
            ledger.OnMultiDashStarted(in start);

            ledger.OnMultiDashTruncated(OTHER_ID, Samples.DASH_COUNT, 1);
            Assert.IsFalse(ledger.MultiDash.IsTruncated);

            ledger.OnMultiDashTruncated(CAST_ID, Samples.DASH_COUNT, 1);
            Assert.IsTrue(ledger.MultiDash.IsTruncated);

            ledger.OnMultiDashCancelled(OTHER_ID);
            Assert.IsTrue(ledger.HasMultiDash);

            ledger.OnCastEnded(CAST_ID);
            Assert.IsFalse(ledger.HasMultiDash);

            ledger.OnMultiDashStarted(in start);
            ledger.OnMultiDashCancelled(CAST_ID);
            Assert.IsFalse(ledger.HasMultiDash);
        }

        [Test]
        public void Battlefield_HitThenCastEnded_KeepsFirstHitAndRemoves()
        {
            var ledger = new BossPhaseEffectLedger();
            BossBattlefieldStart start = Samples.MakeBattlefield(CAST_ID);
            ledger.OnBattlefieldStarted(in start);

            Assert.IsFalse(ledger.Battlefield.IsStruck);

            ledger.OnBattlefieldHit(OTHER_ID, EVENT_TIME);
            Assert.IsFalse(ledger.Battlefield.IsStruck);

            ledger.OnBattlefieldHit(CAST_ID, EVENT_TIME);
            ledger.OnBattlefieldHit(CAST_ID, LATER_TIME);
            Assert.IsTrue(ledger.Battlefield.IsStruck);
            Assert.AreEqual(EVENT_TIME, ledger.Battlefield.StruckServerTime, TIME_EPSILON, "단발이라 첫 타격 시각을 지킨다");

            ledger.OnCastEnded(CAST_ID);
            Assert.IsFalse(ledger.HasBattlefield);

            ledger.OnBattlefieldStarted(in start);
            ledger.OnBattlefieldCancelled(CAST_ID);
            Assert.IsFalse(ledger.HasBattlefield);
        }

        [Test]
        public void Charge_Ended_KeepsOutcomeUntilCastEnded()
        {
            var ledger = new BossPhaseEffectLedger();
            BossChargeOutcome outcome = Samples.MakeCharge(CAST_ID);
            ledger.OnChargeEnded(in outcome);

            Assert.IsTrue(ledger.HasCharge, "시전이 끝날 때까지 회복 표시를 이어 그릴 수 있게 남긴다");
            Samples.AssertSame(in outcome, ledger.Charge);

            ledger.OnCastEnded(OTHER_ID);
            Assert.IsTrue(ledger.HasCharge);

            ledger.OnCastEnded(CAST_ID);
            Assert.IsFalse(ledger.HasCharge, "돌진 결과는 중단 사건이 따로 없어 시전 종료로 지운다");
        }

        [Test]
        public void ChargeGroggy_PendingThenStarted_SurvivesCastEndUntilNextCast()
        {
            var ledger = new BossPhaseEffectLedger();
            var pending = new BossChargeGroggy(Samples.BOSS_ID, CAST_ID, STRUCTURE_ID, EVENT_TIME, double.NaN, double.NaN);
            ledger.OnChargeGroggyPending(in pending);

            Assert.IsTrue(ledger.HasGroggy);
            Assert.IsFalse(ledger.Groggy.HasStarted);

            ledger.OnCastEnded(CAST_ID);
            Assert.IsTrue(ledger.HasGroggy, "그로기는 부순 시전의 회복 뒤에 시작하므로 시전 종료로 지우지 않는다");

            var started = new BossChargeGroggy(Samples.BOSS_ID, CAST_ID, STRUCTURE_ID, EVENT_TIME, GROGGY_START, GROGGY_END);
            ledger.OnChargeGroggyStarted(in started);
            Assert.AreEqual(GROGGY_END, ledger.Groggy.GroggyEndServerTime, TIME_EPSILON);

            ledger.OnCastStarted(CAST_ID);
            Assert.IsTrue(ledger.HasGroggy, "같은 시전의 시작 알림은 그로기를 끝내지 않는다");

            ledger.OnCastStarted(OTHER_ID);
            Assert.IsFalse(ledger.HasGroggy, "그로기 중에는 선택이 막혀 새 시전이 시작됐으면 그로기는 끝났다");
            Assert.IsTrue(ledger.IsEmpty);
        }

        [Test]
        public void ChargeGroggy_CancelledOtherCast_KeepsAndSameCast_Removes()
        {
            var ledger = new BossPhaseEffectLedger();
            var pending = new BossChargeGroggy(Samples.BOSS_ID, CAST_ID, STRUCTURE_ID, EVENT_TIME, double.NaN, double.NaN);
            ledger.OnChargeGroggyPending(in pending);

            ledger.OnChargeGroggyCancelled(OTHER_ID);
            Assert.IsTrue(ledger.HasGroggy);

            ledger.OnChargeGroggyCancelled(CAST_ID);
            Assert.IsFalse(ledger.HasGroggy);
        }

        [Test]
        public void ChargeGroggy_WriteReadReplay_RestoresAfterCharge()
        {
            var source = new BossPhaseEffectLedger();
            BossChargeOutcome charge = Samples.MakeCharge(CAST_ID);
            source.OnChargeEnded(in charge);
            var started = new BossChargeGroggy(Samples.BOSS_ID, CAST_ID, STRUCTURE_ID, EVENT_TIME, GROGGY_START, GROGGY_END);
            source.OnChargeGroggyStarted(in started);

            var target = new BossPhaseEffectLedger();
            RoundTripGroggy(source, target);

            var listener = new BossEventRecorder();
            target.Replay(listener, null);

            Assert.AreEqual(1, listener.RestoredCharges.Count);
            Assert.AreEqual(1, listener.RestoredGroggies.Count);
            BossChargeGroggy restored = listener.RestoredGroggies[0];
            Assert.AreEqual(CAST_ID, restored.CastId);
            Assert.AreEqual(STRUCTURE_ID, restored.StructureId);
            Assert.AreEqual(EVENT_TIME, restored.CollisionServerTime, TIME_EPSILON);
            Assert.AreEqual(GROGGY_START, restored.GroggyStartServerTime, TIME_EPSILON);
            Assert.AreEqual(GROGGY_END, restored.GroggyEndServerTime, TIME_EPSILON);
        }

        private static void RoundTripGroggy(BossPhaseEffectLedger source, BossPhaseEffectLedger target)
        {
            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                source.Write(writer);
                using (NetworkReaderPooled reader = NetworkReaderPool.Get(writer.ToArraySegment()))
                {
                    target.Read(reader);
                    Assert.AreEqual(0, reader.Remaining, "쓴 만큼 정확히 읽는다");
                }
            }
        }

        /****************************************
        *          Hard Phase Tests
        ****************************************/

        [Test]
        public void StarStage_Struck_KeepsHighestStageAndRemovesOnEndOrCancel()
        {
            var ledger = new BossPhaseEffectLedger();
            BossStarStageStart start = Samples.MakeStarStage(CAST_ID);
            ledger.OnStarStageStarted(in start);

            ledger.OnStarStageStruck(CAST_ID, 1, ROTATION, EVENT_TIME);
            ledger.OnStarStageStruck(CAST_ID, 0, 0f, EVENT_TIME);
            ledger.OnStarStageStruck(OTHER_ID, 3, ROTATION, EVENT_TIME);
            Assert.AreEqual(2, ledger.StarStage.StruckCount);

            ledger.OnStarStageCancelled(OTHER_ID);
            Assert.IsTrue(ledger.HasStarStage);

            ledger.OnCastEnded(CAST_ID);
            Assert.IsFalse(ledger.HasStarStage);

            ledger.OnStarStageStarted(in start);
            ledger.OnStarStageCancelled(CAST_ID);
            Assert.IsFalse(ledger.HasStarStage);
        }

        [Test]
        public void GridBeam_VerticalLines_CountAfterAllHorizontalLines()
        {
            var ledger = new BossPhaseEffectLedger();
            BossGridBeamStart start = Samples.MakeGridBeam(CAST_ID);
            ledger.OnGridBeamStarted(in start);

            ledger.OnGridBeamLineTelegraphed(CAST_ID, LINE_ID, true, 2, EVENT_TIME, LATER_TIME);
            ledger.OnGridBeamLineStruck(CAST_ID, LINE_ID, true, 1, EVENT_TIME);
            Assert.AreEqual(3, ledger.GridBeam.TelegraphedCount, "가로줄은 줄 번호 + 1이다");
            Assert.AreEqual(2, ledger.GridBeam.StruckCount);

            ledger.OnGridBeamLineTelegraphed(CAST_ID, LINE_ID, false, 1, EVENT_TIME, LATER_TIME);
            ledger.OnGridBeamLineStruck(CAST_ID, LINE_ID, false, 0, EVENT_TIME);
            Assert.AreEqual(Samples.LINE_COUNT + 2, ledger.GridBeam.TelegraphedCount, "세로줄은 가로줄 수를 더해 센다");
            Assert.AreEqual(Samples.LINE_COUNT + 1, ledger.GridBeam.StruckCount);

            ledger.OnGridBeamLineStruck(CAST_ID, LINE_ID, true, 0, EVENT_TIME);
            ledger.OnGridBeamLineStruck(OTHER_ID, LINE_ID, false, 4, EVENT_TIME);
            Assert.AreEqual(Samples.LINE_COUNT + 1, ledger.GridBeam.StruckCount, "늦게 온 앞 줄과 다른 시전의 줄은 수를 바꾸지 않는다");

            ledger.OnGridBeamCancelled(OTHER_ID);
            Assert.IsTrue(ledger.HasGridBeam);

            ledger.OnCastEnded(CAST_ID);
            Assert.IsFalse(ledger.HasGridBeam);

            ledger.OnGridBeamStarted(in start);
            ledger.OnGridBeamCancelled(CAST_ID);
            Assert.IsFalse(ledger.HasGridBeam);
        }

        [Test]
        public void ChainFan_CastEnded_KeepsUntilDangerEndedOrCancelled()
        {
            var ledger = new BossPhaseEffectLedger();
            BossChainFanStart start = Samples.MakeChainFan(CAST_ID);
            ledger.OnChainFanStarted(in start);

            ledger.OnChainFanBlasted(CAST_ID, 2, EVENT_TIME);
            ledger.OnChainFanBlasted(CAST_ID, 1, EVENT_TIME);
            ledger.OnChainFanBlasted(OTHER_ID, 5, EVENT_TIME);
            Assert.AreEqual(3, ledger.ChainFan.BlastedCount);
            Assert.IsFalse(ledger.ChainFan.IsDangerEnded);

            ledger.OnCastEnded(CAST_ID);
            Assert.IsTrue(ledger.HasChainFan, "잔류 위험은 시전의 회복보다 길 수 있어 시전 종료로는 지우지 않는다");

            ledger.OnChainFanDangerEnded(OTHER_ID, LATER_TIME);
            Assert.IsTrue(ledger.HasChainFan);

            ledger.OnChainFanDangerEnded(CAST_ID, LATER_TIME);
            Assert.IsFalse(ledger.HasChainFan);

            ledger.OnChainFanStarted(in start);
            ledger.OnChainFanCancelled(CAST_ID);
            Assert.IsFalse(ledger.HasChainFan);
        }

        [Test]
        public void TrackingArea_MovedThenFixed_KeepsLatestCenterAndIgnoresMoveAfterFix()
        {
            var ledger = new BossPhaseEffectLedger();
            BossTrackingAreaStart start = Samples.MakeTrackingArea(CAST_ID, AREA_ID);
            ledger.OnTrackingAreaStarted(in start);

            Assert.AreEqual(1, ledger.TrackingAreaCount);
            Assert.AreEqual(start.Center, ledger.GetTrackingArea(0).Center);
            Assert.IsFalse(ledger.GetTrackingArea(0).IsFixed);

            ledger.OnTrackingAreaMoved(OTHER_AREA_ID, Vector3.zero, EVENT_TIME);
            ledger.OnTrackingAreaMoved(AREA_ID, MOVED_CENTER, EVENT_TIME);
            Assert.AreEqual(MOVED_CENTER, ledger.GetTrackingArea(0).Center);

            ledger.OnTrackingAreaFixed(AREA_ID, FIXED_CENTER, LATER_TIME);
            ledger.OnTrackingAreaMoved(AREA_ID, MOVED_CENTER, LATER_TIME);

            BossTrackingAreaSnapshot area = ledger.GetTrackingArea(0);
            Assert.IsTrue(area.IsFixed);
            Assert.AreEqual(FIXED_CENTER, area.Center, "고정 뒤에 늦게 온 이동은 무시한다");
            Assert.AreEqual(LATER_TIME, area.FixedServerTime, TIME_EPSILON);
            Samples.AssertSame(in start, area.Start);
        }

        [Test]
        public void TrackingArea_CastEnded_KeepsUntilSameAreaActivatedOrCancelled()
        {
            var ledger = new BossPhaseEffectLedger();
            BossTrackingAreaStart first  = Samples.MakeTrackingArea(CAST_ID, AREA_ID);
            BossTrackingAreaStart second = Samples.MakeTrackingArea(CAST_ID, OTHER_AREA_ID);
            ledger.OnTrackingAreaStarted(in first);
            ledger.OnTrackingAreaStarted(in second);
            ledger.OnTrackingAreaStarted(in first);
            Assert.AreEqual(2, ledger.TrackingAreaCount, "같은 번호가 다시 오면 겹쳐 담지 않는다");

            ledger.OnCastEnded(CAST_ID);
            Assert.AreEqual(2, ledger.TrackingAreaCount, "예고가 시전보다 오래 남아 시전 종료로는 지우지 않는다");

            ledger.OnAreaActivated(AREA_ID);
            Assert.AreEqual(1, ledger.TrackingAreaCount, "활성된 장판은 기본 기록이 이어 맡는다");
            Assert.AreEqual(OTHER_AREA_ID, ledger.GetTrackingArea(0).Start.AreaId);

            ledger.OnTrackingAreaCancelled(CAST_ID, AREA_ID);
            Assert.AreEqual(1, ledger.TrackingAreaCount);

            ledger.OnTrackingAreaCancelled(CAST_ID, OTHER_AREA_ID);
            Assert.AreEqual(0, ledger.TrackingAreaCount);
            Assert.IsTrue(ledger.IsEmpty);
        }

        [Test]
        public void TrackingArea_OverCapacity_LogsErrorAndKeepsLimit()
        {
            var ledger = new BossPhaseEffectLedger();
            for (int i = 0; i < Constants.Network.BOSS_RESTORE_MAX_TRACKING_AREAS; ++i)
            {
                BossTrackingAreaStart start = Samples.MakeTrackingArea(CAST_ID, AREA_ID + i);
                ledger.OnTrackingAreaStarted(in start);
            }

            LogAssert.Expect(LogType.Error, new Regex("추적 장판이 가득 차"));
            BossTrackingAreaStart extra = Samples.MakeTrackingArea(CAST_ID, AREA_ID + Constants.Network.BOSS_RESTORE_MAX_TRACKING_AREAS);
            ledger.OnTrackingAreaStarted(in extra);

            Assert.AreEqual(Constants.Network.BOSS_RESTORE_MAX_TRACKING_AREAS, ledger.TrackingAreaCount);
        }

        [Test]
        public void Barrage_ShotsTelegraphedAndFired_KeepsOrderedShotsAndFiredCount()
        {
            var ledger = new BossPhaseEffectLedger();
            BossBarrageStart start = Samples.MakeBarrage(CAST_ID);
            ledger.OnBarrageStarted(in start);

            Assert.IsTrue(ledger.TryGetBarrage(out BossBarrageSnapshot empty));
            Assert.AreEqual(0, empty.Shots.Length);
            Assert.AreEqual(0, empty.FiredCount);

            BossBarrageShot skipped = Samples.MakeShot(CAST_ID, 1);
            ledger.OnBarrageShotTelegraphed(in skipped);
            Assert.AreEqual(0, ledger.BarrageShotCount, "순서가 맞지 않는 예고는 발 번호와 자리가 어긋나 담지 않는다");

            BossBarrageShot otherCast = Samples.MakeShot(OTHER_ID, 0);
            ledger.OnBarrageShotTelegraphed(in otherCast);
            Assert.AreEqual(0, ledger.BarrageShotCount);

            for (int i = 0; i < SHOWN_SHOTS; ++i)
            {
                BossBarrageShot shot = Samples.MakeShot(CAST_ID, i);
                ledger.OnBarrageShotTelegraphed(in shot);
            }

            ledger.OnBarrageShotFired(CAST_ID, 1, PROJECTILE_ID, true, EVENT_TIME);
            ledger.OnBarrageShotFired(CAST_ID, 0, CombatIds.NONE, false, EVENT_TIME);
            ledger.OnBarrageShotFired(OTHER_ID, 5, PROJECTILE_ID, true, EVENT_TIME);

            Assert.AreEqual(SHOWN_SHOTS, ledger.BarrageShotCount);
            Assert.AreEqual(FIRED_SHOTS, ledger.BarrageFiredCount, "발사 처리된 발 수는 발사 여부와 상관없이 센다");
            Assert.IsTrue(ledger.TryGetBarrage(out BossBarrageSnapshot snapshot));
            Samples.AssertSame(in start, snapshot.Start);
            Assert.AreEqual(FIRED_SHOTS, snapshot.FiredCount);
            Assert.AreEqual(SHOWN_SHOTS, snapshot.Shots.Length);
            for (int i = 0; i < SHOWN_SHOTS; ++i)
            {
                Samples.AssertSame(Samples.MakeShot(CAST_ID, i), in snapshot.Shots[i]);
            }
        }

        [Test]
        public void Barrage_CastEndedCancelledOrRestarted_DropsShots()
        {
            var ledger = new BossPhaseEffectLedger();
            BossBarrageStart start = Samples.MakeBarrage(CAST_ID);
            BossBarrageShot  shot  = Samples.MakeShot(CAST_ID, 0);

            ledger.OnBarrageStarted(in start);
            ledger.OnBarrageShotTelegraphed(in shot);
            ledger.OnBarrageShotFired(CAST_ID, 0, PROJECTILE_ID, true, EVENT_TIME);
            ledger.OnBarrageCancelled(OTHER_ID);
            Assert.IsTrue(ledger.HasBarrage);

            ledger.OnBarrageStarted(in start);
            Assert.AreEqual(0, ledger.BarrageShotCount, "새로 시작하면 앞 시전의 발이 남지 않는다");
            Assert.AreEqual(0, ledger.BarrageFiredCount);

            ledger.OnBarrageShotTelegraphed(in shot);
            ledger.OnCastEnded(CAST_ID);
            Assert.IsFalse(ledger.HasBarrage);
            Assert.AreEqual(0, ledger.BarrageShotCount);
            Assert.IsFalse(ledger.TryGetBarrage(out BossBarrageSnapshot _));

            ledger.OnBarrageStarted(in start);
            ledger.OnBarrageCancelled(CAST_ID);
            Assert.IsFalse(ledger.HasBarrage);
        }

        [Test]
        public void CircleBurst_Struck_MarksAndRemovesOnEndOrCancel()
        {
            var ledger = new BossPhaseEffectLedger();
            BossCircleBurstStart start = Samples.MakeCircleBurst(CAST_ID);
            ledger.OnCircleBurstStarted(in start);

            ledger.OnCircleBurstStruck(OTHER_ID, EVENT_TIME);
            Assert.IsFalse(ledger.CircleBurst.IsStruck);

            ledger.OnCircleBurstStruck(CAST_ID, EVENT_TIME);
            Assert.IsTrue(ledger.CircleBurst.IsStruck);

            ledger.OnCircleBurstCancelled(OTHER_ID);
            Assert.IsTrue(ledger.HasCircleBurst);

            ledger.OnCastEnded(CAST_ID);
            Assert.IsFalse(ledger.HasCircleBurst);

            ledger.OnCircleBurstStarted(in start);
            ledger.OnCircleBurstCancelled(CAST_ID);
            Assert.IsFalse(ledger.HasCircleBurst);
        }

        /****************************************
        *         Clear / Replay / Serialize
        ****************************************/

        [Test]
        public void Clear_EverythingInProgress_BecomesEmpty()
        {
            BossPhaseEffectLedger ledger = MakeFullLedger();
            Assert.IsFalse(ledger.IsEmpty);

            ledger.Clear();

            Assert.IsTrue(ledger.IsEmpty);
            Assert.AreEqual(0, ledger.TrackingAreaCount);
            Assert.AreEqual(0, ledger.BarrageShotCount);
            Assert.AreEqual(0, ledger.BarrageFiredCount);
        }

        [Test]
        public void WriteRead_RoundTripsEverythingInProgress()
        {
            BossPhaseEffectLedger source = MakeFullLedger();

            // 받는 쪽에 다른 값이 남아 있어도 받은 값으로 바뀌는지 본다
            var target = new BossPhaseEffectLedger();
            BossTrackingAreaStart stale = Samples.MakeTrackingArea(OTHER_ID, OTHER_AREA_ID + 1);
            target.OnTrackingAreaStarted(in stale);

            Copy(source, target);

            Assert.IsTrue(target.HasStrikeChain);
            Samples.AssertSame(source.StrikeChain.Start, target.StrikeChain.Start);
            Assert.AreEqual(Vector3.left, target.StrikeChain.Direction);
            Assert.IsTrue(target.StrikeChain.IsDirectionLocked);
            Assert.AreEqual(2, target.StrikeChain.StruckCount);

            Assert.IsTrue(target.HasMultiDash);
            Samples.AssertSame(source.MultiDash, target.MultiDash);
            Assert.AreEqual(1, target.MultiDash.DashIndex);
            Assert.IsTrue(target.MultiDash.HasDashStarted);
            Assert.AreEqual(STOP_DISTANCE, target.MultiDash.StoppedDistance, FLOAT_EPSILON);
            Assert.IsTrue(target.MultiDash.StoppedByWall);
            Assert.IsTrue(target.MultiDash.IsTruncated);

            Assert.IsTrue(target.HasBattlefield);
            Samples.AssertSame(source.Battlefield.Start, target.Battlefield.Start);
            Assert.IsTrue(target.Battlefield.IsStruck);
            Assert.AreEqual(EVENT_TIME, target.Battlefield.StruckServerTime, TIME_EPSILON);

            Assert.IsTrue(target.HasCharge);
            Samples.AssertSame(source.Charge, target.Charge);

            Assert.IsTrue(target.HasStarStage);
            Samples.AssertSame(source.StarStage.Start, target.StarStage.Start);
            Assert.AreEqual(2, target.StarStage.StruckCount);

            Assert.IsTrue(target.HasGridBeam);
            Samples.AssertSame(source.GridBeam.Start, target.GridBeam.Start);
            Assert.AreEqual(Samples.LINE_COUNT + 2, target.GridBeam.TelegraphedCount);
            Assert.AreEqual(3, target.GridBeam.StruckCount);

            Assert.IsTrue(target.HasChainFan);
            Samples.AssertSame(source.ChainFan.Start, target.ChainFan.Start);
            Assert.AreEqual(3, target.ChainFan.BlastedCount);
            Assert.IsFalse(target.ChainFan.IsDangerEnded);

            Assert.AreEqual(2, target.TrackingAreaCount, "받은 값으로 바꾸므로 이전 장판이 남지 않는다");
            Samples.AssertSame(source.GetTrackingArea(0).Start, target.GetTrackingArea(0).Start);
            Assert.AreEqual(FIXED_CENTER, target.GetTrackingArea(0).Center);
            Assert.IsTrue(target.GetTrackingArea(0).IsFixed);
            Assert.AreEqual(LATER_TIME, target.GetTrackingArea(0).FixedServerTime, TIME_EPSILON);
            Assert.AreEqual(OTHER_AREA_ID, target.GetTrackingArea(1).Start.AreaId);
            Assert.AreEqual(MOVED_CENTER, target.GetTrackingArea(1).Center);
            Assert.IsFalse(target.GetTrackingArea(1).IsFixed);

            Assert.IsTrue(source.TryGetBarrage(out BossBarrageSnapshot sourceBarrage));
            Assert.IsTrue(target.TryGetBarrage(out BossBarrageSnapshot targetBarrage));
            Samples.AssertSame(in sourceBarrage, in targetBarrage);
            Assert.AreEqual(FIRED_SHOTS, target.BarrageFiredCount);
            Assert.AreEqual(SHOWN_SHOTS, target.BarrageShotCount);

            Assert.IsTrue(target.HasCircleBurst);
            Samples.AssertSame(source.CircleBurst.Start, target.CircleBurst.Start);
            Assert.IsTrue(target.CircleBurst.IsStruck);
        }

        [Test]
        public void WriteRead_ReadLedgerKeepsRecording_BarrageContinuesFromReceivedShots()
        {
            BossPhaseEffectLedger source = MakeFullLedger();
            var target = new BossPhaseEffectLedger();
            Copy(source, target);

            BossBarrageShot next = Samples.MakeShot(CAST_ID + 7, SHOWN_SHOTS);
            target.OnBarrageShotTelegraphed(in next);

            Assert.AreEqual(SHOWN_SHOTS + 1, target.BarrageShotCount, "늦게 접속한 쪽도 다음 발부터 이어 적는다");
        }

        [Test]
        public void WriteRead_Empty_ReplacesExistingWithNothing()
        {
            var source = new BossPhaseEffectLedger();
            BossPhaseEffectLedger target = MakeFullLedger();

            Copy(source, target);

            Assert.IsTrue(target.IsEmpty);
            Assert.AreEqual(0, target.BarrageShotCount);
        }

        [Test]
        public void Read_TrackingAreaCountOverLimit_Throws()
        {
            var ledger = new BossPhaseEffectLedger();
            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                WriteAbsent(writer, SINGLE_RECORDS_BEFORE_AREAS);
                writer.WriteVarUInt((uint)(Constants.Network.BOSS_RESTORE_MAX_TRACKING_AREAS + 1));
                using (NetworkReaderPooled reader = NetworkReaderPool.Get(writer.ToArraySegment()))
                {
                    Assert.Throws<InvalidDataException>(() => ledger.Read(reader));
                }
            }
        }

        [Test]
        public void Read_BarrageShotCountOverLimit_Throws()
        {
            var ledger = new BossPhaseEffectLedger();
            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                WriteAbsent(writer, SINGLE_RECORDS_BEFORE_AREAS);
                writer.WriteVarUInt(0u);
                writer.WriteBool(true);
                writer.WriteBossBarrageStart(Samples.MakeBarrage(CAST_ID));
                writer.WriteInt(0);
                writer.WriteByte((byte)(Constants.Network.BOSS_BARRAGE_MAX_SHOTS + 1));
                using (NetworkReaderPooled reader = NetworkReaderPool.Get(writer.ToArraySegment()))
                {
                    Assert.Throws<InvalidDataException>(() => ledger.Read(reader));
                }
            }
        }

        [Test]
        public void Replay_NullListeners_DoesNothing()
        {
            BossPhaseEffectLedger ledger = MakeFullLedger();

            Assert.DoesNotThrow(() => ledger.Replay(null, null));
            Assert.IsFalse(ledger.IsEmpty, "복원으로 넘겨도 기록은 그대로다");
        }

        [Test]
        public void Replay_MucheolListenerOnly_SendsMucheolRestoresOnly()
        {
            BossPhaseEffectLedger ledger = MakeFullLedger();
            var listener = new BossEventRecorder();

            ledger.Replay(listener, null);

            Assert.AreEqual(1, listener.RestoredStrikeChains.Count);
            Assert.AreEqual(CAST_ID, listener.RestoredStrikeChains[0].Start.CastId);
            Assert.AreEqual(2, listener.RestoredStrikeChains[0].StruckCount);
            Assert.AreEqual(1, listener.RestoredMultiDashes.Count);
            Samples.AssertSame(ledger.MultiDash, listener.RestoredMultiDashes[0]);
            Assert.AreEqual(1, listener.RestoredBattlefields.Count);
            Assert.IsTrue(listener.RestoredBattlefields[0].IsStruck);
            Assert.AreEqual(1, listener.RestoredCharges.Count);
            Samples.AssertSame(ledger.Charge, listener.RestoredCharges[0]);
            Assert.AreEqual(4, listener.RestoreCount, "하드 페이즈 수신부가 없으면 그쪽 복원은 보내지 않는다");
            Assert.AreEqual(0, listener.Records.Count, "복원은 사건으로 다시 보내지 않는다");
        }

        [Test]
        public void Replay_HardListenerOnly_SendsHardRestoresOnly()
        {
            BossPhaseEffectLedger ledger = MakeFullLedger();
            var listener = new BossEventRecorder();

            ledger.Replay(null, listener);

            Assert.AreEqual(1, listener.RestoredStarStages.Count);
            Assert.AreEqual(2, listener.RestoredStarStages[0].StruckCount);
            Assert.AreEqual(1, listener.RestoredGridBeams.Count);
            Assert.AreEqual(Samples.LINE_COUNT + 2, listener.RestoredGridBeams[0].TelegraphedCount);
            Assert.AreEqual(1, listener.RestoredChainFans.Count);
            Assert.AreEqual(3, listener.RestoredChainFans[0].BlastedCount);
            Assert.AreEqual(2, listener.RestoredTrackingAreas.Count);
            Assert.AreEqual(AREA_ID, listener.RestoredTrackingAreas[0].Start.AreaId);
            Assert.AreEqual(OTHER_AREA_ID, listener.RestoredTrackingAreas[1].Start.AreaId);
            Assert.AreEqual(1, listener.RestoredBarrages.Count);
            Assert.AreEqual(FIRED_SHOTS, listener.RestoredBarrages[0].FiredCount);
            Assert.AreEqual(SHOWN_SHOTS, listener.RestoredBarrages[0].Shots.Length);
            Assert.AreEqual(1, listener.RestoredCircleBursts.Count);
            Assert.IsTrue(listener.RestoredCircleBursts[0].IsStruck);
            Assert.AreEqual(7, listener.RestoreCount, "무철 수신부가 없으면 그쪽 복원은 보내지 않는다");
        }

        [Test]
        public void Replay_EmptyLedger_SendsNothing()
        {
            var ledger   = new BossPhaseEffectLedger();
            var listener = new BossEventRecorder();

            ledger.Replay(listener, listener);

            Assert.AreEqual(0, listener.RestoreCount);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static void WriteAbsent(NetworkWriter writer, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                writer.WriteBool(false);
            }
        }

        private static void Copy(BossPhaseEffectLedger source, BossPhaseEffectLedger target)
        {
            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                source.Write(writer);
                using (NetworkReaderPooled reader = NetworkReaderPool.Get(writer.ToArraySegment()))
                {
                    target.Read(reader);
                    Assert.AreEqual(0, reader.Remaining, "쓴 만큼 정확히 읽는다");
                }
            }
        }

        // 종류마다 시전 번호를 다르게 주고 모두 진행 중인 상태로 만든다. 실제 보스는 한 번에 한 시전만 하지만 기록은 종류별로 따로 담는다
        private static BossPhaseEffectLedger MakeFullLedger()
        {
            var ledger = new BossPhaseEffectLedger();

            BossStrikeChainStart chain = Samples.MakeStrikeChain(CAST_ID);
            ledger.OnStrikeChainStarted(in chain);
            ledger.OnStrikeChainDirectionLocked(EBossStrikeChainKind.Sequence, CAST_ID, Vector3.left);
            ledger.OnStrikeChainHit(EBossStrikeChainKind.Sequence, CAST_ID, 1, EVENT_TIME);

            int dashCast = CAST_ID + 1;
            BossMultiDashStart dash = Samples.MakeMultiDash(dashCast);
            ledger.OnMultiDashStarted(in dash);
            BossMultiDashReaim reaim = new BossMultiDashReaim(dashCast, 1, Vector3.zero, Vector3.forward, Samples.DASH_LENGTH, 3f,
                EVENT_TIME, 0.6f);
            ledger.OnMultiDashReaimStarted(in reaim);
            ledger.OnMultiDashDashStarted(dashCast, 1, Samples.DASH_LENGTH, DASH_SECONDS, DASH_SPEED, LATER_TIME);
            ledger.OnMultiDashDashStopped(dashCast, 1, new Vector3(0f, 0f, STOP_DISTANCE), true, STOP_TIME);
            ledger.OnMultiDashTruncated(dashCast, Samples.DASH_COUNT, 2);

            int battlefieldCast = CAST_ID + 2;
            BossBattlefieldStart battlefield = Samples.MakeBattlefield(battlefieldCast);
            ledger.OnBattlefieldStarted(in battlefield);
            ledger.OnBattlefieldHit(battlefieldCast, EVENT_TIME);

            BossChargeOutcome charge = Samples.MakeCharge(CAST_ID + 3);
            ledger.OnChargeEnded(in charge);

            int starCast = CAST_ID + 4;
            BossStarStageStart star = Samples.MakeStarStage(starCast);
            ledger.OnStarStageStarted(in star);
            ledger.OnStarStageStruck(starCast, 1, ROTATION, EVENT_TIME);

            int gridCast = CAST_ID + 5;
            BossGridBeamStart grid = Samples.MakeGridBeam(gridCast);
            ledger.OnGridBeamStarted(in grid);
            ledger.OnGridBeamLineTelegraphed(gridCast, LINE_ID, false, 1, EVENT_TIME, LATER_TIME);
            ledger.OnGridBeamLineStruck(gridCast, LINE_ID, true, 2, EVENT_TIME);

            int fanCast = CAST_ID + 6;
            BossChainFanStart fan = Samples.MakeChainFan(fanCast);
            ledger.OnChainFanStarted(in fan);
            ledger.OnChainFanBlasted(fanCast, 2, EVENT_TIME);

            BossTrackingAreaStart fixedArea  = Samples.MakeTrackingArea(CAST_ID + 8, AREA_ID);
            BossTrackingAreaStart movingArea = Samples.MakeTrackingArea(CAST_ID + 8, OTHER_AREA_ID);
            ledger.OnTrackingAreaStarted(in fixedArea);
            ledger.OnTrackingAreaStarted(in movingArea);
            ledger.OnTrackingAreaFixed(AREA_ID, FIXED_CENTER, LATER_TIME);
            ledger.OnTrackingAreaMoved(OTHER_AREA_ID, MOVED_CENTER, EVENT_TIME);

            int barrageCast = CAST_ID + 7;
            BossBarrageStart barrage = Samples.MakeBarrage(barrageCast);
            ledger.OnBarrageStarted(in barrage);
            for (int i = 0; i < SHOWN_SHOTS; ++i)
            {
                BossBarrageShot shot = Samples.MakeShot(barrageCast, i);
                ledger.OnBarrageShotTelegraphed(in shot);
            }

            ledger.OnBarrageShotFired(barrageCast, FIRED_SHOTS - 1, PROJECTILE_ID, true, EVENT_TIME);

            int burstCast = CAST_ID + 9;
            BossCircleBurstStart burst = Samples.MakeCircleBurst(burstCast);
            ledger.OnCircleBurstStarted(in burst);
            ledger.OnCircleBurstStruck(burstCast, EVENT_TIME);

            return ledger;
        }
    }
}
