// System
using System;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 출정 계약의 보스로 던전 서버와 클라이언트가 함께 여는 전장 씬을 고른다, 순수 규칙이라 씬 없이 시험한다 </summary>
    public static class DungeonMapRules
    {
        /****************************************
        *                Fields
        ****************************************/

        // 보스 정본 ID는 GameDB BossPatterns와 ProgressionCatalogSO의 보스전 정의가 쓰는 값이다
        private const string MUCHEOL_BOSS_ID = "boss_mucheol";
        private const string WOLBAEK_BOSS_ID = "boss_wolbaek";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 던전이 여는 씬 이름, 전용 전장이 있는 보스만 그 씬이고 일반전과 그 밖의 보스는 숲이다 </summary>
        public static string ResolveSceneName(bool isBossBattle, string bossId)
        {
            if (!isBossBattle)
            {
                return Constants.Scene.DUNGEON_FOREST;
            }

            if (string.Equals(bossId, MUCHEOL_BOSS_ID, StringComparison.Ordinal))
            {
                return Constants.Scene.DUNGEON_MUCHEOL;
            }

            if (string.Equals(bossId, WOLBAEK_BOSS_ID, StringComparison.Ordinal))
            {
                return Constants.Scene.DUNGEON_WOLBAEK;
            }

            return Constants.Scene.DUNGEON_FOREST;
        }

        /// <summary> 프롤로그 출정이 여는 씬 이름, 보스 ID와 무관하게 고정이다 </summary>
        public static string ResolvePrologueSceneName()
        {
            return Constants.Scene.DUNGEON_PROLOGUE;
        }

        /// <summary> 실행 설정 보스(-boss-id)로 서버가 부팅 때 여는 씬 이름, 보스가 없으면 숲이다 </summary>
        public static string ResolveBootSceneName(string defaultBossId)
        {
            return ResolveSceneName(!string.IsNullOrEmpty(defaultBossId), defaultBossId);
        }

        public static string ToScenePath(string sceneName)
        {
            return Constants.Scene.RELEASE_FOLDER + sceneName + Constants.Scene.EXTENSION;
        }

        /// <summary> 활성 씬 이름이 던전 전장 씬 중 하나인지 </summary>
        public static bool IsDungeonSceneName(string sceneName)
        {
            return string.Equals(sceneName, Constants.Scene.DUNGEON_FOREST, StringComparison.Ordinal)
                || string.Equals(sceneName, Constants.Scene.DUNGEON_MUCHEOL, StringComparison.Ordinal)
                || string.Equals(sceneName, Constants.Scene.DUNGEON_WOLBAEK, StringComparison.Ordinal)
                || string.Equals(sceneName, Constants.Scene.DUNGEON_PROLOGUE, StringComparison.Ordinal);
        }
    }
}
