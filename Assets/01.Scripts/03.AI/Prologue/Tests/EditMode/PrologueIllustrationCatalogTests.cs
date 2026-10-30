// Unity
using UnityEditor;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    public class PrologueIllustrationCatalogTests
    {
        private const string CATALOG_PATH = "Assets/09.Data/AI/Prologue/PrologueIllustrationCatalog.asset";

        private const int INTRO_COUNT      = 4;
        private const int TALISMAN_COUNT   = 1;
        private const int APPEARANCE_COUNT = 1;
        private const int ENDING_COUNT     = 2;

        private PrologueIllustrationCatalogSO _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = AssetDatabase.LoadAssetAtPath<PrologueIllustrationCatalogSO>(CATALOG_PATH);
            Assert.IsNotNull(_catalog, "카탈로그 에셋이 없다");
        }

        [Test]
        public void Catalog_PageCounts_MatchPlan()
        {
            Assert.AreEqual(INTRO_COUNT, _catalog.IntroPages.Length);
            Assert.AreEqual(TALISMAN_COUNT, _catalog.TalismanPages.Length);
            Assert.AreEqual(APPEARANCE_COUNT, _catalog.AppearancePages.Length);
            Assert.AreEqual(ENDING_COUNT, _catalog.EndingPages.Length);
        }

        [Test]
        public void Catalog_EveryPage_HasImageAndText()
        {
            AssertPages(_catalog.IntroPages);
            AssertPages(_catalog.TalismanPages);
            AssertPages(_catalog.AppearancePages);
            AssertPages(_catalog.EndingPages);
        }

        [Test]
        public void Catalog_InkWipeMaterial_IsAssignedAndUsesInkShader()
        {
            Assert.IsNotNull(_catalog.InkWipeMaterial, "전환 재질이 연결되지 않았다");
            Assert.AreEqual("Shinshi/UI/InkWipe", _catalog.InkWipeMaterial.shader.name);
            Assert.IsTrue(_catalog.InkWipeMaterial.shader.isSupported);
        }

        [Test]
        public void Catalog_ConfirmedLines_AreExact()
        {
            Assert.AreEqual("신시의 부적과 닮은 문양이다.", _catalog.TalismanPages[0].Text);
            Assert.AreEqual("그 부적...... 아직도 인간에게 전해지고 있었구나.", _catalog.AppearancePages[0].Text);
        }

        private static void AssertPages(IllustrationPage[] pages)
        {
            for (int i = 0; i < pages.Length; ++i)
            {
                Assert.IsNotNull(pages[i].Image, $"{i}번째 그림이 비어 있다");
                Assert.IsFalse(string.IsNullOrEmpty(pages[i].Text), $"{i}번째 문구가 비어 있다");
            }
        }
    }
}
