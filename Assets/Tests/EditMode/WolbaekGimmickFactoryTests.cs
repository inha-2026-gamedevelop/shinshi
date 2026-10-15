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
using Shinshi.Network;

namespace Shinshi.Tests
{
    public sealed class WolbaekGimmickFactoryTests
    {
        // 월백 기믹 묶음의 구성을 본다: 이지는 부적진, 색 결계, 봉인 셋이고 하드는 최후의 영역까지 넷이며 등록 순서가 발동 순서다.
        // 입력이 비었거나 설정이 값을 거절하면 묶음을 내지 않고 이유를 돌려준다. 기믹의 진행은 각 기믹 시험과 모의 전투가 본다

        /****************************************
        *                Fields
        ****************************************/

        private const string CATALOG_PATH     = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const float  REFERENCE_ATTACK = 644.32f;
        private const float  REFERENCE_HEALTH = 3541f;
        private const float  HEALTH_SCALE     = 1f;
        private const int    RANDOM_SEED      = 20261014;
        private const int    REVIVES          = 3;
        private const int    ANY_COMBAT_ID    = 2;
        private const int    EASY_COUNT       = 3;
        private const int    HARD_COUNT       = 4;

        // 부적진의 다음 경계는 발동 비율(75%)보다 낮아야 한다
        private const float  INVALID_TRANSITION_RATE = 0.9f;

        private SessionConfigSO      _config;
        private ParticipantRoster    _roster;
        private CombatTargetRegistry _registry;
        private List<PlayerServer>   _players;
        private BossServer           _boss;
        private float                _transitionRate;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _config  = ScriptableObject.CreateInstance<SessionConfigSO>();
            _roster  = new ParticipantRoster(_config);
            _players = new List<PlayerServer>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_config);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TryCreate_Easy_BuildsThreeGimmicksInTriggerOrder()
        {
            Assert.IsTrue(WolbaekGimmickFactory.TryCreate(Inputs(EBossDifficulty.Easy), out BossGimmickSet set, out string reason), reason);

            Assert.AreEqual(string.Empty, reason);
            Assert.AreEqual(EASY_COUNT, set.Gimmicks.Count);
            Assert.IsInstanceOf<WolbaekTalismanFormation>(set.Gimmicks[0]);
            Assert.IsInstanceOf<WolbaekColorWard>(set.Gimmicks[1]);
            Assert.IsInstanceOf<WolbaekFoxfireSeal>(set.Gimmicks[2]);
            Assert.IsInstanceOf<BossDamageCapComposite>(set.DamageCap);
            Assert.AreEqual(1, set.DeathHolds.Count);
            Assert.AreSame(set.Gimmicks[2], set.DeathHolds[0]);
            Assert.IsNull(set.FinalSuccess, "이지는 최후의 영역이 없다");
            Assert.IsNotNull(set.StopSource);
            Assert.IsNotNull(set.ReviveGate);
            Assert.IsTrue(set.ReplacesBarrier);
        }

        [Test]
        public void TryCreate_Hard_AddsFinalAreaAsHoldAndFinalSuccess()
        {
            Assert.IsTrue(WolbaekGimmickFactory.TryCreate(Inputs(EBossDifficulty.Hard), out BossGimmickSet set, out string reason), reason);

            Assert.AreEqual(HARD_COUNT, set.Gimmicks.Count);
            Assert.IsInstanceOf<WolbaekTalismanFormation>(set.Gimmicks[0]);
            Assert.IsInstanceOf<WolbaekColorWard>(set.Gimmicks[1]);
            Assert.IsInstanceOf<WolbaekFoxfireSeal>(set.Gimmicks[2]);
            Assert.IsInstanceOf<WolbaekFinalArea>(set.Gimmicks[3]);
            Assert.AreEqual(2, set.DeathHolds.Count);
            Assert.AreSame(set.Gimmicks[2], set.DeathHolds[0]);
            Assert.AreSame(set.Gimmicks[3], set.DeathHolds[1]);
            Assert.AreSame(set.Gimmicks[3], set.FinalSuccess);
            Assert.IsTrue(set.ReplacesBarrier);
        }

