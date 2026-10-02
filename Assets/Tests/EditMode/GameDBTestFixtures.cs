// System
using System;
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;

// 프로젝트 내부
using Shinshi.Data;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    /// <summary> 필드 초기화식 초안 값으로 채운 GameDB 루트를 만든다 </summary>
    public static class GameDBTestFixtures
    {
        /****************************************
        *                Fields
        ****************************************/

        // GameDatabaseSO의 필수 참조 필드와 타입, 검증 오류 순서와 같다
        private static readonly (string Field, Type Type)[] REFERENCES =
        {
            ("_player",       typeof(PlayerConfigSO)),
            ("_session",      typeof(SessionConfigSO)),
            ("_skills",       typeof(SkillCatalogSO)),
            ("_talismans",    typeof(TalismanCatalogSO)),
            ("_enemies",      typeof(EnemyCatalogSO)),
            ("_bossPatterns", typeof(BossPatternCatalogSO)),
            ("_waves",        typeof(WaveConfigSO)),
            ("_equipment",    typeof(EquipmentCatalogSO)),
            ("_growth",       typeof(GrowthConfigSO)),
            ("_rewards",      typeof(RewardCatalogSO)),
            ("_life",         typeof(LifeConfigSO)),
            ("_progression",  typeof(ProgressionCatalogSO)),
            ("_avatar",       typeof(AvatarCatalogSO)),
            ("_camera",       typeof(CameraConfigSO))
        };

        public static int ReferenceCount => REFERENCES.Length;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 만든 인스턴스는 created에 넣으니 호출자가 TearDown에서 지운다 </summary>
        public static GameDatabaseSO CreateValidRoot(List<Object> created)
        {
            GameDatabaseSO root = ScriptableObject.CreateInstance<GameDatabaseSO>();
            created.Add(root);

            var serialized = new SerializedObject(root);
            for (int i = 0; i < REFERENCES.Length; ++i)
            {
                ScriptableObject child = ScriptableObject.CreateInstance(REFERENCES[i].Type);
                created.Add(child);
                serialized.FindProperty(REFERENCES[i].Field).objectReferenceValue = child;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return root;
        }
    }
}
