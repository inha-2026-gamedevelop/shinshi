// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Data;

namespace Shinshi.AI
{
    /// <summary> BossPatternCatalogSO를 IBossPatternCatalog로 보이게 하는 어댑터 </summary>
    public sealed class BossPatternCatalogSource : IBossPatternCatalog
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly BossPatternCatalogSO _catalog;

        /****************************************
        *                Methods
        ****************************************/

        public BossPatternCatalogSource(BossPatternCatalogSO catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            _catalog = catalog;
        }

        public bool TryGetBoss(string bossId, out BossPatternCatalogSO.BossDefinition boss)
        {
            return _catalog.TryGetBoss(bossId, out boss);
        }

        public void CollectPatterns(string bossId, int phase, List<BossPatternCatalogSO.PatternDefinition> results)
        {
            _catalog.CollectPatterns(bossId, phase, results);
        }

        public bool IsTuned(string bossId)
        {
            return _catalog.IsTuned(bossId);
        }
    }
}