        [Test]
        public void TryCreate_BeforeCombat_NothingIsStoppedOrHeld()
        {
            Assert.IsTrue(WolbaekGimmickFactory.TryCreate(Inputs(EBossDifficulty.Hard), out BossGimmickSet set, out string reason), reason);

            Assert.IsFalse(set.StopSource.TryGetStop(out EBossGimmickStop kind, out _, out string detail));
            Assert.AreEqual(EBossGimmickStop.None, kind);
            Assert.AreEqual(string.Empty, detail);
            Assert.IsFalse(set.DamageCap.IsDamageBlocked);
            Assert.IsFalse(set.FinalSuccess.HasFinalSuccess);
            for (int i = 0; i < set.DeathHolds.Count; ++i)
            {
                Assert.IsFalse(set.DeathHolds[i].IsDeathHeld);
            }
        }

        [Test]
        public void ReviveGate_BeforeAnyGimmick_IsOpenAndNotManaged()
        {
            Assert.IsTrue(WolbaekGimmickFactory.TryCreate(Inputs(EBossDifficulty.Hard), out BossGimmickSet hard, out string reason), reason);
            Assert.IsTrue(WolbaekGimmickFactory.TryCreate(Inputs(EBossDifficulty.Easy), out BossGimmickSet easy, out reason), reason);

            Assert.IsTrue(hard.ReviveGate.IsReviveWindowOpen);
            Assert.AreEqual(EBossRevivePoint.NotManaged, hard.ReviveGate.TryGetRevivePoint(ANY_COMBAT_ID, out _),
                "최후의 영역이 진행 중이 아니면 기존 지점 선택을 쓴다");
            Assert.IsTrue(easy.ReviveGate.IsReviveWindowOpen);
            Assert.AreEqual(EBossRevivePoint.NotManaged, easy.ReviveGate.TryGetRevivePoint(ANY_COMBAT_ID, out _));
        }

        [Test]
        public void TryCreate_Set_AttachesToBossBeforeBegin()
        {
            Assert.IsTrue(WolbaekGimmickFactory.TryCreate(Inputs(EBossDifficulty.Hard), out BossGimmickSet set, out string reason), reason);

            Assert.IsTrue(_boss.AttachGimmicks(set));

            Assert.IsTrue(_boss.HasGimmicks);
            Assert.IsTrue(_boss.Begin(0.0));
            Assert.IsFalse(_boss.TryGetGimmickStop(out _, out _, out _));
            Assert.IsTrue(_boss.IsReviveWindowOpen);
        }

