// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.UI;

namespace Shinshi.Tests
{
    public sealed class ModernDarkPresentationTests
    {
        private const string BACKDROP_NAME = "MD_HudBackdrop";

        private GameObject _target = null;

        [SetUp]
        public void SetUp()
        {
            _target = new GameObject("HudTarget", typeof(RectTransform));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_target);
        }

        [Test]
        public void AddBackdrop_FirstCall_ReturnsTrueAndAddsOneChild()
        {
            bool created = HudModernStyle.AddBackdrop(_target.transform, UiModernDark.SPRITE_INSET, Color.white, 4f);

            Assert.IsTrue(created);
            Assert.AreEqual(1, _target.transform.childCount);
            Assert.AreEqual(BACKDROP_NAME, _target.transform.GetChild(0).name);
        }

        [Test]
        public void AddBackdrop_CalledTwice_StaysIdempotent()
        {
            HudModernStyle.AddBackdrop(_target.transform, UiModernDark.SPRITE_INSET, Color.white, 4f);

            bool again = HudModernStyle.AddBackdrop(_target.transform, UiModernDark.SPRITE_INSET, Color.white, 4f);

            Assert.IsFalse(again);
            Assert.AreEqual(1, _target.transform.childCount);
        }

        [Test]
        public void AddBackdrop_NewChild_DoesNotBlockRaycasts()
        {
            HudModernStyle.AddBackdrop(_target.transform, UiModernDark.SPRITE_INSET, Color.white, 4f);

            var graphic = _target.transform.GetChild(0).GetComponent<Graphic>();

            Assert.IsNotNull(graphic);
            Assert.IsFalse(graphic.raycastTarget);
        }

        [Test]
        public void Load_MissingCatalog_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => ModernDarkUiCatalog.Load());
        }
    }
}
