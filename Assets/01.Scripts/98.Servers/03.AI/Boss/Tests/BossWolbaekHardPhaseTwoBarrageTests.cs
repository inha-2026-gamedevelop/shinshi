// System
using System;
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

using EPatternGroup     = Shinshi.Data.BossPatternCatalogSO.EPatternGroup;
using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    // 월백 하드 2페이즈 P05 전방위 투사체 난사를 에셋의 실제 정의와 실제 팩토리, 실제 공격 FSM, 실제 투사체 관리자로 확인한다
    // 실제 서버 틱, 복제, 화면은 거치지 않는다. 피해, 시간, 형상 수치는 Play Mode와 다인 접속 검증 전의 초기값이라 값 자체의 정답을 보장하지 않는다
    //
    // 기본 좌표계: 각도는 월드 +Z에서 시계 방향(위에서 볼 때 오른쪽)이고 구역 s는 45 x s도 이상 45 x (s + 1)도 미만이다
    // 시각(시전 시작 기준): 발 k의 예고 시작 = 0.2 x k, 발 k의 발사 = 1.0 + 0.2 x k, 마지막 발 t=2.4, 다음 선택 t=3.4, 투사체 수명 3.5초
    // 난수 스크립트: 정수가 모두 0이면 구역 순열은 [1, 2, 3, 4, 5, 6, 7, 0]이다(Fisher-Yates에서 매번 맨 앞과 교환)
    public class BossWolbaekHardPhaseTwoBarrageTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CATALOG_PATH = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const string WOLBAEK_ID   = "boss_wolbaek";
        private const string HARD_ID      = BossPatternCatalogSO.WOLBAEK_HARD_P2_P05_ID;
        private const string EASY_ID      = BossPatternCatalogSO.WOLBAEK_P2_P05_ID;
        private const float  HREF         = 1000f;
        private const float  RAW_DAMAGE   = 0.18f * HREF;
        private const float  TOLERANCE    = 0.001f;
        private const double TIME_TOL     = 0.0001;
        private const int    SHOTS        = 8;
        private const float  SECTOR_ARC   = 45f;
        private const double SHOT_INTERVAL = 0.2;
        private const double TELEGRAPH    = 1.0;
        private const double LAST_FIRE    = 2.4;
        private const double NEXT_SELECTION = 3.4;
        private const int    PEAK_ACTIVE  = 9; // 앞선 시전 8발 + 다음 시전 첫 발
        private const double STEP         = 0.05;

        private static readonly Vector3 Origin = new Vector3(50f, 0f, -30f);

        /****************************************
        *               Fakes
        ****************************************/

        private sealed class BarrageQuery : ICombatTargetQuery
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

            public List<DamageRequest>             Requests { get; } = new List<DamageRequest>();
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

            public int CountFor(int targetId)
            {
                int count = 0;
                for (int i = 0; i < Requests.Count; ++i)
                {
                    count += (Requests[i].HitTargetId == targetId) ? 1 : 0;
                }

                return count;
            }
        }

        // 정수와 실수를 스크립트대로 돌려주고 호출 인자를 기록한다. 스크립트가 끝나면 기본값이다
        private sealed class ScriptedRandom : IBossRandom
        {
            public Queue<int>   Ints         { get; } = new Queue<int>();
            public Queue<float> Floats       { get; } = new Queue<float>();
            public float        DefaultFloat { get; set; } = 0.5f;
            public List<int>    IntLimits    { get; } = new List<int>();
            public int          FloatCalls   { get; private set; }

            public float NextFloat01()
            {
                ++FloatCalls;
                return (Floats.Count > 0) ? Floats.Dequeue() : DefaultFloat;
            }

            public int NextInt(int maxExclusive)
            {
                IntLimits.Add(maxExclusive);
                return (Ints.Count > 0) ? Ints.Dequeue() : 0;
            }
        }

        private sealed class WallQuery : IBossWallQuery
        {
            public float Limit { get; set; } = float.MaxValue;

            public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
            {
                return Mathf.Min(maxDistance, Limit);
            }
        }

        // 발사 창구만 흉내 내는 가짜, 표시 수신처를 알리지 않는다
        private sealed class BareLauncher : IBossProjectileLauncher
        {
            public float MeasurePath(Vector3 origin, Vector3 direction, float maxDistance, float width)
            {
                return maxDistance;
            }

            public bool TryLaunch(in BossProjectileSpec spec)
            {
                return true;
            }
        }

        private struct TelegraphRecord
        {
            public int     Cast;
            public int     Shot;
            public int     Sector;
            public Vector3 Direction;
            public float   Path;
            public double  Scheduled;
            public double  Processed;
        }

        private struct FireRecord
        {
            public int    Cast;
            public int    Shot;
            public int    ProjectileId;
            public bool   IsLaunched;
            public double Scheduled;
            public double Processed;
        }

        private struct CancelRecord
        {
            public int                    Cast;
            public int                    Telegraphed;
            public int                    Fired;
            public EBossInterruptReason   Reason;
        }

        private sealed class RecordingListener : IBossBarrageListener
        {
            public List<int>             StartedCasts   = new List<int>();
            public List<Vector3>         StartedOrigins = new List<Vector3>();
            public List<double>          StartedAt      = new List<double>();
            public List<int>             StartedShots   = new List<int>();
            public List<float>           StartedLead    = new List<float>();
            public List<float>           StartedGap     = new List<float>();
            public List<float>           StartedWidth   = new List<float>();
            public List<TelegraphRecord> Telegraphs     = new List<TelegraphRecord>();
            public List<FireRecord>      Fires          = new List<FireRecord>();
            public List<CancelRecord>    Cancels        = new List<CancelRecord>();
            public List<string>          Log            = new List<string>();

            public void OnBarrageStarted(int castInstanceId, Vector3 origin, double castStartServerTime, int shotCount,
                float telegraphSeconds, float shotIntervalSeconds, float width)
            {
                StartedCasts.Add(castInstanceId);
                StartedOrigins.Add(origin);
                StartedAt.Add(castStartServerTime);
                StartedShots.Add(shotCount);
                StartedLead.Add(telegraphSeconds);
                StartedGap.Add(shotIntervalSeconds);
                StartedWidth.Add(width);
                Log.Add("S");
            }

            public void OnBarrageShotTelegraphed(int castInstanceId, int shotIndex, int sectorIndex, Vector3 direction,
                float pathLength, double scheduledServerTime, double processedServerTime)
            {
                Telegraphs.Add(new TelegraphRecord { Cast = castInstanceId, Shot = shotIndex, Sector = sectorIndex,
                    Direction = direction, Path = pathLength, Scheduled = scheduledServerTime, Processed = processedServerTime });
                Log.Add("T" + shotIndex);
            }

            public void OnBarrageShotFired(int castInstanceId, int shotIndex, int projectileId, bool isLaunched,
                double scheduledServerTime, double processedServerTime)
            {
                Fires.Add(new FireRecord { Cast = castInstanceId, Shot = shotIndex, ProjectileId = projectileId,
                    IsLaunched = isLaunched, Scheduled = scheduledServerTime, Processed = processedServerTime });
                Log.Add("F" + shotIndex);
            }

            public void OnBarrageCancelled(int castInstanceId, int telegraphedCount, int firedCount, EBossInterruptReason reason)
            {
                Cancels.Add(new CancelRecord { Cast = castInstanceId, Telegraphed = telegraphedCount, Fired = firedCount,
                    Reason = reason });
                Log.Add("C");
            }
        }

        private struct LaunchRecord
        {
            public int     Cast;
            public int     Id;
            public Vector3 Origin;
            public Vector3 Direction;
            public float   Path;
            public double  Time;
        }

        private struct EndRecord
        {
            public int                Cast;
            public int                Id;
            public EBossProjectileEnd End;
            public int                HitTarget;
        }

        private sealed class ProjectileSink : IBossProjectileListener
        {
            public List<LaunchRecord> Launches = new List<LaunchRecord>();
            public List<EndRecord>    Ends     = new List<EndRecord>();

            public void OnProjectileLaunched(int castInstanceId, int projectileId, Vector3 origin, Vector3 direction, float speed,
                float width, float pathLength, double launchServerTime)
            {
                Launches.Add(new LaunchRecord { Cast = castInstanceId, Id = projectileId, Origin = origin, Direction = direction,
                    Path = pathLength, Time = launchServerTime });
            }

            public void OnProjectileEnded(int castInstanceId, int projectileId, EBossProjectileEnd end, Vector3 position, int hitTargetId)
            {
                Ends.Add(new EndRecord { Cast = castInstanceId, Id = projectileId, End = end, HitTarget = hitTargetId });
            }

            public int CountEnds(EBossProjectileEnd end)
            {
                int count = 0;
                for (int i = 0; i < Ends.Count; ++i)
                {
                    count += (Ends[i].End == end) ? 1 : 0;
                }

                return count;
            }
        }

        // 공격 FSM에 실제 팩토리가 만든 패턴을 올리고, 실제 BossCombat처럼 공격 진행 뒤에 지속물 관리자를 진행한다
        private sealed class Run
        {
            public FakeCombatTarget   Self      = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = Origin };
            public BarrageQuery       Query     = new BarrageQuery();
            public ScriptedResolver   Resolver  = new ScriptedResolver();
            public RecordingListener  Listener  = new RecordingListener();
            public ProjectileSink     Sink      = new ProjectileSink();
            public WallQuery          Walls     = new WallQuery();
            public IBossRandom        Random;
            public BossProjectileManager Manager;
            public BossDamageGate     Gate;
            public BossAttackFsm      Fsm       = new BossAttackFsm();
            public PatternDefinition  Definition;
            public BossBarragePattern Pattern;
            public int                CastId;
            public int                Peak;
            public bool               UseRandom = true;

            public Run(IBossRandom random = null, bool useWalls = false)
            {
                Random     = random ?? new SeededBossRandom(11);
                Manager    = new BossProjectileManager(Query, useWalls ? Walls : null, Sink);
                Gate       = new BossDamageGate(Resolver);
                Gate.Open();
                Definition = LoadDefinition(HARD_ID);
            }

            public FakeCombatTarget Add(int id, Vector3 position)
            {
                FakeCombatTarget target = new FakeCombatTarget { CombatId = id, Faction = ECombatFaction.Player, Position = position };
                Query.Targets.Add(target);
                return target;
            }

            public BossBarragePattern StartCast(double time)
            {
                WolbaekPatternFactory factory = new WolbaekPatternFactory(HREF, projectiles: Manager, barrageListener: Listener);
                IBossPattern pattern = factory.Create(Definition);
                Assert.IsNotNull(pattern);
                Pattern = (BossBarragePattern)pattern;
                CastId  = Gate.IssueInstanceId();
                BossPatternContext context = new BossPatternContext(Definition, CastId, CombatIds.NONE, Self, Query, Gate,
                    UseRandom ? Random : null);
                Assert.IsTrue(Fsm.Start(Definition, pattern, in context, time));
                return Pattern;
            }

            public void Step(double time)
            {
                Fsm.Tick(time);
                Manager.Tick(time);
                Peak = Math.Max(Peak, Manager.ActiveCount);
            }

            public void StepRange(double from, double to, double step = STEP)
            {
                for (double t = from; t <= to + 0.000001; t += step)
                {
                    Step(t);
                }
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

        // origin에서 +Z 기준 시계 방향 degrees도, 거리 radius인 점
        private static Vector3 Polar(Vector3 origin, float degrees, float radius)
        {
            float rad = degrees * Mathf.Deg2Rad;
            return origin + (new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * radius);
        }

        // 방향 벡터의 +Z 기준 시계 방향 각도, 0 이상 360 미만
        private static float AngleOf(Vector3 direction)
        {
            float angle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            return (angle < 0f) ? (angle + 360f) : angle;
        }

        private static double TelegraphTime(int shot)
        {
            return shot * SHOT_INTERVAL;
        }

        private static double FireTime(int shot)
        {
            return TELEGRAPH + (shot * SHOT_INTERVAL);
        }

        // 정수가 모두 0이면 구역 순열은 [1, 2, 3, 4, 5, 6, 7, 0]이고 실수 0.5면 구역 가운데다
        private static ScriptedRandom CenteredRandom()
        {
            return new ScriptedRandom { DefaultFloat = 0.5f };
        }

        // 에셋 정의의 선택 데이터는 그대로 두고 난사 구조 값만 바꾼 정의
        private static PatternDefinition MakeVariant(string id = HARD_ID, int count = SHOTS, float interval = 0.2f,
            float telegraph = 1f, float tracking = 0f, float turnRate = 0f, float active = 1.4f, float recovery = 1f,
            bool guardable = true, int maxHits = 2, EBossDifficulty difficulty = EBossDifficulty.Hard,
            EnemyCatalogSO.AttackShapeSpec shape = null)
        {
            return new PatternDefinition(id, WOLBAEK_ID, 2, 22f,
                new[] { 0.5f, 1.4f, 1.7f }, new[] { 1.2f, 1f, 0.7f }, new[] { 1.2f, 0.9f, 0.6f }, new[] { 0.25f, 0.3f, 0.7f, 1f },
                telegraph, tracking, turnRate, active, recovery, shape ?? EnemyCatalogSO.AttackShapeSpec.Projectile(35f, 1f, 10f),
                0.18f, guardable, group: EPatternGroup.Basic, enhancedBaseWeight: 18f, projectileCount: count,
                projectileIntervalSeconds: interval, difficulty: difficulty, projectileMaxHitsPerTarget: maxHits);
        }

        private static BossProjectileSpec MakeSpec(int cast, int action, BossDamageGate gate, BossProjectileHitLimit limit = null)
        {
            return new BossProjectileSpec(cast, action, 1, HARD_ID, Vector3.zero, Vector3.forward, 10f, 35f, 1f, 1f,
                EDamageFlags.None, 0.0, gate, limit);
        }

        /****************************************
        *      Layout: Eight Sectors
        ****************************************/

        [Test]
        public void Layout_EverySeed_UsesEachOfTheEightSectorsOnceWithTheAngleInsideIt()
        {
            for (int seed = 0; seed < 300; ++seed)
            {
                BossBarrageLayout layout = new BossBarrageLayout(Origin, SHOTS, new SeededBossRandom(seed));
                Assert.AreEqual(SHOTS, layout.ShotCount);
                Assert.AreEqual(SECTOR_ARC, layout.SectorArc, TOLERANCE);

                bool[] used = new bool[SHOTS];
                for (int k = 0; k < SHOTS; ++k)
                {
                    int sector = layout.GetSector(k);
                    Assert.IsFalse(used[sector], "seed " + seed + " sector " + sector + " 중복");
                    used[sector] = true;

                    float angle = layout.GetAngle(k);
                    Assert.GreaterOrEqual(angle, sector * SECTOR_ARC, "seed " + seed + " shot " + k);
                    Assert.Less(angle, (sector + 1) * SECTOR_ARC, "seed " + seed + " shot " + k);
                    Assert.AreEqual(angle, AngleOf(layout.GetDirection(k)), 0.01f, "방향은 각도와 같다");
                    Assert.AreEqual(1f, layout.GetDirection(k).magnitude, TOLERANCE);
                    Assert.AreEqual(0f, layout.GetDirection(k).y, TOLERANCE, "수평 방향");
                }

                CollectionAssert.DoesNotContain(used, false);
            }
        }

        [Test]
        public void Layout_SameRandomSequence_GivesTheSameOrderAndAngles_AndSeedsDiffer()
        {
            BossBarrageLayout a = new BossBarrageLayout(Origin, SHOTS, new SeededBossRandom(42));
            BossBarrageLayout b = new BossBarrageLayout(Origin, SHOTS, new SeededBossRandom(42));
            for (int k = 0; k < SHOTS; ++k)
            {
                Assert.AreEqual(a.GetSector(k), b.GetSector(k));
                Assert.AreEqual(a.GetAngle(k), b.GetAngle(k));
            }

            HashSet<string> orders = new HashSet<string>();
            HashSet<int> firstSectors = new HashSet<int>();
            for (int seed = 0; seed < 100; ++seed)
            {
                BossBarrageLayout layout = new BossBarrageLayout(Origin, SHOTS, new SeededBossRandom(seed));
                string order = string.Empty;
                for (int k = 0; k < SHOTS; ++k)
                {
                    order += layout.GetSector(k);
                }

                orders.Add(order);
                firstSectors.Add(layout.GetSector(0));
            }

            Assert.Greater(orders.Count, 90, "구역 순서가 시전마다 무작위로 달라진다");
            Assert.AreEqual(SHOTS, firstSectors.Count, "첫 발의 구역도 어느 구역이든 나온다");
        }

        [Test]
        public void Layout_ScriptedRandom_FixesTheCallOrderAndTheMapping()
        {
            ScriptedRandom random = new ScriptedRandom();
            BossBarrageLayout layout = new BossBarrageLayout(Origin, SHOTS, random);

            CollectionAssert.AreEqual(new[] { 8, 7, 6, 5, 4, 3, 2 }, random.IntLimits, "순열은 마지막 칸부터 NextInt(i + 1)로 섞는다");
            Assert.AreEqual(SHOTS, random.FloatCalls, "각도는 발 순서대로 한 번씩");

            int[] expected = { 1, 2, 3, 4, 5, 6, 7, 0 };
            for (int k = 0; k < SHOTS; ++k)
            {
                Assert.AreEqual(expected[k], layout.GetSector(k), "shot " + k);
                Assert.AreEqual((expected[k] + 0.5f) * SECTOR_ARC, layout.GetAngle(k), TOLERANCE);
            }
        }

        [Test]
        public void Layout_RandomNearOne_StaysInsideItsOwnSector()
        {
            ScriptedRandom random = new ScriptedRandom { DefaultFloat = 1f };
            BossBarrageLayout layout = new BossBarrageLayout(Origin, SHOTS, random);

            for (int k = 0; k < SHOTS; ++k)
            {
                int sector = layout.GetSector(k);
                Assert.Less(layout.GetAngle(k), (sector + 1) * SECTOR_ARC, "구역 끝 경계는 다음 구역이다");
                Assert.AreEqual(sector, (int)Mathf.Floor(layout.GetAngle(k) / SECTOR_ARC));
            }
        }

        [Test]
        public void Layout_RejectsMissingRandomAndNoShots()
        {
            Assert.Throws<ArgumentNullException>(() => new BossBarrageLayout(Origin, SHOTS, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BossBarrageLayout(Origin, 0, new SeededBossRandom(1)));
        }

        /****************************************
        *      Asset, Catalog, Factory
        ****************************************/

        [Test]
        public void Asset_HardP05_HasTheContractValuesAndAHardOnlyId()
        {
            PatternDefinition p = LoadDefinition(HARD_ID);

            Assert.AreEqual("wolbaek_hard_ph2_p05", p.DefinitionId);
            Assert.AreEqual(HARD_ID + ".name", p.DisplayKey);
            Assert.AreEqual(EBossDifficulty.Hard, p.Difficulty);
            Assert.AreEqual(WOLBAEK_ID, p.BossId);
            Assert.AreEqual(2, p.Phase);
            Assert.AreEqual(EPatternGroup.Basic, p.Group);
            Assert.AreEqual(EEnemyAttackShape.Projectile, p.Shape.Shape);
            Assert.AreEqual(35f, p.Shape.Range, TOLERANCE, "사거리");
            Assert.AreEqual(1f, p.Shape.Width, TOLERANCE, "폭");
            Assert.AreEqual(10f, p.Shape.ProjectileSpeed, TOLERANCE, "속도");
            Assert.AreEqual(0.18f, p.DamageRef, TOLERANCE);
            Assert.IsTrue(p.Guardable, "방어와 반격 가능");
            Assert.AreEqual(SHOTS, p.RawProjectileCount, "구역 8개, 구역마다 한 발");
            Assert.AreEqual(0.2f, p.ProjectileIntervalSeconds, TOLERANCE);
            Assert.AreEqual(2, p.ProjectileMaxHitsPerTarget, "대상당 한 시전 2회");
            Assert.AreEqual(1f, p.TelegraphSeconds, TOLERANCE, "첫 발 t=1.0, 각 발 예고는 발사 1초 전");
            Assert.AreEqual(0f, p.TrackingSeconds, TOLERANCE, "대상을 따라 돌지 않는다");
            Assert.AreEqual(0f, p.TurnRate, TOLERANCE);
            Assert.AreEqual(1.4f, p.ActiveSeconds, TOLERANCE, "첫 발부터 마지막 발 t=2.4");
            Assert.AreEqual(1f, p.RecoverySeconds, TOLERANCE);
            Assert.AreEqual(NEXT_SELECTION, p.TelegraphSeconds + p.ActiveSeconds + p.RecoverySeconds, TOLERANCE, "다음 선택 t=3.4");
            Assert.IsFalse(p.HasControlWindow);
            Assert.AreEqual(0, p.MaxConcurrentAreas, "장판 상한으로 후보에서 빠지지 않는다");
            Assert.AreEqual(0, p.RawAreaTargetCount);
            Assert.AreEqual(0, p.RawSweepZoneCount);
        }

        [Test]
        public void Asset_HardP05_SelectionDataFollowsEasyP05WithTheHardRecentTable()
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
            Assert.AreEqual(0.3f, hard.RecentUseMultipliers[1], TOLERANCE, "2회 전 0.3");
            Assert.AreEqual(0.7f, hard.RecentUseMultipliers[2], TOLERANCE, "3회 전 0.7");
            Assert.AreEqual(1f, hard.RecentUseMultipliers[3], TOLERANCE, "그 밖 1.0");
        }

        [Test]
        public void Asset_EasyPhaseTwoP05_IsUnchanged()
        {
            PatternDefinition easy = LoadDefinition(EASY_ID);

            Assert.AreEqual(EBossDifficulty.Easy, easy.Difficulty);
            Assert.AreEqual(3, easy.RawProjectileCount);
            Assert.AreEqual(0.3f, easy.ProjectileIntervalSeconds, TOLERANCE);
            Assert.AreEqual(0, easy.ProjectileMaxHitsPerTarget, "이지는 대상 상한이 없다");
            Assert.AreEqual(0.9f, easy.TelegraphSeconds, TOLERANCE);
            Assert.AreEqual(0.45f, easy.TrackingSeconds, TOLERANCE);
            Assert.AreEqual(120f, easy.TurnRate, TOLERANCE);
            Assert.AreEqual(0.6f, easy.ActiveSeconds, TOLERANCE);
            Assert.AreEqual(0.5f, easy.RecoverySeconds, TOLERANCE);
            Assert.AreEqual(0.18f, easy.DamageRef, TOLERANCE);
            Assert.IsTrue(easy.HasControlWindow);
            Assert.AreEqual(0, LoadDefinition("wolbaek_p04").ProjectileMaxHitsPerTarget, "1페이즈 단발도 그대로다");
        }

        [Test]
        public void Asset_Catalog_StaysValid_AndHardPhaseTwoOpensWithSeven()
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            var easy = new List<PatternDefinition>();
            var hard = new List<PatternDefinition>();
            catalog.CollectPatterns(WOLBAEK_ID, 2, EBossDifficulty.Easy, easy);
            catalog.CollectPatterns(WOLBAEK_ID, 2, EBossDifficulty.Hard, hard);

            Assert.AreEqual(7, easy.Count);
            Assert.AreEqual(7, hard.Count);
            Assert.AreEqual(HARD_ID, hard[4].DefinitionId);
            Assert.AreEqual(BossPatternCatalogSO.WOLBAEK_HARD_P2_P06_ID, hard[5].DefinitionId);
            Assert.AreEqual(BossPatternCatalogSO.WOLBAEK_HARD_P2_P07_ID, hard[6].DefinitionId);
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
            Assert.IsTrue(catalog.IsPatternReady(WOLBAEK_ID, LoadDefinition(HARD_ID)), "P05 한 개의 데이터 자체는 준비됐다");
        }

        [Test]
        public void Validate_BarrageContract_RejectsBrokenVariantsAndAcceptsTheBaseline()
        {
            BossPatternCatalogSO catalog = LoadCatalog();

            Assert.IsTrue(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant()), "기준 변형은 에셋과 같아 준비된다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant(count: 7, active: 1.2f)), "구역 수는 8이다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant(maxHits: 1)), "대상당 2회");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant(maxHits: 3)));
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant(maxHits: 0)));
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant(maxHits: 9)), "상한 밖");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant(tracking: 0.3f)), "대상을 따라 돌지 않는다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant(turnRate: 90f)));
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant(guardable: false)), "방어와 반격 가능");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant(interval: 0f)));
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant(telegraph: 0f)));
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant(active: 1.0f)), "실행 시간이 첫 발부터 마지막 발을 덮어야 한다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant(difficulty: EBossDifficulty.Easy)));
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant(shape: EnemyCatalogSO.AttackShapeSpec.Circle(5f))));
        }

        [Test]
        public void Validate_MaxHitsField_IsOnlyForProjectilesAndOtherIdsAreNotPinned()
        {
            BossPatternCatalogSO catalog = LoadCatalog();

            Assert.IsTrue(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant("x_other", maxHits: 3)), "다른 ID의 투사체 상한은 범위 안이면 된다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID,
                MakeVariant("x_circle", count: 0, interval: 0f, shape: EnemyCatalogSO.AttackShapeSpec.Circle(5f))),
                "투사체가 아닌 형태에 대상 상한을 둘 수 없다");
        }

        [Test]
        public void Factory_NeedsLauncherDisplayAndListener_AndEasyLinksAreUnchanged()
        {
            BarrageQuery query = new BarrageQuery();
            PatternDefinition hard = LoadDefinition(HARD_ID);
            RecordingListener listener = new RecordingListener();
            BossProjectileManager shown = new BossProjectileManager(query, null, new ProjectileSink());
            BossProjectileManager hidden = new BossProjectileManager(query);

            Assert.IsTrue(shown.IsDisplayConnected);
            Assert.IsFalse(hidden.IsDisplayConnected);

            WolbaekPatternFactory bare = new WolbaekPatternFactory(HREF);
            Assert.IsFalse(bare.CanCreate(hard));
            Assert.IsNull(bare.Create(hard));

            WolbaekPatternFactory noListener = new WolbaekPatternFactory(HREF, projectiles: shown);
            Assert.IsFalse(noListener.CanCreate(hard), "발별 예고 알림이 없으면 보이지 않는 피해가 된다");
            Assert.IsNull(noListener.Create(hard));

            WolbaekPatternFactory noLauncher = new WolbaekPatternFactory(HREF, barrageListener: listener);
            Assert.IsFalse(noLauncher.CanCreate(hard));
            Assert.IsNull(noLauncher.Create(hard));

            WolbaekPatternFactory noDisplay = new WolbaekPatternFactory(HREF, projectiles: hidden, barrageListener: listener);
            Assert.IsFalse(noDisplay.CanCreate(hard), "발사와 소멸을 알릴 표시 수신처가 없다");
            Assert.IsNull(noDisplay.Create(hard));

            WolbaekPatternFactory unknownLauncher = new WolbaekPatternFactory(HREF, projectiles: new BareLauncher(), barrageListener: listener);
            Assert.IsFalse(unknownLauncher.CanCreate(hard), "표시 수신처 유무를 알릴 수 없는 창구는 믿지 않는다");
            Assert.IsNull(unknownLauncher.Create(hard));

            WolbaekPatternFactory full = new WolbaekPatternFactory(HREF, projectiles: shown, barrageListener: listener);
            Assert.IsTrue(full.CanCreate(hard));
            Assert.IsInstanceOf<BossBarragePattern>(full.Create(hard));

            PatternDefinition easy = LoadDefinition(EASY_ID);
            WolbaekPatternFactory projectileOnly = new WolbaekPatternFactory(HREF, projectiles: hidden);
            Assert.IsTrue(projectileOnly.CanCreate(easy), "이지는 표시 수신처 없이도 만들어진다");
            Assert.IsInstanceOf<BossProjectilePattern>(projectileOnly.Create(easy));
            Assert.IsInstanceOf<BossProjectilePattern>(full.Create(easy), "이지 ID는 난사 실행에 연결하지 않는다");
            Assert.IsInstanceOf<BossProjectilePattern>(full.Create(LoadDefinition("wolbaek_hard_p04")));
            Assert.IsInstanceOf<BossProjectilePattern>(full.Create(LoadDefinition("wolbaek_p04")));
            Assert.IsNull(new WolbaekPatternFactory(HREF).Create(easy), "발사 창구가 없으면 이지도 만들지 않는다");
        }

        /****************************************
        *      Start And Timeline
        ****************************************/

        [Test]
        public void Start_AnnouncesOriginAndSchedule_AndTheFirstTelegraphImmediately()
        {
            Run r = new Run(new SeededBossRandom(5));
            r.StartCast(5.0);

            Assert.IsFalse(r.Pattern.IsStartRejected);
            Assert.AreEqual(1, r.Listener.StartedCasts.Count);
            Assert.AreEqual(r.CastId, r.Listener.StartedCasts[0]);
            Assert.AreEqual(Origin, r.Listener.StartedOrigins[0], "시전 시작 서버 위치");
            Assert.AreEqual(5.0, r.Listener.StartedAt[0], TIME_TOL);
            Assert.AreEqual(SHOTS, r.Listener.StartedShots[0]);
            Assert.AreEqual(1f, r.Listener.StartedLead[0], TOLERANCE);
            Assert.AreEqual(0.2f, r.Listener.StartedGap[0], TOLERANCE);
            Assert.AreEqual(1f, r.Listener.StartedWidth[0], TOLERANCE);

            Assert.AreEqual("S", r.Listener.Log[0]);
            Assert.AreEqual(1, r.Listener.Telegraphs.Count, "첫 발의 예고는 시전 시작과 함께");
            Assert.AreEqual(0, r.Listener.Telegraphs[0].Shot);
            Assert.AreEqual(5.0, r.Listener.Telegraphs[0].Scheduled, TIME_TOL);
            Assert.AreEqual(5.0, r.Listener.Telegraphs[0].Processed, TIME_TOL);
            Assert.AreEqual(0, r.Listener.Fires.Count);
            Assert.AreEqual(0, r.Manager.ActiveCount);
        }

        [Test]
        public void Timeline_EachShotIsTelegraphedOneSecondEarly_FiresEveryTwoTenths_AndTheNextSelectionIsAt3Point4()
        {
            Run r = new Run(new SeededBossRandom(9));
            r.StartCast(0.0);
            r.StepRange(0.0, 3.5);

            Assert.AreEqual(SHOTS, r.Listener.Telegraphs.Count);
            Assert.AreEqual(SHOTS, r.Listener.Fires.Count);
            Assert.AreEqual(SHOTS, r.Sink.Launches.Count);

            for (int k = 0; k < SHOTS; ++k)
            {
                TelegraphRecord t = r.Listener.Telegraphs[k];
                FireRecord f = r.Listener.Fires[k];
                Assert.AreEqual(k, t.Shot);
                Assert.AreEqual(k, f.Shot);
                Assert.AreEqual(TelegraphTime(k), t.Scheduled, TIME_TOL, "예고 시작 " + k);
                Assert.AreEqual(FireTime(k), f.Scheduled, TIME_TOL, "발사 " + k);
                Assert.AreEqual(1.0, f.Scheduled - t.Scheduled, TIME_TOL, "예고는 발사 1초 전");
                Assert.IsTrue(f.IsLaunched);
                Assert.AreEqual(FireTime(k), r.Sink.Launches[k].Time, TIME_TOL, "투사체 발사 시각은 예정 시각");
                Assert.AreEqual(f.ProjectileId, r.Sink.Launches[k].Id);
                Assert.GreaterOrEqual(t.Processed, t.Scheduled - TIME_TOL);
                Assert.LessOrEqual(t.Processed - t.Scheduled, STEP + TIME_TOL, "촘촘한 틱에서는 한 틱 안에 처리한다");
                Assert.LessOrEqual(f.Processed - f.Scheduled, STEP + TIME_TOL);
            }

            Assert.AreEqual(LAST_FIRE, r.Listener.Fires[SHOTS - 1].Scheduled, TIME_TOL, "마지막 발 t=2.4");
            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State);
            Assert.AreEqual(0, r.Resolver.Requests.Count);
        }

        [Test]
        public void Timeline_ShotTelegraphsOverlap()
        {
            Run r = new Run(new SeededBossRandom(9));
            r.StartCast(0.0);
            r.StepRange(0.0, 1.25);

            int telegraphed = 0;
            int fired = 0;
            for (int i = 0; i < r.Listener.Telegraphs.Count; ++i)
            {
                telegraphed += (r.Listener.Telegraphs[i].Scheduled <= 1.2 + TIME_TOL) ? 1 : 0;
            }

            for (int i = 0; i < r.Listener.Fires.Count; ++i)
            {
                fired += (r.Listener.Fires[i].Scheduled <= 1.2 + TIME_TOL) ? 1 : 0;
            }

            Assert.AreEqual(7, telegraphed, "t=1.2에는 발 0~6의 예고가 시작됐다");
            Assert.AreEqual(2, fired, "발 0, 1은 나갔다");
            Assert.AreEqual(5, telegraphed - fired, "발 2~6의 예고가 동시에 보인다");
        }

        [Test]
        public void Timeline_BoundariesAreExact_NothingEarlyAndNothingLate()
        {
            Run r = new Run(new SeededBossRandom(9));
            r.StartCast(0.0);

            r.Step(0.19);
            Assert.AreEqual(1, r.Listener.Telegraphs.Count, "발 1의 예고는 0.2");
            r.Step(0.2);
            Assert.AreEqual(2, r.Listener.Telegraphs.Count);
            r.Step(0.9999);
            Assert.AreEqual(5, r.Listener.Telegraphs.Count, "0.8까지 발 0~4");
            Assert.AreEqual(0, r.Listener.Fires.Count, "첫 발 t=1.0 전에는 나가지 않는다");
            r.Step(1.0);
            Assert.AreEqual(1, r.Listener.Fires.Count, "경계에서 첫 발");
            Assert.AreEqual(6, r.Listener.Telegraphs.Count, "발 5의 예고도 t=1.0");
            r.Step(2.3999);
            Assert.AreEqual(7, r.Listener.Fires.Count, "마지막 발 t=2.4 전에는 7발");
            r.Step(2.4);
            Assert.AreEqual(SHOTS, r.Listener.Fires.Count, "마지막 발은 실행 끝 경계에서 놓치지 않는다");
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State);
            r.Step(3.3999);
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State);
            r.Step(3.4);
            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State, "다음 선택 t=3.4");
        }

        [Test]
        public void Timeline_BigTickJump_ProcessesEveryScheduledEventOnceInOrder_AndKeepsScheduledAndProcessedApart()
        {
            Run r = new Run(new SeededBossRandom(9));
            r.StartCast(0.0);
            r.Step(10.0);
            r.Step(10.5);
            r.Step(30.0);

            Assert.AreEqual(SHOTS, r.Listener.Telegraphs.Count, "한 번씩만");
            Assert.AreEqual(SHOTS, r.Listener.Fires.Count);
            for (int k = 0; k < SHOTS; ++k)
            {
                Assert.AreEqual(k, r.Listener.Telegraphs[k].Shot, "번호 순서대로");
                Assert.AreEqual(k, r.Listener.Fires[k].Shot);
                Assert.AreEqual(TelegraphTime(k), r.Listener.Telegraphs[k].Scheduled, TIME_TOL);
                Assert.AreEqual(FireTime(k), r.Listener.Fires[k].Scheduled, TIME_TOL, "예정 시각은 그대로");
                Assert.AreEqual(10.0, r.Listener.Fires[k].Processed, TIME_TOL, "처리 시각은 틱 시각");
                Assert.AreEqual(FireTime(k), r.Sink.Launches[k].Time, TIME_TOL, "투사체 발사 시각도 예정 시각");
                Assert.IsTrue(r.Listener.Fires[k].IsLaunched);
            }

            Assert.AreEqual(0.0, r.Listener.Telegraphs[0].Processed, TIME_TOL, "첫 예고는 시작 때");
            Assert.AreEqual(10.0, r.Listener.Telegraphs[1].Processed, TIME_TOL);

            // 예정 시각 순서: 모든 알림 로그에서 같은 종류 안의 번호가 늘어나고, 예고 k는 발사 k보다 먼저다
            int lastT = -1;
            int lastF = -1;
            for (int i = 0; i < r.Listener.Log.Count; ++i)
            {
                string entry = r.Listener.Log[i];
                if (entry.StartsWith("T"))
                {
                    int shot = int.Parse(entry.Substring(1));
                    Assert.AreEqual(lastT + 1, shot);
                    lastT = shot;
                }
                else if (entry.StartsWith("F"))
                {
                    int shot = int.Parse(entry.Substring(1));
                    Assert.AreEqual(lastF + 1, shot);
                    Assert.GreaterOrEqual(lastT, shot, "발사 전에 같은 발의 예고가 먼저 처리됐다");
                    lastF = shot;
                }
            }

            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State);
            Assert.AreEqual(SHOTS, r.Sink.CountEnds(EBossProjectileEnd.Range), "발사 시각부터 이어서 진행해 사거리에서 모두 끝난다");
            Assert.AreEqual(0, r.Manager.ActiveCount);
        }

        [Test]
        public void Timeline_MediumTicks_StillSeparateScheduledFromProcessed()
        {
            Run r = new Run(new SeededBossRandom(9));
            r.StartCast(0.0);
            r.StepRange(0.0, 3.5, 0.5);

            Assert.AreEqual(SHOTS, r.Listener.Fires.Count);
            for (int k = 0; k < SHOTS; ++k)
            {
                Assert.AreEqual(FireTime(k), r.Listener.Fires[k].Scheduled, TIME_TOL);
                Assert.GreaterOrEqual(r.Listener.Fires[k].Processed, r.Listener.Fires[k].Scheduled - TIME_TOL);
                Assert.Less(r.Listener.Fires[k].Processed - r.Listener.Fires[k].Scheduled, 0.5 + TIME_TOL);
            }

            Assert.AreEqual(1.0, r.Listener.Fires[0].Processed, TIME_TOL, "발 0은 0.5 간격 틱의 1.0에서");
            Assert.AreEqual(1.5, r.Listener.Fires[1].Processed, TIME_TOL, "발 1~2는 1.5 틱에서 함께");
        }

        /****************************************
        *      Fixed Origin And Directions
        ****************************************/

        [Test]
        public void Origin_IsFixedAtCastStart_EvenIfTheBossMoves()
        {
            Run r = new Run(new SeededBossRandom(9));
            r.StartCast(0.0);
            r.Step(0.5);
            r.Self.Position = Origin + new Vector3(7f, 0f, 3f);
            r.StepRange(0.55, 3.5);

            Assert.AreEqual(SHOTS, r.Sink.Launches.Count);
            for (int k = 0; k < SHOTS; ++k)
            {
                Assert.AreEqual(Origin, r.Sink.Launches[k].Origin, "발 " + k);
            }

            Assert.AreEqual(Origin, r.Pattern.Layout.Origin);
        }

        [Test]
        public void Directions_TelegraphAndLaunchShareTheFixedLayout_AndCoverEachSectorOnce()
        {
            for (int seed = 0; seed < 25; ++seed)
            {
                Run r = new Run(new SeededBossRandom(seed));
                r.StartCast(0.0);
                r.StepRange(0.0, 3.5);

                bool[] sectors = new bool[SHOTS];
                for (int k = 0; k < SHOTS; ++k)
                {
                    TelegraphRecord t = r.Listener.Telegraphs[k];
                    Assert.IsFalse(sectors[t.Sector], "seed " + seed + " 구역 중복 " + t.Sector);
                    sectors[t.Sector] = true;

                    Assert.AreEqual(r.Pattern.Layout.GetSector(k), t.Sector);
                    Assert.AreEqual(0f, (t.Direction - r.Pattern.Layout.GetDirection(k)).magnitude, TOLERANCE);
                    Assert.AreEqual(0f, (t.Direction - r.Sink.Launches[k].Direction).magnitude, TOLERANCE, "예고한 방향으로 나간다");
                    float angle = AngleOf(t.Direction);
                    Assert.GreaterOrEqual(angle, (t.Sector * SECTOR_ARC) - 0.01f);
                    Assert.Less(angle, ((t.Sector + 1) * SECTOR_ARC) + 0.01f);
                }

                CollectionAssert.DoesNotContain(sectors, false);
            }
        }

        [Test]
        public void Directions_SameSeedRepeatsTheSameCast_AndOnlyTheSeedChangesIt()
        {
            Run a = new Run(new SeededBossRandom(77));
            Run b = new Run(new SeededBossRandom(77));
            Run c = new Run(new SeededBossRandom(78));
            a.StartCast(0.0);
            b.StartCast(0.0);
            c.StartCast(0.0);
            a.StepRange(0.0, 3.5);
            b.StepRange(0.0, 3.5);
            c.StepRange(0.0, 3.5);

            bool differs = false;
            for (int k = 0; k < SHOTS; ++k)
            {
                Assert.AreEqual(a.Listener.Telegraphs[k].Sector, b.Listener.Telegraphs[k].Sector);
                Assert.AreEqual(0f, (a.Listener.Telegraphs[k].Direction - b.Listener.Telegraphs[k].Direction).magnitude, 0.000001f);
                differs |= a.Listener.Telegraphs[k].Sector != c.Listener.Telegraphs[k].Sector;
            }

            Assert.IsTrue(differs, "다른 시드는 다른 순서를 낸다");
        }

        [Test]
        public void Randomness_IsDrawnOnceAtStart_AndNeverAgainDuringTheCast()
        {
            ScriptedRandom random = CenteredRandom();
            Run r = new Run(random);
            r.StartCast(0.0);

            Assert.AreEqual(7, random.IntLimits.Count, "구역 순열 7번 교환");
            Assert.AreEqual(SHOTS, random.FloatCalls, "각도 8번");

            r.StepRange(0.0, 3.5);
            Assert.AreEqual(7, random.IntLimits.Count);
            Assert.AreEqual(SHOTS, random.FloatCalls);
        }

        [Test]
        public void Path_UsesTheWallQueryForBothTelegraphAndFlight()
        {
            Run r = new Run(CenteredRandom(), useWalls: true);
            r.Walls.Limit = 12f;
            r.StartCast(0.0);

            // 첫 발은 1.0초에 쏘고 벽까지 12m를 속도 10m/s로 날아 2.2초에 닿으므로 그 전에는 끝난 발이 없다
            const double FIRST_WALL_HIT = TELEGRAPH + (12.0 / 10.0);
            r.StepRange(0.0, FIRST_WALL_HIT - 0.1);
            Assert.AreEqual(0, r.Sink.Ends.Count, "벽에 닿기 전에는 끝나지 않는다");

            // 마지막 발은 2.4초에 쏘고 3.6초에 닿으므로 그 뒤까지 진행해야 8발이 모두 끝난다
            double lastWallHit = FireTime(SHOTS - 1) + (12.0 / 10.0);
            r.StepRange(FIRST_WALL_HIT - 0.05, lastWallHit + 0.2);

            for (int k = 0; k < SHOTS; ++k)
            {
                Assert.AreEqual(12f, r.Listener.Telegraphs[k].Path, TOLERANCE, "예고 경로 길이는 벽 앞까지");
                Assert.AreEqual(12f, r.Sink.Launches[k].Path, TOLERANCE, "실제 비행 길이와 같다");
            }

            Assert.AreEqual(SHOTS, r.Sink.CountEnds(EBossProjectileEnd.Wall));
            Assert.AreEqual(SHOTS, r.Sink.Ends.Count, "벽 말고 다른 이유로 끝난 발은 없다");
        }

        /****************************************
        *      Damage And Per-Target Cap
        ****************************************/

        [Test]
        public void Hit_TargetOnOneShotLine_GetsOneRequestWithTheContractedFields()
        {
            Run r = new Run(CenteredRandom());
            r.StartCast(0.0);

            // 정수가 0이면 발 3은 구역 4이고 가운데 각도는 202.5도
            r.Add(10, Polar(Origin, 202.5f, 10f));
            r.StepRange(0.0, 6.0);

            Assert.AreEqual(1, r.Resolver.Requests.Count);
            DamageRequest request = r.Resolver.Requests[0];
            Assert.AreEqual(10, request.HitTargetId);
            Assert.AreEqual(1, request.AttackerId);
            Assert.AreEqual(r.CastId, request.CastInstanceId);
            Assert.AreEqual(r.Listener.Fires[3].ProjectileId, request.ActionInstanceId);
            Assert.AreEqual(RAW_DAMAGE, request.RawDamage, TOLERANCE);
            Assert.AreEqual(EDamageFlags.None, request.Flags, "방어와 반격 가능");
            Assert.AreEqual(Origin, request.SourcePosition, "후방 판정 기준은 고정 원점");
            Assert.AreEqual(HARD_ID, request.SourceDefinitionId);
            Assert.AreEqual(1, r.Sink.CountEnds(EBossProjectileEnd.Hit));
            Assert.AreEqual(SHOTS - 1, r.Sink.CountEnds(EBossProjectileEnd.Range));
            Assert.AreEqual(10, r.Sink.Ends.Find(e => e.End == EBossProjectileEnd.Hit).HitTarget);
        }

        [Test]
        public void Cap_TargetOnTheBoss_IsHitTwicePerCast_AndTheRestPassThrough()
        {
            Run r = new Run(CenteredRandom());
            r.StartCast(0.0);
            r.Add(10, Origin);
            r.StepRange(0.0, 6.0);

            Assert.AreEqual(2, r.Resolver.CountFor(10), "대상당 한 시전 2회");
            Assert.AreEqual(r.Listener.Fires[0].ProjectileId, r.Resolver.Requests[0].ActionInstanceId);
            Assert.AreEqual(r.Listener.Fires[1].ProjectileId, r.Resolver.Requests[1].ActionInstanceId);
            Assert.AreEqual(2, r.Sink.CountEnds(EBossProjectileEnd.Hit));
            Assert.AreEqual(SHOTS - 2, r.Sink.CountEnds(EBossProjectileEnd.Range), "상한 뒤의 발은 대상을 지나쳐 날아간다");
            Assert.AreEqual(2, r.Pattern.HitLimit.GetHitCount(10));
            Assert.IsTrue(r.Pattern.HitLimit.IsExhausted(10));
        }

        [Test]
        public void Cap_GuardedHitsConsumeTheCapToo()
        {
            Run r = new Run(CenteredRandom());
            r.Resolver.Outcomes[10] = EDamageOutcome.Guarded;
            r.StartCast(0.0);
            r.Add(10, Origin);
            r.StepRange(0.0, 3.5);

            Assert.AreEqual(2, r.Resolver.CountFor(10), "방어와 반격으로 막은 유효 피격도 상한을 쓴다");
            Assert.AreEqual(2, r.Pattern.HitLimit.GetHitCount(10));
            Assert.AreEqual(2, r.Sink.CountEnds(EBossProjectileEnd.Hit));
        }

        [Test]
        public void Cap_MixedAppliedAndGuarded_AlsoStopsAtTwo()
        {
            Run r = new Run(CenteredRandom());
            r.StartCast(0.0);
            r.Add(10, Origin);
            r.StepRange(0.0, 1.1);
            Assert.AreEqual(1, r.Resolver.CountFor(10), "첫 발은 적용으로 맞았다");
            r.Resolver.Outcomes[10] = EDamageOutcome.Guarded;
            r.StepRange(1.15, 3.5);

            Assert.AreEqual(2, r.Resolver.CountFor(10));
        }

        [Test]
        public void Cap_InvulnerableDoesNotConsumeTheCap_ButEachShotStillDisappears()
        {
            Run r = new Run(CenteredRandom());
            r.Resolver.Outcomes[10] = EDamageOutcome.Invulnerable;
            r.StartCast(0.0);
            r.Add(10, Origin);
            r.StepRange(0.0, 3.5);

            Assert.AreEqual(SHOTS, r.Resolver.CountFor(10), "피해가 들어가지 않은 요청은 세지 않아 모든 발이 대상에게 간다");
            Assert.AreEqual(0, r.Pattern.HitLimit.GetHitCount(10));
            Assert.AreEqual(SHOTS, r.Sink.CountEnds(EBossProjectileEnd.Hit), "무적으로 막혀도 발은 사라진다");
        }

        [Test]
        public void Cap_IsPerTarget_TwoTargetsOnTheBossGetTwoEachInOrder()
        {
            Run r = new Run(CenteredRandom());
            r.StartCast(0.0);
            r.Add(11, Origin);
            r.Add(10, Origin);
            r.StepRange(0.0, 3.5);

            Assert.AreEqual(2, r.Resolver.CountFor(10));
            Assert.AreEqual(2, r.Resolver.CountFor(11));
            Assert.AreEqual(4, r.Resolver.Requests.Count);
            Assert.AreEqual(10, r.Resolver.Requests[0].HitTargetId, "같은 거리면 CombatId가 작은 쪽");
            Assert.AreEqual(10, r.Resolver.Requests[1].HitTargetId);
            Assert.AreEqual(11, r.Resolver.Requests[2].HitTargetId, "상한에 닿은 대상 뒤의 대상을 맞힌다");
            Assert.AreEqual(11, r.Resolver.Requests[3].HitTargetId);
            Assert.AreEqual(4, r.Sink.CountEnds(EBossProjectileEnd.Hit));
        }

        [Test]
        public void Cap_ResetsWithEachCast()
        {
            Run r = new Run(CenteredRandom());
            r.Add(10, Origin);
            r.StartCast(0.0);
            r.StepRange(0.0, 3.5);
            Assert.AreEqual(2, r.Resolver.CountFor(10));
            int firstCast = r.CastId;

            r.StartCast(4.0);
            r.StepRange(4.0, 7.5);

            Assert.AreEqual(4, r.Resolver.CountFor(10), "새 시전은 새 상한");
            Assert.AreNotEqual(firstCast, r.CastId);
            Assert.AreEqual(r.CastId, r.Resolver.Requests[3].CastInstanceId);
            Assert.AreEqual(2, r.Pattern.HitLimit.GetHitCount(10));
        }

        [Test]
        public void Cap_ClosedGate_NoRequestsAndNothingIsCounted()
        {
            Run r = new Run(CenteredRandom());
            r.StartCast(0.0);
            r.Add(10, Origin);
            r.StepRange(0.0, 0.95);
            r.Gate.CloseForGood();
            r.StepRange(1.0, 3.5);

            Assert.AreEqual(0, r.Resolver.Requests.Count);
            Assert.AreEqual(0, r.Pattern.HitLimit.GetHitCount(10));
        }

        [Test]
        public void Legacy_ShotsWithoutALimit_KeepHittingTheSameTarget()
        {
            RecordingTarget fixture = new RecordingTarget();
            BossProjectileManager manager = new BossProjectileManager(fixture.Query, null, new ProjectileSink());
            fixture.Query.Targets.Add(new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = Vector3.zero });

            for (int i = 1; i <= 3; ++i)
            {
                BossProjectileSpec spec = MakeSpec(5, 100 + i, fixture.Gate);
                Assert.IsTrue(manager.TryLaunch(in spec));
            }

            manager.Tick(0.5);

            Assert.AreEqual(3, fixture.Resolver.CountFor(10), "상한이 없는 발은 기존대로 발마다 맞힌다");
        }

        private sealed class RecordingTarget
        {
            public BarrageQuery     Query    = new BarrageQuery();
            public ScriptedResolver Resolver = new ScriptedResolver();
            public BossDamageGate   Gate;

            public RecordingTarget()
            {
                Gate = new BossDamageGate(Resolver);
                Gate.Open();
            }
        }

        [Test]
        public void HitLimit_Contract()
        {
            BossProjectileHitLimit limit = new BossProjectileHitLimit(2);

            Assert.AreEqual(2, limit.MaxHits);
            Assert.AreEqual(0, limit.GetHitCount(7));
            Assert.IsFalse(limit.IsExhausted(7));
            limit.RecordHit(7);
            Assert.IsFalse(limit.IsExhausted(7));
            limit.RecordHit(7);
            Assert.IsTrue(limit.IsExhausted(7));
            limit.RecordHit(7);
            Assert.AreEqual(2, limit.GetHitCount(7), "상한을 넘겨 세지 않는다");
            Assert.AreEqual(0, limit.GetHitCount(8));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BossProjectileHitLimit(0));
        }

        /****************************************
        *      Slots: Eight Shots Are Guaranteed
        ****************************************/

        [Test]
        public void Slots_SharedCapacityStaysEightForShotsWithoutALimit()
        {
            RecordingTarget fixture = new RecordingTarget();
            BossProjectileManager manager = new BossProjectileManager(fixture.Query);

            int accepted = 0;
            for (int i = 1; i <= 12; ++i)
            {
                BossProjectileSpec spec = MakeSpec(i, i, fixture.Gate);
                accepted += manager.TryLaunch(in spec) ? 1 : 0;
            }

            Assert.AreEqual(BossProjectileManager.SharedCapacity, accepted);
            Assert.AreEqual(8, accepted);
        }

        [Test]
        public void Slots_ShotsWithALimitUseTheReserveEvenWhenTheSharedSlotsAreFull()
        {
            RecordingTarget fixture = new RecordingTarget();
            BossProjectileManager manager = new BossProjectileManager(fixture.Query);

            for (int i = 1; i <= BossProjectileManager.SharedCapacity; ++i)
            {
                BossProjectileSpec legacy = MakeSpec(i, i, fixture.Gate);
                Assert.IsTrue(manager.TryLaunch(in legacy));
            }

            BossProjectileHitLimit limit = new BossProjectileHitLimit(2);
            int accepted = 0;
            for (int i = 1; i <= 10; ++i)
            {
                BossProjectileSpec limited = MakeSpec(50, 50 + i, fixture.Gate, limit);
                accepted += manager.TryLaunch(in limited) ? 1 : 0;
            }

            Assert.AreEqual(8, accepted, "공용 슬롯이 가득 차도 예비 슬롯 8개로 한 시전 분이 나간다");
            Assert.AreEqual(BossProjectileManager.TotalCapacity, manager.ActiveCount);
            Assert.AreEqual(16, BossProjectileManager.TotalCapacity);

            BossProjectileSpec late = MakeSpec(60, 60, fixture.Gate);
            Assert.IsFalse(manager.TryLaunch(in late), "상한 없는 발은 전체가 차 있으면 받지 않는다");
        }

        [Test]
        public void Slots_LimitedShotsFillingTheSharedPartBlockLegacyShots()
        {
            RecordingTarget fixture = new RecordingTarget();
            BossProjectileManager manager = new BossProjectileManager(fixture.Query);
            BossProjectileHitLimit limit = new BossProjectileHitLimit(2);

            for (int i = 1; i <= 9; ++i)
            {
                BossProjectileSpec limited = MakeSpec(50, 50 + i, fixture.Gate, limit);
                Assert.IsTrue(manager.TryLaunch(in limited), "limited " + i);
            }

            BossProjectileSpec legacy = MakeSpec(70, 70, fixture.Gate);
            Assert.IsFalse(manager.TryLaunch(in legacy), "공용 수를 넘긴 상태에서는 상한 없는 발을 받지 않는다");
        }

        [Test]
        public void Overlap_BackToBackCasts_NeverLoseAShot_AndThePeakIsNine()
        {
            Run r = new Run(new SeededBossRandom(3));
            r.StartCast(0.0);
            int casts = 1;
            for (int i = 1; i <= 320; ++i)
            {
                double t = i * STEP;
                r.Step(t);

                // 다음 선택이 가능한 가장 이른 틱에서 곧바로 다시 시전한다(앞선 투사체가 가장 많이 남는 경우)
                if ((r.Fsm.State == EBossAttackState.Ready) && (casts < 3))
                {
                    r.StartCast(t);
                    ++casts;
                }
            }

            Assert.AreEqual(3, casts);
            Assert.AreEqual(3 * SHOTS, r.Listener.Fires.Count);
            Assert.AreEqual(3 * SHOTS, r.Sink.Launches.Count, "모든 발이 나갔다");
            for (int i = 0; i < r.Listener.Fires.Count; ++i)
            {
                Assert.IsTrue(r.Listener.Fires[i].IsLaunched, "발사 창구가 받지 않은 발 " + i);
            }

            Assert.AreEqual(PEAK_ACTIVE, r.Peak, "앞선 8발이 남은 채 다음 시전 첫 발이 나가는 최악");
            Assert.Greater(r.Peak, BossProjectileManager.SharedCapacity, "공용 슬롯 8개만으로는 모자랐던 경우");
            Assert.LessOrEqual(r.Peak, BossProjectileManager.TotalCapacity);
            Assert.AreEqual(0, r.Manager.ActiveCount);
        }

        [Test]
        public void Overlap_PreviousCastShotsKeepFlyingAndHitting_WhileTheNextCastRuns()
        {
            Run r = new Run(CenteredRandom());
            r.StartCast(0.0);
            r.StepRange(0.0, NEXT_SELECTION + 0.05);
            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State);
            int firstCast = r.CastId;

            // 앞선 시전 투사체가 날고 있는 중에 다음 시전을 시작한다. 발 0(구역 1, 67.5도)은 4.5초에 사라진다
            Assert.AreEqual(SHOTS, r.Manager.ActiveCount, "첫 발의 4.5초 소멸 전이라 8발이 모두 남아 있다");
            r.Add(10, Polar(Origin, 67.5f, 33f));
            r.StartCast(NEXT_SELECTION + 0.05);
            r.StepRange(NEXT_SELECTION + 0.1, 5.0);

            Assert.AreEqual(1, r.Resolver.CountFor(10), "다음 시전이 진행 중에도 앞선 시전의 발이 33m 끝에서 맞힌다");
            Assert.AreEqual(firstCast, r.Resolver.Requests[0].CastInstanceId);
        }

        /****************************************
        *      Start Rejection
        ****************************************/

        [Test]
        public void Start_WithoutRandom_RejectsWithoutAnnouncingOrFiring()
        {
            Run r = new Run();
            r.UseRandom = false;
            r.StartCast(0.0);

            Assert.IsTrue(r.Pattern.IsStartRejected);
            r.Fsm.Cancel(EBossInterruptReason.None);
            r.StepRange(0.0, 4.0);

            Assert.AreEqual(0, r.Listener.Log.Count, "시작하지 못한 시전은 예고도 취소도 알리지 않는다");
            Assert.AreEqual(0, r.Sink.Launches.Count);
            Assert.AreEqual(0, r.Resolver.Requests.Count);
        }

        [Test]
        public void Start_InvalidDefinitions_RejectWithoutAnnouncing()
        {
            PatternDefinition[] broken =
            {
                MakeVariant("x_hits_zero", maxHits: 0),
                MakeVariant("x_no_interval", interval: 0f),
                MakeVariant("x_no_telegraph", telegraph: 0f),
                MakeVariant("x_nine_shots", count: 9, active: 1.6f),
                MakeVariant("x_no_shots", count: 0, interval: 0f)
            };

            for (int i = 0; i < broken.Length; ++i)
            {
                Run r = new Run();
                BossBarragePattern pattern = new BossBarragePattern(HREF, r.Manager, r.Listener);
                BossPatternContext context = new BossPatternContext(broken[i], r.Gate.IssueInstanceId(), CombatIds.NONE, r.Self, r.Query,
                    r.Gate, r.Random);
                Assert.IsTrue(r.Fsm.Start(broken[i], pattern, in context, 0.0));

                Assert.IsTrue(pattern.IsStartRejected, broken[i].DefinitionId);
                r.StepRange(0.0, 4.0);
                Assert.AreEqual(0, r.Listener.Log.Count, broken[i].DefinitionId);
            }
        }

        /****************************************
        *      Cancel And Cleanup
        ****************************************/

        [TestCase(EBossInterruptReason.Death)]
        [TestCase(EBossInterruptReason.CombatEnd)]
        [TestCase(EBossInterruptReason.PhaseTransition)]
        public void Cancel_MidCast_StopsLaterShotsAndClearsFlyingProjectiles(EBossInterruptReason reason)
        {
            Run r = new Run(CenteredRandom());
            r.StartCast(0.0);
            r.StepRange(0.0, 1.5);
            Assert.AreEqual(3, r.Listener.Fires.Count, "t=1.5까지 발 0~2");
            Assert.AreEqual(3, r.Manager.ActiveCount);

            int cast = r.Fsm.Cancel(reason);
            r.Manager.Cancel(reason, CombatIds.NONE);

            Assert.AreEqual(r.CastId, cast);
            Assert.AreEqual(1, r.Listener.Cancels.Count);
            Assert.AreEqual(8, r.Listener.Cancels[0].Telegraphed, "t=1.5까지 예고는 발 0~7이 모두 시작됐다");
            Assert.AreEqual(3, r.Listener.Cancels[0].Fired);
            Assert.AreEqual(reason, r.Listener.Cancels[0].Reason);
            Assert.AreEqual(0, r.Manager.ActiveCount);
            Assert.AreEqual(3, r.Sink.CountEnds(EBossProjectileEnd.Cancelled));

            int logCount = r.Listener.Log.Count;
            r.StepRange(1.55, 10.0);
            Assert.AreEqual(logCount, r.Listener.Log.Count, "취소 뒤에는 예고도 발사도 없다");
            Assert.AreEqual(3, r.Sink.Launches.Count);
            Assert.AreEqual(0, r.Resolver.Requests.Count);
        }

        [Test]
        public void Cancel_BeforeTheFirstShot_ReportsOnlyTheTelegraphsAlreadyStarted()
        {
            Run r = new Run(CenteredRandom());
            r.StartCast(0.0);
            r.StepRange(0.0, 0.5);

            r.Fsm.Cancel(EBossInterruptReason.Death);

            Assert.AreEqual(1, r.Listener.Cancels.Count);
            Assert.AreEqual(3, r.Listener.Cancels[0].Telegraphed, "0, 0.2, 0.4");
            Assert.AreEqual(0, r.Listener.Cancels[0].Fired);
            r.StepRange(0.55, 5.0);
            Assert.AreEqual(0, r.Sink.Launches.Count);
        }

        [Test]
        public void Cancel_AfterEveryShotIsOut_StillReportsAndTheProjectilesAreTheManagersToClear()
        {
            Run r = new Run(CenteredRandom());
            r.StartCast(0.0);
            r.StepRange(0.0, 2.45);

            r.Fsm.Cancel(EBossInterruptReason.CombatEnd);
            Assert.AreEqual(SHOTS, r.Listener.Cancels[0].Fired);
            Assert.Greater(r.Manager.ActiveCount, 0, "패턴 밖의 투사체는 관리자가 따로 정리한다");

            r.Manager.Cancel(EBossInterruptReason.CombatEnd, CombatIds.NONE);
            Assert.AreEqual(0, r.Manager.ActiveCount);
        }

        [Test]
        public void NormalFinish_LeavesTheFlyingProjectilesAlone_AndEndsWithoutACancelNotice()
        {
            Run r = new Run(CenteredRandom());
            r.StartCast(0.0);
            r.StepRange(0.0, 3.5);

            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State);
            Assert.AreEqual(0, r.Listener.Cancels.Count);
            Assert.Greater(r.Manager.ActiveCount, 0, "패턴이 끝나도 투사체는 남아 날고 있다");
            Assert.IsTrue(r.Pattern.IsFinished);
            r.StepRange(3.55, 6.0);
            Assert.AreEqual(0, r.Manager.ActiveCount, "마지막 발은 2.4 + 3.5 = 5.9초에 사라진다");
        }

        /****************************************
        *      Selection Weights
        ****************************************/

        [Test]
        public void Weight_RecentUseMultipliers_AreQuarterThirdTenthsAndSevenTenths()
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            Assert.IsTrue(catalog.TryGetBoss(WOLBAEK_ID, out BossPatternCatalogSO.BossDefinition boss));
            PatternDefinition hard = LoadDefinition(HARD_ID);
            int none = BossBlackboard.RecentSlotNone;

            float baseline = BossPatternSelector.ComputeSecondPhaseWeight(hard, boss, 1f, EPatternGroup.None, 1, 1, 1, none);
            Assert.Greater(baseline, 0f);
            Assert.AreEqual(0.25f, BossPatternSelector.ComputeSecondPhaseWeight(hard, boss, 1f, EPatternGroup.None, 1, 1, 1, 0) / baseline, 0.0001f);
            Assert.AreEqual(0.3f, BossPatternSelector.ComputeSecondPhaseWeight(hard, boss, 1f, EPatternGroup.None, 1, 1, 1, 1) / baseline, 0.0001f);
            Assert.AreEqual(0.7f, BossPatternSelector.ComputeSecondPhaseWeight(hard, boss, 1f, EPatternGroup.None, 1, 1, 1, 2) / baseline, 0.0001f);

            BossBlackboard blackboard = new BossBlackboard();
            blackboard.RecordUse(HARD_ID, EPatternGroup.Basic);
            Assert.AreEqual(0, blackboard.GetRecentSlot(HARD_ID));
            blackboard.RecordUse("other_a");
            Assert.AreEqual(1, blackboard.GetRecentSlot(HARD_ID));
            blackboard.RecordUse("other_b");
            Assert.AreEqual(2, blackboard.GetRecentSlot(HARD_ID));
            blackboard.RecordUse("other_c");
            Assert.AreEqual(none, blackboard.GetRecentSlot(HARD_ID), "3회 밖이면 그 밖 1.0");
        }

        /****************************************
        *      Through Boss Brain
        ****************************************/

        private sealed class BrainRig
        {
            public FakeCombatTarget   Self     = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = Origin };
            public FakeCombatTarget   Player   = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = Origin + new Vector3(0f, 0f, 200f) };
            public BarrageQuery       Query    = new BarrageQuery();
            public ScriptedResolver   Resolver = new ScriptedResolver();
            public FakeBossVitals     Vitals   = new FakeBossVitals();
            public FakeDeathPolicy    Death    = new FakeDeathPolicy();
            public RecordingListener  Listener = new RecordingListener();
            public ProjectileSink     Sink     = new ProjectileSink();
            public BossProjectileManager Projectiles;
            public BossBrain          Brain;
            public int                Peak;
            public List<string>       Used     = new List<string>();

            public BrainRig(int seed)
            {
                Query.Targets.Add(Player);
                Projectiles = new BossProjectileManager(Query, null, Sink);

                // 투사체 창구와 난사 알림만 연결해 하드 2페이즈에서 실행 가능한 후보는 P05뿐이다
                Brain = new BossBrain(WOLBAEK_ID, new BossBrainDependencies
                {
                    Catalog     = new BossPatternCatalogSource(LoadCatalog(), EBossDifficulty.Hard),
                    Self        = Self,
                    Vitals      = Vitals,
                    Targets     = Query,
                    Damage      = Resolver,
                    Patterns    = new WolbaekPatternFactory(HREF, projectiles: Projectiles, barrageListener: Listener),
                    Effects     = Projectiles,
                    Control     = new FakeControlSource(),
                    Random      = new SeededBossRandom(seed),
                    DeathPolicy = Death
                });
            }

            public void TransitionAndHandoff()
            {
                Assert.IsTrue(Brain.Begin(0.0));
                Vitals.HealthRate = 0.5f;
                Brain.Tick(0.25);
                Assert.IsTrue(Brain.CompleteHandoff());
            }

            public void Run(double from, double to)
            {
                string last = string.Empty;
                for (double t = from; t <= to + 0.000001; t += STEP)
                {
                    Brain.Tick(t);
                    Peak = Math.Max(Peak, Projectiles.ActiveCount);
                    string id = Brain.ActivePatternId;
                    if ((!string.IsNullOrEmpty(id)) && (id != last))
                    {
                        Used.Add(id);
                    }

                    last = id;
                }
            }
        }

        [Test]
        public void Brain_HardPhaseTwo_RepeatsTheBarrageBackToBack_WithoutLosingAnyShot()
        {
            BrainRig r = new BrainRig(21);
            r.TransitionAndHandoff();

            r.Run(0.3, 18.0);

            Assert.GreaterOrEqual(r.Used.Count, 3, "후보가 P05뿐이라 계속 다시 고른다");
            for (int i = 0; i < r.Used.Count; ++i)
            {
                Assert.AreEqual(HARD_ID, r.Used[i]);
            }

            // 마지막 시전은 끝까지 진행되지 않았을 수 있어 앞선 시전만 8발을 모두 쏜다
            Assert.GreaterOrEqual(r.Listener.Fires.Count, (r.Used.Count - 1) * SHOTS);
            Assert.LessOrEqual(r.Listener.Fires.Count, r.Used.Count * SHOTS);
            for (int i = 0; i < r.Listener.Fires.Count; ++i)
            {
                Assert.IsTrue(r.Listener.Fires[i].IsLaunched, "발 " + i);
            }

            Dictionary<int, HashSet<int>> sectorsByCast = new Dictionary<int, HashSet<int>>();
            for (int i = 0; i < r.Listener.Telegraphs.Count; ++i)
            {
                TelegraphRecord t = r.Listener.Telegraphs[i];
                if (!sectorsByCast.TryGetValue(t.Cast, out HashSet<int> sectors))
                {
                    sectors = new HashSet<int>();
                    sectorsByCast.Add(t.Cast, sectors);
                }

                Assert.IsTrue(sectors.Add(t.Sector), "한 시전 안에서 구역이 겹친다");
            }

            int fullCasts = 0;
            foreach (KeyValuePair<int, HashSet<int>> pair in sectorsByCast)
            {
                fullCasts += (pair.Value.Count == SHOTS) ? 1 : 0;
            }

            Assert.GreaterOrEqual(fullCasts, r.Used.Count - 1, "예고가 끝난 시전은 8구역을 모두 한 번씩 썼다");

            Assert.GreaterOrEqual(r.Peak, PEAK_ACTIVE, "앞선 시전 투사체가 남은 채 다음 시전이 나간다");
            Assert.LessOrEqual(r.Peak, BossProjectileManager.TotalCapacity);
            Assert.AreEqual(0, r.Resolver.Requests.Count, "대상이 사거리 밖이라 피해는 없다");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Brain_DeathOrCombatEndMidBarrage_ClearsEverythingAndStopsDamage(bool isDeath)
        {
            BrainRig r = new BrainRig(21);
            r.TransitionAndHandoff();
            r.Player.Position = Origin;
            r.Death.Confirm = true;

            double t = 0.3;
            for (; t < 8.0; t += STEP)
            {
                r.Brain.Tick(t);
                if (r.Listener.Fires.Count >= 3)
                {
                    break;
                }
            }

            Assert.GreaterOrEqual(r.Listener.Fires.Count, 3, "발사가 시작될 때까지 진행했다");
            int requestsBefore = r.Resolver.Requests.Count;
            if (isDeath)
            {
                r.Vitals.HealthRate   = 0f;
                r.Vitals.IsHealthZero = true;
            }
            else
            {
                r.Brain.RequestCombatEnd();
            }

            r.Brain.Tick(t + STEP);

            Assert.AreEqual(isDeath ? EBossFlowState.Defeated : EBossFlowState.Ended, r.Brain.FlowState);
            Assert.AreEqual(0, r.Projectiles.ActiveCount);
            Assert.AreEqual(1, r.Listener.Cancels.Count);
            Assert.AreEqual(isDeath ? EBossInterruptReason.Death : EBossInterruptReason.CombatEnd, r.Listener.Cancels[0].Reason);

            int launched = r.Sink.Launches.Count;
            int logCount = r.Listener.Log.Count;
            r.Brain.Tick(t + 100.0);

            Assert.AreEqual(launched, r.Sink.Launches.Count, "죽은 뒤에는 새 발이 나가지 않는다");
            Assert.AreEqual(logCount, r.Listener.Log.Count);
            Assert.AreEqual(requestsBefore, r.Resolver.Requests.Count, "정리 뒤에는 피해 요청이 나가지 않는다");
        }
    }
}
