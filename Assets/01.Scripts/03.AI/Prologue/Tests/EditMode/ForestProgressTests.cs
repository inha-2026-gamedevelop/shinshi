// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    public class ForestProgressTests
    {
        [Test]
        public void Constructor_TalismanNotChecked_StartsWithInputBlock()
        {
            var progress = new ForestProgress(false);

            Assert.AreEqual(EForestPhase.Talisman, progress.Phase);
            Assert.IsTrue(progress.BlocksInput);
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
        public void CurrentHint_FirstFight_GuidesAttackThenQ()
        {
            var progress = new ForestProgress(true);
            Assert.AreEqual(EForestHint.BasicAttack, progress.CurrentHint);

            progress.OnAttackUsed();
            Assert.AreEqual(EForestHint.SkillQ, progress.CurrentHint);

            progress.OnSkillQUsed();
            Assert.AreEqual(EForestHint.None, progress.CurrentHint);
        }

        [Test]
        public void FirstFight_KilledBothEnemiesButQUnused_StaysUntilQUsed()
        {
            var progress = new ForestProgress(true);
            progress.OnAttackUsed();
            progress.OnEnemyKilled();
            progress.OnEnemyKilled();

            Assert.AreEqual(EForestPhase.FirstFight, progress.Phase);
            Assert.AreEqual(EForestHint.SkillQ, progress.CurrentHint);

            progress.OnSkillQUsed();

            Assert.AreEqual(EForestPhase.SecondFight, progress.Phase);
        }

        [Test]
        public void FirstFight_UsedBothBeforeKills_WaitsForKills()
        {
            var progress = new ForestProgress(true);
            progress.OnAttackUsed();
            progress.OnSkillQUsed();
            progress.OnEnemyKilled();

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

            Assert.AreEqual(EForestHint.None, progress.CurrentHint);
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
            progress.OnSkillQUsed();
            progress.OnEnemyKilled();
            progress.OnEnemyKilled();
            return progress;
        }
    }
}
