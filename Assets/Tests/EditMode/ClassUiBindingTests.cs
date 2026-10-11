// System
using System;
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
    public sealed class ClassUiBindingTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int SLOT_INDEX = 0;
        private const int KNOWN_LEVEL = 5;
        private const string MEMBER_NAME = "파티원";
        private const BindingFlags PRIVATE_FIELDS = BindingFlags.Instance | BindingFlags.NonPublic;

        private GameObject _root;
        private Texture2D _texture;
        private Sprite _badge;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("ClassUiBindingTest", typeof(RectTransform));
            _root.SetActive(false);
            _texture = new Texture2D(1, 1);
            _badge = Sprite.Create(_texture, new Rect(0f, 0f, 1f, 1f), Vector2.zero);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_root);
            UnityEngine.Object.DestroyImmediate(_badge);
            UnityEngine.Object.DestroyImmediate(_texture);
        }

        /****************************************
        *                Tests
        ****************************************/

        [TestCase(EPlayerClass.Warrior, "무사")]
        [TestCase(EPlayerClass.Sorcerer, "도술사")]
        [TestCase(EPlayerClass.Summoner, "신령사")]
        [TestCase(EPlayerClass.Swordsman, "검사")]
        [TestCase(EPlayerClass.Mageknight, "마검사")]
        [TestCase(EPlayerClass.Archer, "궁수")]
        public void ClassName_KnownClass_DetailAndPrepUseKoreanName(EPlayerClass playerClass, string expected)
        {
            SortiePrepView view = CreatePrepView(out TMP_Text name, out _);

            view.SetMembers(new[] { Member(playerClass, KNOWN_LEVEL) });

            Assert.AreEqual(expected, CharDetailRules.ClassName((int)playerClass));
            StringAssert.Contains(expected, name.text);
            StringAssert.Contains("Lv.5", name.text);
        }

        [TestCase(-1)]
        [TestCase(6)]
        public void ClassName_UnknownClass_RemainsEmpty(int classId)
        {
            Assert.AreEqual(string.Empty, CharDetailRules.ClassName(classId));
        }

        [TestCase(EPlayerClass.Swordsman)]
        [TestCase(EPlayerClass.Mageknight)]
        [TestCase(EPlayerClass.Archer)]
        public void SetMember_LegacyToUnassignedNewBadge_ClearsAndCanRestoreLegacy(EPlayerClass playerClass)
        {
            HudPartyView view = CreatePartyView(out Image image);
            SetField(view, "_warriorBadge", _badge);
            view.SetMember(SLOT_INDEX, MEMBER_NAME, EPlayerClass.Warrior, false, 0, 0);
            Assert.AreSame(_badge, image.sprite);
            Assert.IsTrue(image.enabled);

            view.SetMember(SLOT_INDEX, MEMBER_NAME, playerClass, false, 0, 0);

            Assert.IsNull(image.sprite);
            Assert.IsFalse(image.enabled);
            view.SetMember(SLOT_INDEX, MEMBER_NAME, EPlayerClass.Warrior, false, 0, 0);
            Assert.AreSame(_badge, image.sprite);
            Assert.IsTrue(image.enabled);
        }

        [TestCase(EPlayerClass.Swordsman, "_swordsmanBadge")]
        [TestCase(EPlayerClass.Mageknight, "_mageknightBadge")]
        [TestCase(EPlayerClass.Archer, "_archerBadge")]
        public void SetMember_ConfiguredNewBadge_ShowsAssignedSprite(EPlayerClass playerClass, string field)
        {
            HudPartyView view = CreatePartyView(out Image image);
            SetField(view, field, _badge);

            view.SetMember(SLOT_INDEX, MEMBER_NAME, playerClass, false, 0, 0);

            Assert.AreSame(_badge, image.sprite);
            Assert.IsTrue(image.enabled);
        }

        [Test]
        public void ClearMember_AfterLegacyBadge_ClearsSpriteAndResetsCachedMember()
        {
            HudPartyView view = CreatePartyView(out Image image);
            SetField(view, "_warriorBadge", _badge);
            view.SetMember(SLOT_INDEX, MEMBER_NAME, EPlayerClass.Warrior, false, 0, 0);

            view.ClearMember(SLOT_INDEX);
            Assert.IsNull(image.sprite);
            Assert.IsFalse(image.enabled);

            view.SetMember(SLOT_INDEX, MEMBER_NAME, EPlayerClass.Warrior, false, 0, 0);
            Assert.AreSame(_badge, image.sprite);
            Assert.IsTrue(image.enabled);
        }

        [TestCase(EPlayerClass.Swordsman)]
        [TestCase(EPlayerClass.Mageknight)]
        [TestCase(EPlayerClass.Archer)]
        public void SetMembers_NewClassWithoutIcon_ClearsPreviousSprite(EPlayerClass playerClass)
        {
            SortiePrepView view = CreatePrepView(out _, out Image image);
            image.sprite = _badge;
            image.enabled = true;

            view.SetMembers(new[] { Member(playerClass, KNOWN_LEVEL) });

            Assert.IsNull(image.sprite);
            Assert.IsFalse(image.enabled);
        }

        [Test]
        public void SetMembers_UnknownLevel_ShowsDashAndClearsEmptySlotIcon()
        {
            SortiePrepView view = CreatePrepView(out TMP_Text name, out Image image);

            view.SetMembers(new[] { Member(EPlayerClass.Archer, -1) });
            StringAssert.Contains("궁수", name.text);
            StringAssert.Contains("Lv.-", name.text);
            image.sprite = _badge;
            image.enabled = true;

            view.SetMembers(null);

            Assert.IsNull(image.sprite);
            Assert.IsFalse(image.enabled);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private HudPartyView CreatePartyView(out Image badge)
        {
            var view = _root.AddComponent<HudPartyView>();
            badge = Child<Image>("PartyBadge");
            TMP_Text name = Child<TextMeshProUGUI>("PartyName");
            Type slotType = typeof(HudPartyView).GetNestedType("Slot", BindingFlags.NonPublic);
            object slot = Activator.CreateInstance(slotType, true);
            SetField(slot, "_root", badge.gameObject);
            SetField(slot, "_badge", badge);
            SetField(slot, "_name", name);
            SetSingleRow(view, "_slots", slotType, slot);
            return view;
        }

        private SortiePrepView CreatePrepView(out TMP_Text name, out Image icon)
        {
            var view = _root.AddComponent<SortiePrepView>();
            name = Child<TextMeshProUGUI>("PrepName");
            icon = Child<Image>("PrepIcon");
            Type rowType = typeof(SortiePrepView).GetNestedType("MemberRow", BindingFlags.NonPublic);
            object row = Activator.CreateInstance(rowType, true);
            SetField(row, "_nameLabel", name);
            SetField(row, "_classIcon", icon);
            SetSingleRow(view, "_members", rowType, row);
            return view;
        }

        private T Child<T>(string name) where T : Component
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(_root.transform, false);
            return child.AddComponent<T>();
        }

        private static SortieMemberData Member(EPlayerClass playerClass, int level)
        {
            return new SortieMemberData(MEMBER_NAME, playerClass, true, false, true, true, level, 0);
        }

        private static void SetSingleRow(object view, string field, Type rowType, object row)
        {
            Array rows = Array.CreateInstance(rowType, 1);
            rows.SetValue(row, SLOT_INDEX);
            SetField(view, field, rows);
        }

        private static void SetField(object target, string field, object value)
        {
            target.GetType().GetField(field, PRIVATE_FIELDS).SetValue(target, value);
        }
    }
}
