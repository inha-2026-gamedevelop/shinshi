// System
using System.Collections.Generic;
using System.Text.RegularExpressions;

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

namespace Shinshi.Tests
{
    public class BossServerWolbaekTests
    {
        // 실제 월백 카탈로그로 서버 몸체를 돌려 여섯 패턴이 모두 만들어지고(돌진, 투사체, 장판 포함) 사건이 나가는지,
        // 50% 인계 대기에 들어갈 때 남은 투사체와 장판과 예고가 복원 기록에서도 사라지는지 본다. 물리 벽은 넣지 않는다.
        // 월백은 전환 틱에 결계 전개가 시작되고 전개가 끝난 틱에 서버 몸체가 인계를 마치므로 그 뒤 2페이즈 선택이 이어지고,
        // 별형 빔, 빔, 반월 연쇄 사건도 나가는지 본다. 결계는 사망과 전투 종료에 정리되고 복원 기록에서도 사라지는지 본다.
        // 무철은 패턴 없는 팩토리를 쓰고 수치가 비어 전투를 열지 않는지 본다

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
        private const int    PHASE_TWO         = 2;
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
        private const int    PHASE_TWO_COUNT   = 6;
        private const int    PHASE_TWO_SECONDS = 30;     // 인계 뒤 첫 2페이즈 공격을 기다리는 상한
        private const int    PHASE_ONE         = 1;
        private const int    BARRIER_SECONDS   = 20;     // 결계 전개 시작, 완료를 기다리는 상한(임시값 합은 7초 이하)
        private const int    BARRIER_TICKS     = Constants.Network.SERVER_TICK_RATE * BARRIER_SECONDS;
        private const float  FLOAT_EPSILON     = 0.001f;
        private const double TIME_EPSILON      = 0.000001;

        private static readonly string[] PHASE_TWO_IDS =
        {
            BossPatternCatalogSO.WOLBAEK_P2_P01_ID, BossPatternCatalogSO.WOLBAEK_P2_P02_ID,
            BossPatternCatalogSO.WOLBAEK_P2_P03_ID, BossPatternCatalogSO.WOLBAEK_P2_P04_ID,
            BossPatternCatalogSO.WOLBAEK_P2_P05_ID, BossPatternCatalogSO.WOLBAEK_P2_P06_ID
        };

        private BossPatternCatalogSource _catalog;
        private CombatTargetRegistry     _registry;
        private EnemyTestRigPlayer       _player;
        private BossServer               _boss;
        private CountingEvents           _events;
        private float                    _transitionRate;
        private double                   _now;

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
        public void Tick_HealthBelowTransition_ClearsLingeringEffectsThenCompletesHandoff()
        {
            Build();

            for (int i = 0; (i < MAX_TICKS) && (_events.Ledger.AreaCount == 0); ++i)
            {
                Step();
            }

            Assume.That(_events.Ledger.AreaCount, Is.GreaterThan(0), "장판이 생길 때까지 돌리지 못했다");

            ForceTransition();

            Assert.AreEqual(EBossPhaseState.AwaitingHandoff, _boss.PhaseState, "전환한 틱에는 인계 대기다");
            Assert.AreEqual(EBossFlowState.Combat, _boss.FlowState, "인계 대기는 전투 종료나 사망이 아니다");
            Assert.IsFalse(_boss.HasUnreportedDefeat);
            Assert.AreEqual(0, _boss.ActiveProjectileCount);
            Assert.IsFalse(_events.Ledger.HasCast, "진행 중이던 예고도 정리됐다");
            Assert.AreEqual(0, _events.Ledger.ProjectileCount);
            Assert.AreEqual(0, _events.Ledger.AreaCount);

            _events.BeginPhaseTwo();
            StepUntilBarrierCompleted();

            Assert.AreNotEqual(EBossPhaseState.AwaitingHandoff, _boss.PhaseState, "결계 전개가 끝난 틱에 인계를 마친다");

            for (int i = 0; (i < (Constants.Network.SERVER_TICK_RATE * PHASE_TWO_SECONDS)) && (_events.PhaseTwoAttacks == 0); ++i)
            {
                Step();
            }

            Assert.Greater(_events.PhaseTwoAttacks, 0, "인계 뒤 2페이즈 선택이 이어진다");
            Assert.AreEqual(EBossPhaseState.Active, _boss.PhaseState);
            foreach (string patternId in _events.PhaseTwoPatterns)
            {
                CollectionAssert.Contains(PHASE_TWO_IDS, patternId, "인계 뒤에는 2페이즈 패턴만 고른다");
            }
        }

