using System;
using System.Reflection;
using NUnit.Framework;
using Shinshi.Combat;
using Shinshi.Editor;
using Shinshi.UI;
using TMPro;

namespace Shinshi.Tests
{
    public sealed class HudCombatMeterPageTests
    {
        private const string FIELD_SELF_SLOT_ROWS = "_selfSlotRows";
        private const string PROPERTY_HITS        = "Hits";
        private const string PROPERTY_DAMAGE      = "Damage";

        private PresentationTestHarness _harness;
        private HudCombatMeterPage      _page;

        [SetUp]
        public void SetUp()
        {
            _harness = new PresentationTestHarness();
            _page    = _harness.Track(new HudCombatMeterPage());
            _page.Attach(_harness.Context);
            _page.Apply(_harness.Context);
        }

        [TearDown]
        public void TearDown()
        {
            _harness.Dispose();
        }

        private static string ReadSlotText(object view, int index, string property)
        {
            FieldInfo rowsField = view.GetType().GetField(FIELD_SELF_SLOT_ROWS, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(rowsField, FIELD_SELF_SLOT_ROWS);

            Array  rows = (Array)rowsField.GetValue(view);
            object row  = rows.GetValue(index);
            Assert.IsNotNull(row, "슬롯 행 " + index);

            PropertyInfo textProperty = row.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(textProperty, property);

            TMP_Text text = textProperty.GetValue(row) as TMP_Text;
            Assert.IsNotNull(text, property + " 텍스트 연결 " + index);
            return text.text;
        }

        /****************************************
        *               Slot count
        ****************************************/

        [Test]
        public void SlotCount_Constant_CoversEveryCombatMeterSlot()
        {
            Assert.AreEqual(8, HudCombatMeterPage.SLOT_COUNT);
            Assert.AreEqual(Enum.GetValues(typeof(ECombatMeterSlot)).Length, HudCombatMeterPage.SLOT_COUNT);
        }

        [Test]
        public void Attach_Default_BuildsMeterAndResultWithEightSlotRows()
        {
            Assert.IsFalse(_page.IsRefused, _page.RefusalReason);
            Assert.IsNotNull(_page.MeterView);
            Assert.IsNotNull(_page.ResultView);
            Assert.AreEqual(HudCombatMeterPage.SLOT_COUNT, _page.MeterView.SelfSlotCount);
            Assert.AreEqual(HudCombatMeterPage.SLOT_COUNT, _page.ResultView.SelfSlotCount);
            Assert.AreEqual(HudCombatMeterPage.MEMBER_COUNT, _page.MeterView.PartyRowCount);
            Assert.AreEqual(2, _page.Fixtures.Count);
        }

        [Test]
        public void Apply_SeededValues_ReachEveryOneOfEightSlotRowsInBothViews()
        {
            for (int i = 0; i < HudCombatMeterPage.SLOT_COUNT; ++i)
            {
                string expectedHits = (120 - (i * 10)).ToString();

                Assert.AreEqual(expectedHits, ReadSlotText(_page.MeterView, i, PROPERTY_HITS), "미터 슬롯 " + i);
                Assert.AreEqual(expectedHits, ReadSlotText(_page.ResultView, i, PROPERTY_HITS), "결과 슬롯 " + i);
            }
        }

        [Test]
        public void Apply_EditedLastSlot_ReachesEighthRowOfBothViews()
        {
            int last = HudCombatMeterPage.SLOT_COUNT - 1;
            _harness.Scenario.SetInt(HudCombatMeterPage.Key("slot", last, "hits"), 777);
            _harness.Scenario.SetFloat(HudCombatMeterPage.Key("slot", last, "damage"), 1234.0f);

            _page.Apply(_harness.Context);

            string expectedDamage = CombatMeterViewRules.FormatDamage(1234.0f);
            Assert.AreEqual("777", ReadSlotText(_page.MeterView, last, PROPERTY_HITS));
            Assert.AreEqual("777", ReadSlotText(_page.ResultView, last, PROPERTY_HITS));
            Assert.AreEqual(expectedDamage, ReadSlotText(_page.MeterView, last, PROPERTY_DAMAGE));
            Assert.AreEqual(expectedDamage, ReadSlotText(_page.ResultView, last, PROPERTY_DAMAGE));
        }

        [Test]
        public void Seed_Default_CreatesKeysForEverySlot()
        {
            for (int i = 0; i < HudCombatMeterPage.SLOT_COUNT; ++i)
            {
                Assert.IsTrue(_harness.Scenario.Contains(HudCombatMeterPage.Key("slot", i, "damage")), "damage " + i);
                Assert.IsTrue(_harness.Scenario.Contains(HudCombatMeterPage.Key("slot", i, "hits")), "hits " + i);
                Assert.IsTrue(_harness.Scenario.Contains(HudCombatMeterPage.Key("slot", i, "maxHit")), "maxHit " + i);
            }

            Assert.IsFalse(_harness.Scenario.Contains(HudCombatMeterPage.Key("slot", HudCombatMeterPage.SLOT_COUNT, "hits")));
        }

        [Test]
        public void Requirement_Default_NamesAllEightSlots()
        {
            StringAssert.Contains("8칸", _page.Requirement);
            StringAssert.Contains("기본 공격", _page.Requirement);
            StringAssert.Contains("소환물", _page.Requirement);
            StringAssert.Contains("추가 기술", _page.Requirement);
            StringAssert.Contains("기타", _page.Requirement);
        }

        /****************************************
        *            Events / Detach
        ****************************************/

        [Test]
        public void Attach_Default_SubscribesPhaseAndSelfRowEvents()
        {
            Assert.AreEqual(1, PresentationTestHarness.SubscriberCount(_page.MeterView, "PhaseToggleClicked"));
            Assert.AreEqual(1, PresentationTestHarness.SubscriberCount(_page.MeterView, "SelfRowClicked"));
            Assert.AreEqual(1, PresentationTestHarness.SubscriberCount(_page.ResultView, "SelfRowClicked"));
        }

        [Test]
        public void PhaseToggleClicked_RealEvent_FlipsPhaseModeEachClick()
        {
            bool before = _harness.Scenario.GetBool(HudCombatMeterPage.KEY_PHASE_MODE);

            PresentationTestHarness.RaiseEvent(_page.MeterView, "PhaseToggleClicked");
            Assert.AreEqual(!before, _harness.Scenario.GetBool(HudCombatMeterPage.KEY_PHASE_MODE));

            PresentationTestHarness.RaiseEvent(_page.MeterView, "PhaseToggleClicked");
            Assert.AreEqual(before, _harness.Scenario.GetBool(HudCombatMeterPage.KEY_PHASE_MODE));
        }

        [Test]
        public void SelfRowClicked_MeterAndResult_ToggleTheirOwnScenarioKeys()
        {
            bool breakdown = _harness.Scenario.GetBool(HudCombatMeterPage.KEY_BREAKDOWN);
            bool detail    = _harness.Scenario.GetBool(HudCombatMeterPage.KEY_SELF_DETAIL);

            PresentationTestHarness.RaiseEvent(_page.MeterView, "SelfRowClicked");
            Assert.AreEqual(!breakdown, _harness.Scenario.GetBool(HudCombatMeterPage.KEY_BREAKDOWN));
            Assert.AreEqual(detail, _harness.Scenario.GetBool(HudCombatMeterPage.KEY_SELF_DETAIL));

            PresentationTestHarness.RaiseEvent(_page.ResultView, "SelfRowClicked");
            Assert.AreEqual(!detail, _harness.Scenario.GetBool(HudCombatMeterPage.KEY_SELF_DETAIL));
        }

        [Test]
        public void Detach_AfterAttach_UnsubscribesAllEventsAndDestroysFixtures()
        {
            HudCombatMeterView       meter  = _page.MeterView;
            HudCombatMeterResultView result = _page.ResultView;

            _page.Detach();

            Assert.AreEqual(0, PresentationTestHarness.SubscriberCount(meter, "PhaseToggleClicked"));
            Assert.AreEqual(0, PresentationTestHarness.SubscriberCount(meter, "SelfRowClicked"));
            Assert.AreEqual(0, PresentationTestHarness.SubscriberCount(result, "SelfRowClicked"));
            Assert.IsNull(_page.MeterView);
            Assert.IsNull(_page.ResultView);
            Assert.AreEqual(0, PresentationTestHarness.CountFixtureCanvases());
        }

        [Test]
        public void PhaseToggleClicked_AfterDetach_DoesNotChangeScenario()
        {
            HudCombatMeterView meter = _page.MeterView;
            bool before = _harness.Scenario.GetBool(HudCombatMeterPage.KEY_PHASE_MODE);

            _page.Detach();
            PresentationTestHarness.RaiseEvent(meter, "PhaseToggleClicked");

            Assert.AreEqual(before, _harness.Scenario.GetBool(HudCombatMeterPage.KEY_PHASE_MODE));
        }
    }
}
