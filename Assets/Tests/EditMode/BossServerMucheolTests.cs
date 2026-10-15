// System
using System.Collections.Generic;

// Unity
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
    public class BossServerMucheolTests
    {
        // 카탈로그 코드 기본값의 무철 정의로 서버 몸체를 돌려, 무철 전용 패턴의 알림이 중계를 거쳐 선택 계약 사건으로 나가는지 본다.
        // 에셋이 아니라 코드 기본값을 쓰는 것은 무철 수치가 아직 임시 시작값이라 에셋 조정과 무관하게 연결만 보기 위해서다.
        // 1페이즈는 콤보와 기본 예고, 2페이즈는 순차 타격과 연속 돌진, 하드는 전장 공격과 밀림까지 본다.
        // 전용 시전의 정상 종료는 기본 사건의 OnFinished로 한 번만 오고, 사망으로 끊긴 시전은 종류별 중단 사건으로 온다

        /****************************************
        *                Fields
        ****************************************/

        private const string BOSS_ID          = MucheolPatternFactory.BOSS_ID;
        private const string PHASE_ONE_PREFIX = "mucheol_p0";
        private const int    RANDOM_SEED      = 20261013;
        private const int    PHASE_ONE        = 1;
        private const int    PHASE_TWO        = 2;
        private const int    PATTERN_COUNT    = 6;      // 이지 1, 2페이즈 각각의 정의 수
        private const int    HARD_TWO_COUNT   = 7;      // 하드 2페이즈는 이지 여섯에 전장 공격을 더한다
        private const int    MAX_SECONDS      = 900;    // 모든 패턴이 한 번씩 나오기를 기다리는 모의 시간 상한
        private const int    SETTLE_SECONDS   = 10;     // 마지막으로 본 시전이 끝날 때까지 더 돌리는 시간
        private const int    COMBO_HITS       = 2;
        private const int    DASH_COUNT       = 2;      // 강화 구간이 아닌 연속 돌진의 횟수
        private const float  BATTLEFIELD_RADIUS = 44f;  // m
        private const float  BATTLEFIELD_ARC    = 315f; // 도
        private const float  PUSH_DISTANCE      = 1f;   // m, 전장 공격의 밀림 거리
        private const float  FLOAT_EPSILON      = 0.001f;
        private const double TIME_EPSILON       = 0.000001;

        private static readonly string[] SEQUENCE_IDS =
        {
            MucheolPhase2PatternFactory.P01_ID, MucheolPhase2PatternFactory.P02_ID,
            MucheolPhase2PatternFactory.P04_ID, MucheolPhase2PatternFactory.P05_ID
        };

        private static readonly string[] MULTI_DASH_IDS =
        {
            MucheolPhase2PatternFactory.P03_ID, MucheolPhase2PatternFactory.P06_ID
        };

        private static readonly string[] DEDICATED_CANCELS =
        {
            nameof(IBossMucheolEventListener.OnStrikeChainCancelled), nameof(IBossMucheolEventListener.OnMultiDashCancelled),
            nameof(IBossMucheolEventListener.OnBattlefieldCancelled)
        };

        private BossPatternCatalogSO _asset;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _asset = ScriptableObject.CreateInstance<BossPatternCatalogSO>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_asset);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void IsSortieReady_EasyCatalog_IsReady()
        {
            BossMockCombat combat = Create(EBossDifficulty.Easy, false);

            Assert.IsTrue(combat.Boss.IsSortieReady(out string reason), reason);
            Assert.IsEmpty(reason);
        }

        [Test]
        public void IsSortieReady_HardWithDisplaceTargets_IsReady()
        {
            BossMockCombat combat = Create(EBossDifficulty.Hard, true);

            Assert.IsTrue(combat.Boss.IsSortieReady(out string reason), reason);
            Assert.IsEmpty(reason);
        }

        [Test]
        public void IsSortieReady_HardWithoutDisplaceTargets_NamesOnlyBattlefieldPattern()
        {
            BossMockCombat combat = Create(EBossDifficulty.Hard, false);

            Assert.IsFalse(combat.Boss.IsSortieReady(out string reason), "밀 대상을 찾지 못하면 전장 공격을 만들지 않는다");
            StringAssert.Contains(MucheolPhase2PatternFactory.P07_ID, reason);
            StringAssert.DoesNotContain(MucheolPhase2PatternFactory.P06_ID, reason, "밀림이 필요 없는 패턴은 준비됐다");
            StringAssert.DoesNotContain("phase 1", reason);
        }

        [Test]
        public void CanCreatePattern_EasyAndHardDefinitions_AllConnected()
        {
            BossMockCombat easy = Create(EBossDifficulty.Easy, false);
            BossMockCombat hard = Create(EBossDifficulty.Hard, true);
            var definitions     = new List<BossPatternCatalogSO.PatternDefinition>();

            easy.Catalog.CollectPatterns(BOSS_ID, PHASE_ONE, definitions);
            Assert.AreEqual(PATTERN_COUNT, definitions.Count, "무철 1페이즈 정의는 여섯 개다");
            AssertAllCreatable(easy.Boss, definitions);

            easy.Catalog.CollectPatterns(BOSS_ID, PHASE_TWO, definitions);
            Assert.AreEqual(PATTERN_COUNT, definitions.Count, "무철 이지 2페이즈 정의는 여섯 개다");
            AssertAllCreatable(easy.Boss, definitions);

            hard.Catalog.CollectPatterns(BOSS_ID, PHASE_TWO, definitions);
            Assert.AreEqual(HARD_TWO_COUNT, definitions.Count, "무철 하드 2페이즈는 전장 공격을 더한 일곱 개다");
            AssertAllCreatable(hard.Boss, definitions);
        }

        [Test]
        public void Tick_PhaseOneCombat_SendsComboAndBasicTelegraphsWithCombatIdAndPatternIds()
        {
            BossMockCombat combat = Create(EBossDifficulty.Easy, false);
            BossEventRecorder events = combat.Events;
            Assert.IsTrue(combat.Begin());

            bool sawAll = combat.StepUntil(() => events.PatternsInPhase(PHASE_ONE).Count == PATTERN_COUNT, MAX_SECONDS);
            combat.StepSeconds(SETTLE_SECONDS);
            Debug.Log($"[BossMockCombat] mucheol easy phase 1: {string.Join(",", events.PatternsInPhase(PHASE_ONE))}");

            Assert.IsTrue(sawAll, $"1페이즈 여섯 패턴이 모두 나오지 않았다: {string.Join(",", events.PatternsInPhase(PHASE_ONE))}");
            Assert.AreEqual(PHASE_ONE, combat.Boss.Phase);

            Assert.Greater(events.Telegraphs.Count, 0, "부채꼴, 원형, 돌진은 기본 예고로 온다");
            for (int i = 0; i < events.Telegraphs.Count; ++i)
            {
                BossTelegraphStart telegraph = events.Telegraphs[i];
                Assert.AreEqual(combat.Boss.CombatId, telegraph.BossId, "BossId는 정의 ID가 아니라 전투 번호다");
                StringAssert.StartsWith(PHASE_ONE_PREFIX, telegraph.PatternId);
                Assert.AreNotEqual(MucheolPatternFactory.P05_ID, telegraph.PatternId, "콤보는 기본 예고로 오지 않는다");
            }

            Assert.Greater(events.StrikeChains.Count, 0, "콤보가 타 목록 사건으로 온다");
            for (int i = 0; i < events.StrikeChains.Count; ++i)
            {
                BossStrikeChainStart combo = events.StrikeChains[i];
                Assert.AreEqual(EBossStrikeChainKind.Combo, combo.Kind);
                Assert.AreEqual(combat.Boss.CombatId, combo.BossId);
                Assert.AreEqual(MucheolPatternFactory.P05_ID, combo.PatternId);
                Assert.AreEqual(COMBO_HITS, combo.StepCount);
                AssertStepListsMatch(in combo);
                for (int step = 0; step < combo.StepCount; ++step)
                {
                    Assert.AreEqual(EBossTelegraphShape.Fan, combo.Shapes[step], "콤보의 타는 모두 부채꼴이다");
                }
            }

            AssertStrikeChainsHitInOrder(events);
            AssertDedicatedCastsFinishedOnce(events);
            AssertNoDedicatedCancel(events);

            Assert.Greater(events.Charges.Count, 0, "뿔 들이받기의 돌진 결과가 온다");
            for (int i = 0; i < events.Charges.Count; ++i)
            {
                BossChargeOutcome outcome = events.Charges[i];
                Assert.AreEqual(combat.Boss.CombatId, outcome.BossId);
                Assert.AreEqual(MucheolPatternFactory.P06_ID, FindTelegraphPattern(events, outcome.CastId),
                    "돌진 결과의 시전은 기본 돌진 예고로 시작한 뿔 들이받기다");
                Assert.AreEqual(1, events.Count(nameof(IBossMucheolEventListener.OnChargeEnded), EBossRecordKey.Cast,
                    outcome.CastId), "돌진 결과는 한 시전에 한 번이다");
                CollectionAssert.DoesNotContain(events.DedicatedCasts, outcome.CastId, "이 시전의 종료는 기본 경로 하나로만 온다");
                Assert.LessOrEqual(events.Count(nameof(IBossEventListener.OnFinished), EBossRecordKey.Cast, outcome.CastId), 1);
            }
        }

        [Test]
        public void Tick_PhaseTwoCombat_SendsSequenceAndMultiDashEventsInOrder()
        {
            BossMockCombat combat = Create(EBossDifficulty.Easy, false);
            BossEventRecorder events = combat.Events;
            Assert.IsTrue(combat.Begin());
            combat.ForceTransition();
            Assert.AreEqual(PHASE_TWO, combat.Boss.Phase, "체력이 전환 비율 아래로 내려가 2페이즈다");

            bool sawAll = combat.StepUntil(() => events.PatternsInPhase(PHASE_TWO).Count == PATTERN_COUNT, MAX_SECONDS);
            combat.StepSeconds(SETTLE_SECONDS);
            Debug.Log($"[BossMockCombat] mucheol easy phase 2: {string.Join(",", events.PatternsInPhase(PHASE_TWO))}");

            Assert.IsTrue(sawAll, $"2페이즈 여섯 패턴이 모두 나오지 않았다: {string.Join(",", events.PatternsInPhase(PHASE_TWO))}");
            Assert.AreEqual(EBossPhaseState.Active, combat.Boss.PhaseState, "결계가 없는 보스는 다음 틱에 인계를 마친다");
            Assert.IsFalse(combat.Boss.HasBarrier);
            Assert.AreEqual(0, events.Battlefields.Count, "이지에는 전장 공격이 없다");

            MucheolPhase2Spec spec = MucheolSortieData.CreatePhaseTwoSpec();
            int sequences = 0;
            for (int i = 0; i < events.StrikeChains.Count; ++i)
            {
                BossStrikeChainStart chain = events.StrikeChains[i];
                if (chain.Kind != EBossStrikeChainKind.Sequence)
                {
                    continue;
                }

                ++sequences;
                Assert.AreEqual(combat.Boss.CombatId, chain.BossId);
                CollectionAssert.Contains(SEQUENCE_IDS, chain.PatternId);
                AssertStepListsMatch(in chain);
                AssertMatchesSpec(in chain, FindSequenceSpec(spec, chain.PatternId));
            }

            Assert.Greater(sequences, 0, "순차 타격이 타 목록 사건으로 온다");
            AssertStrikeChainsHitInOrder(events);

            int finishedDashes = 0;
            for (int i = 0; i < events.MultiDashes.Count; ++i)
            {
                BossMultiDashStart dash = events.MultiDashes[i];
                Assert.AreEqual(combat.Boss.CombatId, dash.BossId);
                CollectionAssert.Contains(MULTI_DASH_IDS, dash.PatternId);
                Assert.AreEqual(DASH_COUNT, dash.DashCount, "강화 구간이 아니면 두 번 달린다");
                if (events.Count(nameof(IBossEventListener.OnFinished), EBossRecordKey.Cast, dash.CastId) == 1)
                {
                    ++finishedDashes;
                    AssertMultiDashOrder(events, dash.CastId);
                }
            }

            Assert.Greater(finishedDashes, 0, "끝까지 달린 연속 돌진이 있다");

            AssertDedicatedCastsFinishedOnce(events);
            AssertNoDedicatedCancel(events);
        }

        [Test]
        public void Tick_HardPhaseTwoCombat_SendsBattlefieldAndDisplacesHitTarget()
        {
            BossMockCombat combat = Create(EBossDifficulty.Hard, true);
            BossEventRecorder events = combat.Events;
            Assert.IsTrue(combat.Begin());
            combat.ForceTransition();

            bool sawPush = combat.StepUntil(() => (combat.Displace.Destinations.Count > 0)
                && (events.Count(nameof(IBossMucheolEventListener.OnBattlefieldHit)) > 0), MAX_SECONDS);
            combat.StepSeconds(SETTLE_SECONDS);
            Debug.Log($"[BossMockCombat] mucheol hard phase 2: {string.Join(",", events.PatternsInPhase(PHASE_TWO))}");

            Assert.IsTrue(sawPush, $"전장 공격의 타격과 밀림이 나오지 않았다: {string.Join(",", events.PatternsInPhase(PHASE_TWO))}");

            int hitCasts = 0;
            for (int i = 0; i < events.Battlefields.Count; ++i)
            {
                BossBattlefieldStart start = events.Battlefields[i];
                Assert.AreEqual(combat.Boss.CombatId, start.BossId);
                Assert.AreEqual(BATTLEFIELD_RADIUS, start.Radius, FLOAT_EPSILON);
                Assert.AreEqual(BATTLEFIELD_ARC, start.ArcAngle, FLOAT_EPSILON);

                List<string> names = events.NamesOf(EBossRecordKey.Cast, start.CastId);
                Assert.AreEqual(nameof(IBossMucheolEventListener.OnBattlefieldStarted), names[0]);

                List<BossEventRecord> hits = events.Find(nameof(IBossMucheolEventListener.OnBattlefieldHit),
                    EBossRecordKey.Cast, start.CastId);
                if (hits.Count == 0)
                {
                    continue;
                }

                ++hitCasts;
                Assert.AreEqual(1, hits.Count, "전장 공격은 단발이다");
                Assert.AreEqual(MucheolPhase2PatternFactory.P07_ID, hits[0].PatternId);
                Assert.GreaterOrEqual(hits[0].Time, start.StartServerTime + start.TelegraphSeconds - TIME_EPSILON,
                    "예고가 끝나기 전에는 타격하지 않는다");
            }

            Assert.Greater(hitCasts, 0);
            CollectionAssert.Contains(events.PatternsInPhase(PHASE_TWO), MucheolPhase2PatternFactory.P07_ID);

            BossMockCombat.RecordingDisplaceLookup displace = combat.Displace;
            for (int i = 0; i < displace.Destinations.Count; ++i)
            {
                Vector3 moved = displace.Destinations[i] - displace.Origins[i];
                Assert.Greater(moved.magnitude, 0f, "실제로 움직일 거리가 있을 때만 대상에 넘긴다");
                Assert.LessOrEqual(moved.magnitude, PUSH_DISTANCE + FLOAT_EPSILON);
                Assert.AreEqual(0f, moved.y, FLOAT_EPSILON, "수평으로만 민다");
                Assert.IsTrue(HasBattlefieldHitAt(events, displace.DisplaceTimes[i]), "밀림은 전장 공격이 타격한 틱에만 일어난다");
            }

            AssertDedicatedCastsFinishedOnce(events);
            AssertNoDedicatedCancel(events);
        }

        [TestCase(EBossDifficulty.Easy, false, nameof(IBossMucheolEventListener.OnStrikeChainStarted),
            nameof(IBossMucheolEventListener.OnStrikeChainCancelled))]
        [TestCase(EBossDifficulty.Easy, true, nameof(IBossMucheolEventListener.OnStrikeChainStarted),
            nameof(IBossMucheolEventListener.OnStrikeChainCancelled))]
        [TestCase(EBossDifficulty.Easy, true, nameof(IBossMucheolEventListener.OnMultiDashStarted),
            nameof(IBossMucheolEventListener.OnMultiDashCancelled))]
        [TestCase(EBossDifficulty.Hard, true, nameof(IBossMucheolEventListener.OnBattlefieldStarted),
            nameof(IBossMucheolEventListener.OnBattlefieldCancelled))]
        public void Tick_BossKilledDuringDedicatedCast_SendsCancelOfThatCastOnNextTick(EBossDifficulty difficulty,
            bool isPhaseTwo, string startName, string cancelName)
        {
            BossMockCombat combat = Create(difficulty, true);
            BossEventRecorder events = combat.Events;
            Assert.IsTrue(combat.Begin());
            if (isPhaseTwo)
            {
                combat.ForceTransition();
            }

            // 시작 사건이 온 틱에서 멈추므로 그 시전은 아직 예고 중이다
            Assert.IsTrue(combat.StepUntil(() => events.Count(startName) > 0, MAX_SECONDS), $"{startName}이 오지 않았다");
            int castId = events.DedicatedCasts[events.DedicatedCasts.Count - 1];
            Assert.AreEqual(1, events.Count(startName, EBossRecordKey.Cast, castId), "마지막 전용 시전이 방금 시작한 그 시전이다");
            Assert.AreEqual(0, events.Count(cancelName, EBossRecordKey.Cast, castId));
            Assert.IsFalse(events.PhaseLedger.IsEmpty, "진행 중인 전용 시전은 복원 기록에 남아 있다");

            combat.KillBoss();
            Assert.AreEqual(0, events.Count(cancelName, EBossRecordKey.Cast, castId), "정리는 다음 틱에 한다");
            combat.Step();

            Assert.IsTrue(combat.Boss.IsBossDeathRecorded);
            Assert.AreEqual(1, events.Count(cancelName, EBossRecordKey.Cast, castId), "진행 중이던 전용 시전의 중단이 한 번 온다");
            Assert.AreEqual(0, events.Count(nameof(IBossEventListener.OnFinished), EBossRecordKey.Cast, castId),
                "중단된 시전에는 정상 종료가 오지 않는다");
            Assert.IsTrue(events.PhaseLedger.IsEmpty, "중단 사건만으로 복원 기록이 비워진다");
        }

        /****************************************
        *                Helpers
        ****************************************/

        private BossMockCombat Create(EBossDifficulty difficulty, bool withDisplace)
        {
            return new BossMockCombat(_asset, BOSS_ID, difficulty, RANDOM_SEED, false, withDisplace);
        }

        private static void AssertAllCreatable(BossServer boss, List<BossPatternCatalogSO.PatternDefinition> definitions)
        {
            for (int i = 0; i < definitions.Count; ++i)
            {
                Assert.IsTrue(boss.CanCreatePattern(definitions[i]), $"{definitions[i].DefinitionId}를 만들 수 있다");
            }
        }

        // 직렬화는 길이가 어긋난 타 목록을 거절하므로 서버가 보낸 값이 그대로 실릴 수 있는지 본다
        private static void AssertStepListsMatch(in BossStrikeChainStart chain)
        {
            int count = chain.StepCount;
            Assert.Greater(count, 0);
            Assert.LessOrEqual(count, Constants.Network.BOSS_STRIKE_CHAIN_MAX_STEPS);
            Assert.AreEqual(count, chain.Shapes.Count);
            Assert.AreEqual(count, chain.Ranges.Count);
            Assert.AreEqual(count, chain.ArcAngles.Count);
            Assert.AreEqual(count, chain.Widths.Count);
            Assert.AreEqual(count, chain.AimOffsetDegrees.Count);
            Assert.AreEqual(count, chain.DelaySeconds.Count);
        }

        private static void AssertMatchesSpec(in BossStrikeChainStart chain, MucheolSequenceSpec spec)
        {
            Assert.IsNotNull(spec, $"{chain.PatternId}의 실행 데이터가 없다");
            Assert.AreEqual(spec.StepCount, chain.StepCount, $"{chain.PatternId}의 타 수가 실행 데이터와 같다");
            for (int i = 0; i < spec.StepCount; ++i)
            {
                MucheolSequenceStep step = spec.Steps[i];
                Assert.AreEqual(step.Range, chain.Ranges[i], FLOAT_EPSILON);
                Assert.AreEqual(step.ArcAngle, chain.ArcAngles[i], FLOAT_EPSILON);
                Assert.AreEqual(step.Width, chain.Widths[i], FLOAT_EPSILON);
                Assert.AreEqual(step.AimOffsetDegrees, chain.AimOffsetDegrees[i], FLOAT_EPSILON);
                Assert.AreEqual(step.DelaySeconds, chain.DelaySeconds[i], FLOAT_EPSILON);
            }
        }

        private static MucheolSequenceSpec FindSequenceSpec(MucheolPhase2Spec spec, string patternId)
        {
            switch (patternId)
            {
                case MucheolPhase2PatternFactory.P01_ID:
                    return spec.P01;
                case MucheolPhase2PatternFactory.P02_ID:
                    return spec.P02;
                case MucheolPhase2PatternFactory.P04_ID:
                    return spec.P04;
                case MucheolPhase2PatternFactory.P05_ID:
                    return spec.P05;
                default:
                    return null;
            }
        }

        // 정상 종료한 타 목록 시전은 타가 0번부터 빠짐없이 순서대로 확정된다
        private static void AssertStrikeChainsHitInOrder(BossEventRecorder events)
        {
            int finishedChains = 0;
            for (int i = 0; i < events.StrikeChains.Count; ++i)
            {
                BossStrikeChainStart chain = events.StrikeChains[i];
                if (events.Count(nameof(IBossEventListener.OnFinished), EBossRecordKey.Cast, chain.CastId) == 0)
                {
                    continue;
                }

                List<BossEventRecord> hits = events.Find(nameof(IBossMucheolEventListener.OnStrikeChainHit),
                    EBossRecordKey.Cast, chain.CastId);
                Assert.AreEqual(chain.StepCount, hits.Count, $"{chain.PatternId} castId={chain.CastId}의 확정된 타 수");
                for (int step = 0; step < hits.Count; ++step)
                {
                    Assert.AreEqual(step, hits[step].Index);
                    Assert.AreEqual(chain.Kind == EBossStrikeChainKind.Sequence, hits[step].Flag, "타 사건의 종류가 시작과 같다");
                }

                ++finishedChains;
            }

            Assert.Greater(finishedChains, 0, "정상 종료한 타 목록 시전이 있다");
        }

        // 마지막 전용 시전은 돌리기를 멈춘 순간 아직 진행 중일 수 있어 한 번 이하만 본다
        private static void AssertDedicatedCastsFinishedOnce(BossEventRecorder events)
        {
            int last = events.DedicatedCasts.Count - 1;
            Assert.GreaterOrEqual(last, 0, "전용 시전이 하나도 없다");
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

        private static void AssertNoDedicatedCancel(BossEventRecorder events)
        {
            for (int i = 0; i < DEDICATED_CANCELS.Length; ++i)
            {
                Assert.AreEqual(0, events.Count(DEDICATED_CANCELS[i]), $"끊은 적이 없는데 {DEDICATED_CANCELS[i]}이 왔다");
            }
        }

        // 첫 돌진은 예고 시작 뒤에, 다음 돌진은 재조준 시작 뒤에 달리고 달린 돌진은 모두 멈춘다
        private static void AssertMultiDashOrder(BossEventRecorder events, int castId)
        {
            var order = new List<string>();
            List<string> names = events.NamesOf(EBossRecordKey.Cast, castId);
            for (int i = 0; i < names.Count; ++i)
            {
                if ((names[i] == nameof(IBossMucheolEventListener.OnMultiDashStarted))
                    || (names[i] == nameof(IBossMucheolEventListener.OnMultiDashReaimStarted))
                    || (names[i] == nameof(IBossMucheolEventListener.OnMultiDashDashStarted))
                    || (names[i] == nameof(IBossMucheolEventListener.OnMultiDashDashStopped)))
                {
                    order.Add(names[i]);
                }
            }

            CollectionAssert.AreEqual(new[]
            {
                nameof(IBossMucheolEventListener.OnMultiDashStarted),
                nameof(IBossMucheolEventListener.OnMultiDashDashStarted),
                nameof(IBossMucheolEventListener.OnMultiDashDashStopped),
                nameof(IBossMucheolEventListener.OnMultiDashReaimStarted),
                nameof(IBossMucheolEventListener.OnMultiDashDashStarted),
                nameof(IBossMucheolEventListener.OnMultiDashDashStopped)
            }, order, $"castId={castId}: {string.Join(",", order)}");

            List<BossEventRecord> reaims = events.Find(nameof(IBossMucheolEventListener.OnMultiDashReaimStarted),
                EBossRecordKey.Cast, castId);
            Assert.AreEqual(1, reaims[0].Index, "재조준은 둘째 돌진의 것이다");

            List<BossEventRecord> starts = events.Find(nameof(IBossMucheolEventListener.OnMultiDashDashStarted),
                EBossRecordKey.Cast, castId);
            Assert.AreEqual(0, starts[0].Index);
            Assert.AreEqual(1, starts[1].Index);
        }

        private static string FindTelegraphPattern(BossEventRecorder events, int castId)
        {
            for (int i = 0; i < events.Telegraphs.Count; ++i)
            {
                if (events.Telegraphs[i].CastId == castId)
                {
                    return events.Telegraphs[i].PatternId;
                }
            }

            return string.Empty;
        }

        private static bool HasBattlefieldHitAt(BossEventRecorder events, double serverTime)
        {
            for (int i = 0; i < events.Records.Count; ++i)
            {
                BossEventRecord record = events.Records[i];
                if ((record.Name == nameof(IBossMucheolEventListener.OnBattlefieldHit))
                    && (System.Math.Abs(record.Time - serverTime) <= TIME_EPSILON))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
