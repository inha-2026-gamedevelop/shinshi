// Unity
using UnityEngine;
using UnityEngine.UI;

// 테스트
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.UI;

namespace Shinshi.Tests
{
    public class ModernDarkHudPlaceholderTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        [Test]
        public void HidePlaceholderIcons_DisablesOnlySpritelessNamedImages_AndKeepsGroups()
        {
            _root = new GameObject("Layout_AlwaysVisible", typeof(RectTransform));
            Image debuff = NewIcon("StatusEffectGroup/DebuffGroup/DebuffIcon");
            Image buff   = NewIcon("StatusEffectGroup/BuffGroup/BuffIcon");
            Image other  = NewIcon("StatusEffectGroup/OtherGroup/OtherIcon");

            HudModernLayout.HidePlaceholderIcons(_root.transform);
            Assert.IsFalse(debuff.enabled);
            Assert.IsFalse(buff.enabled);
            Assert.IsTrue(other.enabled, "이름이 다른 칸은 건드리지 않는다");
            Assert.IsTrue(_root.transform.Find("StatusEffectGroup/DebuffGroup").gameObject.activeSelf);
            Assert.IsTrue(debuff.gameObject.activeSelf, "오브젝트는 켠 채 Image만 끈다");

            HudModernLayout.HidePlaceholderIcons(_root.transform);
            Assert.IsFalse(debuff.enabled);
        }

        [Test]
        public void HidePlaceholderIcons_KeepsImageWithSprite()
        {
            _root = new GameObject("Layout_AlwaysVisible", typeof(RectTransform));
            Image buff = NewIcon("StatusEffectGroup/BuffGroup/BuffIcon");
            var tex = new Texture2D(2, 2);
            buff.sprite = Sprite.Create(tex, new Rect(0f, 0f, 2f, 2f), Vector2.zero);

            HudModernLayout.HidePlaceholderIcons(_root.transform);
            Assert.IsTrue(buff.enabled);

            Object.DestroyImmediate(buff.sprite);
            Object.DestroyImmediate(tex);
        }

        [Test]
        public void HidePlaceholderIcons_MissingNodesDoNotThrow()
        {
            _root = new GameObject("Layout_AlwaysVisible", typeof(RectTransform));
            Assert.DoesNotThrow(() => HudModernLayout.HidePlaceholderIcons(_root.transform));
            Assert.DoesNotThrow(() => HudModernLayout.HidePlaceholderIcons(null));
        }

        private Image NewIcon(string path)
        {
            Transform parent = _root.transform;
            string[] parts = path.Split('/');
            for (int i = 0; i < parts.Length; ++i)
            {
                Transform child = parent.Find(parts[i]);
                if (child == null)
                {
                    var go = new GameObject(parts[i], typeof(RectTransform));
                    go.transform.SetParent(parent, false);
                    child = go.transform;
                }

                parent = child;
            }

            return parent.gameObject.AddComponent<Image>();
        }
    }
}
