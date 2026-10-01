// System
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Data;

namespace Shinshi.AI
{
    /// <summary> 보스 정의와 패턴 정의 조회, BossPatternCatalogSO를 감싼다 </summary>
    public interface IBossPatternCatalog
    {
        bool TryGetBoss(string bossId, out BossPatternCatalogSO.BossDefinition boss);

        /// <summary> 보스와 페이즈에 속한 패턴을 결과 목록에 채운다, 목록은 먼저 비운다 </summary>
        void CollectPatterns(string bossId, int phase, List<BossPatternCatalogSO.PatternDefinition> results);

        /// <summary> 보스와 패턴의 필수 수치가 모두 지정됐는가, 거짓이면 전투를 열지 않는다 </summary>
        bool IsTuned(string bossId);
    }
}
