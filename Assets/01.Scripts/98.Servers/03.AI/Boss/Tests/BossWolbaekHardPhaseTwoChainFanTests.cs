// System
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

using EPatternGroup     = Shinshi.Data.BossPatternCatalogSO.EPatternGroup;
using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    // 월백 하드 2페이즈 P03 잔류 부채꼴 연쇄를 에셋의 실제 정의와 실제 팩토리, 실제 공격 FSM으로 확인한다
    // 실제 서버 틱, 복제, 화면은 거치지 않는다. 전장 중심과 반지름은 고정 전장 입력으로 넣는다
    // 피해, 시간, 형상 수치는 Play Mode와 다인 접속 검증 전의 초기값이라 값 자체의 정답을 보장하지 않는다
    //
    // 기본 좌표계: 보스는 전장 중심에 서고 난수(Roll)가 0이면 피난 방향 가운데는 월드 +Z다
    // 각도는 +Z에서 시계 방향(위에서 볼 때 오른쪽)이다. 피난 방향은 -22.5도~+22.5도이고 구역 k의 가운데는 45 + 45 x k도다
    // 구역 k의 폭발 시각은 시전 시작 + 5.0 + 0.5 x k, 위험 종료는 8.8, 다음 선택은 10.0이다
    // 같은 대상은 0.5초 간격으로 다시 맞으므로 구역마다 대상을 따로 두지 않은 테스트는 요청 수를 손으로 센 값을 쓴다
    public class BossWolbaekHardPhaseTwoChainFanTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CATALOG_PATH  = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const string WOLBAEK_ID    = "boss_wolbaek";
        private const string HARD_ID       = BossPatternCatalogSO.WOLBAEK_HARD_P2_P03_ID;
        private const string EASY_ID       = BossPatternCatalogSO.WOLBAEK_P2_P03_ID;
        private const float  HREF          = 1000f;
        private const float  RAW_DAMAGE    = 0.18f * HREF;
        private const float  TOLERANCE     = 0.001f;
        private const double TIME_TOL      = 0.0001;
        private const float  ARENA_RADIUS  = 18f;
        private const int    ZONE_COUNT    = 7;
        private const float  ZONE_ARC      = 45f;
        private const float  SAFE_ARC      = 45f;
        private const float  PROBE_RADIUS  = 10f; // 보스에서 대상까지 거리, 최소 사거리 16m 안쪽이다

        // 설계 시각(전부 정확한 이진 분수라 FSM 경계와 오차가 없다). 위험 종료 6.3은 0.8초가 이진 분수가 아니라 허용 오차로 비교한다
        private const double FIRST_STRIKE     = 5.0;
        private const double STRIKE_INTERVAL  = 0.5;
        private const double DANGER_END       = 8.8;
        private const double NEXT_SELECTION   = 10.0;

        private static readonly Vector3 ArenaCenter = new Vector3(50f, 0f, -30f);

        /****************************************
        *               Fakes
        ****************************************/

        private sealed class ChainQuery : ICombatTargetQuery
        {
            public List<ICombatTarget> Targets { get; } = new List<ICombatTarget>();

            public int GetLivingPlayers(List<ICombatTarget> results)
            {
                results.Clear();
                for (int i = 0; i < Targets.Count; ++i)
                {
                    if ((Targets[i].LifeState == EParticipantLifeState.Alive) && (!Targets[i].IsSummon))
                    {
                        results.Add(Targets[i]);
                    }
                }

                return results.Count;
            }

            public int GetLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons,
                List<ICombatTarget> results)
            {
                results.Clear();
                for (int i = 0; i < Targets.Count; ++i)
                {
                    ICombatTarget target = Targets[i];
                    Vector3 offset = target.Position - center;
                    offset.y = 0f;
                    if ((target.LifeState == EParticipantLifeState.Alive) && (target.Faction == faction)
                        && (includeSummons || (!target.IsSummon)) && (offset.magnitude <= radius))
                    {
                        results.Add(target);
                    }
                }

                return results.Count;
            }

            public int CountLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons)
            {
                return GetLivingInRadius(center, radius, faction, includeSummons, new List<ICombatTarget>());
            }

            public bool TryGet(int combatId, out ICombatTarget target)
            {
                for (int i = 0; i < Targets.Count; ++i)
                {
                    if (Targets[i].CombatId == combatId)
                    {
                        target = Targets[i];
                        return true;
                    }
                }

                target = null;
                return false;
            }
        }

        // 대상별로 정해 둔 결과를 돌려준다. 정하지 않은 대상은 적용이다
        private sealed class ScriptedResolver : IDamageResolver
        {
            private int _nextId = 1;

            public List<DamageRequest>           Requests { get; } = new List<DamageRequest>();
            public Dictionary<int, EDamageOutcome> Outcomes { get; } = new Dictionary<int, EDamageOutcome>();

            public DamageResult Apply(in DamageRequest request)
            {
                Requests.Add(request);
                if (!Outcomes.TryGetValue(request.HitTargetId, out EDamageOutcome outcome))
                {
                    return DamageResult.Applied(1f, false, false);
                }

                switch (outcome)
                {
                    case EDamageOutcome.Applied:
                        return DamageResult.Applied(1f, false, false);
                    case EDamageOutcome.Guarded:
                        return DamageResult.Guarded();
                    default:
                        return DamageResult.Ignored(outcome);
                }
            }

            public int IssueInstanceId()
            {
                int issued = _nextId;
                ++_nextId;
                return issued;
            }
        }

        private struct Blast
        {
            public int    ZoneIndex;
            public double Scheduled;
            public double Processed;
            public int    HitCount;
        }

        private sealed class RecordingListener : IBossChainFanListener
        {
            public List<int>                  StartedCasts   = new List<int>();
            public List<BossChainFanLayout>   StartedLayouts = new List<BossChainFanLayout>();
            public List<double>               StartedAt      = new List<double>();
            public List<float>                Reveals        = new List<float>();
            public List<float>                FirstBlasts    = new List<float>();
            public List<float>                Intervals      = new List<float>();
            public List<float>                Lingers        = new List<float>();
            public List<Blast>                Blasts         = new List<Blast>();
            public List<double>               DangerEnds     = new List<double>();
            public List<int>                  CancelBlasted  = new List<int>();
            public List<bool>                 CancelEnded    = new List<bool>();
            public List<EBossInterruptReason> CancelReasons  = new List<EBossInterruptReason>();

            public void OnChainFanStarted(int castInstanceId, in BossChainFanLayout layout, double castStartServerTime,
                float safeRevealSeconds, float firstBlastSeconds, float blastIntervalSeconds, float lingerSeconds)
            {
                StartedCasts.Add(castInstanceId);
                StartedLayouts.Add(layout);
                StartedAt.Add(castStartServerTime);
                Reveals.Add(safeRevealSeconds);
                FirstBlasts.Add(firstBlastSeconds);
                Intervals.Add(blastIntervalSeconds);
                Lingers.Add(lingerSeconds);
            }

            public void OnChainFanBlasted(int castInstanceId, int zoneIndex, double scheduledServerTime,
                double processedServerTime, int hitCount)
            {
                Blasts.Add(new Blast { ZoneIndex = zoneIndex, Scheduled = scheduledServerTime, Processed = processedServerTime,
                    HitCount = hitCount });
            }

            public void OnChainFanDangerEnded(int castInstanceId, double scheduledServerTime)
            {
                DangerEnds.Add(scheduledServerTime);
            }

            public void OnChainFanCancelled(int castInstanceId, int blastedCount, bool dangerEnded, EBossInterruptReason reason)
            {
                CancelBlasted.Add(blastedCount);
                CancelEnded.Add(dangerEnded);
                CancelReasons.Add(reason);
            }
        }

        // 공격 FSM에 실제 팩토리가 만든 패턴을 올린다. 기본 대상이 없어 요청 수는 이 테스트가 놓은 대상만 센다
        private sealed class Run
        {
            public FakeCombatTarget    Self      = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = ArenaCenter };
            public ChainQuery          Query     = new ChainQuery();
            public ScriptedResolver    Resolver  = new ScriptedResolver();
            public RecordingListener   Listener  = new RecordingListener();
            public BossFixedArena      Arena     = new BossFixedArena(ArenaCenter, ARENA_RADIUS);
            public FakeBossRandom      Random    = new FakeBossRandom();
            public BossDamageGate      Gate;
            public BossAttackFsm       Fsm       = new BossAttackFsm();
            public PatternDefinition   Definition;
            public BossChainFanPattern Pattern;
            public int                 CastId;
            public bool                UseRandom = true;

            public Run()
            {
                Gate = new BossDamageGate(Resolver);
                Gate.Open();
                Definition = TimelineDefinition();
                CastId     = Gate.IssueInstanceId();
            }

            public FakeCombatTarget Add(int id, Vector3 position, bool isSummon = false)
            {
                FakeCombatTarget target = new FakeCombatTarget { CombatId = id, Faction = ECombatFaction.Player,
                    Position = position, IsSummon = isSummon };
                Query.Targets.Add(target);
                return target;
            }

            // 보스 위치(시작 순간 고정)를 기준으로 시계 방향 각도와 거리의 점에 놓는다
            public FakeCombatTarget AddPolar(int id, float degrees, float radius = PROBE_RADIUS)
            {
                return Add(id, Polar(Self.Position, degrees, radius));
            }

            public void Start(double time = 0.0)
            {
                WolbaekPatternFactory factory = new WolbaekPatternFactory(HREF, arena: Arena, chainFanListener: Listener);
                IBossPattern pattern = factory.Create(Definition);
                Assert.IsNotNull(pattern);
                Pattern = (BossChainFanPattern)pattern;
                BossPatternContext context = new BossPatternContext(Definition, CastId, CombatIds.NONE, Self, Query, Gate,
                    UseRandom ? Random : null);
                Assert.IsTrue(Fsm.Start(Definition, pattern, in context, time));
            }

            public void TickAt(double time)
            {
                Fsm.Tick(time);
            }

            public void TickRange(double from, double to, double step = 0.1)
            {
                for (double t = from; t <= to + 0.000001; t += step)
                {
                    TickAt(t);
                }
            }

            public int Hits(int targetId)
            {
                int count = 0;
                for (int i = 0; i < Resolver.Requests.Count; ++i)
                {
                    count += (Resolver.Requests[i].HitTargetId == targetId) ? 1 : 0;
                }

                return count;
            }
        }

        /****************************************
        *               Helpers
        ****************************************/

        private static BossPatternCatalogSO LoadCatalog()
        {
            BossPatternCatalogSO catalog = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assert.IsNotNull(catalog);
            return catalog;
        }

        private static PatternDefinition LoadDefinition(string id)
        {
            Assert.IsTrue(LoadCatalog().TryGetPattern(id, out PatternDefinition definition), id);
            return definition;
        }

        // 시간표 경계와 중단 검사는 고정한 실행 입력으로 확인하고, 최신 밸런스 에셋 계약은 Asset_ 검사에서 별도로 확인한다.
        // 복사본만 바꾸므로 운영 카탈로그와 다른 테스트가 읽는 정의에는 영향을 주지 않는다.
        private static PatternDefinition TimelineDefinition()
        {
            PatternDefinition fixture = JsonUtility.FromJson<PatternDefinition>(JsonUtility.ToJson(LoadDefinition(HARD_ID)));
            JsonUtility.FromJsonOverwrite("{\"_telegraphSeconds\":5.0,\"_activeSeconds\":3.0,\"_recoverySeconds\":2.0}", fixture);
            return fixture;
        }

        // origin에서 +Z 기준 시계 방향 degrees도, 거리 radius인 점
        private static Vector3 Polar(Vector3 origin, float degrees, float radius)
        {
            float rad = degrees * Mathf.Deg2Rad;
            return origin + (new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * radius);
        }

        // 구역 k 가운데의 각도
        private static float ZoneCenter(int zoneIndex)
        {
            return (SAFE_ARC * 0.5f) + (ZONE_ARC * (zoneIndex + 0.5f));
        }

        private static double Strike(int zoneIndex)
        {
            return FIRST_STRIKE + (zoneIndex * STRIKE_INTERVAL);
        }

        private static BossChainFanLayout MakeLayout(Vector3 origin, float minRange = 16f, float arenaRadius = ARENA_RADIUS)
        {
            return new BossChainFanLayout(origin, Vector3.forward, minRange, ZONE_COUNT, ZONE_ARC, ArenaCenter, arenaRadius);
        }

        // 에셋 정의를 바탕으로 chain 값만 바꾼 정의, 선택 데이터는 에셋 그대로다
        private static PatternDefinition MakeVariant(string id, int zones = 7, float interval = 0.5f, float arc = 315f,
            float telegraph = 2.5f, float active = 3f, float recovery = 1f, float linger = 0.8f, float reveal = 1f,
            float cooldown = 0.5f, int maxHits = 2, int gridLines = 0)
        {
            PatternDefinition source = LoadDefinition(HARD_ID);
            return new PatternDefinition(id, WOLBAEK_ID, 2, source.BaseWeight,
                new[] { 1.3f, 1.2f, 0.7f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1.15f, 1.3f }, new[] { 0.25f, 0.3f, 0.7f, 1f },
                telegraph, 0f, 0f, active, recovery, EnemyCatalogSO.AttackShapeSpec.Fan(16f, arc), 0.18f, true,
                group: EPatternGroup.Large, enhancedBaseWeight: 14f, sweepZoneCount: zones, sweepIntervalSeconds: interval,
                difficulty: EBossDifficulty.Hard, gridLineCount: gridLines,
                chainLingerSeconds: linger, chainSafeRevealSeconds: reveal, chainHitCooldownSeconds: cooldown,
                chainMaxHitsPerTarget: maxHits);
        }

        /****************************************
        *      Asset, Catalog, Factory
        ****************************************/

        [Test]
        public void Asset_HardP03_HasStartingValuesAndAHardOnlyId()
        {
            PatternDefinition p = LoadDefinition(HARD_ID);

            Assert.AreEqual("wolbaek_hard_ph2_p03", p.DefinitionId);
            Assert.AreEqual(HARD_ID + ".name", p.DisplayKey);
            Assert.AreEqual(EBossDifficulty.Hard, p.Difficulty);
            Assert.AreEqual(WOLBAEK_ID, p.BossId);
            Assert.AreEqual(2, p.Phase);
            Assert.AreEqual(EPatternGroup.Large, p.Group);
            Assert.AreEqual(EEnemyAttackShape.Fan, p.Shape.Shape);
            Assert.AreEqual(16f, p.Shape.Range, TOLERANCE, "최소 사거리");
            Assert.AreEqual(315f, p.Shape.ArcAngle, TOLERANCE, "폭발 구역 전체, 나머지 45도가 피난 방향");
            Assert.AreEqual(ZONE_COUNT, p.RawSweepZoneCount);
            Assert.AreEqual(0.5f, p.SweepIntervalSeconds, TOLERANCE);
            Assert.AreEqual(2.4f, p.TelegraphSeconds, TOLERANCE, "d60ac1a9: 하드 예고를 이지 이하로 단축");
            Assert.AreEqual(3f, p.ActiveSeconds, TOLERANCE, "7구역의 0.5초 간격은 유지한다");
            Assert.AreEqual(1.5f, p.RecoverySeconds, TOLERANCE);
            Assert.AreEqual(0.18f, p.DamageRef, TOLERANCE);
            Assert.IsTrue(p.Guardable, "방어와 반격 가능");
            Assert.IsFalse(p.HasControlWindow);
            Assert.AreEqual(0.8f, p.ChainLingerSeconds, TOLERANCE, "마지막 폭발 뒤 잔류 시간은 유지한다");
            Assert.AreEqual(1f, p.ChainSafeRevealSeconds, TOLERANCE);
            Assert.AreEqual(0.5f, p.ChainHitCooldownSeconds, TOLERANCE);
            Assert.AreEqual(2, p.ChainMaxHitsPerTarget);
            Assert.AreEqual(0, p.MaxConcurrentAreas, "장판 상한으로 후보에서 빠지지 않는다");
            Assert.AreEqual(0, p.RawAreaTargetCount);
            Assert.AreEqual(6.9f, p.TelegraphSeconds + p.ActiveSeconds + p.RecoverySeconds, TOLERANCE);
        }

        [Test]
        public void Asset_HardP03_SelectionDataFollowsEasyP03WithTheHardRecentTable()
        {
            PatternDefinition hard = LoadDefinition(HARD_ID);
            PatternDefinition easy = LoadDefinition(EASY_ID);

            Assert.AreEqual(easy.BaseWeight, hard.BaseWeight);
            Assert.AreEqual(easy.EnhancedBaseWeight, hard.EnhancedBaseWeight);
            Assert.AreEqual(easy.Group, hard.Group);
            BossWolbaekHardDataKit.AssertSameTable(easy.DistanceMultipliers, hard.DistanceMultipliers, "distance");
            BossWolbaekHardDataKit.AssertSameTable(easy.DirectionMultipliers, hard.DirectionMultipliers, "direction");
            BossWolbaekHardDataKit.AssertSameTable(easy.NearbyMultipliers, hard.NearbyMultipliers, "nearby");
            BossWolbaekHardDataKit.AssertTable(BossWolbaekHardDataKit.G_HardRecent, hard.RecentUseMultipliers, "recent");
            Assert.AreEqual(0.25f, hard.RecentUseMultipliers[0], TOLERANCE, "직전 사용 0.25");
            Assert.AreEqual(0.3f, hard.RecentUseMultipliers[1], TOLERANCE);
            Assert.AreEqual(0.7f, hard.RecentUseMultipliers[2], TOLERANCE);
            Assert.AreEqual(1f, hard.RecentUseMultipliers[3], TOLERANCE);
        }

        [Test]
        public void Asset_EasyP03_IsUnchanged_AndKeepsTheSweepExecution()
        {
            PatternDefinition easy = LoadDefinition(EASY_ID);

            Assert.AreEqual(EBossDifficulty.Easy, easy.Difficulty);
            Assert.AreEqual(4, easy.RawSweepZoneCount);
            Assert.AreEqual(0.25f, easy.SweepIntervalSeconds, TOLERANCE);
            Assert.AreEqual(EEnemyAttackShape.Fan, easy.Shape.Shape);
            Assert.AreEqual(18f, easy.Shape.Range, TOLERANCE, "bfc50fea4: 월백 반월 패턴 사거리 확장");
            Assert.AreEqual(180f, easy.Shape.ArcAngle, TOLERANCE);
            Assert.AreEqual(2.4f, easy.TelegraphSeconds, TOLERANCE);
            Assert.AreEqual(0.75f, easy.ActiveSeconds, TOLERANCE);
            Assert.AreEqual(1.6f, easy.RecoverySeconds, TOLERANCE);
            Assert.AreEqual(0.18f, easy.DamageRef, TOLERANCE);
            Assert.AreEqual(0f, easy.ChainLingerSeconds, "이지는 잔류가 없다");
            Assert.AreEqual(0f, easy.ChainSafeRevealSeconds);
            Assert.AreEqual(0f, easy.ChainHitCooldownSeconds);
            Assert.AreEqual(0, easy.ChainMaxHitsPerTarget);

            BossFixedArena arena = new BossFixedArena(ArenaCenter, ARENA_RADIUS);
            WolbaekPatternFactory full = new WolbaekPatternFactory(HREF, arena: arena, chainFanListener: new RecordingListener());
            Assert.IsInstanceOf<BossSweepBlastPattern>(full.Create(easy), "이지 ID는 반월 연쇄 실행에 연결된 채다");
            Assert.IsInstanceOf<BossChainFanPattern>(full.Create(LoadDefinition(HARD_ID)), "하드 ID는 잔류 연쇄 실행에 연결된다");
            Assert.IsInstanceOf<BossSweepBlastPattern>(new WolbaekPatternFactory(HREF).Create(easy), "이지는 전장 입력과 알림 없이도 만들어진다");
            Assert.IsTrue(new WolbaekPatternFactory(HREF).CanCreate(easy));
        }

        [Test]
        public void Asset_Catalog_SeparatesDifficultiesStaysValidAndHardPhaseTwoOpensWithSeven()
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            var easy = new List<PatternDefinition>();
            var hard = new List<PatternDefinition>();
            catalog.CollectPatterns(WOLBAEK_ID, 2, EBossDifficulty.Easy, easy);
            catalog.CollectPatterns(WOLBAEK_ID, 2, EBossDifficulty.Hard, hard);

            Assert.AreEqual(7, easy.Count);
            Assert.AreEqual(7, hard.Count);
            Assert.AreEqual(HARD_ID, hard[2].DefinitionId);
            for (int i = 0; i < easy.Count; ++i)
            {
                Assert.AreNotEqual(HARD_ID, easy[i].DefinitionId);
            }

            var errors = new List<string>();
            catalog.Validate(errors);
            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.IsTrue(catalog.IsPhaseTuned(WOLBAEK_ID, 2), "이지 2페이즈 준비 결과는 그대로다");
            Assert.IsTrue(catalog.IsTuned(WOLBAEK_ID));

            var hardErrors = new List<string>();
            catalog.ValidateTuning(WOLBAEK_ID, 2, EBossDifficulty.Hard, hardErrors);
            Assert.IsTrue(catalog.IsPhaseTuned(WOLBAEK_ID, 2, EBossDifficulty.Hard), "P01~P07 일곱 개가 갖춰져 하드 2페이즈가 열린다");
            Assert.IsEmpty(hardErrors, string.Join("\n", hardErrors));
            Assert.IsTrue(catalog.IsPatternReady(WOLBAEK_ID, LoadDefinition(HARD_ID)), "P03 한 개의 데이터 자체는 준비됐다");
        }

        [Test]
        public void Factory_NeedsTheArenaAndAListener_AndReadinessAgrees()
        {
            PatternDefinition p = LoadDefinition(HARD_ID);
            BossFixedArena arena = new BossFixedArena(ArenaCenter, ARENA_RADIUS);
            RecordingListener listener = new RecordingListener();

            Assert.IsFalse(new WolbaekPatternFactory(HREF).CanCreate(p));
            Assert.IsNull(new WolbaekPatternFactory(HREF).Create(p));
            Assert.IsFalse(new WolbaekPatternFactory(HREF, arena: arena).CanCreate(p), "알림을 받을 쪽이 없으면 보이지 않는 피해가 나가므로 만들지 않는다");
            Assert.IsNull(new WolbaekPatternFactory(HREF, arena: arena).Create(p));
            Assert.IsFalse(new WolbaekPatternFactory(HREF, chainFanListener: listener).CanCreate(p), "전장 입력이 없으면 만들 수 없다");
            Assert.IsNull(new WolbaekPatternFactory(HREF, chainFanListener: listener).Create(p));
            Assert.IsFalse(new WolbaekPatternFactory(HREF, arena: arena, gridListener: new BossChainFanNoGridListener(), starStageListener: null).CanCreate(p),
                "다른 하드 알림을 연결해도 P03 알림이 아니면 만들지 않는다");
            Assert.IsTrue(new WolbaekPatternFactory(HREF, arena: arena, chainFanListener: listener).CanCreate(p));
            Assert.IsInstanceOf<BossChainFanPattern>(new WolbaekPatternFactory(HREF, arena: arena, chainFanListener: listener).Create(p));
        }

        // 격자 알림만 연결한 구성을 흉내 내는 빈 구현이다
        private sealed class BossChainFanNoGridListener : IBossGridBeamListener
        {
            public void OnGridStarted(int castInstanceId, in BossGridBeamLayout layout, double castStartServerTime,
                float firstStrikeSeconds, float strikeIntervalSeconds, float axisGapSeconds, int firstLineId)
            {
            }

            public void OnLineTelegraphed(int castInstanceId, int lineId, EBossGridBeamAxis axis, int lineIndex,
                double telegraphServerTime, double strikeServerTime)
            {
            }

            public void OnLineStruck(int castInstanceId, int lineId, EBossGridBeamAxis axis, int lineIndex,
                double scheduledServerTime, double processedServerTime, int hitCount)
            {
            }

            public void OnGridCancelled(int castInstanceId, int telegraphedCount, int struckCount, EBossInterruptReason reason)
            {
            }
        }

        [Test]
        public void Factory_OtherHardPhaseTwoPoliciesAreUnchanged()
        {
            BossFixedArena arena = new BossFixedArena(ArenaCenter, ARENA_RADIUS);
            PatternDefinition p01 = LoadDefinition(BossPatternCatalogSO.WOLBAEK_HARD_P2_P01_ID);
            PatternDefinition p02 = LoadDefinition(BossPatternCatalogSO.WOLBAEK_HARD_P2_P02_ID);

            Assert.IsTrue(new WolbaekPatternFactory(HREF, arena: arena).CanCreate(p01), "하드 P01은 전장 입력만 있으면 된다");
            Assert.IsFalse(new WolbaekPatternFactory(HREF, arena: arena).CanCreate(p02), "하드 P02는 격자 알림이 필요하다");
            Assert.IsTrue(new WolbaekPatternFactory(HREF, arena: arena, gridListener: new BossChainFanNoGridListener()).CanCreate(p02));
        }

        [Test]
        public void Validate_ChainFields_RejectMixedAndIncompleteData()
        {
            BossPatternCatalogSO catalog = LoadCatalog();

            Assert.IsTrue(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant("x_ok")), "기준 변형은 에셋과 같아 준비된다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant("x_full_circle", arc: 360f)), "구역이 360도면 피난 방향이 없다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant("x_linger_long", linger: 1.2f)), "잔류가 회복보다 길면 패턴이 먼저 끝난다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant("x_reveal_long", reveal: 2.5f)), "피난 방향 공개가 예고를 다 쓰면 바닥 예고가 없다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant("x_reveal_zero", reveal: 0f)));
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant("x_active_short", active: 2.5f)), "실행 시간이 마지막 폭발과 맞지 않는다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant("x_active_long", active: 3.5f)));
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant("x_cooldown_zero", cooldown: 0f)));
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant("x_hits_zero", maxHits: 0)));
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant("x_linger_zero", linger: 0f)));
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant("x_no_zones", zones: 0, interval: 0f, active: 0f)), "구역 수 없이 잔류 값만 있다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant("x_grid_mix", gridLines: 12)), "격자 값과 섞을 수 없다");
        }

        /****************************************
        *      Layout: Range, Zones, Safe Wedge
        ****************************************/

        [Test]
        public void Layout_RangeIsAtLeastTheMinimumAndReachesTheFarBoundary()
        {
            Assert.AreEqual(18f, MakeLayout(ArenaCenter).Range, TOLERANCE, "전장 중심의 보스는 반지름 18m까지 닿는다");
            Assert.AreEqual(16f, MakeLayout(ArenaCenter, 16f, 10f).Range, TOLERANCE, "먼 경계가 최소 사거리보다 가까우면 최소 사거리");
            Assert.AreEqual(28f, MakeLayout(ArenaCenter + new Vector3(10f, 0f, 0f)).Range, TOLERANCE, "중심에서 10m 벗어나면 10 + 18");
            Assert.AreEqual(38f, MakeLayout(ArenaCenter + new Vector3(0f, 0f, -20f)).Range, TOLERANCE);
            Assert.AreEqual(28f, MakeLayout(ArenaCenter + new Vector3(10f, 50f, 0f)).Range, TOLERANCE, "높이는 거리에 넣지 않는다");
            Assert.AreEqual(20f, MakeLayout(ArenaCenter, 20f).Range, TOLERANCE, "최소 사거리가 더 크면 그 값이다");
        }

        [Test]
        public void Layout_HasSevenZonesOf45Degrees_AndA45DegreeSafeWedge()
        {
            BossChainFanLayout layout = MakeLayout(ArenaCenter);

            Assert.AreEqual(ZONE_COUNT, layout.ZoneCount);
            Assert.AreEqual(ZONE_ARC, layout.ZoneArc, TOLERANCE);
            Assert.AreEqual(SAFE_ARC, layout.SafeArc, TOLERANCE);

            for (int k = 0; k < ZONE_COUNT; ++k)
            {
                Vector3 expected = Polar(Vector3.zero, ZoneCenter(k), 1f);
                Assert.AreEqual(0f, (layout.GetZoneDirection(k) - expected).magnitude, TOLERANCE, "zone " + k);
                Vector3 center = Polar(ArenaCenter, ZoneCenter(k), PROBE_RADIUS);

                for (int j = 0; j < ZONE_COUNT; ++j)
                {
                    Assert.AreEqual(j == k, layout.Contains(j, center), "zone " + k + " center in zone " + j);
                }

                Assert.IsTrue(layout.ContainsAny(ZONE_COUNT, center));
            }
        }

        [Test]
        public void Layout_SafeWedge_BoundariesAreDangerousAndTheInsideIsSafe()
        {
            BossChainFanLayout layout = MakeLayout(ArenaCenter);

            Assert.IsFalse(layout.ContainsAny(ZONE_COUNT, Polar(ArenaCenter, 0f, PROBE_RADIUS)), "피난 방향 가운데");
            Assert.IsFalse(layout.ContainsAny(ZONE_COUNT, Polar(ArenaCenter, 22f, PROBE_RADIUS)), "시계 쪽 경계 안");
            Assert.IsFalse(layout.ContainsAny(ZONE_COUNT, Polar(ArenaCenter, -22f, PROBE_RADIUS)), "반시계 쪽 경계 안");
            Assert.IsTrue(layout.ContainsAny(ZONE_COUNT, Polar(ArenaCenter, 22.5f, PROBE_RADIUS)), "경계 위는 위험, 구역 0");
            Assert.IsTrue(layout.ContainsAny(ZONE_COUNT, Polar(ArenaCenter, -22.5f, PROBE_RADIUS)), "경계 위는 위험, 구역 6");
            Assert.IsTrue(layout.ContainsAny(ZONE_COUNT, Polar(ArenaCenter, 23f, PROBE_RADIUS)));
            Assert.IsTrue(layout.ContainsAny(ZONE_COUNT, Polar(ArenaCenter, -23f, PROBE_RADIUS)));
            Assert.IsTrue(layout.Contains(0, Polar(ArenaCenter, 22.5f, PROBE_RADIUS)));
            Assert.IsTrue(layout.Contains(6, Polar(ArenaCenter, -22.5f, PROBE_RADIUS)));
        }

        [Test]
        public void Layout_SafeWedgeFollowsTheSafeDirection()
        {
            BossChainFanLayout layout = new BossChainFanLayout(ArenaCenter, Vector3.right, 16f, ZONE_COUNT, ZONE_ARC,
                ArenaCenter, ARENA_RADIUS);

            Assert.IsFalse(layout.ContainsAny(ZONE_COUNT, ArenaCenter + (Vector3.right * PROBE_RADIUS)), "새 피난 방향 가운데");
            Assert.IsTrue(layout.ContainsAny(ZONE_COUNT, ArenaCenter + (Vector3.forward * PROBE_RADIUS)), "이전 피난 방향은 이제 위험");
            Assert.IsTrue(layout.ContainsAny(ZONE_COUNT, ArenaCenter + (Vector3.left * PROBE_RADIUS)));
        }

        [Test]
        public void Layout_ArenaCutsTheJudgement_AndTheFarBoundaryIsReached()
        {
            Vector3 origin = ArenaCenter + new Vector3(10f, 0f, 0f);
            BossChainFanLayout layout = MakeLayout(origin);
            Vector3 outsideArena = ArenaCenter + new Vector3(0f, 0f, -18.5f);
            Vector3 farInside    = ArenaCenter + new Vector3(-17f, 0f, 0f);
            Vector3 onBoundary   = ArenaCenter + new Vector3(0f, 0f, -18f);
            Vector3 justOutside  = ArenaCenter + new Vector3(0f, 0f, -18.01f);

            Assert.AreEqual(28f, layout.Range, TOLERANCE);
            Assert.IsTrue(BossFanShape.IsInsideSector(origin, layout.GetZoneDirection(4), outsideArena, layout.Range, ZONE_ARC),
                "전장 밖 점은 부채꼴 모양으로는 안이다");
            Assert.IsFalse(layout.ContainsAny(ZONE_COUNT, outsideArena), "전장 원 밖은 자른다");
            Assert.IsFalse(layout.Contains(4, outsideArena));

            Assert.IsTrue(layout.Contains(5, farInside), "먼 쪽 경계 안쪽까지 닿는다");
            Assert.IsFalse(BossFanShape.IsInsideSector(origin, layout.GetZoneDirection(5), farInside, 16f, ZONE_ARC),
                "최소 사거리 16m만으로는 닿지 않는 거리다");

            Assert.IsTrue(layout.ContainsAny(5, onBoundary), "전장 경계 위는 안이다");
            Assert.IsFalse(layout.ContainsAny(5, justOutside));
        }

        [Test]
        public void Layout_ContainsAny_OnlyCountsTheExplodedZones()
        {
            BossChainFanLayout layout = MakeLayout(ArenaCenter);
            Vector3 inZoneThree = Polar(ArenaCenter, ZoneCenter(3), PROBE_RADIUS);

            Assert.IsFalse(layout.ContainsAny(0, inZoneThree));
            Assert.IsFalse(layout.ContainsAny(3, inZoneThree), "구역 0~2만 폭발했다");
            Assert.IsTrue(layout.ContainsAny(4, inZoneThree), "구역 3이 폭발했다");
            Assert.IsTrue(layout.ContainsAny(ZONE_COUNT, inZoneThree));
            Assert.IsTrue(layout.ContainsAny(99, inZoneThree), "구역 수를 넘겨도 구역 수까지만 본다");
        }

        /****************************************
        *      Start: Fixed Layout And Announcement
        ****************************************/

        [Test]
        public void Start_AnnouncesLayoutAndTheSchedule_AndDrawsTheSafeDirectionOnce()
        {
            Run r = new Run();
            r.Start(5.0);

            Assert.IsFalse(r.Pattern.IsStartRejected);
            Assert.AreEqual(1, r.Listener.StartedLayouts.Count);
            Assert.AreEqual(r.CastId, r.Listener.StartedCasts[0]);
            Assert.AreEqual(5.0, r.Listener.StartedAt[0], TIME_TOL, "시전 시작 서버 시각");
            Assert.AreEqual(1f, r.Listener.Reveals[0], TOLERANCE, "t0~1 여우불");
            Assert.AreEqual(5f, r.Listener.FirstBlasts[0], TOLERANCE, "t1~5 바닥 예고, 첫 폭발 t=5");
            Assert.AreEqual(0.5f, r.Listener.Intervals[0], TOLERANCE);
            Assert.AreEqual(0.8f, r.Listener.Lingers[0], TOLERANCE);

            BossChainFanLayout layout = r.Listener.StartedLayouts[0];
            Assert.AreEqual(ArenaCenter, layout.Origin, "시전 시작 서버 위치");
            Assert.AreEqual(0f, (layout.SafeDirection - Vector3.forward).magnitude, TOLERANCE, "난수 0 -> +Z");
            Assert.AreEqual(ZONE_COUNT, layout.ZoneCount);
            Assert.AreEqual(ZONE_ARC, layout.ZoneArc, TOLERANCE);
            Assert.AreEqual(SAFE_ARC, layout.SafeArc, TOLERANCE);
            Assert.AreEqual(18f, layout.Range, TOLERANCE);
            Assert.AreEqual(ArenaCenter, layout.ArenaCenter);
            Assert.AreEqual(ARENA_RADIUS, layout.ArenaRadius, TOLERANCE);
            Assert.AreEqual(1, r.Random.FloatCalls, "피난 방향은 한 번만 추첨한다");

            r.TickRange(5.0, 15.0);
            Assert.AreEqual(1, r.Random.FloatCalls, "시전 중 다시 추첨하지 않는다");
            Assert.AreEqual(1, r.Listener.StartedLayouts.Count);
        }

        [Test]
        public void Start_SafeDirectionFollowsTheRoll()
        {
            Run r = new Run();
            r.Random.Roll = 0.25f;
            r.Start();

            BossChainFanLayout layout = r.Listener.StartedLayouts[0];
            Assert.AreEqual(0f, (layout.SafeDirection - Vector3.right).magnitude, TOLERANCE, "난수 0.25 -> 90도, +X");
            r.AddPolar(10, 90f);
            r.AddPolar(11, 270f);
            r.TickRange(0.0, 10.5);

            Assert.AreEqual(0, r.Hits(10), "그려진 피난 방향 가운데는 끝까지 안전하다");
            Assert.Greater(r.Hits(11), 0, "반대쪽은 위험하다");
        }

        [Test]
        public void Start_RejectsWithNothingAnnounced_WhenRandomOrArenaIsMissing()
        {
            Run noRandom = new Run();
            noRandom.UseRandom = false;
            noRandom.AddPolar(10, ZoneCenter(0));
            noRandom.Start();
            noRandom.TickRange(0.0, 11.5);

            Assert.IsTrue(noRandom.Pattern.IsStartRejected, "난수가 없으면 피난 방향을 정할 수 없다");
            Assert.AreEqual(0, noRandom.Listener.StartedLayouts.Count);
            Assert.AreEqual(0, noRandom.Listener.Blasts.Count);
            Assert.AreEqual(0, noRandom.Resolver.Requests.Count);

            Run noArena = new Run();
            noArena.Arena.Set(ArenaCenter, 0f);
            noArena.AddPolar(10, ZoneCenter(0));
            noArena.Start();
            noArena.TickRange(0.0, 11.5);

            Assert.IsTrue(noArena.Pattern.IsStartRejected);
            Assert.AreEqual(0, noArena.Listener.StartedLayouts.Count);
            Assert.AreEqual(0, noArena.Resolver.Requests.Count);
            Assert.AreEqual(0, noArena.Listener.CancelReasons.Count, "시작하지 못한 시전은 취소도 알리지 않는다");
        }

        [Test]
        public void Start_NeedsNoSelectedTarget()
        {
            Run r = new Run();
            r.Start();

            Assert.IsFalse(r.Pattern.IsStartRejected, "대상 조회가 실패해도 시작한다");
            Assert.AreEqual(1, r.Listener.StartedLayouts.Count);
        }

        [Test]
        public void Layout_StaysFixed_WhenTheBossMovesAfterTheStart()
        {
            Run r = new Run();
            r.AddPolar(10, ZoneCenter(0));
            r.Start();
            r.Self.Position = ArenaCenter + new Vector3(8f, 0f, 8f);
            r.Arena.Set(ArenaCenter + new Vector3(100f, 0f, 0f), 5f);

            r.TickAt(4.5);
            r.TickAt(5.0);

            Assert.AreEqual(ArenaCenter, r.Pattern.Layout.Origin, "중심은 시전 시작 위치에 고정된다");
            Assert.AreEqual(1, r.Hits(10), "처음 중심 기준 구역 0 안의 대상이 맞는다");
        }

        /****************************************
        *      Strike And Danger Boundaries
        ****************************************/

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void Boundaries_EachZoneBlastsExactlyAtItsScheduledTime(int zone)
        {
            Run r = new Run();
            r.AddPolar(10, ZoneCenter(zone));
            r.Start();

            double blast = Strike(zone);
            r.TickRange(0.0, blast - 0.1, 0.1);
            r.TickAt(blast - 0.01);
            Assert.AreEqual(0, r.Hits(10), "폭발 직전에는 맞지 않는다");

            r.TickAt(blast);
            Assert.AreEqual(1, r.Hits(10), "예정 시각에 정확히 한 번");
            Assert.AreEqual(zone + 1, r.Pattern.StrikeCount);
            Assert.AreEqual(zone, r.Listener.Blasts[zone].ZoneIndex);
            Assert.AreEqual(blast, r.Listener.Blasts[zone].Scheduled, TIME_TOL);
            Assert.AreEqual(1, r.Listener.Blasts[zone].HitCount);
        }

        [Test]
        public void Boundaries_StrikeTimesAreMeasuredFromTheCastStart()
        {
            Run r = new Run();
            r.AddPolar(10, ZoneCenter(2));
            r.Start(100.0);

            r.TickRange(100.0, 105.99, 0.1);
            r.TickAt(105.99);
            Assert.AreEqual(0, r.Hits(10));
            r.TickAt(106.0);
            Assert.AreEqual(1, r.Hits(10), "시전 시작 100 + 5.0 + 2 x 0.5");
            Assert.AreEqual(106.0, r.Pattern.GetStrikeTime(2), TIME_TOL);
            Assert.AreEqual(108.8, r.Pattern.DangerEndTime, TIME_TOL);
        }

        [Test]
        public void Boundaries_UnexplodedZonesAreSafe_AndExplodedOnesStayDangerous()
        {
            Run r = new Run();
            r.AddPolar(10, ZoneCenter(6));
            r.Start();

            r.TickRange(0.0, 7.9, 0.1);
            Assert.AreEqual(0, r.Hits(10), "마지막 구역은 5.5 전까지 안전하다");
            r.TickAt(8.0);
            Assert.AreEqual(1, r.Hits(10));

            Run late = new Run();
            FakeCombatTarget lateTarget = late.AddPolar(10, 0f);
            late.Start();
            late.TickRange(0.0, 6.6, 0.1);
            lateTarget.Position =Polar(ArenaCenter, ZoneCenter(0), PROBE_RADIUS);
            late.TickAt(6.7);
            Assert.AreEqual(1, late.Hits(10), "구역 0은 5.0에 폭발했고 6.7에 들어온 대상도 맞는다");
        }

        [TestCase(DANGER_END - 0.05, 1)]
        [TestCase(DANGER_END, 1)]
        [TestCase(DANGER_END + 0.01, 0)]
        [TestCase(DANGER_END + 0.1, 0)]
        public void Danger_EndsExactlyAtTheLastStrikePlusPointEightSeconds(double enterAt, int expectedHits)
        {
            Run r = new Run();
            FakeCombatTarget target = r.AddPolar(10, 0f);
            r.Start();
            r.TickRange(0.0, 8.7, 0.1);
            Assert.AreEqual(0, r.Hits(10), "피난 방향에서는 맞지 않는다");

            target.Position = Polar(ArenaCenter, ZoneCenter(0), PROBE_RADIUS);
            r.TickAt(enterAt);

            Assert.AreEqual(expectedHits, r.Hits(10));
            Assert.AreEqual(expectedHits, r.Pattern.GetHitCount(10));
        }

        [Test]
        public void Danger_EndIsAnnouncedOnceAfterTheWindow_AndNothingHitsAfterIt()
        {
            Run r = new Run();
            FakeCombatTarget target = r.AddPolar(10, 0f);
            r.Start();
            r.TickRange(0.0, 8.8, 0.1);
            Assert.AreEqual(0, r.Listener.DangerEnds.Count, "8.8 그 순간까지는 위험이 끝나지 않았다");
            Assert.IsFalse(r.Pattern.IsDangerEnded);

            r.TickAt(8.81);
            Assert.AreEqual(1, r.Listener.DangerEnds.Count);
            Assert.AreEqual(DANGER_END, r.Listener.DangerEnds[0], TIME_TOL, "예정 시각을 알린다");
            Assert.IsTrue(r.Pattern.IsDangerEnded);

            target.Position = Polar(ArenaCenter, ZoneCenter(3), PROBE_RADIUS);
            r.TickRange(8.81, 9.95, 0.01);
            Assert.AreEqual(0, r.Hits(10));
            Assert.AreEqual(1, r.Listener.DangerEnds.Count, "한 번만 알린다");
        }

        [Test]
        public void Fsm_NextSelectionOpensAfterRecovery_AndDangerEndsBeforeIt()
        {
            Run r = new Run();
            r.Start(4.0);

            r.TickAt(4.0 + NEXT_SELECTION - 0.1);
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State);
            Assert.AreEqual(1, r.Listener.DangerEnds.Count, "위험은 다음 선택보다 먼저 끝난다");

            r.TickAt(4.0 + NEXT_SELECTION - 0.01);
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State);
            r.TickAt(4.0 + NEXT_SELECTION);
            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State);
            Assert.IsTrue(r.Pattern.IsFinished);
            Assert.AreEqual(ZONE_COUNT, r.Listener.Blasts.Count);
        }

        [Test]
        public void Fsm_StatesFollowTheDesignedTimeline()
        {
            Run r = new Run();
            r.Start();

            r.TickAt(1.0);
            Assert.AreEqual(EBossAttackState.Telegraph, r.Fsm.State, "여우불 구간");
            r.TickAt(4.99);
            Assert.AreEqual(EBossAttackState.Telegraph, r.Fsm.State, "바닥 예고 구간");
            r.TickAt(5.0);
            Assert.AreEqual(EBossAttackState.Execute, r.Fsm.State);
            r.TickAt(7.99);
            Assert.AreEqual(EBossAttackState.Execute, r.Fsm.State);
            r.TickAt(8.0);
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State);
        }

        /****************************************
        *      Hit Cooldown And Cap
        ****************************************/

        [Test]
        public void Hits_SecondHitWaitsForTheCooldown_AndTheCapStopsAtTwo()
        {
            Run r = new Run();
            r.AddPolar(10, ZoneCenter(0));
            r.Start();

            r.TickAt(5.0);
            Assert.AreEqual(1, r.Hits(10));
            r.TickAt(5.4);
            r.TickAt(5.49);
            Assert.AreEqual(1, r.Hits(10), "0.5초가 지나기 전에는 다시 맞지 않는다");
            r.TickAt(5.5);
            Assert.AreEqual(2, r.Hits(10), "정확히 0.5초 뒤에는 다시 맞는다");

            r.TickRange(5.5, 8.8, 0.1);
            Assert.AreEqual(2, r.Hits(10), "한 시전 최대 2회");
            Assert.AreEqual(2, r.Pattern.GetHitCount(10));
        }

        [Test]
        public void Hits_EachRequestUsesTheConfiguredValues()
        {
            Run r = new Run();
            r.AddPolar(10, ZoneCenter(0));
            r.Start();
            r.TickRange(5.0, 5.5, 0.5);

            Assert.AreEqual(2, r.Resolver.Requests.Count);
            for (int i = 0; i < r.Resolver.Requests.Count; ++i)
            {
                DamageRequest request = r.Resolver.Requests[i];
                Assert.AreEqual(RAW_DAMAGE, request.RawDamage, TOLERANCE, "0.18 x Href");
                Assert.AreEqual(EDamageFlags.None, request.Flags, "방어와 반격이 가능하다");
                Assert.AreEqual(r.CastId, request.CastInstanceId);
                Assert.AreEqual(1, request.AttackerId);
                Assert.AreEqual(ArenaCenter, request.SourcePosition, "피해 위치는 고정한 중심");
                Assert.AreEqual(HARD_ID, request.SourceDefinitionId);
            }

            Assert.AreNotEqual(r.Resolver.Requests[0].ActionInstanceId, r.Resolver.Requests[1].ActionInstanceId,
                "재피격은 새 동작 번호라 중복으로 거부되지 않는다");
        }

        [Test]
        public void Hits_CooldownFollowsTheTargetAcrossZones()
        {
            Run r = new Run();
            FakeCombatTarget target = r.AddPolar(10, ZoneCenter(0));
            r.Start();

            r.TickAt(5.0);
            target.Position = Polar(ArenaCenter, ZoneCenter(1), PROBE_RADIUS);
            r.TickAt(5.1);
            r.TickAt(5.49);
            Assert.AreEqual(1, r.Hits(10), "구역이 달라도 직전 피격에서 0.5초가 지나야 한다");
            r.TickAt(5.5);
            Assert.AreEqual(2, r.Hits(10), "구역 1 폭발 순간에 두 번째");
            Assert.AreEqual(1, r.Listener.Blasts[1].HitCount);
        }

        [Test]
        public void Hits_StationaryTargetsInDifferentZonesAreCountedSeparately()
        {
            Run r = new Run();
            r.AddPolar(10, ZoneCenter(0));
            r.AddPolar(11, ZoneCenter(1));
            r.Start();

            r.TickRange(0.0, 8.8, 0.1);

            Assert.AreEqual(2, r.Hits(10), "구역 0: 2.5와 3.0");
            Assert.AreEqual(2, r.Hits(11), "구역 1: 3.0과 3.5");
            Assert.AreEqual(4, r.Resolver.Requests.Count);
        }

        [Test]
        public void Cap_GuardedHitsCountTowardsTheLimitAndTheCooldown()
        {
            Run r = new Run();
            r.Resolver.Outcomes[10] = EDamageOutcome.Guarded;
            r.AddPolar(10, ZoneCenter(0));
            r.Start();

            r.TickAt(5.0);
            r.TickAt(5.49);
            Assert.AreEqual(1, r.Hits(10), "막힌 타격도 간격을 시작한다");
            r.TickRange(5.5, 8.8, 0.1);

            Assert.AreEqual(2, r.Hits(10), "막은 유효 타격도 2회 상한을 소모한다");
            Assert.AreEqual(2, r.Pattern.GetHitCount(10));
            Assert.AreEqual(1, r.Listener.Blasts[0].HitCount, "막힌 타격도 구역의 확정 피격 수에 센다");
        }

        [Test]
        public void Cap_IgnoredRequestsDoNotConsumeTheLimit()
        {
            Run r = new Run();
            r.Resolver.Outcomes[10] = EDamageOutcome.Invulnerable;
            r.AddPolar(10, ZoneCenter(0));
            r.Start();

            r.TickRange(5.0, 5.7, 0.1);
            Assert.AreEqual(0, r.Pattern.GetHitCount(10), "무적으로 들어가지 않은 요청은 상한을 소모하지 않는다");
            Assert.Greater(r.Hits(10), 0, "요청은 나갔다");

            r.Resolver.Outcomes[10] = EDamageOutcome.Applied;
            int before = r.Hits(10);
            r.TickAt(5.8);
            Assert.AreEqual(before + 1, r.Hits(10));
            Assert.AreEqual(1, r.Pattern.GetHitCount(10));
            r.TickAt(6.29);
            Assert.AreEqual(1, r.Pattern.GetHitCount(10));
            r.TickAt(6.3);
            Assert.AreEqual(2, r.Pattern.GetHitCount(10));
        }

        [Test]
        public void Targets_SummonsAreHit_DeadAndEnemiesAreNot_AndOutsideRangeIsNot()
        {
            Run r = new Run();
            r.Add(10, Polar(ArenaCenter, ZoneCenter(0), PROBE_RADIUS), true);
            FakeCombatTarget dead = r.AddPolar(11, ZoneCenter(0));
            dead.LifeState = EParticipantLifeState.Dead;
            FakeCombatTarget enemy = r.AddPolar(12, ZoneCenter(0));
            enemy.Faction = ECombatFaction.Enemy;
            r.AddPolar(13, ZoneCenter(0), 25f);
            r.Start();

            r.TickAt(5.0);

            Assert.AreEqual(1, r.Hits(10), "소환물도 범위 피해를 받는다");
            Assert.AreEqual(0, r.Hits(11));
            Assert.AreEqual(0, r.Hits(12));
            Assert.AreEqual(0, r.Hits(13), "전장 밖은 판정하지 않는다");
        }

        [Test]
        public void Targets_UseTheirCurrentPositionAtEachJudgement()
        {
            Run r = new Run();
            FakeCombatTarget target = r.AddPolar(10, ZoneCenter(0));
            r.Start();

            target.Position = Polar(ArenaCenter, 0f, PROBE_RADIUS);
            r.TickAt(5.0);
            Assert.AreEqual(0, r.Hits(10), "폭발 순간 피난 방향으로 나가 있으면 맞지 않는다");

            target.Position = Polar(ArenaCenter, ZoneCenter(3), PROBE_RADIUS);
            r.TickAt(6.4);
            Assert.AreEqual(0, r.Hits(10), "구역 3은 4.0에 폭발한다");
            r.TickAt(6.5);
            Assert.AreEqual(1, r.Hits(10));
        }

        [Test]
        public void Arena_FarBoundaryIsReachedAndTheOutsideIsCut()
        {
            Run r = new Run();
            r.Self.Position = ArenaCenter + new Vector3(10f, 0f, 0f);
            r.Add(10, ArenaCenter + new Vector3(0f, 0f, -18.5f));
            r.Add(11, ArenaCenter + new Vector3(-17f, 0f, 0f));
            r.Start();

            Assert.AreEqual(28f, r.Pattern.Layout.Range, TOLERANCE);
            r.TickRange(0.0, 7.4, 0.1);
            Assert.AreEqual(0, r.Hits(11), "구역 5는 5.0에 폭발한다");
            r.TickAt(7.5);
            Assert.AreEqual(1, r.Hits(11), "먼 쪽 전장 경계 안쪽까지 닿는다");
            r.TickRange(7.5, 8.8, 0.1);
            Assert.AreEqual(0, r.Hits(10), "전장 밖은 구역 안이어도 맞지 않는다");
        }

        /****************************************
        *      Big Tick Jumps
        ****************************************/

        [Test]
        public void BigTick_FromStartToAfterTheEnd_ProcessesEveryZoneOnceInOrder()
        {
            Run r = new Run();
            r.AddPolar(10, ZoneCenter(0));
            r.AddPolar(11, ZoneCenter(6));
            r.AddPolar(12, 0f);
            r.Start();

            r.TickAt(0.0);
            r.TickAt(13.5);

            Assert.AreEqual(ZONE_COUNT, r.Listener.Blasts.Count, "7개 구역을 모두 한 번씩");
            for (int k = 0; k < ZONE_COUNT; ++k)
            {
                Assert.AreEqual(k, r.Listener.Blasts[k].ZoneIndex, "순서대로");
                Assert.AreEqual(Strike(k), r.Listener.Blasts[k].Scheduled, TIME_TOL, "예정 시각을 알린다");
                Assert.AreEqual(13.5, r.Listener.Blasts[k].Processed, TIME_TOL, "처리 시각은 틱 시각");
            }

            Assert.AreEqual(2, r.Hits(10), "구역 0의 대상: 5.0과 5.5, 이후 상한");
            Assert.AreEqual(1, r.Hits(11), "구역 6의 대상: 8.0 한 번, 종료 뒤 틱에는 판정하지 않는다");
            Assert.AreEqual(0, r.Hits(12));
            Assert.AreEqual(1, r.Listener.DangerEnds.Count);
            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State, "같은 틱 안에서 끝까지 진행한다");
        }

        [Test]
        public void BigTick_AcrossTwoZones_StrikesBothOnceAndDoesNotRepeatThem()
        {
            Run r = new Run();
            r.AddPolar(10, ZoneCenter(0));
            r.Start();

            r.TickAt(4.9);
            r.TickAt(5.7);
            Assert.AreEqual(2, r.Listener.Blasts.Count);
            Assert.AreEqual(2, r.Hits(10), "2.5와 3.0의 예정 판정을 모두 처리, 간격 0.5는 예정 시각 기준");

            r.TickAt(5.7);
            r.TickAt(5.8);
            r.TickAt(5.9);
            Assert.AreEqual(2, r.Listener.Blasts.Count, "처리한 구역은 다시 처리하지 않는다");
            Assert.AreEqual(2, r.Hits(10));
        }

        [Test]
        public void BigTick_UsesCurrentTargetPositions_NotARestoredPastPosition()
        {
            Run r = new Run();
            FakeCombatTarget target = r.AddPolar(10, 0f);
            r.Start();
            r.TickAt(4.9);

            target.Position = Polar(ArenaCenter, ZoneCenter(1), PROBE_RADIUS);
            r.TickAt(5.6);

            Assert.AreEqual(0, r.Listener.Blasts[0].HitCount, "구역 0 판정에는 구역 1의 대상이 없다");
            Assert.AreEqual(1, r.Listener.Blasts[1].HitCount, "구역 1은 처리 시점의 현재 위치로 판정한다");
            Assert.AreEqual(1, r.Hits(10));
        }

        [Test]
        public void BigTick_ThatSkipsTheWholeWindow_NeverHitsAfterTheDangerEnded()
        {
            Run r = new Run();
            FakeCombatTarget target = r.AddPolar(10, 0f);
            r.Start();
            r.TickAt(4.5);

            target.Position = Polar(ArenaCenter, ZoneCenter(2), PROBE_RADIUS);
            r.TickAt(9.95);

            Assert.AreEqual(ZONE_COUNT, r.Listener.Blasts.Count);
            Assert.AreEqual(1, r.Listener.DangerEnds.Count);
            r.TickAt(10.0);
            r.TickAt(11.5);
            Assert.AreEqual(ZONE_COUNT, r.Listener.Blasts.Count);
            Assert.AreEqual(1, r.Listener.DangerEnds.Count);
        }

        [Test]
        public void Tick_LowerBoundaryTimesFromTheFsmAreNotJudgedAgain()
        {
            Run r = new Run();
            r.AddPolar(10, ZoneCenter(6));
            r.Start();

            r.TickAt(4.5);
            r.TickAt(7.5);
            int blasts = r.Listener.Blasts.Count;
            r.TickAt(8.4);

            Assert.AreEqual(ZONE_COUNT, r.Listener.Blasts.Count, "5.0까지 구역 0~5, 5.9에 구역 6");
            Assert.AreEqual(blasts + 1, r.Listener.Blasts.Count);
            Assert.AreEqual(1, r.Hits(10));
        }

        /****************************************
        *      Cancel And Cleanup
        ****************************************/

        [TestCase(EBossInterruptReason.Death)]
        [TestCase(EBossInterruptReason.CombatEnd)]
        [TestCase(EBossInterruptReason.PhaseTransition)]
        [TestCase(EBossInterruptReason.DirectControl)]
        public void Cancel_AfterTheFirstZone_ClearsTheRestAndTheLingeringDanger(EBossInterruptReason reason)
        {
            Run r = new Run();
            r.AddPolar(10, ZoneCenter(0));
            r.AddPolar(11, ZoneCenter(5));
            r.Start();
            r.TickRange(0.0, 5.0, 0.25);
            int before = r.Resolver.Requests.Count;
            Assert.AreEqual(1, before);

            r.Fsm.Cancel(reason);

            Assert.AreEqual(1, r.Listener.CancelReasons.Count);
            Assert.AreEqual(reason, r.Listener.CancelReasons[0]);
            Assert.AreEqual(1, r.Listener.CancelBlasted[0], "구역 0만 폭발했다");
            Assert.IsFalse(r.Listener.CancelEnded[0]);

            r.TickRange(5.0, 11.5, 0.1);
            r.Pattern.Tick(EBossAttackState.Execute, 6.5);
            r.Pattern.EnterState(EBossAttackState.Recover, 5.5);
            Assert.AreEqual(before, r.Resolver.Requests.Count, "남은 구역과 잔류 피해는 나가지 않는다");
            Assert.AreEqual(1, r.Listener.Blasts.Count);
            Assert.AreEqual(0, r.Listener.DangerEnds.Count);
            Assert.IsTrue(r.Pattern.IsCancelled);
            Assert.AreEqual(reason, r.Pattern.CancelReason);
        }

        [Test]
        public void Cancel_DuringTheFoxfirePhase_AnnouncesOnceAndNeverStrikes()
        {
            Run r = new Run();
            r.AddPolar(10, ZoneCenter(0));
            r.Start();
            r.TickAt(0.5);

            r.Fsm.Cancel(EBossInterruptReason.PhaseTransition);
            r.TickRange(0.5, 11.5, 0.1);

            Assert.AreEqual(1, r.Listener.CancelReasons.Count);
            Assert.AreEqual(0, r.Listener.CancelBlasted[0]);
            Assert.AreEqual(0, r.Resolver.Requests.Count);
            Assert.AreEqual(0, r.Listener.Blasts.Count);
        }

        [Test]
        public void Cancel_AfterTheDangerEnded_ReportsTheEndedDanger()
        {
            Run r = new Run();
            r.AddPolar(10, 0f);
            r.Start();
            r.TickRange(0.0, 9.9, 0.1);
            Assert.AreEqual(1, r.Listener.DangerEnds.Count);

            r.Fsm.Cancel(EBossInterruptReason.CombatEnd);

            Assert.AreEqual(ZONE_COUNT, r.Listener.CancelBlasted[0]);
            Assert.IsTrue(r.Listener.CancelEnded[0]);
        }

        [Test]
        public void Cancel_ClosedGateStopsDamageEvenIfTheFsmIsNotCancelled()
        {
            Run r = new Run();
            r.AddPolar(10, ZoneCenter(0));
            r.Start();
            r.TickAt(5.0);
            int before = r.Hits(10);

            r.Gate.CloseForGood();
            r.TickRange(5.0, 9.9, 0.1);

            Assert.AreEqual(before, r.Hits(10));
            Assert.AreEqual(1, r.Pattern.GetHitCount(10), "게이트가 닫힌 뒤 나가지 못한 요청은 맞은 것으로 세지 않는다");
        }

        [Test]
        public void Finish_AfterTheRecovery_LeavesNothingBehind()
        {
            Run r = new Run();
            r.AddPolar(10, ZoneCenter(0));
            r.Start();
            r.TickRange(0.0, 9.9, 0.1);
            r.TickAt(NEXT_SELECTION);
            int requests = r.Resolver.Requests.Count;

            r.Pattern.Tick(EBossAttackState.Recover, 8.7);
            r.Pattern.Tick(EBossAttackState.Recover, 10.5);

            Assert.IsTrue(r.Pattern.IsFinished);
            Assert.AreEqual(requests, r.Resolver.Requests.Count);
            Assert.AreEqual(0, r.Listener.CancelReasons.Count);
        }

        /****************************************
        *      Easy P03 Regression
        ****************************************/

        [Test]
        public void EasyP03_StillSweepsOnceWithNoLingerAndHitsOncePerCast()
        {
            PatternDefinition easy = LoadDefinition(EASY_ID);
            FakeCombatTarget self = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = ArenaCenter };
            ChainQuery query = new ChainQuery();
            ScriptedResolver resolver = new ScriptedResolver();
            BossDamageGate gate = new BossDamageGate(resolver);
            gate.Open();
            FakeCombatTarget aim = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player,
                Position = ArenaCenter + (Vector3.forward * 3f) };
            query.Targets.Add(aim);

            IBossPattern pattern = new WolbaekPatternFactory(HREF).Create(easy);
            Assert.IsInstanceOf<BossSweepBlastPattern>(pattern);
            BossAttackFsm fsm = new BossAttackFsm();
            BossPatternContext context = new BossPatternContext(easy, gate.IssueInstanceId(), aim.CombatId, self, query, gate,
                new FakeBossRandom());
            Assert.IsTrue(fsm.Start(easy, pattern, in context, 0.0));

            // 난수 0이면 왼쪽(반시계)이라 구역 0은 정면에서 -45도~0도다
            // 예고 시간은 float 2.4f라 double로 바꾸면 2.4보다 조금 크다. 리터럴 2.4는 FSM 경계 이전이므로 에셋 값을 그대로 경계로 쓴다
            double castBoundary = (double)easy.TelegraphSeconds;

            aim.Position = Polar(ArenaCenter, -10f, 3f);
            fsm.Tick(castBoundary - 0.01);
            Assert.AreEqual(0, resolver.Requests.Count, "예고 경계 직전에는 구역이 없다");
            fsm.Tick(castBoundary);
            Assert.AreEqual(1, resolver.Requests.Count, "예고 경계 정각에 첫 구역");

            for (double t = castBoundary; t <= 7.0; t += 0.1)
            {
                fsm.Tick(t);
            }

            Assert.AreEqual(1, resolver.Requests.Count, "이지는 시전 전체에서 대상당 한 번이고 잔류가 없다");
            Assert.AreEqual(EBossAttackState.Ready, fsm.State);
        }

        /****************************************
        *      Brain: Hard Catalog Selection
        ****************************************/

        // 두뇌 통합은 운영 카탈로그를 그대로 사용하며 최신 입력의 예고/실행/회복 합으로 관찰할 경계를 정한다.
        private static double TunedFirstStrike => LoadDefinition(HARD_ID).TelegraphSeconds;

        private static double TunedNextSelection
        {
            get
            {
                PatternDefinition definition = LoadDefinition(HARD_ID);
                return (double)definition.TelegraphSeconds + definition.ActiveSeconds + definition.RecoverySeconds;
            }
        }

        private sealed class BrainRig
        {
            public FakeCombatTarget   Self     = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = ArenaCenter };
            public ChainQuery         Query    = new ChainQuery();
            public ScriptedResolver   Damage   = new ScriptedResolver();
            public FakeBossVitals     Vitals   = new FakeBossVitals();
            public FakeBossRandom     Random   = new FakeBossRandom();
            public RecordingListener  Listener = new RecordingListener();
            public BossFixedArena     Arena    = new BossFixedArena(ArenaCenter, ARENA_RADIUS);
            public BossBrain          Brain;

            public BrainRig(IBossEffectLifetime effects = null)
            {
                Query.Targets.Add(new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player,
                    Position = Polar(ArenaCenter, 0f, 3f) });

                WolbaekPatternFactory factory = new WolbaekPatternFactory(HREF, arena: Arena, chainFanListener: Listener);
                Brain = new BossBrain(WOLBAEK_ID, new BossBrainDependencies
                {
                    Catalog  = new BossPatternCatalogSource(LoadCatalog(), EBossDifficulty.Hard),
                    Self     = Self,
                    Vitals   = Vitals,
                    Targets  = Query,
                    Damage   = Damage,
                    Patterns = factory,
                    Effects  = effects ?? new BossEffectComposite(),
                    Control  = new FakeControlSource(),
                    Random   = Random
                });
            }

            public void TransitionAndHandoff()
            {
                Assert.IsTrue(Brain.Begin(0.0));
                Vitals.HealthRate = 0.5f;
                Brain.Tick(0.25);
                Assert.IsTrue(Brain.CompleteHandoff());
                Random.Roll = 0.999f; // 가중치 누적에서 마지막 후보인 P03을 고른다
            }

            // P03이 시작된 틱의 서버 시각, 시작하지 않았으면 음수다
            public double RunUntilStarted(double from = 0.5, double to = 5.0)
            {
                for (double t = from; t < to; t += 0.05)
                {
                    Brain.Tick(t);
                    if (Brain.AttackState != EBossAttackState.Ready)
                    {
                        return t;
                    }
                }

                return -1.0;
            }
        }

        [Test]
        public void Brain_HardPhaseTwo_RunsP03UntilTunedRecoveryEndsWithoutPickingAnother()
        {
            BrainRig r = new BrainRig();
            r.TransitionAndHandoff();

            double started = r.RunUntilStarted();
            Assert.GreaterOrEqual(started, 0.0);
            Assert.AreEqual(HARD_ID, r.Brain.ActivePatternId);

            for (double t = started + 0.05; t < (started + TunedNextSelection - 0.06); t += 0.05)
            {
                r.Brain.Tick(t);
                Assert.AreEqual(HARD_ID, r.Brain.ActivePatternId, "t=" + t);
            }

            LogAssert.NoUnexpectedReceived();
            Assert.AreEqual(1, r.Listener.StartedLayouts.Count);
            Assert.AreEqual(ZONE_COUNT, r.Listener.Blasts.Count);
            Assert.AreEqual(1, r.Listener.DangerEnds.Count, "다음 선택 전에 위험이 끝난다");
            Assert.AreEqual(0, r.Listener.CancelReasons.Count);
        }

        [Test]
        public void Brain_ActiveAreasAtTheirLimit_DoNotBlockP03OrRedrawTheSafeDirection()
        {
            FakeEffectLifetime effects = new FakeEffectLifetime();
            effects.Areas["wolbaek_hard_p05"] = 99;
            effects.Areas["wolbaek_p05"]      = 99;
            effects.Areas["wolbaek_ph2_p04"]  = 99;
            effects.Areas[HARD_ID]            = 99;
            BrainRig r = new BrainRig(effects);
            r.TransitionAndHandoff();

            double started = r.RunUntilStarted();

            Assert.GreaterOrEqual(started, 0.0);
            Assert.AreEqual(HARD_ID, r.Brain.ActivePatternId, "장판이 가득 차 있어도 P03 선택을 막지 않는다");
            Vector3 drawn = r.Listener.StartedLayouts[0].SafeDirection;
            for (double t = started + 0.05; t < (started + 3.0); t += 0.05)
            {
                r.Brain.Tick(t);
            }

            Assert.AreEqual(1, r.Listener.StartedLayouts.Count);
            Assert.AreEqual(0f, (r.Listener.StartedLayouts[0].SafeDirection - drawn).magnitude, TOLERANCE, "피난 방향을 다시 추첨하지 않는다");
        }

        [Test]
        public void Brain_CombatEndAfterTheFirstZone_StopsTheRemainingZonesAndTheLingeringDanger()
        {
            BrainRig r = new BrainRig();
            r.TransitionAndHandoff();
            double started = r.RunUntilStarted();
            Assert.GreaterOrEqual(started, 0.0);

            for (double t = started + 0.05; t <= (started + TunedFirstStrike + 0.1); t += 0.05)
            {
                r.Brain.Tick(t);
            }

            Assert.AreEqual(1, r.Listener.Blasts.Count);
            r.Brain.RequestCombatEnd();
            r.Brain.Tick(started + TunedFirstStrike + 0.5);
            int requests = r.Damage.Requests.Count;
            for (double t = started + TunedFirstStrike + 0.55; t < (started + TunedNextSelection + 1.5); t += 0.05)
            {
                r.Brain.Tick(t);
            }

            Assert.AreEqual(1, r.Listener.Blasts.Count, "전투 종료 뒤 남은 구역은 폭발하지 않는다");
            Assert.AreEqual(requests, r.Damage.Requests.Count, "잔류 피해도 나가지 않는다");
            Assert.AreEqual(1, r.Listener.CancelReasons.Count);
            Assert.AreEqual(EBossInterruptReason.CombatEnd, r.Listener.CancelReasons[0]);
        }

        [Test]
        public void Brain_DeathWhileTheDangerLingers_StopsFurtherDamage()
        {
            BrainRig r = new BrainRig();
            r.TransitionAndHandoff();
            double started = r.RunUntilStarted();
            Assert.GreaterOrEqual(started, 0.0);

            for (double t = started + 0.05; t <= (started + TunedFirstStrike + 3.1); t += 0.05)
            {
                r.Brain.Tick(t);
            }

            Assert.AreEqual(ZONE_COUNT, r.Listener.Blasts.Count);
            r.Vitals.IsHealthZero = true;
            r.Brain.Tick(started + TunedFirstStrike + 3.2);
            int requests = r.Damage.Requests.Count;
            for (double t = started + TunedFirstStrike + 3.25; t < (started + TunedNextSelection + 1.5); t += 0.05)
            {
                r.Brain.Tick(t);
            }

            Assert.AreEqual(requests, r.Damage.Requests.Count, "사망 뒤에는 남은 잔류 위험이 피해를 내지 않는다");
            Assert.AreEqual(1, r.Listener.CancelReasons.Count);
            Assert.AreEqual(EBossInterruptReason.Death, r.Listener.CancelReasons[0]);
            Assert.IsFalse(r.Listener.CancelEnded[0], "위험이 끝나기 전에 끊겼다");
        }
    }
}
