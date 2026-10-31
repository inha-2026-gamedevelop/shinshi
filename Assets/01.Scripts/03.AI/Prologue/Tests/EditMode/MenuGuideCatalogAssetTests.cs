// Unity
using UnityEditor;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    public class MenuGuideCatalogAssetTests
    {
        private const string CATALOG_PATH = "Assets/09.Data/AI/Prologue/MenuGuideCatalog.asset";
        private const string COMMON_LINE  = "지금 실행하지 않아도 됩니다. 도움말에서 언제든 다시 볼 수 있습니다.";

        private MenuGuideCatalogSO _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = AssetDatabase.LoadAssetAtPath<MenuGuideCatalogSO>(CATALOG_PATH);
            Assert.IsNotNull(_catalog, "성장 안내 카탈로그 에셋이 없다");
        }

        [Test]
        public void Training_UsesTheDraftFirstGuideAndTheCommonLine()
        {
            Assert.IsTrue(_catalog.TryGetSteps("menu_training", out MenuGuideStepData[] steps));
            Assert.AreEqual(2, steps.Length);
            Assert.AreEqual("수련으로 능력치를 높일 수 있습니다. 항목과 필요한 정기를 확인해보세요.", steps[0].Text);
            Assert.AreEqual(COMMON_LINE, steps[1].Text);
        }

        [Test]
        public void Talisman_UsesTheDraftFirstGuideAndTheCommonLine()
        {
            Assert.IsTrue(_catalog.TryGetSteps("menu_talisman", out MenuGuideStepData[] steps));
            Assert.AreEqual(2, steps.Length);
            Assert.AreEqual("부적으로 기술에 효과를 더할 수 있습니다. 기술별 효과를 살펴보세요.", steps[0].Text);
            Assert.AreEqual(COMMON_LINE, steps[1].Text);
        }

        [Test]
        public void NoStep_ContainsTheTemporaryPlaceholderMarker()
        {
            foreach (string menuId in new[] { "menu_training", "menu_talisman" })
            {
                _catalog.TryGetSteps(menuId, out MenuGuideStepData[] steps);
                for (int i = 0; i < steps.Length; ++i)
                {
                    StringAssert.DoesNotContain("[임시]", steps[i].Text);
                }
            }
        }
    }
}
