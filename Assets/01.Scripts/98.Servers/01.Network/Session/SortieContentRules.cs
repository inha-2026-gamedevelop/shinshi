// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Network
{
    /// <summary> 출정 계약과 GameDB 원본으로 세션에 넣을 보상 원본을 고른다, 출정 시작 때 한 번만 부른다 </summary>
    public static class SortieContentRules
    {
        // 시작 때 한 번 도는 경로라 리스트, 배열 할당을 허용한다

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 웨이브 안 배치 순번(0부터)의 적 정의 ID, Enemies 항목을 앞에서부터 개수만큼 이어 센다 </summary>
        public static bool TryGetEnemyId(WaveConfigSO.WaveDefinition wave, int spawnIndex, out string enemyId)
        {
            enemyId = null;
            if ((wave == null) || (wave.Enemies == null) || (spawnIndex < 0))
            {
                return false;
            }

            int cursor = spawnIndex;
            for (int i = 0; i < wave.Enemies.Count; ++i)
            {
                WaveConfigSO.WaveEnemyEntry entry = wave.Enemies[i];
                if ((entry == null) || (entry.Count <= 0))
                {
                    continue;
                }

                if (cursor < entry.Count)
                {
                    enemyId = entry.EnemyId;
                    return true;
                }

                cursor -= entry.Count;
            }

            return false;
        }

        /// <summary> 배치 계획 전체의 source ID와 적 드롭을 세션에 등록한다, 등록한 수를 돌려준다(실패하면 그 자리에서 멈춘다) </summary>
        private static int ExperienceKind(EnemyCatalogSO enemies, string enemyId)
        {
            if ((enemies == null) || !enemies.TryGetEnemy(enemyId, out EnemyCatalogSO.EnemyDefinition enemy))
            {
                return 0;
            }
            return enemy.Role == EEnemyRole.Elite ? Shinshi.Contracts.CharacterExperienceSource.ELITE_ENEMY
                : Shinshi.Contracts.CharacterExperienceSource.NORMAL_ENEMY;
        }

        public static int RegisterWaveSources(DungeonSession session, WaveConfigSO waves, RewardCatalogSO rewards, EnemyCatalogSO enemies = null)
        {
            if ((session == null) || (waves == null) || (rewards == null) || (waves.Waves == null))
            {
                return 0;
            }

            int registered = 0;
            for (int waveIndex = 0; waveIndex < waves.Waves.Count; ++waveIndex)
            {
                WaveConfigSO.WaveDefinition wave = waves.Waves[waveIndex];
                int count = (wave != null) ? wave.CountEnemies() : 0;
                for (int spawnIndex = 0; spawnIndex < count; ++spawnIndex)
                {
                    if ((!TryGetEnemyId(wave, spawnIndex, out string enemyId))
                        || (!rewards.TryGetEnemyDrop(enemyId, out RewardCatalogSO.EnemyDropSpec drop))
                        || (!session.RegisterEnemySource(waveIndex, waves.GetSourceId(waveIndex, spawnIndex), drop, ExperienceKind(enemies, enemyId))))
                    {
                        return registered;
                    }

                    ++registered;
                }
            }

            return registered;
        }

        /// <summary> 한 직업의 전체 부적 효과 ID를 모은다, 카탈로그 순서 그대로다(세션이 정렬한다) </summary>
        public static void CollectClassEffects(TalismanCatalogSO talismans, EPlayerClass playerClass, List<string> effectIds)
        {
            if (effectIds == null)
            {
                throw new ArgumentNullException(nameof(effectIds));
            }

            effectIds.Clear();
            if ((talismans == null) || (talismans.Effects == null))
            {
                return;
            }

            for (int i = 0; i < talismans.Effects.Count; ++i)
            {
                TalismanCatalogSO.TalismanEffectDefinition effect = talismans.Effects[i];
                if ((effect != null) && (effect.PlayerClass == playerClass) && (!string.IsNullOrEmpty(effect.DefinitionId)))
                {
                    effectIds.Add(effect.DefinitionId);
                }
            }
        }

        /// <summary> 보스와 난이도에 맞는 보상 표를 찾는다 </summary>
        public static bool TryFindBossReward(IReadOnlyList<RewardCatalogSO.BossRewardSpec> specs, string bossId,
            EBossDifficulty difficulty, out RewardCatalogSO.BossRewardSpec spec)
        {
            spec = null;
            if ((specs == null) || (string.IsNullOrEmpty(bossId)))
            {
                return false;
            }

            for (int i = 0; i < specs.Count; ++i)
            {
                RewardCatalogSO.BossRewardSpec candidate = specs[i];
                if ((candidate != null) && (string.Equals(candidate.BossId, bossId, StringComparison.Ordinal))
                    && (candidate.Difficulty == difficulty))
                {
                    spec = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary> 출정에서 열 보스, 보스전 계약이면 계약 값이고 그 밖에는 실행 설정 기본 보스(-boss-id)다 </summary>
        public static string ResolveBossId(in SortieDescriptor sortie, string defaultBossId)
        {
            return sortie.IsBoss ? sortie.BossId : defaultBossId;
        }
    }
}
