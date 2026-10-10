// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.AI
{
    /// <summary> BossPatternCatalogSO를 IBossPatternCatalog로 보이게 하는 어댑터, 만들 때 받은 난이도의 패턴만 수집하고 튜닝을 확인한다 </summary>
    public sealed class BossPatternCatalogSource : IBossPatternCatalog, IBossPatternReadiness
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly BossPatternCatalogSO _catalog;
        private readonly EBossDifficulty      _difficulty;

        /****************************************
        *                Methods
        ****************************************/

        // 난이도를 주지 않는 기존 호출은 이지로 동작한다
        public BossPatternCatalogSource(BossPatternCatalogSO catalog)
            : this(catalog, EBossDifficulty.Easy)
        {
        }

        public BossPatternCatalogSource(BossPatternCatalogSO catalog, EBossDifficulty difficulty)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            _catalog    = catalog;
            _difficulty = difficulty;
        }

        public bool TryGetBoss(string bossId, out BossPatternCatalogSO.BossDefinition boss)
        {
            return _catalog.TryGetBoss(bossId, out boss);
        }

        public void CollectPatterns(string bossId, int phase, List<BossPatternCatalogSO.PatternDefinition> results)
        {
            _catalog.CollectPatterns(bossId, phase, _difficulty, results);
        }

        public bool IsTuned(string bossId)
        {
            return _catalog.IsTuned(bossId, _difficulty);
        }

        public bool IsPatternReady(string bossId, BossPatternCatalogSO.PatternDefinition pattern)
        {
            return _catalog.IsPatternReady(bossId, pattern);
        }
    }
}
