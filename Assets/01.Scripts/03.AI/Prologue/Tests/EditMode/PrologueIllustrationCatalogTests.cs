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
        public void Catalog_EveryPage_HasImage()
        {
            AssertImages(_catalog.IntroPages);
            AssertImages(_catalog.TalismanPages);
            AssertImages(_catalog.AppearancePages);
            AssertImages(_catalog.EndingPages);
        }

        // 마무리 첫 장(압도적인 술식)은 자막과 대사가 없다고 확정됐다. 나머지는 모두 문구가 있어야 한다
        [Test]
        public void Catalog_EveryPageHasText_ExceptTheSilentFirstEndingPage()
        {
            AssertText(_catalog.IntroPages, -1);
            AssertText(_catalog.TalismanPages, -1);
            AssertText(_catalog.AppearancePages, -1);
            AssertText(_catalog.EndingPages, 0);
            Assert.IsTrue(string.IsNullOrEmpty(_catalog.EndingPages[0].Text));
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
            // 줄임표는 기호 문자를 소스에 직접 쓰지 않으려고 이스케이프로 적는다
            const string ELLIPSIS = "……";

            Assert.AreEqual("인간과 신령이 함께 터를 잡은 곳, 신시.\n서로 다른 이들이 이곳에서 삶을 이어 왔다.", _catalog.IntroPages[0].Text);
            Assert.AreEqual("사람들은 무예를 익히고, 부적의 가르침을 이어받았다.\n소중한 이들과 삶의 터전을 지키기 위해.", _catalog.IntroPages[1].Text);
            Assert.AreEqual("그러나 숲 너머에서 인간을 습격하는 산귀들이 나타났다.\n흩어져 있던 습격의 흔적은 점차 하나의 숲으로 이어졌다.", _catalog.IntroPages[2].Text);
            Assert.AreEqual("신시의 토벌대원인 당신은 그 흔적을 따라 숲으로 향했다.", _catalog.IntroPages[3].Text);
            Assert.AreEqual("신시의 부적과 닮은 문양이다.", _catalog.TalismanPages[0].Text);
            Assert.AreEqual("그 부적" + ELLIPSIS + " 아직도 인간에게 전해지고 있었구나.", _catalog.AppearancePages[0].Text);
            Assert.AreEqual(ELLIPSIS + "이대로는 상대할 수 없어.\n신시로 돌아가자. 다시 맞설 힘이 필요해.", _catalog.EndingPages[1].Text);
        }

        private static void AssertImages(IllustrationPage[] pages)
        {
            for (int i = 0; i < pages.Length; ++i)
            {
                Assert.IsNotNull(pages[i].Image, $"{i}번째 그림이 비어 있다");
            }
        }

        // silentIndex는 문구가 없어도 되는 장의 번호, 없으면 -1이다
        private static void AssertText(IllustrationPage[] pages, int silentIndex)
        {
            for (int i = 0; i < pages.Length; ++i)
            {
                if (i == silentIndex)
                {
                    continue;
                }

                Assert.IsFalse(string.IsNullOrEmpty(pages[i].Text), $"{i}번째 문구가 비어 있다");
            }
        }
    }
}
