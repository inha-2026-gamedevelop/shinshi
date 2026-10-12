// System
using System;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.AI
{
    // 선택 창의 보스 ID + 난이도를 진행 카탈로그의 보스전 ID로 바꾸는 순수 규칙이다. 엔진 오브젝트와 GameDB를 직접 읽지 않아 시험에서 카탈로그를 넘긴다
    // 대응하는 보스전이 없으면 같은 보스의 다른 난이도나 다른 보스의 보스전으로 바꾸지 않고 사유만 돌려준다
    // 해금, 입장권, 파티 조건 같은 입장 판정은 하지 않는다. 그것은 서버가 정한다
    /// <summary> 보스 ID와 난이도를 정확한 보스전 ID로 해석한다 </summary>
    public static class BossSelectRules
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 보스 ID와 난이도에 정확히 대응하는 보스전을 찾는다, 없으면 사유가 담긴 선택 값을 돌려준다 </summary>
        public static BossBattleSelection Resolve(ProgressionCatalogSO progression, BossPatternCatalogSO bossPatterns, string bossId,
            EBossDifficulty difficulty)
        {
            if (string.IsNullOrEmpty(bossId))
            {
                return new BossBattleSelection(EBossBattleSelectState.None, bossId, difficulty, string.Empty, false);
            }

            if ((progression == null) || (bossPatterns == null))
            {
                return new BossBattleSelection(EBossBattleSelectState.MissingCatalog, bossId, difficulty, string.Empty, false);
            }

            if (!bossPatterns.ContainsBoss(bossId))
            {
                return new BossBattleSelection(EBossBattleSelectState.UnknownBoss, bossId, difficulty, string.Empty, false);
            }

            if ((!Enum.IsDefined(typeof(EBossDifficulty), difficulty))
                || (!progression.TryGetBattle(bossId, difficulty, out ProgressionCatalogSO.BossBattleDefinition battle)))
            {
                return new BossBattleSelection(EBossBattleSelectState.NoBattleForDifficulty, bossId, difficulty, string.Empty, false);
            }

            return new BossBattleSelection(EBossBattleSelectState.Resolved, bossId, difficulty, battle.DefinitionId, battle.AvailableInTrial);
        }

        /// <summary> 그 보스에 그 난이도의 보스전이 있는가, 난이도 단추를 켜고 끄는 데 쓴다 </summary>
        public static bool HasBattle(ProgressionCatalogSO progression, BossPatternCatalogSO bossPatterns, string bossId,
            EBossDifficulty difficulty)
        {
            return Resolve(progression, bossPatterns, bossId, difficulty).IsResolved;
        }
    }
}
