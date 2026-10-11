// System
using System.IO;
using System.Text;

// Unity
using UnityEditor;
using UnityEngine;

// 프로젝트 내부
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.Editor
{
    /// <summary> 기존 루트를 보존하고 경험치 도메인만 추가한다 </summary>
    public static class CharacterLevelContentBuilder
    {

        /****************************************
        *                Fields
        ****************************************/
        public const string CONTENT_VERSION = "0.1.2";
        public const string ASSET_PATH = "Assets/09.Data/Growth/CharacterLevelConfig.asset";

        [MenuItem("Shinshi/Build Character Level Content")]
        public static void Build()
        {
            GameDatabaseSO root = Resources.Load<GameDatabaseSO>("GameDB");
            if (root == null)
            {
                throw new System.InvalidOperationException("GameDB가 없다");
            }

            CharacterLevelConfigSO levels = AssetDatabase.LoadAssetAtPath<CharacterLevelConfigSO>(ASSET_PATH);
            if (levels == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/09.Data/Growth"))
                {
                    AssetDatabase.CreateFolder("Assets/09.Data", "Growth");
                }

                levels = ScriptableObject.CreateInstance<CharacterLevelConfigSO>();
                AssetDatabase.CreateAsset(levels, ASSET_PATH);
            }

            SerializedObject serialized = new SerializedObject(root);
            serialized.FindProperty("_characterLevel").objectReferenceValue = levels;
            serialized.FindProperty("_contentVersion").stringValue = CONTENT_VERSION;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(root);
            AssetDatabase.SaveAssetIfDirty(levels);
            AssetDatabase.SaveAssetIfDirty(root);
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            File.WriteAllText(GrowthContentExport.GetPath(projectRoot), GrowthContentExport.Serialize(root), new UTF8Encoding(false));
            Debug.Log("[CharacterLevel] 경험치 설정과 백엔드 콘텐츠를 생성했다");
        }
    }
}