        [Test]
        public void Tick_Transition_StartsBarrierAndHandsOffOnlyAfterCompletion()
        {
            Build();
            Assert.IsTrue(_boss.HasBarrier, "월백은 결계 전개를 쓴다");

            ForceTransition();
            Assert.AreEqual(EBossPhaseState.AwaitingHandoff, _boss.PhaseState);
            StepUntilBarrierStarted();

            BossBarrierStart start = _events.LastBarrier;
            Assert.AreEqual(_boss.CombatId, start.BossId);
            Assert.AreNotEqual(CombatIds.NONE, start.BarrierId);
            Assert.AreEqual(_boss.ArenaRadius, start.OuterRadius, FLOAT_EPSILON, "바깥 반경은 전장 반경이다");
            Assert.Less(start.InnerRadius, start.OuterRadius, "안쪽 안전 구역이 바깥 원보다 작다");
            Assert.Greater(start.ActivateServerTime, start.TelegraphServerTime);
            Assert.GreaterOrEqual(start.CompleteServerTime, start.ActivateServerTime);
            Assert.IsTrue(_events.Ledger.HasBarrier, "예고 중인 결계가 복원 기록에 남는다");
            Assert.IsTrue(_boss.TryGetBarrierSnapshot(out BossBarrierStart snapshot));
            Assert.AreEqual(start.BarrierId, snapshot.BarrierId, "복원 기록과 서버 현재 값이 같은 결계다");

            StepUntilBarrierCompleted();

            Assert.AreEqual(1, _events.BarrierStarts, "결계는 한 번만 시작한다");
            Assert.AreEqual(1, _events.BarrierActivations);
            Assert.GreaterOrEqual(_now, start.CompleteServerTime - TIME_EPSILON, "완료 시각 전에는 인계하지 않는다");
            Assert.AreNotEqual(EBossPhaseState.AwaitingHandoff, _boss.PhaseState, "완료한 틱에 인계를 마친다");
            Assert.AreEqual(EBossBarrierState.Completed, _boss.BarrierState, "완료 뒤에도 위험 구역은 남는다");
            Assert.IsTrue(_events.Ledger.HasBarrier, "완료 뒤에도 정리 전까지 복원 기록에 남는다");
            Assert.AreEqual(0, _events.BarrierClears);
        }

        [Test]
        public void Tick_BossDiesDuringBarrier_ClearsBarrierAndRestoreRecord()
        {
            Build();
            ForceTransition();
            StepUntilBarrierStarted();

            _boss.ApplyDamage(0f, true, _now);
            Step();

            Assert.IsTrue(_boss.IsBossDeathRecorded);
            Assert.AreEqual(1, _events.BarrierClears, "사망한 틱에 결계를 정리한다");
            Assert.AreEqual(_events.LastBarrier.BarrierId, _events.LastClearedBarrierId);
            Assert.AreEqual(EBossBarrierState.Cleared, _boss.BarrierState);
            Assert.IsFalse(_events.Ledger.HasBarrier, "정리된 결계는 복원 기록에서 지운다");
            Assert.IsFalse(_boss.TryGetBarrierSnapshot(out BossBarrierStart _));
        }

        [Test]
        public void EndCombat_AfterBarrierCompleted_ClearsBarrier()
        {
            Build();
            ForceTransition();
            StepUntilBarrierCompleted();

            _now += TICK_SECONDS;
            _boss.EndCombat(_now);

            Assert.AreEqual(EBossFlowState.Ended, _boss.FlowState);
            Assert.AreEqual(1, _events.BarrierClears, "완료 뒤 남은 위험 구역도 전투 종료에 정리한다");
            Assert.AreEqual(EBossBarrierState.Cleared, _boss.BarrierState);
            Assert.IsFalse(_events.Ledger.HasBarrier);
        }

