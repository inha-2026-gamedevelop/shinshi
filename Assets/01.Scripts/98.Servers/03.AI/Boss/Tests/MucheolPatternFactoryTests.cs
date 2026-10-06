// Unity
using UnityEditor;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Common;
using Shinshi.Data;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    // 산문지기 무철은 뼈대만 연결한 상태다. 정의가 카탈로그에 있고, 패턴이 없어 전투가 열리지 않는 것을 고정한다
    public class MucheolPatternFactoryTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CATALOG_PATH = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const string WOLBAEK_ID   = "boss_wolbaek";

        /****************************************
        *               Helpers
        ****************************************/

        private static PatternDefinition NewDefinition()
        {
            return new PatternDefinition("mucheol_p01", MucheolPatternFactory.BOSS_ID, 1, EEnemyAttackShape.Fan, false);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Create_AnyDefinition_ReturnsNull()
        {
            var factory = new MucheolPatternFactory();

            Assert.IsNull(factory.Create(NewDefinition()));
            Assert.IsNull(factory.Create(null));
        }

        [Test]
        public void CanCreate_AnyDefinition_ReturnsFalse()
        {
            var factory = new MucheolPatternFactory();

            Assert.IsFalse(factory.CanCreate(NewDefinition()));
            Assert.IsFalse(factory.CanCreate(null));
        }

        [Test]
        public void CatalogAsset_HasMucheolBoss_ButIsNotTuned()
        {
            BossPatternCatalogSO catalog = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assert.IsNotNull(catalog);

            Assert.IsTrue(catalog.TryGetBoss(MucheolPatternFactory.BOSS_ID, out _));
            Assert.IsFalse(catalog.IsTuned(MucheolPatternFactory.BOSS_ID));

            // 새 보스를 더해도 월백 수치는 그대로여야 한다
            Assert.IsTrue(catalog.IsTuned(WOLBAEK_ID));
        }
    }
}
