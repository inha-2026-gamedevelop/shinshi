// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Tests
{
    // 보스 선택의 보스 ID + 난이도가 진행 카탈로그의 정확한 보스전 ID로 이어지는지 기본 카탈로그 값으로 검증한다
    // 해금과 입장 판정은 서버 몫이라 여기서 확인하지 않는다
    public class BossSelectRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private ProgressionCatalogSO _progression;
        private BossPatternCatalogSO _bosses;

        /****************************************
        *               Set Up
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _progression = ScriptableObject.CreateInstance<ProgressionCatalogSO>();
            _bosses = ScriptableObject.CreateInstance<BossPatternCatalogSO>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_progression);
            Object.DestroyImmediate(_bosses);
        }

        /****************************************
        *               Tests
        ****************************************/

        [TestCase("boss_wolbaek", EBossDifficulty.Easy, "battle_wolbaek_easy", true)]
        [TestCase("boss_wolbaek", EBossDifficulty.Hard, "battle_wolbaek_hard", true)]
        [TestCase("boss_mucheol", EBossDifficulty.Easy, "battle_mucheol_easy", true)]
        [TestCase("boss_mucheol", EBossDifficulty.Hard, "battle_mucheol_hard", true)]
        public void Resolve_ExistingCombination_ReturnsTheExactBattle(string bossId, EBossDifficulty difficulty, string battleId, bool inTrial)
        {
            BossBattleSelection selection = BossSelectRules.Resolve(_progression, _bosses, bossId, difficulty);

            Assert.That(selection.IsResolved, Is.True);
            Assert.That(selection.State, Is.EqualTo(EBossBattleSelectState.Resolved));
            Assert.That(selection.BattleId, Is.EqualTo(battleId));
            Assert.That(selection.BossId, Is.EqualTo(bossId));
            Assert.That(selection.Difficulty, Is.EqualTo(difficulty));
            Assert.That(selection.AvailableInTrial, Is.EqualTo(inTrial));
        }

        [Test]
        public void Resolve_EveryBattleInTheCatalog_RoundTripsToItself()
        {
            for (int i = 0; i < _progression.BossBattles.Count; ++i)
            {
                ProgressionCatalogSO.BossBattleDefinition battle = _progression.BossBattles[i];

                BossBattleSelection selection = BossSelectRules.Resolve(_progression, _bosses, battle.BossId, battle.Difficulty);

                Assert.That(selection.IsResolved, Is.True, battle.DefinitionId);
                Assert.That(selection.BattleId, Is.EqualTo(battle.DefinitionId));
            }
        }

        [Test]
        public void Resolve_EverySelectableBoss_NeverMapsToAnotherBossOrDifficulty()
        {
            for (int b = 0; b < _bosses.Bosses.Count; ++b)
            {
                string bossId = _bosses.Bosses[b].DefinitionId;
                for (int d = 0; d < 2; ++d)
                {
                    EBossDifficulty difficulty = (EBossDifficulty)d;
                    BossBattleSelection selection = BossSelectRules.Resolve(_progression, _bosses, bossId, difficulty);
                    if (!selection.IsResolved)
                    {
                        Assert.That(selection.BattleId, Is.Empty, bossId + " " + difficulty);
                        continue;
                    }

                    Assert.That(_progression.TryGetBattle(selection.BattleId, out ProgressionCatalogSO.BossBattleDefinition battle), Is.True);
                    Assert.That(battle.BossId, Is.EqualTo(bossId));
                    Assert.That(battle.Difficulty, Is.EqualTo(difficulty));
                }
            }
        }

        [Test]
        public void Resolve_FormerCheolgakId_IsAnUnknownBossAndNotSubstituted()
        {
            BossBattleSelection selection = BossSelectRules.Resolve(_progression, _bosses, "boss_cheolgak", EBossDifficulty.Easy);

            Assert.That(selection.IsResolved, Is.False);
            Assert.That(selection.State, Is.EqualTo(EBossBattleSelectState.UnknownBoss));
            Assert.That(selection.BattleId, Is.Empty);
        }

        [Test]
        public void Resolve_UndefinedDifficulty_HasNoBattleAndIsNotSubstituted()
        {
            BossBattleSelection selection = BossSelectRules.Resolve(_progression, _bosses, "boss_wolbaek", (EBossDifficulty)99);

            Assert.That(selection.IsResolved, Is.False);
            Assert.That(selection.State, Is.EqualTo(EBossBattleSelectState.NoBattleForDifficulty));
            Assert.That(selection.BattleId, Is.Empty);
        }

        [Test]
        public void Resolve_MissingInputs_ReportTheReason()
        {
            Assert.That(BossSelectRules.Resolve(_progression, _bosses, null, EBossDifficulty.Easy).State, Is.EqualTo(EBossBattleSelectState.None));
            Assert.That(BossSelectRules.Resolve(_progression, _bosses, string.Empty, EBossDifficulty.Easy).State, Is.EqualTo(EBossBattleSelectState.None));
            Assert.That(BossSelectRules.Resolve(null, _bosses, "boss_wolbaek", EBossDifficulty.Easy).State, Is.EqualTo(EBossBattleSelectState.MissingCatalog));
            Assert.That(BossSelectRules.Resolve(_progression, null, "boss_wolbaek", EBossDifficulty.Easy).State, Is.EqualTo(EBossBattleSelectState.MissingCatalog));
        }

        [Test]
        public void HasBattle_FollowsResolve()
        {
            Assert.That(BossSelectRules.HasBattle(_progression, _bosses, "boss_mucheol", EBossDifficulty.Hard), Is.True);
            Assert.That(BossSelectRules.HasBattle(_progression, _bosses, "boss_cheolgak", EBossDifficulty.Hard), Is.False);
            Assert.That(BossSelectRules.HasBattle(_progression, _bosses, "boss_wolbaek", (EBossDifficulty)5), Is.False);
        }

        [Test]
        public void Resolve_DoesNotJudgeUnlockOrEntry_HardWithoutClearStillResolves()
        {
            BossBattleSelection selection = BossSelectRules.Resolve(_progression, _bosses, "boss_mucheol", EBossDifficulty.Hard);

            Assert.That(selection.IsResolved, Is.True);

            // 체험판은 무철 하드를 미리 연다. 정식판 해금 조건(이지 처치)은 그대로 남고, 선택 해석은 입장 판정을 하지 않는다
            Assert.That(selection.AvailableInTrial, Is.True);
            Assert.That(_progression.TryGetBattle(selection.BattleId, out ProgressionCatalogSO.BossBattleDefinition battle), Is.True);
            Assert.That(battle.TrialPreUnlocked, Is.True);
            Assert.That(battle.Requirement.ClearedBattleId, Is.EqualTo("battle_mucheol_easy"));
        }
    }
}
