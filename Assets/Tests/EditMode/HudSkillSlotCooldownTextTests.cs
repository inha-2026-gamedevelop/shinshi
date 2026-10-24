// System
using System.Reflection;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using NUnit.Framework;
using TMPro;

// 프로젝트 내부
using Shinshi.UI;

namespace Shinshi.Tests
{
    public sealed class HudSkillSlotCooldownTextTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const BindingFlags PRIVATE_INSTANCE = BindingFlags.NonPublic | BindingFlags.Instance;
        private const float        DURATION         = 20f;

        private GameObject       _root;
        private HudSkillSlotView _view;
        private TMP_Text         _text;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            // 글자 메시를 만들려면 Canvas와 폰트가 있어야 하므로 키 글자에 기본 폰트를 넣어 숫자 글자가 따라 쓰게 한다
            _root = new GameObject("HudSkillSlotCooldownTextTest", typeof(RectTransform), typeof(Canvas));

            var cooldownObject = new GameObject("Cooldown", typeof(RectTransform));
            cooldownObject.transform.SetParent(_root.transform, false);
            Image cooldown = cooldownObject.AddComponent<Image>();

            var keyObject = new GameObject("Key", typeof(RectTransform));
            keyObject.transform.SetParent(_root.transform, false);
            TMP_Text key = keyObject.AddComponent<TextMeshProUGUI>();
            key.font = TMP_Settings.defaultFontAsset;

            _view = _root.AddComponent<HudSkillSlotView>();
            typeof(HudSkillSlotView).GetField("_cooldown", PRIVATE_INSTANCE).SetValue(_view, cooldown);
            typeof(HudSkillSlotView).GetField("_keyText", PRIVATE_INSTANCE).SetValue(_view, key);

            // 에디트 모드에서는 Awake가 자동으로 불리지 않으므로 직접 불러 숫자 글자를 만든다
            typeof(HudSkillSlotView).GetMethod("Awake", PRIVATE_INSTANCE).Invoke(_view, null);
            _text = (TMP_Text)typeof(HudSkillSlotView).GetField("_cooldownText", PRIVATE_INSTANCE).GetValue(_view);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Awake_WithoutLinkedText_CreatesHiddenTextAboveCooldown()
        {
            Assert.That(_text, Is.Not.Null);
            Assert.That(_text.enabled, Is.False);
            Assert.That(_text.transform.GetSiblingIndex(), Is.EqualTo(1));
        }

        [TestCase(12.2f, "13")]
        [TestCase(10f, "10")]
        [TestCase(4.25f, "4.3")]
        [TestCase(0.04f, "0.1")]
        public void SetCooldown_Remaining_ShowsRoundedUpSeconds(float remaining, string expected)
        {
            _view.SetCooldown(remaining, DURATION);
            _text.ForceMeshUpdate();

            Assert.That(_text.enabled, Is.True);
            Assert.That(_text.GetParsedText(), Is.EqualTo(expected));
        }

        [Test]
        public void SetCooldown_Finished_HidesText()
        {
            _view.SetCooldown(3f, DURATION);
            _view.SetCooldown(0f, DURATION);

            Assert.That(_text.enabled, Is.False);
        }
    }
}
