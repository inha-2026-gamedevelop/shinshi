// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using NUnit.Framework;
using TMPro;

// 프로젝트 내부
using Shinshi.Editor;
using Shinshi.UI;

namespace Shinshi.Tests
{
    /// <summary> 캐릭터 상세 화면이 편집 값을 실제 CharDetailView 글자에 넣는 흐름과 닫기 이벤트, 정리를 확인한다 </summary>
    public sealed class CharDetailSandboxPageTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int SLOT_COUNT     = 6;
        private const int TRAINING_COUNT = 5;
        private const int STAT_COUNT     = 6;
        private const int SEED_SLOTS     = 3;

        private EconomyTestHarness    _harness;
        private CharDetailSandboxPage _page;
        private CharDetailView        _view;

        /****************************************
        *                Methods
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _harness = new EconomyTestHarness();
            _page    = new CharDetailSandboxPage();
            _harness.AttachAndApply(_page);
            _view = _page.View;
            Assert.NotNull(_view, _page.FixtureNote);
        }

        [TearDown]
        public void TearDown()
        {
            _harness?.Dispose();
            _harness = null;
            _page    = null;
            _view    = null;
        }

        [Test]
        public void Apply_DefaultScenario_ShowsIdentityStatsSlotsTrainingAndTalisman()
        {
            Assert.AreEqual("시험 캐릭터", Text("NameLabel"));
            Assert.AreEqual("직업 0", Text("ClassLabel"));
            Assert.AreEqual("120", Text("StatValue0"));
            Assert.AreEqual("5.0", Text("StatValue5"));
            Assert.AreEqual("낡은 검", Text("SlotName0"));
            Assert.AreEqual("+3", Text("SlotEnhance0"));
            Assert.AreEqual("Lv.3", Text("TrainingValue0"));
            Assert.AreEqual("부적 3개 장착", Text("TalismanLabel"));
        }

        [Test]
        public void Apply_EditedNameAndClass_ReplacesViewText()
        {
            Scenario().SetString(CharDetailSandboxPage.KEY_NAME, "바뀐 이름");
            Scenario().SetString(CharDetailSandboxPage.KEY_CLASS, "직업 4");

            _page.Apply(_harness.Context);

            Assert.AreEqual("바뀐 이름", Text("NameLabel"));
            Assert.AreEqual("직업 4", Text("ClassLabel"));
        }

        [Test]
        public void Apply_StatsNotComputed_BlanksEveryStatLabel()
        {
            Scenario().SetBool(CharDetailSandboxPage.KEY_COMPUTED, false);

            _page.Apply(_harness.Context);

            for (int i = 0; i < STAT_COUNT; ++i)
            {
                Assert.AreEqual(string.Empty, Text("StatValue" + i), "능력치 " + i);
            }

            Assert.AreEqual("시험 캐릭터", Text("NameLabel"));
        }

        [Test]
        public void Apply_DefaultSeedRows_LeavesSlotsAfterTheSeedBlank()
        {
            for (int i = 0; i < SEED_SLOTS; ++i)
            {
                Assert.IsNotEmpty(Text("SlotName" + i), "기본 장비 줄 " + i);
            }

            for (int i = SEED_SLOTS; i < SLOT_COUNT; ++i)
            {
                Assert.AreEqual(string.Empty, Text("SlotName" + i), "빈 장비 줄 " + i);
            }
        }

        [Test]
        public void Apply_FewerRowsThanSlots_BlanksTheRemainingSlotsAndTraining()
        {
            EconomyList.SetCount(Scenario(), CharDetailSandboxPage.SlotSchema, 1);
            EconomyList.SetCount(Scenario(), CharDetailSandboxPage.TrainingSchema, 0);

            _page.Apply(_harness.Context);

            Assert.AreEqual("낡은 검", Text("SlotName0"));
            for (int i = 1; i < SLOT_COUNT; ++i)
            {
                Assert.AreEqual(string.Empty, Text("SlotName" + i), "장비 " + i);
                Assert.AreEqual(string.Empty, Text("SlotEnhance" + i), "강화 " + i);
            }

            for (int i = 0; i < TRAINING_COUNT; ++i)
            {
                Assert.AreEqual(string.Empty, Text("TrainingValue" + i), "수련 " + i);
            }
        }

        [Test]
        public void Apply_FullSlotAndTrainingRows_FillsLastLabels()
        {
            EconomyList.SetCount(Scenario(), CharDetailSandboxPage.SlotSchema, SLOT_COUNT);
            EconomyList.SetCount(Scenario(), CharDetailSandboxPage.TrainingSchema, TRAINING_COUNT);
            EconomyList.SetText(Scenario(), CharDetailSandboxPage.SlotSchema, SLOT_COUNT - 1, "name", "여섯째 장비");
            EconomyList.SetText(Scenario(), CharDetailSandboxPage.TrainingSchema, TRAINING_COUNT - 1, "text", "마지막 수련");

            _page.Apply(_harness.Context);

            Assert.AreEqual("여섯째 장비", Text("SlotName" + (SLOT_COUNT - 1)));
            Assert.AreEqual("마지막 수련", Text("TrainingValue" + (TRAINING_COUNT - 1)));
        }

        [Test]
        public void CloseButtonClick_OpenWindow_ClosesViewAndCountsOnce()
        {
            Assert.IsTrue(_view.IsOpen);

            EconomyTestHarness.Click(EconomyTestHarness.Child<Button>(_view, "CloseButton"));

            Assert.IsFalse(_view.IsOpen);
            Assert.AreEqual(1, _page.ClosedCount);
            StringAssert.Contains("닫혔다", _harness.Context.Status);
        }

        [Test]
        public void Detach_AfterAttach_RemovesClosedSubscriberAndDestroysFixture()
        {
            CharDetailView view = _view;
            GameObject fixtureRoot = view.transform.root.gameObject;
            Assert.AreEqual(1, EconomyTestHarness.SubscriberCount(view, "Closed"));

            _page.Detach();

            Assert.AreEqual(0, EconomyTestHarness.SubscriberCount(view, "Closed"));
            Assert.IsTrue(fixtureRoot == null);
        }

        [Test]
        public void Reattach_AfterDetach_DoesNotDuplicateClosedHandler()
        {
            _page.Detach();
            _harness.AttachAndApply(_page);
            _view = _page.View;
            Assert.NotNull(_view, _page.FixtureNote);

            EconomyTestHarness.Click(EconomyTestHarness.Child<Button>(_view, "CloseButton"));

            Assert.AreEqual(1, EconomyTestHarness.SubscriberCount(_view, "Closed"));
            Assert.AreEqual(1, _page.ClosedCount);
        }

        private UiSandboxScenarioSO Scenario()
        {
            return _harness.Context.Scenario;
        }

        private string Text(string labelName)
        {
            return EconomyTestHarness.Child<TMP_Text>(_view, labelName).text;
        }
    }
}
