// System
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public class AvatarTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float DNA_MIN             = 0f;
        private const float DNA_NEUTRAL         = 0.5f;
        private const float DNA_MAX             = 1f;
        private const float ONE_SIDED_MIN       = 0.6f;
        private const float ONE_SIDED_NEUTRAL   = 1f;
        private const float TOLERANCE           = 0.0001f;
        private const int   PALETTE_SIZE        = 4;
        private const int   UNKNOWN_HAIR        = 9999;
        private const int   BODY_WARDROBE_SLOTS = 2;  // 몸통은 상의와 하의
        private const int   MISSING_ONE_CLASS   = 2;  // 세 직업 중 마지막 직업 헤어를 뺀다

        private AvatarCatalogSO _catalog;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _catalog = ScriptableObject.CreateInstance<AvatarCatalogSO>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_catalog);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void ToDnaValue_Neutral_ReturnsDnaNeutral()
        {
            float value = AvatarSliderRules.ToDnaValue(CharacterAppearance.SLIDER_NEUTRAL, DNA_MIN, DNA_NEUTRAL, DNA_MAX, false);

            Assert.AreEqual(DNA_NEUTRAL, value, TOLERANCE);
        }

        [Test]
        public void ToDnaValue_Extremes_ReturnDnaMinAndMax()
        {
            Assert.AreEqual(DNA_MIN, AvatarSliderRules.ToDnaValue(CharacterAppearance.VALUE_MIN, DNA_MIN, DNA_NEUTRAL, DNA_MAX, false), TOLERANCE);
            Assert.AreEqual(DNA_MAX, AvatarSliderRules.ToDnaValue(CharacterAppearance.VALUE_MAX, DNA_MIN, DNA_NEUTRAL, DNA_MAX, false), TOLERANCE);
        }

        [Test]
        public void ToDnaValue_Inverted_SwapsDirection()
        {
            float value = AvatarSliderRules.ToDnaValue(CharacterAppearance.VALUE_MAX, DNA_MIN, DNA_NEUTRAL, DNA_MAX, true);

            Assert.AreEqual(DNA_MIN, value, TOLERANCE);
        }

        [Test]
        public void ToDnaValue_OneSidedRange_StaysAtNeutralAbove()
        {
            float above = AvatarSliderRules.ToDnaValue(CharacterAppearance.VALUE_MAX, ONE_SIDED_MIN, ONE_SIDED_NEUTRAL, ONE_SIDED_NEUTRAL, false);
            float below = AvatarSliderRules.ToDnaValue(CharacterAppearance.VALUE_MIN, ONE_SIDED_MIN, ONE_SIDED_NEUTRAL, ONE_SIDED_NEUTRAL, false);

            Assert.AreEqual(ONE_SIDED_NEUTRAL, above, TOLERANCE);
            Assert.AreEqual(ONE_SIDED_MIN, below, TOLERANCE);
        }

        [Test]
        public void ToDnaValue_OutOfRangeStored_IsClamped()
        {
            float value = AvatarSliderRules.ToDnaValue(CharacterAppearance.VALUE_MAX + 1, DNA_MIN, DNA_NEUTRAL, DNA_MAX, false);

            Assert.AreEqual(DNA_MAX, value, TOLERANCE);
        }

        [Test]
        public void ClampPaletteIndex_OutsidePalette_ReturnsFirst()
        {
            Assert.AreEqual(0, AvatarSliderRules.ClampPaletteIndex(PALETTE_SIZE, PALETTE_SIZE));
            Assert.AreEqual(0, AvatarSliderRules.ClampPaletteIndex(-1, PALETTE_SIZE));
            Assert.AreEqual(-1, AvatarSliderRules.ClampPaletteIndex(0, 0));
        }

        [Test]
        public void Validate_DefaultCatalog_HasNoErrors()
        {
            var errors = new List<string>();

            _catalog.Validate(errors);

            CollectionAssert.IsEmpty(errors, string.Join("; ", errors));
        }

        [Test]
        public void IsHairAllowed_ClassDefaultHair_ReturnsTrueAndOtherFalse()
        {
            Assert.IsTrue(_catalog.TryGetClassHair(EPlayerClass.Warrior, out AvatarCatalogSO.ClassHair hair));
            Assert.IsTrue(_catalog.IsHairAllowed(EPlayerClass.Warrior, hair.HairId));
            Assert.IsFalse(_catalog.IsHairAllowed(EPlayerClass.Warrior, UNKNOWN_HAIR));
        }

        [Test]
        public void CreateDefaultAppearance_EachClass_IsWellFormedAndAllowed()
        {
            foreach (EPlayerClass playerClass in Enum.GetValues(typeof(EPlayerClass)))
            {
                CharacterAppearance appearance = _catalog.CreateDefaultAppearance(playerClass);

                Assert.IsTrue(appearance.IsWellFormed(), playerClass.ToString());
                Assert.IsTrue(_catalog.IsHairAllowed(playerClass, appearance.HairId), playerClass.ToString());
            }
        }

        [Test]
        public void ToUnitValue_Range_MapsZeroToOneAndClamps()
        {
            Assert.AreEqual(0f, AvatarSliderRules.ToUnitValue(CharacterAppearance.VALUE_MIN), TOLERANCE);
            Assert.AreEqual(1f, AvatarSliderRules.ToUnitValue(CharacterAppearance.VALUE_MAX), TOLERANCE);
            Assert.AreEqual(1f, AvatarSliderRules.ToUnitValue(CharacterAppearance.VALUE_MAX + 1), TOLERANCE);
        }

        [Test]
        public void Validate_MissingClassHair_ReportsError()
        {
            var serialized = new SerializedObject(_catalog);
            serialized.FindProperty("_classHairs").arraySize = MISSING_ONE_CLASS;

            // 값을 바꾸면 OnValidate가 인스펙터용 오류를 바로 남긴다
            LogAssert.Expect(LogType.Error, new Regex("_classHairs"));
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var errors = new List<string>();

            _catalog.Validate(errors);

            Assert.IsTrue(errors.Exists(e => e.Contains("_classHairs")), string.Join("; ", errors));
        }

        [Test]
        public void Validate_DuplicateHairId_ReportsError()
        {
            var serialized = new SerializedObject(_catalog);
            SerializedProperty hairs = serialized.FindProperty("_classHairs");
            int firstHairId = hairs.GetArrayElementAtIndex(0).FindPropertyRelative("_hairId").intValue;
            hairs.GetArrayElementAtIndex(1).FindPropertyRelative("_hairId").intValue = firstHairId;

            LogAssert.Expect(LogType.Error, new Regex("hairId"));
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var errors = new List<string>();

            _catalog.Validate(errors);

            Assert.IsTrue(errors.Exists(e => e.Contains("hairId")), string.Join("; ", errors));
        }

        [Test]
        public void GetWardrobeSlots_WeaponAndBody_ReturnEmptyAndTwo()
        {
            Assert.AreEqual(0, _catalog.GetWardrobeSlots(EEquipmentSlot.Weapon).Count);
            Assert.AreEqual(BODY_WARDROBE_SLOTS, _catalog.GetWardrobeSlots(EEquipmentSlot.Body).Count);
        }
    }
}