        [Test]
        public void Constructor_MucheolBossId_UsesPatternlessFactoryAndBeginRefuses()
        {
            BossPatternCatalogSO asset = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assume.That(asset, Is.Not.Null, "보스 카탈로그 에셋이 없다");
            var catalog  = new BossPatternCatalogSource(asset);
            var registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            Assume.That(catalog.TryGetBoss(MucheolPatternFactory.BOSS_ID, out BossPatternCatalogSO.BossDefinition _), Is.True,
                "무철 정의가 카탈로그에 없다");
            Assume.That(catalog.IsTuned(MucheolPatternFactory.BOSS_ID), Is.False, "무철 수치가 지정돼 이 시험의 전제가 바뀌었다");

            var boss = new BossServer(MucheolPatternFactory.BOSS_ID, registry.IssueCombatId(), Vector3.zero, Vector3.forward,
                Vector3.zero, REFERENCE_ATTACK, REFERENCE_HEALTH, HEALTH_SCALE, catalog, registry, new AlwaysAppliedResolver(),
                null, new SeededBossRandom(RANDOM_SEED));

            var definitions = new List<BossPatternCatalogSO.PatternDefinition>();
            catalog.CollectPatterns(Constants.Combat.DEV_BOSS_ID, PHASE_ONE, definitions);
            Assume.That(definitions.Count, Is.GreaterThan(0), "월백 1페이즈 정의가 없다");
            for (int i = 0; i < definitions.Count; ++i)
            {
                Assert.IsFalse(boss.CanCreatePattern(definitions[i]), "무철 팩토리는 월백 패턴을 만들지 않는다");
            }

            Assert.IsFalse(boss.HasBarrier, "결계 전개는 월백만 쓴다");

            LogAssert.Expect(LogType.Error, new Regex("최대 체력이 0"));
            Assert.IsFalse(boss.Begin(0.0), "수치가 비어 전투를 열지 않는다");
            Assert.AreEqual(EBossFlowState.Inactive, boss.FlowState);
            Assert.IsFalse(boss.IsActive);
        }

        [Test]
        public void CanCreatePattern_AllPhaseTwoDefinitions_IncludingStarAndBeam()
        {
            Build();

            var definitions = new List<BossPatternCatalogSO.PatternDefinition>();
            _catalog.CollectPatterns(Constants.Combat.DEV_BOSS_ID, PHASE_TWO, definitions);

            Assert.AreEqual(PHASE_TWO_COUNT, definitions.Count, "월백 2페이즈 정의는 여섯 개다");
            for (int i = 0; i < definitions.Count; ++i)
            {
                Assert.IsTrue(_boss.CanCreatePattern(definitions[i]),
                    $"서버 몸체의 팩토리가 {definitions[i].DefinitionId}를 만들 수 있다(별형 빔, 빔 창구와 전장 입력 연결)");
            }
        }

        [Test]
        public void Tick_PhaseTwo_SendsStarBeamAndSweepEventsAndRecordsRestores()
        {
            Build();
            ForceTransition();
            _events.BeginPhaseTwo();

            for (int i = 0; (i < MAX_TICKS) && (!_events.HasSeenPhaseTwoSpecials); ++i)
            {
                Step();
                _events.ObserveLedger();
            }

            Assert.IsTrue(_events.HasSeenPhaseTwoSpecials, _events.DescribePhaseTwo());
            Assert.Greater(_events.MaxLedgerBeams, 0, "예고 중이거나 활성된 빔이 복원 기록에 남는다");
            Assert.Greater(_events.MaxLedgerStars, 0, "예고 중이거나 활성된 별형 빔이 복원 기록에 남는다");
        }

        /****************************************
        *                Helpers
        ****************************************/

