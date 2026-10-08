using System.Collections.Generic;
using NUnit.Framework;
using Shinshi.Editor;
using Shinshi.UI;

namespace Shinshi.Tests
{
    public sealed class CharacterSelectPageTests
    {
        private PresentationTestHarness _harness;
        private CharacterSelectPage     _page;

        [SetUp]
        public void SetUp()
        {
            _harness = new PresentationTestHarness();
            _page    = _harness.Track(new CharacterSelectPage());
            _page.Attach(_harness.Context);
        }

        [TearDown]
        public void TearDown()
        {
            _harness.Dispose();
        }

        /****************************************
        *            Attach / Detach
        ****************************************/

        [Test]
        public void Attach_Default_BuildsOnlySelectionView()
        {
            Assert.IsFalse(_page.IsRefused, _page.RefusalReason);
            Assert.IsNotNull(_page.Selection);
            Assert.IsTrue(_page.UsesFixture);
            Assert.AreEqual(1, _page.Fixtures.Count);
            Assert.IsTrue(_page.Selection.isActiveAndEnabled);
            Assert.AreEqual(CharacterSelectSelection.NO_SELECTION, _page.Selection.SelectedSlot);
        }

        [Test]
        public void Attach_Default_SubscribesBothEventsOnce()
        {
            Assert.AreEqual(1, PresentationTestHarness.SubscriberCount(_page.Selection, "SelectionChanged"));
            Assert.AreEqual(1, PresentationTestHarness.SubscriberCount(_page.Selection, "HoverChanged"));
        }

        [Test]
        public void Detach_AfterAttach_UnsubscribesAndDestroysFixture()
        {
            CharacterSelectSelection selection = _page.Selection;

            _page.Detach();

            Assert.AreEqual(0, PresentationTestHarness.SubscriberCount(selection, "SelectionChanged"));
            Assert.AreEqual(0, PresentationTestHarness.SubscriberCount(selection, "HoverChanged"));
            Assert.IsNull(_page.Selection);
            Assert.AreEqual(0, PresentationTestHarness.CountFixtureCanvases());
        }

        [Test]
        public void Attach_UserValueExists_DoesNotOverwriteSeed()
        {
            _page.Detach();
            _harness.Scenario.SetString(CharacterSelectPage.SlotKey(0, "name"), "사용자");

            _page.Attach(_harness.Context);

            Assert.AreEqual("사용자", _harness.Scenario.GetString(CharacterSelectPage.SlotKey(0, "name")));
            Assert.AreEqual("마검사", _harness.Scenario.GetString(CharacterSelectPage.SlotKey(1, "name")));
        }

        /****************************************
        *       Selection <-> Scenario sync
        ****************************************/

        [Test]
        public void Select_ViewCall_WritesChosenSlotIntoScenario()
        {
            _page.Selection.Select(2);

            Assert.AreEqual(2, _page.Selection.SelectedSlot);
            Assert.AreEqual(2, _harness.Scenario.GetInt(CharacterSelectPage.KEY_SELECTED));
        }

        [Test]
        public void Hover_ViewCall_WritesHoveredSlotIntoScenario()
        {
            _page.Selection.Hover(1);
            Assert.AreEqual(1, _harness.Scenario.GetInt(CharacterSelectPage.KEY_HOVERED));

            _page.Selection.Unhover(1);
            Assert.AreEqual(CharacterSelectSelection.NO_SELECTION, _harness.Scenario.GetInt(CharacterSelectPage.KEY_HOVERED));
        }

        [Test]
        public void Apply_ScenarioSelectedSlot_SelectsInView()
        {
            _harness.Scenario.SetInt(CharacterSelectPage.KEY_SELECTED, 3);

            _page.Apply(_harness.Context);

            Assert.AreEqual(3, _page.Selection.SelectedSlot);
        }

        [Test]
        public void Apply_ScenarioHoveredSlot_HoversThenUnhoversInView()
        {
            _harness.Scenario.SetInt(CharacterSelectPage.KEY_HOVERED, 2);
            _page.Apply(_harness.Context);
            Assert.AreEqual(2, _page.Selection.HoveredSlot);

            _harness.Scenario.SetInt(CharacterSelectPage.KEY_HOVERED, CharacterSelectSelection.NO_SELECTION);
            _page.Apply(_harness.Context);
            Assert.AreEqual(CharacterSelectSelection.NO_SELECTION, _page.Selection.HoveredSlot);
        }

