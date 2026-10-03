// System
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Tests
{
    public class BossServerTests
    {
        // 패턴이 없는 가짜 카탈로그로 보스 몸체의 체력, 생사, 무적, 속박 칸, 종료 처리만 본다. 패턴 실행은 보스 테스트가 맡는다

        /****************************************
        *                Fields
        ****************************************/

        private const string BOSS_ID          = "boss_server_test";
        private const int    COMBAT_ID        = 7;
        private const float  HEALTH_REF       = 700f;
        private const float  DEFENSE          = 200f;
        private const float  REFERENCE_ATTACK = 10f;
        private const float  REFERENCE_HEALTH = 3541f;
        private const float  HEALTH_SCALE     = 1.8f;
        private const float  DAMAGE_SCALE     = 1.1f;
        private const float  RAW_DAMAGE       = 100f;
        private const float  ROOT_SECONDS     = 0.8f;
        private const float  FLOAT_EPSILON    = 0.001f;
        private const string ROOT_SOURCE      = "skill_root_test";
        private const string OTHER_SOURCE     = "skill_root_other";
        private const double TIME_EPSILON     = 0.000001;

        // 전장 지름 36m(반경 18m)에서 몸체 반경 2m를 뺀 거리가 중심에서 갈 수 있는 끝이다
        private const float  ARENA_LIMIT       = 16f;
        private const float  FAR_DISTANCE      = 100f;
        private const float  MOVE_DISTANCE     = 8f;
        private const float  WALL_DISTANCE     = 3f;
        private const int    CAST_ID           = 21;
        private const double TICK_TIME         = 4.5;
        private const double BOUNDARY_TIME     = 6.25;
        private const float  FAN_RADIUS        = 8f;
        private const float  FAN_ARC           = 90f;
        private const float  LINE_LENGTH       = 14f;
        private const float  LINE_WIDTH        = 4f;
        private const float  TELEGRAPH_SECONDS = 1.6f;
        private const float  PROJECTILE_SPEED  = 12f;

        private FakeBossCatalog      _catalog;
        private CombatTargetRegistry _registry;
        private RecordingResolver    _resolver;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _catalog  = new FakeBossCatalog();
            _registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            _resolver = new RecordingResolver();
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Constructor_MaxHealthIsHealthRefTimesArefTimesScale()
        {
            BossServer boss = CreateBoss();

            Assert.AreEqual(HEALTH_REF * REFERENCE_ATTACK * HEALTH_SCALE, boss.MaxHealth, FLOAT_EPSILON);
            Assert.AreEqual(boss.MaxHealth, boss.Health, FLOAT_EPSILON);
            Assert.AreEqual(DEFENSE, boss.Defense, FLOAT_EPSILON);
            Assert.AreEqual(ECombatFaction.Enemy, boss.Faction);
            Assert.AreEqual(1f, boss.HealthRate, FLOAT_EPSILON);
        }

        [Test]
        public void Begin_OpensCombatAndDropsInvulnerability()
        {
            BossServer boss = CreateBoss();
            Assert.IsTrue(boss.IsInvulnerable, "시작 전에는 피해를 받지 않는다");
            Assert.IsFalse(boss.AllowsRoot);

            Assert.IsTrue(boss.Begin(0.0));

            Assert.AreEqual(EBossFlowState.Combat, boss.FlowState);
            Assert.IsFalse(boss.IsInvulnerable);
            Assert.IsTrue(boss.AllowsRoot);
            Assert.IsTrue(boss.IsActive);
        }

        [Test]
        public void Begin_MissingDefinition_RefusesWithError()
        {
            _catalog.Boss = null;
            BossServer boss = CreateBoss();
            LogAssert.Expect(LogType.Error, new Regex("최대 체력이 0"));

            Assert.IsFalse(boss.Begin(0.0));
            Assert.AreEqual(EBossFlowState.Inactive, boss.FlowState);
        }

        [Test]
        public void Begin_UntunedCatalog_Refuses()
        {
            _catalog.Tuned = false;
            BossServer boss = CreateBoss();

            Assert.IsFalse(boss.Begin(0.0));
            Assert.IsTrue(boss.IsInvulnerable);
        }

        [Test]
        public void ApplyDamage_LethalMarksDeadAndTickRecordsDefeatOnce()
        {
            BossServer boss = CreateBoss();
            Assert.IsTrue(boss.Begin(0.0));

            boss.ApplyDamage(RAW_DAMAGE, false, 0.1);
            Assert.AreEqual(boss.MaxHealth - RAW_DAMAGE, boss.Health, FLOAT_EPSILON);
            Assert.AreEqual(EParticipantLifeState.Alive, boss.LifeState);

            boss.ApplyDamage(boss.Health, true, 0.2);
            Assert.AreEqual(EParticipantLifeState.Dead, boss.LifeState, "치명타는 그 자리에서 사망으로 바꾼다");
            Assert.IsTrue(boss.IsHealthZero);
            Assert.IsTrue(boss.IsInvulnerable);

            boss.Tick(0.2);

            Assert.AreEqual(EBossFlowState.Defeated, boss.FlowState);
            Assert.IsTrue(boss.HasUnreportedDefeat);
            boss.MarkDefeatReported();
            Assert.IsFalse(boss.HasUnreportedDefeat);
            Assert.IsFalse(boss.IsActive);
        }

        [Test]
        public void ApplyDamage_AfterDeath_Ignored()
        {
            BossServer boss = CreateBoss();
            Assert.IsTrue(boss.Begin(0.0));
            boss.ApplyDamage(boss.Health, true, 0.1);

            boss.ApplyDamage(RAW_DAMAGE, false, 0.2);

            Assert.AreEqual(0f, boss.Health, FLOAT_EPSILON);
        }

        [Test]
        public void ApplyRoot_KeepsFirstOfTickAndClearsAtTickEnd()
        {
            BossServer boss = CreateBoss();
            Assert.IsTrue(boss.Begin(0.0));

            boss.ApplyRoot(ROOT_SOURCE, ROOT_SECONDS, 0.1);
            boss.ApplyRoot(OTHER_SOURCE, ROOT_SECONDS, 0.1);

            Assert.IsTrue(boss.TryConsumeDirectControl(out BossDirectControl control));
            Assert.AreEqual(ROOT_SOURCE, control.SourceId, "한 틱의 첫 속박만 남긴다");
            Assert.AreEqual(ROOT_SECONDS, control.RequestedSeconds, FLOAT_EPSILON);
            Assert.IsFalse(boss.TryConsumeDirectControl(out _), "한 번 꺼내면 비어 있다");

            boss.ApplyRoot(ROOT_SOURCE, ROOT_SECONDS, 0.2);
            boss.Tick(0.2);

            Assert.IsFalse(boss.TryConsumeDirectControl(out _), "틱이 끝나면 남은 속박을 버린다");
        }

        [Test]
        public void ApplyRoot_BeforeCombat_Ignored()
        {
            BossServer boss = CreateBoss();

            boss.ApplyRoot(ROOT_SOURCE, ROOT_SECONDS, 0.0);

            Assert.IsFalse(boss.TryConsumeDirectControl(out _));
        }

        [Test]
        public void EndCombat_StopsAtSameTimeAndKeepsBossAlive()
        {
            BossServer boss = CreateBoss();
            Assert.IsTrue(boss.Begin(0.0));

            boss.EndCombat(1.0);

            Assert.AreEqual(EBossFlowState.Ended, boss.FlowState);
            Assert.IsTrue(boss.IsCombatEndRecorded);
            Assert.AreEqual(EParticipantLifeState.Alive, boss.LifeState);
            Assert.IsTrue(boss.IsInvulnerable, "종료 뒤에는 피해를 받지 않는다");
            Assert.IsFalse(boss.HasUnreportedDefeat);
        }

        [Test]
        public void EndCombat_AfterDefeat_KeepsDefeated()
        {
            BossServer boss = CreateBoss();
            Assert.IsTrue(boss.Begin(0.0));
            boss.ApplyDamage(boss.Health, true, 0.1);
            boss.Tick(0.1);

            boss.EndCombat(0.1);

            Assert.AreEqual(EBossFlowState.Defeated, boss.FlowState, "사망이 먼저 확정되면 종료 요청을 받지 않는다");
            Assert.IsFalse(boss.IsCombatEndRecorded);
        }

        [Test]
        public void TelegraphAim_TurnsBodyOnHorizontalPlane()
        {
            BossServer boss = CreateBoss();
            IBossFanTelegraphListener fan = boss;

            fan.OnAimChanged(1, new Vector3(2f, 5f, 0f));
            Assert.AreEqual(Vector3.right, boss.Forward);

            fan.OnDirectionLocked(1, Vector3.zero);
            Assert.AreEqual(Vector3.right, boss.Forward, "방향이 없으면 이전 정면을 유지한다");
        }

        [Test]
        public void Move_PastArenaEdge_StopsAtBoundaryMinusBodyRadius()
        {
            BossServer boss = CreateBoss();
            Assert.IsTrue(boss.Begin(0.0));

            Vector3 reached = boss.Move(Vector3.zero, Vector3.right * FAR_DISTANCE, Constants.Combat.BOSS_BODY_RADIUS);

            Assert.AreEqual(ARENA_LIMIT, reached.x, FLOAT_EPSILON);
            Assert.AreEqual(reached, boss.Position, "이동 결과가 몸체 위치가 된다");
        }

        [Test]
        public void Move_BeforeCombat_Ignored()
        {
            BossServer boss = CreateBoss();

            Vector3 reached = boss.Move(Vector3.zero, Vector3.right * MOVE_DISTANCE, Constants.Combat.BOSS_BODY_RADIUS);

            Assert.AreEqual(Vector3.zero, reached);
            Assert.AreEqual(Vector3.zero, boss.Position);
        }

        [Test]
        public void Move_AfterDeath_Ignored()
        {
            BossServer boss = CreateBoss();
            Assert.IsTrue(boss.Begin(0.0));
            boss.ApplyDamage(boss.Health, true, 0.1);

            boss.Move(Vector3.zero, Vector3.right * MOVE_DISTANCE, Constants.Combat.BOSS_BODY_RADIUS);

            Assert.AreEqual(Vector3.zero, boss.Position, "사망 뒤에 늦게 온 이동은 무시한다");
        }

        [Test]
        public void GetClearDistance_OnBoundary_AllowsInwardOnly()
        {
            BossServer boss = CreateBoss();
            Assert.IsTrue(boss.Begin(0.0));
            boss.Move(Vector3.zero, Vector3.right * FAR_DISTANCE, Constants.Combat.BOSS_BODY_RADIUS);

            float outward = boss.GetClearDistance(boss.Position, Vector3.right, MOVE_DISTANCE, Constants.Combat.BOSS_BODY_RADIUS);
            float inward  = boss.GetClearDistance(boss.Position, Vector3.left, MOVE_DISTANCE, Constants.Combat.BOSS_BODY_RADIUS);

            Assert.AreEqual(0f, outward, FLOAT_EPSILON);
            Assert.AreEqual(MOVE_DISTANCE, inward, FLOAT_EPSILON, "경계에 닿은 뒤에도 안쪽으로는 다시 돌진할 수 있다");
        }

        [Test]
        public void GetClearDistance_TerrainCloser_UsesTerrain()
        {
            var terrain = new FixedTerrain { Clear = WALL_DISTANCE };
            BossServer boss = CreateBoss(terrain);

            float clear = boss.GetClearDistance(Vector3.zero, Vector3.forward, MOVE_DISTANCE, Constants.Combat.BOSS_BODY_RADIUS);

            Assert.AreEqual(WALL_DISTANCE, clear, FLOAT_EPSILON);
        }

        [Test]
        public void FanTelegraph_ForwardsStartWithCombatIdShapeAndTickTime()
        {
            BossServer boss = CreateBoss();
            var events = new RecordingEvents();
            boss.SetEventListener(events);
            Assert.IsTrue(boss.Begin(0.0));
            boss.Tick(TICK_TIME);

            ((IBossFanTelegraphListener)boss).OnTelegraphStarted(CAST_ID, Vector3.zero, Vector3.right, FAN_RADIUS, FAN_ARC,
                TELEGRAPH_SECONDS);

            Assert.AreEqual(1, events.Starts.Count);
            BossTelegraphStart start = events.Starts[0];
            Assert.AreEqual(COMBAT_ID, start.BossId);
            Assert.AreEqual(CAST_ID, start.CastId);
            Assert.AreEqual(EBossTelegraphShape.Fan, start.Shape);
            Assert.AreEqual(TICK_TIME, start.StartServerTime, TIME_EPSILON);
            Assert.AreEqual(FAN_RADIUS, start.Length, FLOAT_EPSILON);
            Assert.AreEqual(Vector3.right, boss.Forward, "예고 방향으로 몸을 돌린다");
        }

        [Test]
        public void FanAim_UsesRadiusFromStart()
        {
            BossServer boss = CreateBoss();
            var events = new RecordingEvents();
            boss.SetEventListener(events);
            IBossFanTelegraphListener fan = boss;

            fan.OnTelegraphStarted(CAST_ID, Vector3.zero, Vector3.forward, FAN_RADIUS, FAN_ARC, TELEGRAPH_SECONDS);
            fan.OnAimChanged(CAST_ID, Vector3.right);
            fan.OnDirectionLocked(CAST_ID, Vector3.right);

            Assert.AreEqual(FAN_RADIUS, events.LastAimLength, FLOAT_EPSILON);
            Assert.AreEqual(FAN_RADIUS, events.LastLockLength, FLOAT_EPSILON);
        }

        [Test]
        public void CastExecuted_SingleStrikeShape_SendsStruckAtBoundary()
        {
            BossServer boss = CreateBoss();
            var events = new RecordingEvents();
            boss.SetEventListener(events);

            ((IBossLineTelegraphListener)boss).OnTelegraphStarted(CAST_ID, Vector3.zero, Vector3.forward, LINE_LENGTH,
                LINE_WIDTH, TELEGRAPH_SECONDS);
            boss.OnCastExecuted(CAST_ID, BOUNDARY_TIME);

            Assert.AreEqual(1, events.Struck.Count);
            Assert.AreEqual(BOUNDARY_TIME, events.StruckTime, TIME_EPSILON);
        }

        [Test]
        public void CastExecuted_Dash_DoesNotSendStruck()
        {
            BossServer boss = CreateBoss();
            var events = new RecordingEvents();
            boss.SetEventListener(events);

            ((IBossDashTelegraphListener)boss).OnTelegraphStarted(CAST_ID, Vector3.zero, Vector3.forward, LINE_LENGTH,
                LINE_WIDTH, TELEGRAPH_SECONDS);
            boss.OnCastExecuted(CAST_ID, BOUNDARY_TIME);

            Assert.AreEqual(0, events.Struck.Count, "돌진은 돌진 시작 사건이 발동이다");
        }

        [Test]
        public void CastFinished_AfterProjectileLaunch_SendsNoFinish()
        {
            BossServer boss = CreateBoss();
            var events = new RecordingEvents();
            boss.SetEventListener(events);

            ((IBossProjectileTelegraphListener)boss).OnTelegraphStarted(CAST_ID, Vector3.zero, Vector3.forward, LINE_LENGTH,
                LINE_WIDTH, TELEGRAPH_SECONDS);
            ((IBossProjectileListener)boss).OnProjectileLaunched(CAST_ID, Vector3.zero, Vector3.forward, PROJECTILE_SPEED,
                LINE_WIDTH, LINE_LENGTH, BOUNDARY_TIME);
            boss.OnCastFinished(CAST_ID, BOUNDARY_TIME + TELEGRAPH_SECONDS);

            Assert.AreEqual(1, events.Projectiles.Count);
            Assert.AreEqual(COMBAT_ID, events.Projectiles[0].BossId);
            Assert.AreEqual(CAST_ID, events.Projectiles[0].ProjectileId);
            Assert.AreEqual(0, events.Finished.Count, "발사로 이미 예고가 끝났다");
        }

        [Test]
        public void CastFinished_FanCast_SendsFinishOnce()
        {
            BossServer boss = CreateBoss();
            var events = new RecordingEvents();
            boss.SetEventListener(events);

            ((IBossFanTelegraphListener)boss).OnTelegraphStarted(CAST_ID, Vector3.zero, Vector3.forward, FAN_RADIUS, FAN_ARC,
                TELEGRAPH_SECONDS);
            boss.OnCastFinished(CAST_ID, BOUNDARY_TIME);
            boss.OnCastFinished(CAST_ID, BOUNDARY_TIME);

            Assert.AreEqual(1, events.Finished.Count);
        }

        [Test]
        public void CastExecuted_AreaNotActivated_SendsCancelInsteadOfStrike()
        {
            BossServer boss = CreateBoss();
            var events = new RecordingEvents();
            boss.SetEventListener(events);

            ((IBossAreaTelegraphListener)boss).OnTelegraphStarted(CAST_ID, Vector3.zero, FAN_RADIUS, TELEGRAPH_SECONDS);
            boss.OnCastExecuted(CAST_ID, BOUNDARY_TIME);
            boss.OnCastFinished(CAST_ID, BOUNDARY_TIME + TELEGRAPH_SECONDS);

            Assert.AreEqual(0, events.Struck.Count, "설치에 실패한 장판은 피해 없는 폭발로 보이지 않는다");
            CollectionAssert.AreEqual(new[] { CAST_ID }, events.Cancelled);
            Assert.AreEqual(0, events.Finished.Count);
        }

        [Test]
        public void Begin_ArenaNotLargerThanBody_Refuses()
        {
            _catalog.Boss = new BossPatternCatalogSO.BossDefinition(BOSS_ID, 0f, HEALTH_REF, DEFENSE, 0f, 0f,
                5f, 12f, 60f, 120f, 5f, 0.25f, 1f, 2f);
            BossServer boss = CreateBoss();

            LogAssert.Expect(LogType.Error, new Regex("전장 반경"));
            Assert.IsFalse(boss.Begin(0.0));
            Assert.AreEqual(EBossFlowState.Inactive, boss.FlowState);
        }

        [Test]
        public void ProjectileAndAreaEnds_MapToFates()
        {
            BossServer boss = CreateBoss();
            var events = new RecordingEvents();
            boss.SetEventListener(events);

            ((IBossProjectileListener)boss).OnProjectileEnded(CAST_ID, EBossProjectileEnd.Wall, Vector3.zero, CombatIds.NONE);
            ((IBossAreaListener)boss).OnAreaEnded(CAST_ID, EBossAreaEnd.Expired, BOUNDARY_TIME);

            Assert.AreEqual(EBossProjectileFate.Wall, events.LastProjectileFate);
            Assert.AreEqual(EBossAreaFate.Expired, events.LastAreaFate);
        }

        [Test]
        public void Registry_RegistersBossAsLivingEnemy()
        {
            BossServer boss = CreateBoss();
            Assert.IsTrue(boss.Begin(0.0));

            Assert.IsTrue(_registry.Register(boss));

            Assert.AreEqual(1, _registry.CountLivingInRadius(Vector3.zero, 1f, ECombatFaction.Enemy, false));
            Assert.AreEqual(0, _registry.GetLivingPlayers(new List<ICombatTarget>()), "보스는 플레이어 조회에 들어가지 않는다");
        }

        [Test]
        public void Constructor_InvalidReference_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BossServer(BOSS_ID, COMBAT_ID, Vector3.zero, Vector3.forward,
                Vector3.zero, 0f, REFERENCE_HEALTH, HEALTH_SCALE, _catalog, _registry, _resolver, null,
                new SeededBossRandom(1)));
        }

        [Test]
        public void DamageScaler_MultipliesRawDamageAndKeepsIds()
        {
            var scaler  = new BossDamageScaler(_resolver, DAMAGE_SCALE);
            var request = new DamageRequest(COMBAT_ID, 2, 3, 4, RAW_DAMAGE, EDamageFlags.Unblockable, Vector3.one, ROOT_SOURCE);

            scaler.Apply(in request);

            DamageRequest sent = _resolver.Last;
            Assert.AreEqual(RAW_DAMAGE * DAMAGE_SCALE, sent.RawDamage, FLOAT_EPSILON);
            Assert.AreEqual(request.AttackerId, sent.AttackerId);
            Assert.AreEqual(request.HitTargetId, sent.HitTargetId);
            Assert.AreEqual(request.CastInstanceId, sent.CastInstanceId);
            Assert.AreEqual(request.ActionInstanceId, sent.ActionInstanceId);
            Assert.AreEqual(request.Flags, sent.Flags);
            Assert.AreEqual(request.SourcePosition, sent.SourcePosition);
            Assert.AreEqual(request.SourceDefinitionId, sent.SourceDefinitionId);
            Assert.AreEqual(_resolver.NextId, scaler.IssueInstanceId(), "번호 발급은 안쪽에 맡긴다");
        }

        [Test]
        public void DamageScaler_NonPositiveScale_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BossDamageScaler(_resolver, 0f));
        }

        /****************************************
        *                Methods
        ****************************************/

        // 지형 검사를 넘기지 않으면 전장 경계만 본다
        private BossServer CreateBoss(IBossWallQuery terrain = null)
        {
            return new BossServer(BOSS_ID, COMBAT_ID, Vector3.zero, Vector3.forward, Vector3.zero,
                REFERENCE_ATTACK, REFERENCE_HEALTH, HEALTH_SCALE,
                _catalog, _registry, _resolver, terrain, new SeededBossRandom(1));
        }

        // 페이즈 전환 비율 0은 전환을 시험하지 않으려는 값이다
        private static BossPatternCatalogSO.BossDefinition MakeBoss()
        {
            return new BossPatternCatalogSO.BossDefinition(BOSS_ID, 36f, HEALTH_REF, DEFENSE, 0f, 0f,
                5f, 12f, 60f, 120f, 5f, 0.25f, 1f, 2f);
        }

        private sealed class FakeBossCatalog : IBossPatternCatalog
        {
            public BossPatternCatalogSO.BossDefinition Boss  { get; set; } = MakeBoss();
            public bool                                Tuned { get; set; } = true;

            public bool TryGetBoss(string bossId, out BossPatternCatalogSO.BossDefinition boss)
            {
                boss = ((Boss != null) && (bossId == Boss.DefinitionId)) ? Boss : null;
                return boss != null;
            }

            public void CollectPatterns(string bossId, int phase, List<BossPatternCatalogSO.PatternDefinition> results)
            {
                results.Clear();
            }

            public bool IsTuned(string bossId)
            {
                return Tuned;
            }
        }

        private sealed class RecordingResolver : IDamageResolver
        {
            public DamageRequest Last   { get; private set; }
            public int           NextId { get; private set; } = 1;

            public DamageResult Apply(in DamageRequest request)
            {
                Last = request;
                return DamageResult.Applied(request.RawDamage, false, false);
            }

            public int IssueInstanceId()
            {
                return NextId;
            }
        }

        private sealed class FixedTerrain : IBossWallQuery
        {
            public float Clear { get; set; }

            public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
            {
                return Mathf.Min(Clear, maxDistance);
            }
        }

        private sealed class RecordingEvents : IBossEventListener
        {
            public List<BossTelegraphStart>  Starts      { get; } = new List<BossTelegraphStart>();
            public List<int>                 Struck      { get; } = new List<int>();
            public List<int>                 Finished    { get; } = new List<int>();
            public List<int>                 Cancelled   { get; } = new List<int>();
            public List<BossProjectileStart> Projectiles { get; } = new List<BossProjectileStart>();

            public float               LastAimLength      { get; private set; }
            public float               LastLockLength     { get; private set; }
            public double              StruckTime         { get; private set; }
            public EBossProjectileFate LastProjectileFate { get; private set; }
            public EBossAreaFate       LastAreaFate       { get; private set; }

            public void OnTelegraphStarted(in BossTelegraphStart start)
            {
                Starts.Add(start);
            }

            public void OnAimChanged(int castId, Vector3 direction, float length, double serverTime)
            {
                LastAimLength = length;
            }

            public void OnDirectionLocked(int castId, Vector3 direction, float length)
            {
                LastLockLength = length;
            }

            public void OnStruck(int castId, double serverTime)
            {
                Struck.Add(castId);
                StruckTime = serverTime;
            }

            public void OnDashStarted(int castId, float distance, float seconds, float speed, double startServerTime)
            {
            }

            public void OnDashStopped(int castId, Vector3 position, bool stoppedByWall)
            {
            }

            public void OnFinished(int castId)
            {
                Finished.Add(castId);
            }

            public void OnCancelled(int castId)
            {
                Cancelled.Add(castId);
            }

            public void OnProjectileLaunched(in BossProjectileStart start)
            {
                Projectiles.Add(start);
            }

            public void OnProjectileEnded(int projectileId, EBossProjectileFate fate, Vector3 position)
            {
                LastProjectileFate = fate;
            }

            public void OnAreaActivated(in BossAreaStart start)
            {
            }

            public void OnAreaTicked(int areaId, int hitIndex, double roundServerTime)
            {
            }

            public void OnAreaEnded(int areaId, EBossAreaFate fate)
            {
                LastAreaFate = fate;
            }
        }
    }
}
