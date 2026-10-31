// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    public class ForestProgressTests
    {
        [Test]
        public void Constructor_TalismanNotChecked_WaitsForApproachWithoutBlockingInput()
        {
            var progress = new ForestProgress(false);

            Assert.AreEqual(EForestPhase.Talisman, progress.Phase);
            Assert.IsFalse(progress.BlocksInput);
        }

        [Test]
        public void CurrentHint_WalkingToTalisman_IsMoveThenNoneWhileScreenOpen()
        {
            var progress = new ForestProgress(false);
            Assert.AreEqual(EForestHint.Move, progress.CurrentHint);

            progress.OnTalismanApproached();
            Assert.AreEqual(EForestHint.None, progress.CurrentHint);

            progress.OnTalismanConfirmed();
            Assert.AreEqual(EForestHint.BasicAttack, progress.CurrentHint);
        }

        [Test]
        public void OnTalismanApproached_BlocksInputUntilConfirmed()
        {
            var progress = new ForestProgress(false);

            progress.OnTalismanApproached();
            Assert.IsTrue(progress.BlocksInput);

            progress.OnTalismanConfirmed();
            Assert.IsFalse(progress.BlocksInput);
            Assert.AreEqual(EForestPhase.FirstFight, progress.Phase);
        }

        [Test]
        public void OnTalismanApproached_AlreadyChecked_DoesNotBlock()
        {
            var progress = new ForestProgress(true);

            progress.OnTalismanApproached();

            Assert.IsFalse(progress.BlocksInput);
        }

        [Test]
        public void Constructor_TalismanAlreadyChecked_SkipsToFirstFight()
        {
            var progress = new ForestProgress(true);

            Assert.AreEqual(EForestPhase.FirstFight, progress.Phase);
            Assert.IsFalse(progress.BlocksInput);
        }

        [Test]
        public void OnTalismanConfirmed_MovesToFirstFight()
        {
            var progress = new ForestProgress(false);

            progress.OnTalismanConfirmed();

            Assert.AreEqual(EForestPhase.FirstFight, progress.Phase);
        }

        [Test]
        public void CurrentHint_FirstFight_AttackThenQAtSecondEnemyThenKillRemaining()
        {
            var progress = new ForestProgress(true);
            Assert.AreEqual(EForestHint.BasicAttack, progress.CurrentHint);

            // 기본 공격을 썼지만 두 번째 산귀가 아직 안 나왔으면 Q 안내 대신 남은 산귀 처치다
            progress.OnAttackUsed();
            Assert.AreEqual(EForestHint.KillRemaining, progress.CurrentHint);

            // 첫 산귀를 잡아 두 번째가 나오면 Q를 안내한다
            progress.OnEnemyKilled();
            Assert.AreEqual(EForestHint.SkillQ, progress.CurrentHint);

            progress.OnSkillQUsed();
            Assert.AreEqual(EForestHint.KillRemaining, progress.CurrentHint);
        }

        [Test]
        public void FirstFight_KilledBothEnemiesButQUnused_ShowsUseSkillOnceUntilQUsed()
        {
            var progress = new ForestProgress(true);
            progress.OnAttackUsed();
            progress.OnEnemyKilled();
            progress.OnEnemyKilled();

            Assert.AreEqual(EForestPhase.FirstFight, progress.Phase);
            Assert.AreEqual(EForestHint.UseSkillOnce, progress.CurrentHint);

            progress.OnSkillQUsed();

            Assert.AreEqual(EForestPhase.SecondFight, progress.Phase);
        }

        [Test]
        public void FirstFight_KilledBothEnemiesButAttackUnused_ShowsUseAttackOnce()
        {
            var progress = new ForestProgress(true);
            progress.OnEnemyKilled();
            progress.OnEnemyKilled();

            // 둘 다 남았다는 안내가 뜬 동안에는 Q를 먼저 써도 인정된다
            progress.OnSkillQUsed();

            Assert.AreEqual(EForestHint.UseAttackOnce, progress.CurrentHint);
        }

        [Test]
        public void OnSkillQUsed_BeforeSkillQHint_IsIgnored()
        {
            var progress = new ForestProgress(true);

            // 기본 공격 안내 중에 쓴 Q는 인정되지 않는다
            progress.OnSkillQUsed();
            progress.OnAttackUsed();
            progress.OnEnemyKilled();

            Assert.AreEqual(EForestHint.SkillQ, progress.CurrentHint);
        }

        [Test]
        public void OnAttackUsed_AfterAttackAlreadyUsed_DoesNotChangeHint()
        {
            var progress = new ForestProgress(true);
            progress.OnAttackUsed();

            progress.OnAttackUsed();

            Assert.AreEqual(EForestHint.KillRemaining, progress.CurrentHint);
        }

        [Test]
        public void FirstFight_KilledBothEnemiesWithoutUsingEither_ShowsUseBothOnce()
        {
            var progress = new ForestProgress(true);
            progress.OnEnemyKilled();
            progress.OnEnemyKilled();

            Assert.AreEqual(EForestHint.UseBothOnce, progress.CurrentHint);

            progress.OnAttackUsed();
            Assert.AreEqual(EForestHint.UseSkillOnce, progress.CurrentHint);
        }

        [Test]
        public void FirstFight_UsedBothBeforeLastKill_WaitsForKills()
        {
            var progress = new ForestProgress(true);
            progress.OnAttackUsed();
            progress.OnEnemyKilled();
            progress.OnSkillQUsed();

            Assert.AreEqual(EForestPhase.FirstFight, progress.Phase);

            progress.OnEnemyKilled();

            Assert.AreEqual(EForestPhase.SecondFight, progress.Phase);
        }

        [Test]
        public void OnAttackUsed_DuringTalisman_IsIgnored()
        {
            var progress = new ForestProgress(false);

            progress.OnAttackUsed();
            progress.OnTalismanConfirmed();

            Assert.AreEqual(EForestHint.BasicAttack, progress.CurrentHint);
        }

        [Test]
        public void SecondFight_TelegraphStarted_ShowsLeaveDangerHint()
        {
            var progress = ReachSecondFight();

            progress.OnTelegraphStarted();

            Assert.IsTrue(progress.TelegraphSeen);
            Assert.AreEqual(EForestHint.LeaveDangerZone, progress.CurrentHint);

            progress.OnTelegraphEnded();

            // 예고가 끝나면 다시 예고형 산귀와 싸우는 안내로 돌아온다
            Assert.AreEqual(EForestHint.FightTelegraphEnemy, progress.CurrentHint);
        }

        [Test]
        public void SecondFight_BeforeAnyTelegraph_ShowsFightTelegraphEnemy()
        {
            var progress = ReachSecondFight();

            Assert.AreEqual(EForestHint.FightTelegraphEnemy, progress.CurrentHint);
        }

        [Test]
        public void SecondFight_KilledAfterTelegraph_Done()
        {
            var progress = ReachSecondFight();
            progress.OnTelegraphStarted();

            progress.OnEnemyKilled();

            Assert.IsTrue(progress.IsDone);
            Assert.AreEqual(EForestHint.None, progress.CurrentHint);
        }

        [Test]
        public void SecondFight_KilledBeforeTelegraph_DoneWithoutLearning()
        {
            var progress = ReachSecondFight();

            progress.OnEnemyKilled();

            Assert.IsTrue(progress.IsDone);
            Assert.IsFalse(progress.TelegraphSeen);
        }

        private static ForestProgress ReachSecondFight()
        {
            var progress = new ForestProgress(true);
            progress.OnAttackUsed();
            progress.OnEnemyKilled();
            progress.OnSkillQUsed();
            progress.OnEnemyKilled();
            return progress;
        }
    }
}
