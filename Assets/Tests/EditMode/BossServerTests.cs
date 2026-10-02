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

            boss.OnAimChanged(1, new Vector3(2f, 5f, 0f));
            Assert.AreEqual(Vector3.right, boss.Forward);

            boss.OnDirectionLocked(1, Vector3.zero);
            Assert.AreEqual(Vector3.right, boss.Forward, "방향이 없으면 이전 정면을 유지한다");
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
                0f, REFERENCE_HEALTH, HEALTH_SCALE, _catalog, _registry, _resolver, new EmptyBossEffectLifetime(),
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

        private BossServer CreateBoss()
        {
            return new BossServer(BOSS_ID, COMBAT_ID, Vector3.zero, Vector3.forward,
                REFERENCE_ATTACK, REFERENCE_HEALTH, HEALTH_SCALE,
                _catalog, _registry, _resolver, new EmptyBossEffectLifetime(), new SeededBossRandom(1));
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
    }
}
