// System
using System;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    public class PrologueHintTextsTests
    {
        [Test]
        public void None_HasNeitherObjectiveNorInstruction()
        {
            Assert.AreEqual(string.Empty, PrologueHintTexts.ObjectiveFor(EForestHint.None));
            Assert.AreEqual(string.Empty, PrologueHintTexts.InstructionFor(EForestHint.None));
        }

        [Test]
        public void Move_UsesRightMouseButtonAndWalkObjective()
        {
            Assert.AreEqual("숲길 따라 이동하기", PrologueHintTexts.ObjectiveFor(EForestHint.Move));
            Assert.AreEqual("이동할 곳을 마우스 오른쪽 버튼으로 클릭하세요.", PrologueHintTexts.InstructionFor(EForestHint.Move));
        }

        [Test]
        public void BasicAttack_UsesLeftMouseButton()
        {
            Assert.AreEqual("기본 공격 사용하기", PrologueHintTexts.ObjectiveFor(EForestHint.BasicAttack));
            Assert.AreEqual("적을 향해 마우스를 두고 왼쪽 버튼을 클릭하세요.", PrologueHintTexts.InstructionFor(EForestHint.BasicAttack));
        }

        [Test]
        public void SkillQ_NamesTheQKey()
        {
            Assert.AreEqual("Q 기술 사용하기", PrologueHintTexts.ObjectiveFor(EForestHint.SkillQ));
            Assert.AreEqual("적을 향해 마우스를 두고 [Q]를 눌러 기술을 사용하세요.", PrologueHintTexts.InstructionFor(EForestHint.SkillQ));
        }

        [Test]
        public void MissedActionVariants_MatchTheDraftWording()
        {
            Assert.AreEqual("마우스 왼쪽 버튼을 클릭해 기본 공격을 한 번 사용하세요.", PrologueHintTexts.InstructionFor(EForestHint.UseAttackOnce));
            Assert.AreEqual("[Q] 기술을 한 번 사용하세요. 적에게 맞히지 않아도 됩니다.", PrologueHintTexts.InstructionFor(EForestHint.UseSkillOnce));
            Assert.AreEqual("기본 공격과 [Q] 기술을 한 번씩 사용하세요.", PrologueHintTexts.InstructionFor(EForestHint.UseBothOnce));
        }

        [Test]
        public void TelegraphFight_KeepsTheSameObjectiveWhileInstructionChanges()
        {
            Assert.AreEqual("앞을 막은 산귀 처치하기", PrologueHintTexts.ObjectiveFor(EForestHint.FightTelegraphEnemy));
            Assert.AreEqual("앞을 막은 산귀 처치하기", PrologueHintTexts.ObjectiveFor(EForestHint.LeaveDangerZone));
            Assert.AreEqual("적의 공격을 살피며 싸우세요.", PrologueHintTexts.InstructionFor(EForestHint.FightTelegraphEnemy));
            Assert.AreEqual("공격 예고가 표시된 곳에서 벗어나세요.", PrologueHintTexts.InstructionFor(EForestHint.LeaveDangerZone));
        }

        [Test]
        public void KillRemaining_UsesDraftWording()
        {
            Assert.AreEqual("남은 산귀 처치하기", PrologueHintTexts.ObjectiveFor(EForestHint.KillRemaining));
            Assert.AreEqual("기본 공격과 기술로 남은 산귀를 처치하세요.", PrologueHintTexts.InstructionFor(EForestHint.KillRemaining));
        }

        [Test]
        public void PotionInstruction_DoesNotNameAnyKeyUntilTheTemporaryPotionIsConfirmed()
        {
            Assert.AreEqual("물약으로 체력을 회복하세요.", PrologueHintTexts.POTION_INSTRUCTION);
            StringAssert.DoesNotContain("1", PrologueHintTexts.POTION_INSTRUCTION);
            StringAssert.DoesNotContain("[", PrologueHintTexts.POTION_INSTRUCTION);
        }

        [Test]
        public void EveryHintExceptNone_HasAnInstruction()
        {
            foreach (EForestHint hint in Enum.GetValues(typeof(EForestHint)))
            {
                if (hint == EForestHint.None)
                {
                    continue;
                }

                Assert.IsFalse(string.IsNullOrEmpty(PrologueHintTexts.InstructionFor(hint)), hint.ToString());
            }
        }

        [Test]
        public void NoText_ContainsUnresolvedPlaceholder()
        {
            foreach (EForestHint hint in Enum.GetValues(typeof(EForestHint)))
            {
                StringAssert.DoesNotContain("{", PrologueHintTexts.ObjectiveFor(hint));
                StringAssert.DoesNotContain("{", PrologueHintTexts.InstructionFor(hint));
            }
        }

        [Test]
        public void ResumeNotice_AddsStageSpecificLineOnlyForForestAndWolbaek()
        {
            Assert.AreEqual("중단한 구간의 처음부터 다시 시작합니다.", PrologueHintTexts.ResumeNotice(EPrologueStage.Intro));
            StringAssert.Contains("튜토리얼 물약이 3개로 준비되었습니다.", PrologueHintTexts.ResumeNotice(EPrologueStage.Forest));
            StringAssert.Contains("체력과 물약이 교전 시작 상태로 복원되었습니다.", PrologueHintTexts.ResumeNotice(EPrologueStage.Wolbaek));
        }

        [Test]
        public void WolbaekTexts_AreTheConfirmedObjectiveAndTheDraftInstruction()
        {
            Assert.AreEqual("월백의 공격에 맞서기", PrologueHintTexts.WOLBAEK_OBJECTIVE);
            Assert.AreEqual("기본 공격과 기술을 사용하며 공격 예고에 대응하세요.", PrologueHintTexts.WOLBAEK_INSTRUCTION);
        }
    }
}
