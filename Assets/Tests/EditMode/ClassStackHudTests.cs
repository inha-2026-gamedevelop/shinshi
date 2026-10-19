// System
using System.Reflection;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using NUnit.Framework;
using TMPro;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.UI;

namespace Shinshi.Tests
{
    public sealed class ClassStackHudTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    MAX_STACKS        = 20;
        private const int    PARTIAL_STACKS    = 12;
        private const int    OVERFLOW_STACKS   = 25;
        private const string STACK_OBJECT_NAME = "ClassStackDisplay";

        private GameObject _root;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("ClassStackHudTest", typeof(RectTransform));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        /****************************************
        *                Tests
        ****************************************/

        [TestCase(EPlayerClass.Mageknight, true)]
        [TestCase(EPlayerClass.Archer, true)]
        [TestCase(EPlayerClass.Swordsman, false)]
        [TestCase(EPlayerClass.Warrior, false)]
        [TestCase(EPlayerClass.Sorcerer, false)]
        [TestCase(EPlayerClass.Summoner, false)]
        public void Build_PlayerClass_VisibleOnlyForStackClasses(EPlayerClass playerClass, bool expected)
        {
            ClassStackDisplay display = ClassStackHudRules.Build(playerClass, PARTIAL_STACKS, MAX_STACKS, EArcherElement.Ice);

            Assert.AreEqual(expected, display.IsVisible);
        }

        [Test]
        public void Build_UnknownMax_Hidden()
        {
            Assert.IsFalse(ClassStackHudRules.Build(EPlayerClass.Archer, PARTIAL_STACKS, 0, EArcherElement.Ice).IsVisible);
        }

        [Test]
        public void Build_MageknightOverflow_ClampsToMaxAndDropsElement()
        {
            ClassStackDisplay display = ClassStackHudRules.Build(EPlayerClass.Mageknight, OVERFLOW_STACKS, MAX_STACKS,
                EArcherElement.Fire);

            Assert.AreEqual(MAX_STACKS, display.Stacks);
            Assert.IsTrue(display.IsFull);
            Assert.IsFalse(display.ShowsElement);
            Assert.AreEqual(EArcherElement.None, display.Element);
        }

        [Test]
        public void Format_MageknightAndArcher_ProducesHudAndNameplateText()
        {
            ClassStackDisplay mageknight = ClassStackHudRules.Build(EPlayerClass.Mageknight, PARTIAL_STACKS, MAX_STACKS,
                EArcherElement.None);
            ClassStackDisplay archer = ClassStackHudRules.Build(EPlayerClass.Archer, PARTIAL_STACKS, MAX_STACKS, EArcherElement.Wind);

            Assert.AreEqual("STACK 12 / 20", ClassStackHudRules.FormatHud(in mageknight));
            Assert.AreEqual("WIND  TOTAL STACK 12 / 20", ClassStackHudRules.FormatHud(in archer));
            Assert.AreEqual("스택 12/20", ClassStackHudRules.FormatNameplate(in mageknight));
            Assert.AreEqual("바람 총 스택 12/20", ClassStackHudRules.FormatNameplate(in archer));
            Assert.AreEqual(string.Empty, ClassStackHudRules.FormatHud(ClassStackDisplay.Hidden));
        }

        [Test]
        public void GetColor_FullStacks_DiffersFromPartial()
        {
            ClassStackDisplay partial = ClassStackHudRules.Build(EPlayerClass.Archer, PARTIAL_STACKS, MAX_STACKS, EArcherElement.Ice);
            ClassStackDisplay full    = ClassStackHudRules.Build(EPlayerClass.Archer, MAX_STACKS, MAX_STACKS, EArcherElement.Ice);

            Assert.AreNotEqual(ClassStackHudRules.GetColor(in partial), ClassStackHudRules.GetColor(in full));
        }

        [Test]
        public void StatusView_CreatesLabelAboveHpTrack_AndHidesForHidden()
        {
            HudPlayerStatusView status = CreateStatusView(out RectTransform track);
            ClassStackDisplay display = ClassStackHudRules.Build(EPlayerClass.Archer, PARTIAL_STACKS, MAX_STACKS, EArcherElement.Fire);

            status.SetClassStacks(in display);

            Transform label = track.Find(STACK_OBJECT_NAME);
            Assert.IsNotNull(label);
            var text = label.GetComponent<TMP_Text>();
            Assert.IsTrue(text.enabled);
            Assert.AreEqual("FIRE  TOTAL STACK 12 / 20", text.text);

            status.SetClassStacks(ClassStackDisplay.Hidden);
            Assert.IsFalse(text.enabled);
        }

        [Test]
        public void StatusView_HiddenClass_CreatesNoLabel()
        {
            HudPlayerStatusView status = CreateStatusView(out RectTransform track);
            ClassStackDisplay display = ClassStackHudRules.Build(EPlayerClass.Swordsman, PARTIAL_STACKS, MAX_STACKS, EArcherElement.None);

            status.SetClassStacks(in display);

            Assert.IsNull(track.Find(STACK_OBJECT_NAME));
        }

        [Test]
        public void SkillSlot_SetLocked_DimsAndRestoresIcon()
        {
            var slot = _root.AddComponent<HudSkillSlotView>();
            var iconObject = new GameObject("Icon", typeof(RectTransform));
            iconObject.transform.SetParent(_root.transform, false);
            var icon = iconObject.AddComponent<Image>();
            icon.color = Color.white;
            SetField(slot, "_icon", icon);

            slot.SetLocked(true);
            Assert.Less(icon.color.r, Color.white.r);

            slot.SetLocked(false);
            Assert.AreEqual(Color.white, icon.color);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private HudPlayerStatusView CreateStatusView(out RectTransform track)
        {
            var status = _root.AddComponent<HudPlayerStatusView>();
            var trackObject = new GameObject("Track", typeof(RectTransform));
            track = (RectTransform)trackObject.transform;
            track.SetParent(_root.transform, false);
            var fillObject = new GameObject("Fill", typeof(RectTransform));
            fillObject.transform.SetParent(track, false);
            var valueObject = new GameObject("Value", typeof(RectTransform));
            valueObject.transform.SetParent(track, false);
            var value = valueObject.AddComponent<TextMeshProUGUI>();
            SetField(status, "_hpFill", (RectTransform)fillObject.transform);
            SetField(status, "_hpText", value);
            return status;
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, name);
            field.SetValue(target, value);
        }
    }
}