        private void Build()
        {
            BossPatternCatalogSO asset = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assume.That(asset, Is.Not.Null, "월백 카탈로그 에셋이 없다");

            var catalog = new BossPatternCatalogSource(asset);
            _catalog    = catalog;
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

        // 체력을 전환 비율 바로 아래로 맞추고 한 틱 돌려 2페이즈 전환을 일으킨다
        private void ForceTransition()
        {
            float target = _boss.MaxHealth * (_transitionRate - BELOW_HANDOFF);
            _boss.ApplyDamage(_boss.Health - target, false, _now);
            Step();
        }

        // 결계 예고가 시작될 때까지 돌린다. 전환 틱에 이미 시작했으면 바로 돌아간다
        private void StepUntilBarrierStarted()
        {
            for (int i = 0; (i < BARRIER_TICKS) && (_events.BarrierStarts == 0); ++i)
            {
                Assert.AreEqual(EBossPhaseState.AwaitingHandoff, _boss.PhaseState, "결계 전개 중에는 인계 대기다");
                Step();
            }

            Assert.AreEqual(1, _events.BarrierStarts, "전환 뒤 결계 예고가 시작됐다");
        }

        // 결계 전개가 끝날 때까지 돌리며 그 전에는 인계 대기가 유지되는지 본다
        private void StepUntilBarrierCompleted()
        {
            for (int i = 0; (i < BARRIER_TICKS) && (_events.BarrierCompletes == 0); ++i)
            {
                Assert.AreEqual(EBossPhaseState.AwaitingHandoff, _boss.PhaseState, "결계 전개가 끝나기 전에는 인계하지 않는다");
                Step();
            }

            Assert.AreEqual(1, _events.BarrierCompletes, "결계 전개가 끝났다");
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

            // 인계 뒤에 시작한 공격만 센다
            public HashSet<string> PhaseTwoPatterns { get; } = new HashSet<string>();
            public int             PhaseTwoAttacks  { get; private set; }
            public int             SweepBlasts      { get; private set; }
            public int             BeamActivations  { get; private set; }
            public int             BeamEnds         { get; private set; }
            public int             StarActivations  { get; private set; }
            public int             StarEnds         { get; private set; }
            public int             MaxLedgerBeams   { get; private set; }
            public int             MaxLedgerStars   { get; private set; }

            public int              BarrierStarts         { get; private set; }
            public int              BarrierActivations    { get; private set; }
            public int              BarrierCompletes      { get; private set; }
            public int              BarrierClears         { get; private set; }
            public int              LastClearedBarrierId  { get; private set; }
            public BossBarrierStart LastBarrier           { get; private set; }

            private bool _isPhaseTwo     = false;
            private int  _lastBeamCastId = CombatIds.NONE;

            public bool HasSeenPhaseTwoSpecials => (SweepBlasts > 0) && (BeamActivations > 0) && (BeamEnds > 0)
                && (StarActivations > 0) && (StarEnds > 0);

            public CountingEvents(BossServer boss)
            {
                _boss = boss;
            }

            public string Describe()
            {
                return $"patterns={string.Join(",", _patterns)}, projectiles={Projectiles}, areas={Areas}";
            }

            public string DescribePhaseTwo()
            {
                return $"patterns={string.Join(",", PhaseTwoPatterns)}, attacks={PhaseTwoAttacks}, sweepBlasts={SweepBlasts}, " +
                    $"beams={BeamActivations}/{BeamEnds}, stars={StarActivations}/{StarEnds}";
            }

            public void BeginPhaseTwo()
            {
                _isPhaseTwo = true;
            }

            public void ObserveLedger()
            {
                MaxLedgerBeams = Mathf.Max(MaxLedgerBeams, Ledger.BeamCount);
                MaxLedgerStars = Mathf.Max(MaxLedgerStars, Ledger.StarCount);
            }

            private void CountPhaseTwoAttack(string patternId)
            {
                if (!_isPhaseTwo)
                {
                    return;
                }

                ++PhaseTwoAttacks;
                if (!string.IsNullOrEmpty(patternId))
                {
                    PhaseTwoPatterns.Add(patternId);
                }
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
                if (_isPhaseTwo)
                {
                    CountPhaseTwoAttack(start.PatternId);
                }
                else
                {
                    _patterns.Add(start.PatternId);
                }

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

            public void OnSweepStarted(in BossSweepStart start)
            {
                Assert.AreEqual(_boss.CombatId, start.BossId);
                CountPhaseTwoAttack(BossPatternCatalogSO.WOLBAEK_P2_P03_ID);
                Ledger.OnSweepStarted(in start);
            }

            public void OnSweepZoneScheduled(in BossTelegraphStart zone)
            {
                Assert.AreNotEqual(zone.CastId, zone.GroupId, "구역 예고는 구역 번호가 키이고 시전 번호가 묶음이다");
                Assert.AreEqual(BossPatternCatalogSO.WOLBAEK_P2_P03_ID, zone.PatternId);
                Ledger.OnSweepZoneScheduled(in zone);
            }

            public void OnSweepZoneBlasted(int zoneId, double blastServerTime)
            {
                ++SweepBlasts;
                Ledger.OnSweepZoneBlasted(zoneId, blastServerTime);
            }

            public void OnSweepCancelled(int castId)
            {
                Ledger.OnSweepCancelled(castId);
            }

            // 빔은 한 시전에 여러 줄이라 시전 번호가 바뀔 때만 공격 하나로 센다
            public void OnBeamTelegraphStarted(in BossBeamStart start)
            {
                Assert.AreEqual(_boss.CombatId, start.BossId);
                Assert.Greater(start.Shape.ArenaRadius, 0f, "전장 입력이 연결됐다");
                if (start.CastId != _lastBeamCastId)
                {
                    _lastBeamCastId = start.CastId;
                    CountPhaseTwoAttack(BossPatternCatalogSO.WOLBAEK_P2_P02_ID);
                }

                Ledger.OnBeamTelegraphStarted(in start);
            }

            public void OnBeamCancelled(int beamId)
            {
                Ledger.OnBeamCancelled(beamId);
            }

            public void OnBeamActivated(int beamId, double activateServerTime)
            {
                ++BeamActivations;
                Ledger.OnBeamActivated(beamId, activateServerTime);
            }

            public void OnBeamTicked(int beamId, int hitIndex, double roundServerTime)
            {
                Ledger.OnBeamTicked(beamId, hitIndex, roundServerTime);
            }

            public void OnBeamEnded(int beamId, EBossAreaFate fate)
            {
                ++BeamEnds;
                Ledger.OnBeamEnded(beamId, fate);
            }

            public void OnStarTelegraphStarted(in BossStarStart start)
            {
                Assert.AreEqual(_boss.CombatId, start.BossId);
                Assert.Greater(start.Shape.ArenaRadius, 0f, "전장 입력이 연결됐다");
                CountPhaseTwoAttack(BossPatternCatalogSO.WOLBAEK_P2_P01_ID);
                Ledger.OnStarTelegraphStarted(in start);
            }

            public void OnStarCancelled(int starId)
            {
                Ledger.OnStarCancelled(starId);
            }

            public void OnStarActivated(int starId, double activateServerTime)
            {
                ++StarActivations;
                Ledger.OnStarActivated(starId, activateServerTime);
            }

            public void OnStarTicked(int starId, int hitIndex, double roundServerTime)
            {
                Ledger.OnStarTicked(starId, hitIndex, roundServerTime);
            }

            public void OnStarEnded(int starId, EBossAreaFate fate)
            {
                ++StarEnds;
                Ledger.OnStarEnded(starId, fate);
            }

            public void OnBarrierStarted(in BossBarrierStart start)
            {
                ++BarrierStarts;
                LastBarrier = start;
                Ledger.OnBarrierStarted(in start);
            }

            public void OnBarrierActivated(int barrierId, double activateServerTime)
            {
                ++BarrierActivations;
                Ledger.OnBarrierActivated(barrierId, activateServerTime);
            }

            public void OnBarrierCompleted(int barrierId, double completeServerTime)
            {
                ++BarrierCompletes;
                Ledger.OnBarrierCompleted(barrierId, completeServerTime);
            }

            public void OnBarrierCleared(int barrierId)
            {
                ++BarrierClears;
                LastClearedBarrierId = barrierId;
                Ledger.OnBarrierCleared(barrierId);
            }
        }
    }
}
