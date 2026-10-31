// System
using System;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    public class PrologueDirectorTests
    {
        private FakePrologueSession _session;

        [SetUp]
        public void SetUp()
        {
            _session = new FakePrologueSession();
        }

        private PrologueDirector Create(bool talismanChecked)
        {
            return new PrologueDirector(_session, _session, () => _session.Now, talismanChecked);
        }

        // 부적을 이미 본 캐릭터로 숲을 시작해 두 번째 전투(예고 산귀)까지 보낸다
        private PrologueDirector ReachSecondFight()
        {
            PrologueDirector director = Create(true);
            director.Start(EPrologueStage.Forest, 0);
            _session.RaiseAttack();
            _session.RaiseKilled(0);
            _session.RaiseSkillQ();
            _session.RaiseKilled(1);
            return director;
        }

        [Test]
        public void Start_ForestTalismanChecked_SpawnsFirstEnemyAndGivesThreePotions()
        {
            PrologueDirector director = Create(true);

            director.Start(EPrologueStage.Forest, 0);

            Assert.AreEqual(1, _session.Spawns.Count);
            Assert.AreEqual((EPrologueEnemyKind.Normal, 0), _session.Spawns[0]);
            Assert.AreEqual(3, _session.PotionStock);
        }

        [Test]
        public void Start_ForestTalismanNotChecked_WaitsForConfirm()
        {
            PrologueDirector director = Create(false);
            director.Start(EPrologueStage.Forest, 0);

            // 부적까지 걸어가는 동안은 입력을 막지 않고 산귀도 나오지 않는다
            Assert.AreEqual(0, _session.Spawns.Count);
            Assert.IsFalse(director.BlocksInput);

            _session.RaiseTalismanApproach();
            Assert.IsTrue(director.BlocksInput);
            Assert.AreEqual(0, _session.Spawns.Count);

            _session.RaiseTalisman();

            Assert.AreEqual(1, _session.Spawns.Count);
            Assert.IsFalse(director.BlocksInput);
        }

        [Test]
        public void EnemyKilled_FirstEnemy_SpawnsSecondNormal()
        {
            PrologueDirector director = Create(true);
            director.Start(EPrologueStage.Forest, 0);

            _session.RaiseKilled(0);

            Assert.AreEqual(2, _session.Spawns.Count);
            Assert.AreEqual((EPrologueEnemyKind.Normal, 1), _session.Spawns[1]);
        }

        [Test]
        public void EnemyKilled_BothKilledButSkillsUnused_DoesNotSpawnTelegraph()
        {
            PrologueDirector director = Create(true);
            director.Start(EPrologueStage.Forest, 0);

            _session.RaiseKilled(0);
            _session.RaiseKilled(1);

            Assert.AreEqual(2, _session.Spawns.Count);
            Assert.AreEqual(EForestHint.UseBothOnce, director.CurrentHint);
        }

        [Test]
        public void SkillQUsed_LastRequirementMet_SpawnsTelegraphEnemy()
        {
            PrologueDirector director = Create(true);
            director.Start(EPrologueStage.Forest, 0);
            _session.RaiseAttack();
            _session.RaiseKilled(0);
            _session.RaiseKilled(1);

            _session.RaiseSkillQ();

            Assert.AreEqual(3, _session.Spawns.Count);
            Assert.AreEqual((EPrologueEnemyKind.Telegraph, 0), _session.Spawns[2]);
        }

        [Test]
        public void EnemyKilled_TelegraphEnemy_MovesToWolbaekAndRecordsEntryPotions()
        {
            PrologueDirector director = ReachSecondFight();
            _session.RaisePotionUsed();

            _session.RaiseKilled(0);

            Assert.AreEqual(EPrologueStage.Wolbaek, director.Stage);
            Assert.AreEqual((EPrologueStage.Wolbaek, 2), _session.Reports[_session.Reports.Count - 1]);
        }

        [Test]
        public void PlayerDown_InForest_RevivesWithInvulnerability()
        {
            PrologueDirector director = Create(true);
            director.Start(EPrologueStage.Forest, 0);

            _session.RaisePlayerDown();

            Assert.AreEqual(1, _session.Revives.Count);
            Assert.AreEqual(3f, _session.Revives[0]);
            Assert.AreEqual(EPrologueStage.Forest, director.Stage);
        }

        [Test]
        public void PlayerDown_InForest_ShowsReviveNoticeOnlyDuringTheInvulnerableTime()
        {
            PrologueDirector director = Create(true);
            director.Start(EPrologueStage.Forest, 0);
            Assert.IsFalse(director.IsReviveNoticeActive);

            _session.RaisePlayerDown();
            Assert.IsTrue(director.IsReviveNoticeActive);

            _session.Now += 2.9;
            Assert.IsTrue(director.IsReviveNoticeActive);

            _session.Now += 0.2;
            Assert.IsFalse(director.IsReviveNoticeActive);
        }

        [Test]
        public void PlayerDown_InWolbaek_NeverShowsReviveNotice()
        {
            PrologueDirector director = ReachSecondFight();
            _session.RaiseKilled(0);
            _session.RaiseWolbaekIntro();
            _session.RaiseWolbaekControl();

            _session.RaisePlayerDown();

            Assert.IsFalse(director.IsReviveNoticeActive);
        }

        [Test]
        public void Tick_At30SecondsAfterControl_EndsAsTimeUpAndMovesToEnding()
        {
            PrologueDirector director = ReachSecondFight();
            _session.RaiseKilled(0);
            _session.RaiseWolbaekIntro();
            _session.RaiseWolbaekControl();

            _session.Now += 29.0;
            director.Tick();
            Assert.AreEqual(0, _session.Ends.Count);

            _session.Now += 1.0;
            director.Tick();

            Assert.AreEqual(1, _session.Ends.Count);
            Assert.AreEqual(EWolbaekEnd.TimeUp, _session.Ends[0]);
            Assert.AreEqual(EPrologueStage.Ending, director.Stage);
        }

        [Test]
        public void PlayerDown_InWolbaek_EndsWithoutRevive()
        {
            PrologueDirector director = ReachSecondFight();
            _session.RaiseKilled(0);
            _session.RaiseWolbaekIntro();
            _session.RaiseWolbaekControl();

            _session.Now += 5.0;
            _session.RaisePlayerDown();

            Assert.AreEqual(0, _session.Revives.Count);
            Assert.AreEqual(EWolbaekEnd.PlayerDown, _session.Ends[0]);
            Assert.AreEqual(EPrologueStage.Ending, director.Stage);
        }

        [Test]
        public void Tick_BeforeControlStarted_DoesNotEndWolbaek()
        {
            PrologueDirector director = ReachSecondFight();
            _session.RaiseKilled(0);

            _session.Now += 100.0;
            director.Tick();

            Assert.AreEqual(0, _session.Ends.Count);
            Assert.AreEqual(EPrologueStage.Wolbaek, director.Stage);
        }

        [Test]
        public void WolbaekIntroFinished_AfterForest_OpensWolbaekOnce()
        {
            PrologueDirector director = ReachSecondFight();
            _session.RaiseKilled(0);
            Assert.AreEqual(0, _session.WolbaekStarts);

            _session.RaiseWolbaekIntro();
            _session.RaiseWolbaekIntro();

            Assert.AreEqual(1, _session.WolbaekStarts);
        }

        [Test]
        public void WolbaekIntroFinished_BeforeWolbaekStage_IsIgnored()
        {
            PrologueDirector director = Create(true);
            director.Start(EPrologueStage.Forest, 0);

            _session.RaiseWolbaekIntro();

            Assert.AreEqual(0, _session.WolbaekStarts);
        }

        [Test]
        public void WolbaekControlStarted_BeforeIntroFinished_DoesNotStartTimer()
        {
            PrologueDirector director = ReachSecondFight();
            _session.RaiseKilled(0);

            _session.RaiseWolbaekControl();
            _session.Now += 100.0;
            director.Tick();

            Assert.AreEqual(0, _session.Ends.Count);
            Assert.AreEqual(EPrologueStage.Wolbaek, director.Stage);
        }

        [Test]
        public void Start_ResumeWolbaek_ThenIntroFinished_OpensWolbaek()
        {
            PrologueDirector director = Create(true);
            director.Start(EPrologueStage.Wolbaek, 2);

            _session.RaiseWolbaekIntro();

            Assert.AreEqual(1, _session.WolbaekStarts);
        }

        [Test]
        public void Start_ResumeWolbaek_RestoresStoredPotions()
        {
            PrologueDirector director = Create(true);

            director.Start(EPrologueStage.Wolbaek, 2);

            Assert.AreEqual(2, _session.PotionStock);
            Assert.AreEqual(2, director.PotionCount);
            Assert.AreEqual(0, _session.Spawns.Count);
        }

        [Test]
        public void IllustrationFinished_AtIntro_StartsForest()
        {
            PrologueDirector director = Create(true);
            director.Start(EPrologueStage.Intro, 0);

            _session.RaiseIllustration();

            Assert.AreEqual(EPrologueStage.Forest, director.Stage);
            Assert.AreEqual((EPrologueStage.Forest, 0), _session.Reports[0]);
            Assert.AreEqual(1, _session.Spawns.Count);
        }

        [Test]
        public void IllustrationFinished_AtEnding_CompletesAndClearsPotions()
        {
            PrologueDirector director = Create(true);
            director.Start(EPrologueStage.Ending, 0);

            _session.RaiseIllustration();

            Assert.AreEqual(EPrologueStage.Done, director.Stage);
            Assert.AreEqual(0, _session.PotionStock);
            Assert.AreEqual((EPrologueStage.Done, 0), _session.Reports[_session.Reports.Count - 1]);
        }

        [Test]
        public void Complete_CalledTwice_ReportsOnce()
        {
            PrologueDirector director = Create(true);
            director.Start(EPrologueStage.Ending, 0);

            director.Complete();
            director.Complete();

            Assert.AreEqual(1, _session.Reports.Count);
        }

        [Test]
        public void Dispose_Called_UnsubscribesFromEvents()
        {
            PrologueDirector director = Create(true);

            director.Dispose();

            Assert.IsFalse(_session.HasSubscribers);
        }

        [Test]
        public void Constructor_NullArguments_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new PrologueDirector(null, _session, () => 0.0, true));
            Assert.Throws<ArgumentNullException>(() => new PrologueDirector(_session, null, () => 0.0, true));
            Assert.Throws<ArgumentNullException>(() => new PrologueDirector(_session, _session, null, true));
        }
    }
}
