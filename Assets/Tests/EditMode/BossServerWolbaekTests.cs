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
    public class BossServerWolbaekTests
    {
        // 실제 월백 카탈로그로 서버 몸체를 돌려 여섯 패턴이 모두 만들어지고(돌진, 투사체, 장판 포함) 사건이 나가는지,
        // 50% 인계 대기에 들어갈 때 남은 투사체와 장판과 예고가 복원 기록에서도 사라지는지 본다. 물리 벽은 넣지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private const string CATALOG_PATH      = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const float  REFERENCE_ATTACK  = 644.32f;
        private const float  REFERENCE_HEALTH  = 3541f;
        private const float  HEALTH_SCALE      = 1f;
        private const float  PLAYER_MAX_HEALTH = 1000000f;
        private const int    PATTERN_COUNT     = 6;
        private const int    MAX_SECONDS       = 900;
        private const int    HANDOFF_SECONDS   = 10;
        private const double TICK_SECONDS      = 1.0 / Constants.Network.SERVER_TICK_RATE;
        private const int    MAX_TICKS         = Constants.Network.SERVER_TICK_RATE * MAX_SECONDS;
        private const float  ORBIT_SPEED       = 0.7f;   // 라디안/초
        private const float  RADIUS_SPEED      = 0.23f;  // 라디안/초
        private const float  MIN_ORBIT         = 3f;
        private const float  ORBIT_SPAN        = 11f;
        private const float  HALF              = 0.5f;
        private const float  BELOW_HANDOFF     = 0.02f;  // 전환 비율보다 이만큼 아래로 체력을 맞춘다
        private const float  POSITION_EPSILON  = 0.01f;
        private const int    RANDOM_SEED       = 20261003;

        private CombatTargetRegistry _registry;
        private EnemyTestRigPlayer   _player;
        private BossServer           _boss;
        private CountingEvents       _events;
        private float                _transitionRate;
        private double               _now;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Tick_LongCombat_RunsAllSixPatternsAndStaysInArena()
        {
            Build();

            for (int i = 0; (i < MAX_TICKS) && (!_events.HasSeenAllPatterns); ++i)
            {
                Step();
                Assert.LessOrEqual(Flat(_boss.Position).magnitude, _boss.ArenaRadius - _boss.BodyRadius + POSITION_EPSILON,
                    "몸체는 전장 경계 안에 있다");
            }

            Assert.IsTrue(_events.HasSeenAllPatterns, _events.Describe());
            Assert.Greater(_events.DashStops, 0, "돌진이 시작부터 정지까지 이어진다");
            Assert.Greater(_events.MaxDistanceFromStart, 0f, "돌진이 몸체를 실제로 옮긴다");
        }

        [Test]
        public void Tick_HealthBelowTransition_ClearsLingeringEffectsAndAwaitsHandoff()
        {
            Build();

            for (int i = 0; (i < MAX_TICKS) && (_events.Ledger.AreaCount == 0); ++i)
            {
                Step();
            }

            Assume.That(_events.Ledger.AreaCount, Is.GreaterThan(0), "장판이 생길 때까지 돌리지 못했다");

            float target = _boss.MaxHealth * (_transitionRate - BELOW_HANDOFF);
            _boss.ApplyDamage(_boss.Health - target, false, _now);
            Step();

            Assert.AreEqual(EBossPhaseState.AwaitingHandoff, _boss.PhaseState);
            Assert.AreEqual(EBossFlowState.Combat, _boss.FlowState, "인계 대기는 전투 종료나 사망이 아니다");
            Assert.IsFalse(_boss.HasUnreportedDefeat);
            Assert.AreEqual(0, _boss.ActiveProjectileCount);
            Assert.IsFalse(_events.Ledger.HasCast, "진행 중이던 예고도 정리됐다");
            Assert.AreEqual(0, _events.Ledger.ProjectileCount);
            Assert.AreEqual(0, _events.Ledger.AreaCount);

            int startsBefore = _events.TelegraphStarts;
            for (int i = 0; i < (Constants.Network.SERVER_TICK_RATE * HANDOFF_SECONDS); ++i)
            {
                Step();
            }

            Assert.AreEqual(startsBefore, _events.TelegraphStarts, "인계 대기 중에는 새 패턴을 고르지 않는다");
        }

        /****************************************
        *                Helpers
        ****************************************/

        private void Build()
        {
            BossPatternCatalogSO asset = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assume.That(asset, Is.Not.Null, "월백 카탈로그 에셋이 없다");

            var catalog = new BossPatternCatalogSource(asset);
            Assume.That(catalog.IsTuned(Constants.Combat.DEV_BOSS_ID), Is.True, "월백 수치가 지정되지 않았다");
            Assert.IsTrue(catalog.TryGetBoss(Constants.Combat.DEV_BOSS_ID, out BossPatternCatalogSO.BossDefinition definition));
            _transitionRate = definition.PhaseTransitionHealthRate;

            _registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            int bossId   = _registry.IssueCombatId();
            int playerId = _registry.IssueCombatId();
            _player = new EnemyTestRigPlayer(playerId, new Vector3(0f, 0f, MIN_ORBIT), PLAYER_MAX_HEALTH);
            Assert.IsTrue(_registry.Register(_player));

            _boss = new BossServer(Constants.Combat.DEV_BOSS_ID, bossId, Vector3.zero, Vector3.forward, Vector3.zero,
                REFERENCE_ATTACK, REFERENCE_HEALTH, HEALTH_SCALE, catalog, _registry, new AlwaysAppliedResolver(), null,
                new SeededBossRandom(RANDOM_SEED));
            _events = new CountingEvents(_boss);
            _boss.SetEventListener(_events);

            _now = 0.0;
            Assert.IsTrue(_boss.Begin(_now));
            Assert.IsTrue(_registry.Register(_boss));
        }

        // 플레이어를 원 궤도로 돌리고 반경을 바꿔 거리 구간을 고르게 지나게 한다
        private void Step()
        {
            _now += TICK_SECONDS;
            float t      = (float)_now;
            float radius = MIN_ORBIT + (ORBIT_SPAN * (HALF + (HALF * Mathf.Sin(t * RADIUS_SPEED))));
            float angle  = t * ORBIT_SPEED;
            _player.Position = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

            _boss.Tick(_now);
            _events.ObservePosition(_boss.Position);
        }

        private static Vector3 Flat(Vector3 value)
        {
            value.y = 0f;
            return value;
        }

        private sealed class AlwaysAppliedResolver : IDamageResolver
        {
            private int _nextId = 1;

            public DamageResult Apply(in DamageRequest request)
            {
                return DamageResult.Applied(request.RawDamage, false, false);
            }

            public int IssueInstanceId()
            {
                int id = _nextId;
                ++_nextId;
                return id;
            }
        }

        // 사건을 복원 기록에 그대로 넘기면서 패턴과 지속물 횟수를 센다
        private sealed class CountingEvents : IBossEventListener
        {
            private readonly BossServer      _boss;
            private readonly HashSet<string> _patterns = new HashSet<string>();

            public BossEffectLedger Ledger               { get; } = new BossEffectLedger();
            public int              TelegraphStarts      { get; private set; }
            public int              DashStops            { get; private set; }
            public int              Projectiles          { get; private set; }
            public int              Areas                { get; private set; }
            public float            MaxDistanceFromStart { get; private set; }

            public bool HasSeenAllPatterns => (_patterns.Count == PATTERN_COUNT) && (Projectiles > 0) && (Areas > 0);

            public CountingEvents(BossServer boss)
            {
                _boss = boss;
            }

            public string Describe()
            {
                return $"patterns={string.Join(",", _patterns)}, projectiles={Projectiles}, areas={Areas}";
            }

            public void ObservePosition(Vector3 position)
            {
                MaxDistanceFromStart = Mathf.Max(MaxDistanceFromStart, Flat(position).magnitude);
            }

            public void OnTelegraphStarted(in BossTelegraphStart start)
            {
                Assert.AreEqual(_boss.CombatId, start.BossId);
                Assert.IsFalse(string.IsNullOrEmpty(start.PatternId), "예고 시작에는 패턴 ID가 있다");
                ++TelegraphStarts;
                _patterns.Add(start.PatternId);
                Ledger.OnTelegraphStarted(in start);
            }

            public void OnAimChanged(int castId, Vector3 direction, float length, double serverTime)
            {
                Ledger.OnAimChanged(castId, direction, length, serverTime);
            }

            public void OnDirectionLocked(int castId, Vector3 direction, float length)
            {
                Ledger.OnDirectionLocked(castId, direction, length);
            }

            public void OnStruck(int castId, double serverTime)
            {
                Ledger.OnStruck(castId, serverTime);
            }

            public void OnDashStarted(int castId, float distance, float seconds, float speed, double startServerTime)
            {
                Ledger.OnDashStarted(castId, distance, seconds, speed, startServerTime);
            }

            public void OnDashStopped(int castId, Vector3 position, bool stoppedByWall)
            {
                ++DashStops;
                Ledger.OnDashStopped(castId, position, stoppedByWall);
            }

            public void OnFinished(int castId)
            {
                Ledger.OnFinished(castId);
            }

            public void OnCancelled(int castId)
            {
                Ledger.OnCancelled(castId);
            }

            public void OnProjectileLaunched(in BossProjectileStart start)
            {
                ++Projectiles;
                Ledger.OnProjectileLaunched(in start);
            }

            public void OnProjectileEnded(int projectileId, EBossProjectileFate fate, Vector3 position)
            {
                Ledger.OnProjectileEnded(projectileId, fate, position);
            }

            public void OnAreaActivated(in BossAreaStart start)
            {
                ++Areas;
                Ledger.OnAreaActivated(in start);
            }

            public void OnAreaTicked(int areaId, int hitIndex, double roundServerTime)
            {
                Ledger.OnAreaTicked(areaId, hitIndex, roundServerTime);
            }

            public void OnAreaEnded(int areaId, EBossAreaFate fate)
            {
                Ledger.OnAreaEnded(areaId, fate);
            }
        }
    }
}
