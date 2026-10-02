// System
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public class LocalPlayerHudRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float  TOLERANCE       = 0.0001f;
        private const double NOW             = 100.0;
        private const float  WARRIOR_Q_CD    = 6f;
        private const float  SORCERER_W_CD   = 12f;
        private const float  SUMMONER_E_CD   = 5f;
        private const float  ASCENSION_CD    = 3f;
        private const float  RESONANCE_MAX   = 100f;
        private const int    KEY_COUNT       = 7;

        private static readonly ESkillInputKey[] G_RegularKeys =
        {
            ESkillInputKey.Q,
            ESkillInputKey.W,
            ESkillInputKey.E,
            ESkillInputKey.R
        };

        private static readonly ESkillInputKey[] G_AscensionKeys =
        {
            ESkillInputKey.Z,
            ESkillInputKey.X,
            ESkillInputKey.C
        };

        private SkillCatalogSO _catalog;

        /****************************************
        *                Lifecycle
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _catalog = ScriptableObject.CreateInstance<SkillCatalogSO>();
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
        public void Remaining_FutureReadyAt_ReturnsSecondsLeft()
        {
            Assert.AreEqual(2.5f, LocalPlayerHudRules.Remaining(NOW + 2.5, NOW), TOLERANCE);
        }

        [Test]
        public void Remaining_ReadyAtNowOrPast_ReturnsZero()
        {
            Assert.AreEqual(0f, LocalPlayerHudRules.Remaining(NOW, NOW));
            Assert.AreEqual(0f, LocalPlayerHudRules.Remaining(NOW - 5.0, NOW));
            Assert.AreEqual(0f, LocalPlayerHudRules.Remaining(0.0, NOW));
        }

        [Test]
        public void Remaining_NotANumber_ReturnsZero()
        {
            Assert.AreEqual(0f, LocalPlayerHudRules.Remaining(double.NaN, NOW));
            Assert.AreEqual(0f, LocalPlayerHudRules.Remaining(NOW + 1.0, double.NaN));
        }

        [Test]
        public void ResolveDuration_NotCooling_ReturnsCatalogCooldown()
        {
            Assert.AreEqual(WARRIOR_Q_CD, LocalPlayerHudRules.ResolveDuration(NOW - 1.0, NOW, WARRIOR_Q_CD, true), TOLERANCE);
            Assert.AreEqual(WARRIOR_Q_CD, LocalPlayerHudRules.ResolveDuration(0.0, NOW, WARRIOR_Q_CD, false), TOLERANCE);
        }

        [Test]
        public void ResolveDuration_NewCooldown_UsesObservedRemainingToKeepTalismanScale()
        {
            // 부적이 쿨다운을 절반으로 줄이면 카탈로그 길이보다 짧은 남은 시간이 전체 길이다
            Assert.AreEqual(3f, LocalPlayerHudRules.ResolveDuration(NOW + 3.0, NOW, WARRIOR_Q_CD, true), TOLERANCE);
            Assert.AreEqual(9f, LocalPlayerHudRules.ResolveDuration(NOW + 9.0, NOW, WARRIOR_Q_CD, true), TOLERANCE);
        }

        [Test]
        public void ResolveDuration_CooldownAlreadyRunningOnBind_UsesCatalogLength()
        {
            Assert.AreEqual(WARRIOR_Q_CD, LocalPlayerHudRules.ResolveDuration(NOW + 2.0, NOW, WARRIOR_Q_CD, false), TOLERANCE);
        }

        [Test]
        public void ResolveDuration_RemainingExceedsCatalogOnBind_NeverBelowRemaining()
        {
            Assert.AreEqual(8f, LocalPlayerHudRules.ResolveDuration(NOW + 8.0, NOW, WARRIOR_Q_CD, false), TOLERANCE);
        }

        [Test]
        public void ResolveDuration_ZeroOrNegativeCatalog_FallsBackToRemainingOrZero()
        {
            Assert.AreEqual(0f, LocalPlayerHudRules.ResolveDuration(NOW - 1.0, NOW, 0f, true));
            Assert.AreEqual(0f, LocalPlayerHudRules.ResolveDuration(NOW - 1.0, NOW, -3f, false));
            Assert.AreEqual(2f, LocalPlayerHudRules.ResolveDuration(NOW + 2.0, NOW, 0f, true), TOLERANCE);
            Assert.AreEqual(2f, LocalPlayerHudRules.ResolveDuration(NOW + 2.0, NOW, 0f, false), TOLERANCE);
        }

        [Test]
        public void TryParseSlotKey_AllSevenKeys_Parse()
        {
            AssertParsed("SkillSlotQ", ESkillInputKey.Q);
            AssertParsed("SkillSlotW", ESkillInputKey.W);
            AssertParsed("SkillSlotE", ESkillInputKey.E);
            AssertParsed("SkillSlotR", ESkillInputKey.R);
            AssertParsed("SkillSlotZ", ESkillInputKey.Z);
            AssertParsed("SkillSlotX", ESkillInputKey.X);
            AssertParsed("SkillSlotC", ESkillInputKey.C);
        }

        [Test]
        public void TryParseSlotKey_UnexpectedNames_Fail()
        {
            string[] names = { null, string.Empty, "SkillSlot", "SkillSlotQQ", "SkillSlotA", "skillslotq", "SkillSlotq", "Slot Q", "PlayerStatusGroup" };
            for (int i = 0; i < names.Length; ++i)
            {
                Assert.IsFalse(LocalPlayerHudRules.TryParseSlotKey(names[i], out ESkillInputKey key), names[i]);
                Assert.AreEqual(ESkillInputKey.None, key);
            }
        }

        [Test]
        public void TryResolveSlot_RegularKeys_UseCatalogCooldownForEveryClass()
        {
            AssertResolved(EPlayerClass.Warrior, ESkillInputKey.Q, ESkillRequestKind.Regular, Constants.Combat.SKILL_SLOT_Q, WARRIOR_Q_CD);
            AssertResolved(EPlayerClass.Sorcerer, ESkillInputKey.W, ESkillRequestKind.Regular, Constants.Combat.SKILL_SLOT_W, SORCERER_W_CD);
            AssertResolved(EPlayerClass.Summoner, ESkillInputKey.E, ESkillRequestKind.Regular, Constants.Combat.SKILL_SLOT_E, SUMMONER_E_CD);
        }

        [Test]
        public void TryResolveSlot_EveryClassResolvesAllFourRegularSlots()
        {
            EPlayerClass[] classes = { EPlayerClass.Warrior, EPlayerClass.Sorcerer, EPlayerClass.Summoner };
            for (int c = 0; c < classes.Length; ++c)
            {
                for (int k = 0; k < G_RegularKeys.Length; ++k)
                {
                    Assert.IsTrue(LocalPlayerHudRules.TryResolveSlot(_catalog, classes[c], G_RegularKeys[k], out ESkillRequestKind kind,
                        out byte slot, out float cooldown, out Sprite icon), $"{classes[c]} {G_RegularKeys[k]}");
                    Assert.AreEqual(ESkillRequestKind.Regular, kind);
                    Assert.AreEqual(k, slot);
                    Assert.Greater(cooldown, 0f);
                }
            }
        }

        [Test]
        public void TryResolveSlot_AscensionKeys_OnlySummonerWithSharedCooldown()
        {
            for (int i = 0; i < G_AscensionKeys.Length; ++i)
            {
                AssertResolved(EPlayerClass.Summoner, G_AscensionKeys[i], ESkillRequestKind.Ascension, i, ASCENSION_CD);
                AssertNotResolved(EPlayerClass.Warrior, G_AscensionKeys[i]);
                AssertNotResolved(EPlayerClass.Sorcerer, G_AscensionKeys[i]);
            }
        }

        [Test]
        public void TryResolveSlot_NoneKeyOrNullCatalog_Fails()
        {
            AssertNotResolved(EPlayerClass.Warrior, ESkillInputKey.None);
            Assert.IsFalse(LocalPlayerHudRules.TryResolveSlot(null, EPlayerClass.Warrior, ESkillInputKey.Q, out _, out _, out _, out _));
        }

        [Test]
        public void TryResolveSlot_DefaultCatalogHasNoIcons()
        {
            for (int k = 0; k < G_RegularKeys.Length; ++k)
            {
                LocalPlayerHudRules.TryResolveSlot(_catalog, EPlayerClass.Warrior, G_RegularKeys[k], out _, out _, out _, out Sprite icon);
                Assert.IsNull(icon);
            }

            LocalPlayerHudRules.TryResolveSlot(_catalog, EPlayerClass.Summoner, ESkillInputKey.Z, out _, out _, out _, out Sprite ascensionIcon);
            Assert.IsNull(ascensionIcon);
        }

        [Test]
        public void TryResolveSlot_ConfiguredIcons_AreReturnedForSkillAndAscension()
        {
            var texture = new Texture2D(2, 2);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), Vector2.zero);
            try
            {
                SetIcon(_catalog.Skills[0], sprite);
                SetIcon(_catalog.Ascensions[0], sprite);

                LocalPlayerHudRules.TryResolveSlot(_catalog, EPlayerClass.Warrior, ESkillInputKey.Q, out _, out _, out _, out Sprite skillIcon);
                LocalPlayerHudRules.TryResolveSlot(_catalog, EPlayerClass.Summoner, ESkillInputKey.Z, out _, out _, out _, out Sprite ascensionIcon);
                Assert.AreSame(sprite, skillIcon);
                Assert.AreSame(sprite, ascensionIcon);
            }
            finally
            {
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void CatalogValidation_NullIcons_Pass()
        {
            var errors = new List<string>();
            _catalog.Validate(errors);
            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void HasResonance_OnlySummoner()
        {
            Assert.IsTrue(LocalPlayerHudRules.HasResonance(EPlayerClass.Summoner));
            Assert.IsFalse(LocalPlayerHudRules.HasResonance(EPlayerClass.Warrior));
            Assert.IsFalse(LocalPlayerHudRules.HasResonance(EPlayerClass.Sorcerer));
        }

        [Test]
        public void BuildGauges_Summoner_CarriesResonance()
        {
            LocalPlayerGauges gauges = LocalPlayerHudRules.BuildGauges(50f, 100f, 25f, 100f, 40f, RESONANCE_MAX, EPlayerClass.Summoner);
            Assert.IsTrue(gauges.HasResonance);
            Assert.AreEqual(40f, gauges.Resonance, TOLERANCE);
            Assert.AreEqual(RESONANCE_MAX, gauges.MaxResonance, TOLERANCE);
            Assert.AreEqual(0.4f, gauges.ResonanceRatio, TOLERANCE);
            Assert.AreEqual(0.5f, gauges.HealthRatio, TOLERANCE);
            Assert.AreEqual(0.25f, gauges.EnergyRatio, TOLERANCE);
        }

        [Test]
        public void BuildGauges_OtherClasses_HideResonance()
        {
            LocalPlayerGauges gauges = LocalPlayerHudRules.BuildGauges(50f, 100f, 25f, 100f, 40f, RESONANCE_MAX, EPlayerClass.Warrior);
            Assert.IsFalse(gauges.HasResonance);
            Assert.AreEqual(0f, gauges.Resonance);
            Assert.AreEqual(0f, gauges.MaxResonance);
            Assert.AreEqual(0f, gauges.ResonanceRatio);
        }

        [Test]
        public void ToDisplayInt_RoundsUpAndClampsInsideMax()
        {
            Assert.AreEqual(0, LocalPlayerHudRules.ToDisplayInt(0f, 100f));
            Assert.AreEqual(1, LocalPlayerHudRules.ToDisplayInt(0.3f, 100f));
            Assert.AreEqual(30, LocalPlayerHudRules.ToDisplayInt(30f, 100f));
            Assert.AreEqual(30, LocalPlayerHudRules.ToDisplayInt(30.00001f, 100f));
            Assert.AreEqual(31, LocalPlayerHudRules.ToDisplayInt(30.5f, 100f));
            Assert.AreEqual(100, LocalPlayerHudRules.ToDisplayInt(150f, 100f));
            Assert.AreEqual(0, LocalPlayerHudRules.ToDisplayInt(-5f, 100f));
        }

        [Test]
        public void ToDisplayInt_EmptyMaxOrNotANumber_ReturnsZero()
        {
            Assert.AreEqual(0, LocalPlayerHudRules.ToDisplayInt(10f, 0f));
            Assert.AreEqual(0, LocalPlayerHudRules.ToDisplayInt(float.NaN, 100f));
            Assert.AreEqual(0, LocalPlayerHudRules.ToDisplayInt(10f, float.NaN));
        }

        [Test]
        public void SlotKeyCount_MatchesInputKeysWithoutNone()
        {
            Assert.AreEqual(KEY_COUNT, System.Enum.GetValues(typeof(ESkillInputKey)).Length - 1);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static void AssertParsed(string name, ESkillInputKey expected)
        {
            Assert.IsTrue(LocalPlayerHudRules.TryParseSlotKey(name, out ESkillInputKey key), name);
            Assert.AreEqual(expected, key);
        }

        private void AssertResolved(EPlayerClass playerClass, ESkillInputKey key, ESkillRequestKind expectedKind, int expectedSlot,
            float expectedCooldown)
        {
            Assert.IsTrue(LocalPlayerHudRules.TryResolveSlot(_catalog, playerClass, key, out ESkillRequestKind kind, out byte slot,
                out float cooldown, out _), $"{playerClass} {key}");
            Assert.AreEqual(expectedKind, kind);
            Assert.AreEqual(expectedSlot, slot);
            Assert.AreEqual(expectedCooldown, cooldown, TOLERANCE);
        }

        private void AssertNotResolved(EPlayerClass playerClass, ESkillInputKey key)
        {
            Assert.IsFalse(LocalPlayerHudRules.TryResolveSlot(_catalog, playerClass, key, out _, out _, out float cooldown, out Sprite icon),
                $"{playerClass} {key}");
            Assert.AreEqual(0f, cooldown);
            Assert.IsNull(icon);
        }

        private static void SetIcon(object definition, Sprite sprite)
        {
            FieldInfo field = definition.GetType().GetField("_icon", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "아이콘 필드가 없다");
            field.SetValue(definition, sprite);
        }
    }
}
