// System
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Tests
{
    public class BossServerWolbaekHardEventTests
    {
        // 실제 월백 카탈로그의 하드 정의로 서버 몸체를 돌려, 하드 2페이즈 P01~P06의 알림이 중계를 거쳐 하드 페이즈 사건으로 나가는지 본다.
        // 추적 장판의 활성 뒤와 난사 투사체의 비행은 기본 사건으로 오므로 두 계약의 번호가 서로 맞는지도 본다.
        // 50% 결계 전개와 2페이즈 인계는 이지와 하드가 같은 경로라 두 난이도로 함께 돌린다

        /****************************************
        *                Fields
        ****************************************/

        private const string CATALOG_PATH       = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const string BOSS_ID            = Constants.Combat.DEV_BOSS_ID;
        private const int    RANDOM_SEED        = 20261013;
        private const int    PHASE_TWO          = 2;
        private const int    MAX_SECONDS        = 900;  // 여섯 패턴이 한 번씩 나오기를 기다리는 모의 시간 상한
        private const int    SETTLE_SECONDS     = 20;   // 마지막으로 본 시전과 잔류 위험이 끝날 때까지 더 돌리는 시간
        private const int    BARRIER_SECONDS    = 20;   // 결계 전개 완료를 기다리는 상한
        private const int    FIRST_CAST_SECONDS = 30;   // 인계 뒤 첫 2페이즈 공격을 기다리는 상한
        private const int    BURST_CIRCLES      = 13;   // 동시 폭발의 원 수
        private const int    GRID_AXES          = 2;    // 격자는 가로줄 뒤 세로줄이다
        private const double TIME_EPSILON       = 0.000001;

        // 사망 순간에 복원 기록에 남아 있어야 하는 것.
        // 시전이 끝난 뒤에도 연쇄 부채꼴의 위험만 남은 순간은 지금 수치로 900초 동안 한 번도 나오지 않아 여기서 보지 못한다
        private const string KIND_STAR_STAGE    = "StarStage";
        private const string KIND_GRID_BEAM     = "GridBeam";
        private const string KIND_CHAIN_FAN     = "ChainFan";
        private const string KIND_TRACKING_AREA = "TrackingArea";
        private const string KIND_BARRAGE       = "Barrage";
        private const string KIND_CIRCLE_BURST  = "CircleBurst";

        private static readonly string[] HARD_CANCELS =
        {
            nameof(IBossHardPhaseEventListener.OnStarStageCancelled), nameof(IBossHardPhaseEventListener.OnGridBeamCancelled),
            nameof(IBossHardPhaseEventListener.OnChainFanCancelled), nameof(IBossHardPhaseEventListener.OnTrackingAreaCancelled),
            nameof(IBossHardPhaseEventListener.OnBarrageCancelled), nameof(IBossHardPhaseEventListener.OnCircleBurstCancelled)
        };

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Tick_HardPhaseTwoCombat_SendsAllSixHardPhaseEvents()
        {
            BossMockCombat combat = Create(EBossDifficulty.Hard);
            BossEventRecorder events = combat.Events;
            Assert.IsTrue(combat.Boss.IsSortieReady(out string reason), reason);
            Assert.IsTrue(combat.Begin());
            EnterPhaseTwo(combat);

            bool sawAll = combat.StepUntil(() => (events.StarStages.Count > 0) && (events.GridBeams.Count > 0)
                && (events.ChainFans.Count > 0) && (events.TrackingAreas.Count > 0) && (events.Barrages.Count > 0)
                && (events.CircleBursts.Count > 0), MAX_SECONDS);
            combat.StepSeconds(SETTLE_SECONDS);
            Debug.Log($"[BossMockCombat] wolbaek hard phase 2: {string.Join(",", events.PatternsInPhase(PHASE_TWO))}");

            Assert.IsTrue(sawAll, $"하드 2페이즈 P01~P06이 모두 나오지 않았다: {string.Join(",", events.PatternsInPhase(PHASE_TWO))}");

            AssertStartPattern(events, nameof(IBossHardPhaseEventListener.OnStarStageStarted), WolbaekPatternFactory.HARD_P2_P01_ID);
            AssertStartPattern(events, nameof(IBossHardPhaseEventListener.OnGridBeamStarted), WolbaekPatternFactory.HARD_P2_P02_ID);
            AssertStartPattern(events, nameof(IBossHardPhaseEventListener.OnChainFanStarted), WolbaekPatternFactory.HARD_P2_P03_ID);
            AssertStartPattern(events, nameof(IBossHardPhaseEventListener.OnTrackingAreaStarted),
                WolbaekPatternFactory.HARD_P2_P04_ID);
            AssertStartPattern(events, nameof(IBossHardPhaseEventListener.OnBarrageStarted), WolbaekPatternFactory.HARD_P2_P05_ID);
            AssertStartPattern(events, nameof(IBossHardPhaseEventListener.OnCircleBurstStarted),
                WolbaekPatternFactory.HARD_P2_P06_ID);

            AssertStarStages(combat);
            AssertGridBeams(combat);
            AssertChainFans(combat);
            AssertTrackingAreas(combat);
            AssertBarrages(combat);
            AssertCircleBursts(combat);

            AssertDedicatedCastsFinishedOnce(events);
            for (int i = 0; i < HARD_CANCELS.Length; ++i)
            {
                Assert.AreEqual(0, events.Count(HARD_CANCELS[i]), $"끊은 적이 없는데 {HARD_CANCELS[i]}이 왔다");
            }
        }

        [TestCase(EBossDifficulty.Easy)]
        [TestCase(EBossDifficulty.Hard)]
        public void Tick_Transition_RunsBarrierThenHandsOffToPhaseTwoPatterns(EBossDifficulty difficulty)
        {
            BossMockCombat combat = Create(difficulty);
            BossEventRecorder events = combat.Events;
            Assert.IsTrue(combat.Begin());
            Assert.IsTrue(combat.Boss.HasBarrier, "월백은 난이도와 상관없이 결계 전개를 쓴다");

            combat.ForceTransition();
            Assert.AreEqual(EBossPhaseState.AwaitingHandoff, combat.Boss.PhaseState, "전환한 틱에는 인계 대기다");

            int barrierTicks = Constants.Network.SERVER_TICK_RATE * BARRIER_SECONDS;
            for (int i = 0; (i < barrierTicks) && (events.Count(nameof(IBossEventListener.OnBarrierCompleted)) == 0); ++i)
            {
                Assert.AreEqual(EBossPhaseState.AwaitingHandoff, combat.Boss.PhaseState, "결계 전개가 끝나기 전에는 인계하지 않는다");
                combat.Step();
            }

            Assert.AreEqual(1, events.Barriers.Count, "결계는 한 번만 시작한다");
            BossBarrierStart barrier = events.Barriers[0];
            Assert.AreEqual(combat.Boss.CombatId, barrier.BossId);
            CollectionAssert.AreEqual(new[]
            {
                nameof(IBossEventListener.OnBarrierStarted), nameof(IBossEventListener.OnBarrierActivated),
                nameof(IBossEventListener.OnBarrierCompleted)
            }, events.NamesOf(EBossRecordKey.Barrier, barrier.BarrierId));
            Assert.GreaterOrEqual(combat.Now, barrier.CompleteServerTime - TIME_EPSILON, "완료 시각 전에는 인계하지 않는다");
            Assert.AreNotEqual(EBossPhaseState.AwaitingHandoff, combat.Boss.PhaseState, "완료한 틱에 인계를 마친다");
            Assert.AreEqual(PHASE_TWO, combat.Boss.Phase);

            int beforePhaseTwo = events.Records.Count;
            Assert.IsTrue(combat.StepUntil(() => HasPatternSince(events, beforePhaseTwo), FIRST_CAST_SECONDS),
                "인계 뒤 2페이즈 선택이 이어진다");
            Assert.AreEqual(EBossPhaseState.Active, combat.Boss.PhaseState);

            var definitions = new List<BossPatternCatalogSO.PatternDefinition>();
            combat.Catalog.CollectPatterns(BOSS_ID, PHASE_TWO, definitions);
            var phaseTwoIds = new List<string>();
            for (int i = 0; i < definitions.Count; ++i)
            {
                phaseTwoIds.Add(definitions[i].DefinitionId);
            }

            for (int i = beforePhaseTwo; i < events.Records.Count; ++i)
            {
                string patternId = events.Records[i].PatternId;
                if (!string.IsNullOrEmpty(patternId))
                {
                    CollectionAssert.Contains(phaseTwoIds, patternId, $"인계 뒤에는 {difficulty} 2페이즈 패턴만 고른다");
                }
            }

            Assert.AreEqual(0, events.Count(nameof(IBossEventListener.OnBarrierCleared)), "완료 뒤에도 위험 구역은 남는다");
        }

        // 이미 접속한 클라이언트의 복원 기록은 사건으로만 비워진다. 사망한 틱의 정리 사건이 빠지면 끝난 공격의 표시가 남는다
        [TestCase(KIND_STAR_STAGE)]
        [TestCase(KIND_GRID_BEAM)]
        [TestCase(KIND_CHAIN_FAN)]
        [TestCase(KIND_TRACKING_AREA)]
        [TestCase(KIND_BARRAGE)]
        [TestCase(KIND_CIRCLE_BURST)]
        public void Tick_BossKilledWhileHardPatternInProgress_EventsEmptyRestoreRecord(string kind)
        {
            BossMockCombat combat = Create(EBossDifficulty.Hard);
            BossEventRecorder events = combat.Events;
            Assert.IsTrue(combat.Begin());
            EnterPhaseTwo(combat);

            Assert.IsTrue(combat.StepUntil(() => IsInProgress(events, kind), MAX_SECONDS), $"{kind}이 진행 중인 순간을 만나지 못했다");

            combat.KillBoss();
            combat.Step();

            Assert.IsTrue(combat.Boss.IsBossDeathRecorded);
            Assert.IsTrue(events.PhaseLedger.IsEmpty, $"{kind} 진행 중 사망: 정리 사건만으로 복원 기록이 비워져야 한다. " +
                $"남은 것: {DescribeLedger(events.PhaseLedger)}");
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static bool IsInProgress(BossEventRecorder events, string kind)
        {
            BossPhaseEffectLedger ledger = events.PhaseLedger;
            switch (kind)
            {
                case KIND_STAR_STAGE:
                    return ledger.HasStarStage;
                case KIND_GRID_BEAM:
                    return ledger.HasGridBeam;
                case KIND_CHAIN_FAN:
                    return ledger.HasChainFan;
                case KIND_TRACKING_AREA:
                    return ledger.TrackingAreaCount > 0;
                case KIND_BARRAGE:
                    return ledger.HasBarrage;
                case KIND_CIRCLE_BURST:
                    return ledger.HasCircleBurst;
                default:
                    return false;
            }
        }

        private static string DescribeLedger(BossPhaseEffectLedger ledger)
        {
            return $"starStage={ledger.HasStarStage}, gridBeam={ledger.HasGridBeam}, chainFan={ledger.HasChainFan}, " +
                $"trackingAreas={ledger.TrackingAreaCount}, barrage={ledger.HasBarrage}, circleBurst={ledger.HasCircleBurst}";
        }

        private static BossMockCombat Create(EBossDifficulty difficulty)
        {
            BossPatternCatalogSO asset = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assume.That(asset, Is.Not.Null, "월백 카탈로그 에셋이 없다");

            var combat = new BossMockCombat(asset, BOSS_ID, difficulty, RANDOM_SEED, true, false);
            Assume.That(combat.Catalog.IsTuned(BOSS_ID), Is.True, "월백 수치가 지정되지 않았다");
            return combat;
        }

        // 전환한 뒤 결계 전개가 끝나 인계될 때까지 돌린다
        private static void EnterPhaseTwo(BossMockCombat combat)
        {
            combat.ForceTransition();
            Assert.IsTrue(combat.StepUntil(() => combat.Boss.PhaseState == EBossPhaseState.Active, BARRIER_SECONDS),
                "결계 전개 뒤 2페이즈로 인계되지 않았다");
            Assert.AreEqual(PHASE_TWO, combat.Boss.Phase);
        }

        private static bool HasPatternSince(BossEventRecorder events, int firstIndex)
        {
            for (int i = firstIndex; i < events.Records.Count; ++i)
            {
                if (!string.IsNullOrEmpty(events.Records[i].PatternId))
                {
                    return true;
                }
            }

            return false;
        }

        // 하드 페이즈 사건 값에는 패턴 ID가 없어, 시작 사건이 온 순간 몸체가 시전 중이던 패턴으로 확인한다
        private static void AssertStartPattern(BossEventRecorder events, string startName, string expectedPatternId)
        {
            SortedSet<string> patterns = events.PatternsOf(startName);
            CollectionAssert.AreEqual(new[] { expectedPatternId }, patterns, $"{startName}은 {expectedPatternId}의 사건이다");
        }

        private static bool IsFinished(BossEventRecorder events, int castId)
        {
            return events.Count(nameof(IBossEventListener.OnFinished), EBossRecordKey.Cast, castId) == 1;
        }

        private static void AssertIndexesInOrder(List<BossEventRecord> records, int expectedCount, string message)
        {
            Assert.AreEqual(expectedCount, records.Count, message);
            for (int i = 0; i < records.Count; ++i)
            {
                Assert.AreEqual(i, records[i].Index, message);
            }
        }

        private static void AssertStarStages(BossMockCombat combat)
        {
            BossEventRecorder events = combat.Events;
            int finished = 0;
            for (int i = 0; i < events.StarStages.Count; ++i)
            {
                BossStarStageStart start = events.StarStages[i];
                Assert.AreEqual(combat.Boss.CombatId, start.BossId);
                Assert.Greater(start.StageCount, 0);
                Assert.Greater(start.Shape.ArenaRadius, 0f, "전장 입력이 연결됐다");
                if (!IsFinished(events, start.CastId))
                {
                    continue;
                }

                ++finished;
                List<BossEventRecord> struck = events.Find(nameof(IBossHardPhaseEventListener.OnStarStageStruck),
                    EBossRecordKey.Cast, start.CastId);
                AssertIndexesInOrder(struck, start.StageCount, "끝난 별 단계 시전은 모든 단계를 순서대로 타격한다");
                Assert.AreEqual(start.CastStartServerTime + start.FirstStrikeSeconds, struck[0].Time, TIME_EPSILON,
                    "첫 단계의 예정 시각은 시작 값으로 정해진다");
            }

            Assert.Greater(finished, 0, "끝까지 간 별 단계 시전이 있다");
        }

        private static void AssertGridBeams(BossMockCombat combat)
        {
            BossEventRecorder events = combat.Events;
            int finished = 0;
            for (int i = 0; i < events.GridBeams.Count; ++i)
            {
                BossGridBeamStart start = events.GridBeams[i];
                Assert.AreEqual(combat.Boss.CombatId, start.BossId);
                Assert.Greater(start.LineCount, 0);
                Assert.AreEqual(combat.Boss.ArenaRadius, start.ArenaRadius, "격자는 전장 원에 맞춘다");
                if (!IsFinished(events, start.CastId))
                {
                    continue;
                }

                ++finished;
                int lines = start.LineCount * GRID_AXES;
                List<BossEventRecord> telegraphed = events.Find(nameof(IBossHardPhaseEventListener.OnGridBeamLineTelegraphed),
                    EBossRecordKey.Cast, start.CastId);
                List<BossEventRecord> struck = events.Find(nameof(IBossHardPhaseEventListener.OnGridBeamLineStruck),
                    EBossRecordKey.Cast, start.CastId);
                Assert.AreEqual(lines, telegraphed.Count, "끝난 격자 시전은 가로줄과 세로줄을 모두 예고한다");
                Assert.AreEqual(lines, struck.Count, "예고한 줄은 모두 타격한다");
                for (int line = 0; line < lines; ++line)
                {
                    bool isHorizontal = line < start.LineCount;
                    int  lineIndex    = isHorizontal ? line : (line - start.LineCount);
                    Assert.AreEqual(isHorizontal, struck[line].Flag, "가로줄이 먼저고 세로줄이 뒤다");
                    Assert.AreEqual(lineIndex, struck[line].Index);
                    Assert.AreEqual(isHorizontal, telegraphed[line].Flag);
                    Assert.AreEqual(lineIndex, telegraphed[line].Index);
                }
            }

            Assert.Greater(finished, 0, "끝까지 간 격자 시전이 있다");
        }

        // 잔류 위험은 시전의 회복보다 길 수 있어 시전 종료가 아니라 위험 종료까지 간 것을 본다
        private static void AssertChainFans(BossMockCombat combat)
        {
            BossEventRecorder events = combat.Events;
            int ended = 0;
            for (int i = 0; i < events.ChainFans.Count; ++i)
            {
                BossChainFanStart start = events.ChainFans[i];
                Assert.AreEqual(combat.Boss.CombatId, start.BossId);
                Assert.Greater(start.ZoneCount, 0);

                List<BossEventRecord> dangerEnds = events.Find(nameof(IBossHardPhaseEventListener.OnChainFanDangerEnded),
                    EBossRecordKey.Cast, start.CastId);
                if (dangerEnds.Count == 0)
                {
                    continue;
                }

                ++ended;
                Assert.AreEqual(1, dangerEnds.Count, "위험 종료는 한 번이다");
                List<BossEventRecord> blasted = events.Find(nameof(IBossHardPhaseEventListener.OnChainFanBlasted),
                    EBossRecordKey.Cast, start.CastId);
                AssertIndexesInOrder(blasted, start.ZoneCount, "위험이 끝난 연쇄 부채꼴은 모든 구역이 순서대로 폭발했다");
                Assert.GreaterOrEqual(dangerEnds[0].Time, blasted[blasted.Count - 1].Time, "위험 종료는 마지막 폭발 뒤다");
            }

            Assert.Greater(ended, 0, "위험 종료까지 간 연쇄 부채꼴이 있다");
        }

        // 이동 예고는 하드 페이즈 사건으로, 같은 번호의 활성은 기본 장판 사건으로 온다
        private static void AssertTrackingAreas(BossMockCombat combat)
        {
            BossEventRecorder events = combat.Events;
            int activated = 0;
            for (int i = 0; i < events.TrackingAreas.Count; ++i)
            {
                BossTrackingAreaStart start = events.TrackingAreas[i];
                Assert.AreEqual(combat.Boss.CombatId, start.BossId);
                Assert.AreEqual(combat.Player.CombatId, start.TargetCombatId, "접속한 플레이어를 대상으로 삼는다");

                List<string> names = events.NamesOf(EBossRecordKey.Area, start.AreaId);
                int fixedAt     = names.IndexOf(nameof(IBossHardPhaseEventListener.OnTrackingAreaFixed));
                int activatedAt = names.IndexOf(nameof(IBossEventListener.OnAreaActivated));
                if (activatedAt < 0)
                {
                    continue;
                }

                ++activated;
                Assert.AreEqual(nameof(IBossHardPhaseEventListener.OnTrackingAreaStarted), names[0]);
                Assert.Greater(fixedAt, 0, "활성 전에 중심을 고정한다");
                Assert.Less(fixedAt, activatedAt, "고정 뒤에 같은 번호의 장판이 활성된다");
                Assert.AreEqual(1, events.Count(nameof(IBossHardPhaseEventListener.OnTrackingAreaFixed), EBossRecordKey.Area,
                    start.AreaId));
                Assert.AreEqual(1, events.Count(nameof(IBossEventListener.OnAreaActivated), EBossRecordKey.Area, start.AreaId));
                Assert.AreEqual(-1, names.IndexOf(nameof(IBossHardPhaseEventListener.OnTrackingAreaMoved), fixedAt),
                    "고정 뒤에는 중심을 옮기지 않는다");
            }

            Assert.Greater(activated, 0, "활성까지 간 추적 장판이 있다");
        }

        // 발사된 발의 투사체 번호는 기본 투사체 사건의 번호와 같다
        private static void AssertBarrages(BossMockCombat combat)
        {
            BossEventRecorder events = combat.Events;
            int finished = 0;
            int launched = 0;
            for (int i = 0; i < events.Barrages.Count; ++i)
            {
                BossBarrageStart start = events.Barrages[i];
                Assert.AreEqual(combat.Boss.CombatId, start.BossId);
                Assert.Greater(start.ShotCount, 0);
                Assert.LessOrEqual(start.ShotCount, Constants.Network.BOSS_BARRAGE_MAX_SHOTS, "복원 기록에 다 담을 수 있는 발 수다");
                if (!IsFinished(events, start.CastId))
                {
                    continue;
                }

                ++finished;
                List<BossEventRecord> telegraphed = events.Find(nameof(IBossHardPhaseEventListener.OnBarrageShotTelegraphed),
                    EBossRecordKey.Cast, start.CastId);
                List<BossEventRecord> fired = events.Find(nameof(IBossHardPhaseEventListener.OnBarrageShotFired),
                    EBossRecordKey.Cast, start.CastId);
                AssertIndexesInOrder(telegraphed, start.ShotCount, "끝난 난사는 모든 발을 번호 순서대로 예고한다");
                AssertIndexesInOrder(fired, start.ShotCount, "끝난 난사는 모든 발을 번호 순서대로 발사 처리한다");
            }

            for (int i = 0; i < events.Records.Count; ++i)
            {
                BossEventRecord record = events.Records[i];
                if ((record.Name != nameof(IBossHardPhaseEventListener.OnBarrageShotFired))
                    || (record.Key != EBossRecordKey.Projectile))
                {
                    continue;
                }

                ++launched;
                Assert.AreEqual(1, events.Count(nameof(IBossEventListener.OnProjectileLaunched), EBossRecordKey.Projectile,
                    record.Id), $"발사된 발의 투사체 projectileId={record.Id}가 기본 투사체 사건으로 한 번 온다");
            }

            Assert.Greater(finished, 0, "끝까지 간 난사가 있다");
            Assert.Greater(launched, 0, "실제로 발사된 발이 있다");
        }

        private static void AssertCircleBursts(BossMockCombat combat)
        {
            BossEventRecorder events = combat.Events;
            int finished = 0;
            for (int i = 0; i < events.CircleBursts.Count; ++i)
            {
                BossCircleBurstStart start = events.CircleBursts[i];
                Assert.AreEqual(combat.Boss.CombatId, start.BossId);
                Assert.AreEqual(BURST_CIRCLES, start.CircleCount, "동시 폭발의 원은 열세 개다");
                Assert.AreEqual(BURST_CIRCLES, start.Radii.Count, "중심과 반경의 수가 같아야 직렬화가 받는다");
                Assert.LessOrEqual(start.CircleCount, Constants.Network.BOSS_CIRCLE_BURST_MAX_CIRCLES);
                if (!IsFinished(events, start.CastId))
                {
                    continue;
                }

                ++finished;
                List<BossEventRecord> struck = events.Find(nameof(IBossHardPhaseEventListener.OnCircleBurstStruck),
                    EBossRecordKey.Cast, start.CastId);
                Assert.AreEqual(1, struck.Count, "동시 폭발은 한 번 터진다");
                Assert.AreEqual(start.CastStartServerTime + start.StrikeSeconds, struck[0].Time, TIME_EPSILON,
                    "폭발의 예정 시각은 시작 값으로 정해진다");
            }

            Assert.Greater(finished, 0, "끝까지 간 동시 폭발이 있다");
        }

        // 마지막 전용 시전은 돌리기를 멈춘 순간 아직 진행 중일 수 있어 한 번 이하만 본다
        private static void AssertDedicatedCastsFinishedOnce(BossEventRecorder events)
        {
            int last = events.DedicatedCasts.Count - 1;
            for (int i = 0; i <= last; ++i)
            {
                int castId   = events.DedicatedCasts[i];
                int finished = events.Count(nameof(IBossEventListener.OnFinished), EBossRecordKey.Cast, castId);
                if (i == last)
                {
                    Assert.LessOrEqual(finished, 1, $"castId={castId}");
                    continue;
                }

                Assert.AreEqual(1, finished, $"전용 시전 castId={castId}의 정상 종료는 정확히 한 번 온다");
            }
        }
    }
}