        [Test]
        public void TryCreate_NullInputs_ReturnsFalseWithReason()
        {
            Assert.IsFalse(WolbaekGimmickFactory.TryCreate(null, out BossGimmickSet set, out string reason));

            Assert.IsNull(set);
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void TryCreate_MissingPiece_ReturnsFalseWithReason()
        {
            BuildBoss(EBossDifficulty.Easy);
            var resolver = new AlwaysAppliedResolver();
            var random   = new SeededBossRandom(RANDOM_SEED);
            Func<int> revives = () => REVIVES;

            AssertRejected(new WolbaekGimmickInputs(null, EBossDifficulty.Easy, REFERENCE_HEALTH, resolver, resolver, _registry,
                random, _players, _roster, revives, _transitionRate));
            AssertRejected(new WolbaekGimmickInputs(_boss, EBossDifficulty.Easy, REFERENCE_HEALTH, null, resolver, _registry,
                random, _players, _roster, revives, _transitionRate));
            AssertRejected(new WolbaekGimmickInputs(_boss, EBossDifficulty.Easy, REFERENCE_HEALTH, resolver, null, _registry,
                random, _players, _roster, revives, _transitionRate));
            AssertRejected(new WolbaekGimmickInputs(_boss, EBossDifficulty.Easy, REFERENCE_HEALTH, resolver, resolver, null,
                random, _players, _roster, revives, _transitionRate));
            AssertRejected(new WolbaekGimmickInputs(_boss, EBossDifficulty.Easy, REFERENCE_HEALTH, resolver, resolver, _registry,
                null, _players, _roster, revives, _transitionRate));
            AssertRejected(new WolbaekGimmickInputs(_boss, EBossDifficulty.Easy, REFERENCE_HEALTH, resolver, resolver, _registry,
                random, null, _roster, revives, _transitionRate));
            AssertRejected(new WolbaekGimmickInputs(_boss, EBossDifficulty.Easy, REFERENCE_HEALTH, resolver, resolver, _registry,
                random, _players, null, revives, _transitionRate));
            AssertRejected(new WolbaekGimmickInputs(_boss, EBossDifficulty.Easy, REFERENCE_HEALTH, resolver, resolver, _registry,
                random, _players, _roster, null, _transitionRate));
        }

        [Test]
        public void TryCreate_SpecRejectsValue_CatchesAndReturnsReason()
        {
            BuildBoss(EBossDifficulty.Hard);
            var resolver = new AlwaysAppliedResolver();
            var random   = new SeededBossRandom(RANDOM_SEED);

            // 다음 경계가 발동 비율 이상이면 부적진 설정이 예외로 거절한다
            AssertRejected(new WolbaekGimmickInputs(_boss, EBossDifficulty.Hard, REFERENCE_HEALTH, resolver, resolver, _registry,
                random, _players, _roster, () => REVIVES, INVALID_TRANSITION_RATE));

            // 기준 체력이 0이면 부적진 생성자가 예외로 거절한다
            AssertRejected(new WolbaekGimmickInputs(_boss, EBossDifficulty.Hard, 0f, resolver, resolver, _registry,
                random, _players, _roster, () => REVIVES, _transitionRate));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static void AssertRejected(WolbaekGimmickInputs inputs)
        {
            Assert.IsFalse(WolbaekGimmickFactory.TryCreate(inputs, out BossGimmickSet set, out string reason));
            Assert.IsNull(set, "하나라도 만들지 못하면 묶음을 내지 않는다");
            Assert.IsNotEmpty(reason);
        }

        private WolbaekGimmickInputs Inputs(EBossDifficulty difficulty)
        {
            BuildBoss(difficulty);
            var resolver = new AlwaysAppliedResolver();
            return new WolbaekGimmickInputs(_boss, difficulty, REFERENCE_HEALTH, resolver, resolver, _registry,
                new SeededBossRandom(RANDOM_SEED), _players, _roster, () => REVIVES, _transitionRate);
        }

        // 전투를 열지 않은 실제 월백 몸체, 밀림 대상 조회는 빈 참가자 목록으로 넘긴다
        private void BuildBoss(EBossDifficulty difficulty)
        {
            BossPatternCatalogSO asset = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assume.That(asset, Is.Not.Null, "월백 카탈로그 에셋이 없다");

            var catalog = new BossPatternCatalogSource(asset, difficulty);
            Assume.That(catalog.TryGetBoss(Constants.Combat.DEV_BOSS_ID, out BossPatternCatalogSO.BossDefinition definition), Is.True,
                "월백 정의가 없다");
            Assume.That(catalog.IsTuned(Constants.Combat.DEV_BOSS_ID), Is.True, "월백 수치가 지정되지 않았다");
            _transitionRate = definition.PhaseTransitionHealthRate;

            _registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            _boss     = new BossServer(Constants.Combat.DEV_BOSS_ID, _registry.IssueCombatId(), Vector3.zero, Vector3.forward,
                Vector3.zero, REFERENCE_ATTACK, REFERENCE_HEALTH, HEALTH_SCALE, catalog, _registry, new AlwaysAppliedResolver(),
                null, new SeededBossRandom(RANDOM_SEED), null, null, new BossPlayerDisplaceLookup(_players));
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
    }
}