        /****************************************
        *                 Reset
        ****************************************/

        [Test]
        public void Apply_ScenarioResetToNoSelection_ClearsExistingViewSelection()
        {
            _page.Selection.Select(2);
            Assert.AreEqual(2, _page.Selection.SelectedSlot);

            _harness.Scenario.SetInt(CharacterSelectPage.KEY_SELECTED, CharacterSelectSelection.NO_SELECTION);
            _page.Apply(_harness.Context);

            Assert.AreEqual(CharacterSelectSelection.NO_SELECTION, _page.Selection.SelectedSlot);
            Assert.AreEqual(CharacterSelectSelection.NO_SELECTION, _harness.Scenario.GetInt(CharacterSelectPage.KEY_SELECTED));
        }

        [Test]
        public void Apply_ScenarioResetToNoSelection_RaisesSelectionChangedToOtherSubscribers()
        {
            List<int> received = new List<int>();
            _page.Selection.SelectionChanged += received.Add;
            _page.Selection.Select(1);
            received.Clear();

            _harness.Scenario.SetInt(CharacterSelectPage.KEY_SELECTED, CharacterSelectSelection.NO_SELECTION);
            _page.Apply(_harness.Context);

            Assert.AreEqual(1, received.Count);
            Assert.AreEqual(CharacterSelectSelection.NO_SELECTION, received[0]);
        }

        [Test]
        public void Apply_ScenarioResetToNoSelection_ClearsHoverAndRaisesHoverChanged()
        {
            List<int> received = new List<int>();
            _page.Selection.HoverChanged += received.Add;
            _page.Selection.Select(1);
            _page.Selection.Hover(3);
            received.Clear();

            _harness.Scenario.SetInt(CharacterSelectPage.KEY_SELECTED, CharacterSelectSelection.NO_SELECTION);
            _harness.Scenario.SetInt(CharacterSelectPage.KEY_HOVERED, CharacterSelectSelection.NO_SELECTION);
            _page.Apply(_harness.Context);

            Assert.AreEqual(CharacterSelectSelection.NO_SELECTION, _page.Selection.HoveredSlot);
            Assert.IsTrue(received.Contains(CharacterSelectSelection.NO_SELECTION));
        }

        [Test]
        public void Apply_ResetWhileHoverKept_PreservesHoveredSlot()
        {
            _page.Selection.Select(1);
            _page.Selection.Hover(3);

            _harness.Scenario.SetInt(CharacterSelectPage.KEY_SELECTED, CharacterSelectSelection.NO_SELECTION);
            _page.Apply(_harness.Context);

            Assert.AreEqual(CharacterSelectSelection.NO_SELECTION, _page.Selection.SelectedSlot);
            Assert.AreEqual(3, _page.Selection.HoveredSlot);
        }

        [Test]
        public void Apply_NothingSelectedAndNoSelectionScenario_RaisesNoEvents()
        {
            int count = 0;
            _page.Selection.SelectionChanged += slot => ++count;
            _page.Selection.HoverChanged     += slot => ++count;

            _page.Apply(_harness.Context);
            _page.Apply(_harness.Context);

            Assert.AreEqual(0, count);
        }

        [Test]
        public void Apply_ResetThenSelectAgain_SelectsNewSlot()
        {
            _page.Selection.Select(2);
            _harness.Scenario.SetInt(CharacterSelectPage.KEY_SELECTED, CharacterSelectSelection.NO_SELECTION);
            _page.Apply(_harness.Context);

            _harness.Scenario.SetInt(CharacterSelectPage.KEY_SELECTED, 2);
            _page.Apply(_harness.Context);

            Assert.AreEqual(2, _page.Selection.SelectedSlot);
        }

        /****************************************
        *           Requirement wording
        ****************************************/

        [Test]
        public void Requirement_Default_StatesUnverifiedClickHighlightAndCamera()
        {
            StringAssert.Contains("검증하지 않는다", _page.Requirement);
            StringAssert.Contains("CharacterSelectSlotClick", _page.Requirement);
            StringAssert.Contains("CharacterSelectListHighlight", _page.Requirement);
            StringAssert.Contains("CharacterSelectCameraFocus", _page.Requirement);
            StringAssert.Contains("화면 미반영", _page.Requirement);
        }

        [Test]
        public void Requirement_Default_DoesNotBlameServerCallsForMissingParts()
        {
            StringAssert.Contains("서버 호출 때문이 아니다", _page.Requirement);
        }
    }
}
