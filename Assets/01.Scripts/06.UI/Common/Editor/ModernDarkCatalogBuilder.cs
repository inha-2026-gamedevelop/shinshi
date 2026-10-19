#if UNITY_EDITOR
// Unity
using UnityEditor;
using UnityEngine;

// 서드파티
using TMPro;

namespace Shinshi.UI.Editor
{
    /// <summary> Creates or refreshes the Resources catalog that references the original training prefab, nothing is copied </summary>
    public static class ModernDarkCatalogBuilder
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string CATALOG_PATH = "Assets/02.Prefab/UI/Resources/" + ModernDarkUiCatalog.RESOURCE_NAME + ".asset";

        private const string MENU_PATH            = "Shinshi/UI/Build Modern Dark Catalog";
        private const string RESOURCES_FOLDER     = "Assets/02.Prefab/UI/Resources";
        private const string TRAINING_PREFAB_GUID = "82f63217435eab5439270245912d2745";
        private const string BACKGROUND_PATH      = "Assets/01.Scripts/05.Art/UI/ModernDark/Textures/MoonlitShrineBackground.png";
        private const string FONT_PATH            ="Assets/01.Scripts/06.UI/Hud/Fonts/HudQuestKorean-Regular.asset";

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem(MENU_PATH)]
        public static void BuildFromMenu()
        {
            ModernDarkUiCatalog catalog = Build();
            if (catalog != null)
            {
                Selection.activeObject = catalog;
            }
        }

        /// <summary> Returns the catalog, or null when the training prefab cannot be found by its GUID </summary>
        public static ModernDarkUiCatalog Build()
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(TRAINING_PREFAB_GUID);
            var prefab        = string.IsNullOrEmpty(prefabPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError("[ModernDark] 수련 창 프리팹(GUID " + TRAINING_PREFAB_GUID + ")을 찾지 못해 카탈로그를 만들지 않았다");
                return null;
            }

            if (!AssetDatabase.IsValidFolder(RESOURCES_FOLDER))
            {
                AssetDatabase.CreateFolder("Assets/02.Prefab/UI", "Resources");
            }

            var font    = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_PATH);
            var catalog = AssetDatabase.LoadAssetAtPath<ModernDarkUiCatalog>(CATALOG_PATH);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ModernDarkUiCatalog>();
                AssetDatabase.CreateAsset(catalog, CATALOG_PATH);
            }

            catalog.Assign(prefab, font, LoadBackground());
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
            Debug.Log("[ModernDark] 카탈로그를 갱신했다: " + CATALOG_PATH);
            return catalog;
        }

        // 이 이미지 하나만 스프라이트로 가져온다. 다른 에셋은 건드리거나 저장하지 않는다
        private static Sprite LoadBackground()
        {
            var importer = AssetImporter.GetAtPath(BACKGROUND_PATH) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning("[ModernDark] 배경 이미지가 없어 카탈로그 배경을 비워 둔다: " + BACKGROUND_PATH);
                return null;
            }

            if ((importer.textureType != TextureImporterType.Sprite) || (importer.spriteImportMode != SpriteImportMode.Single)
                || (importer.mipmapEnabled))
            {
                importer.textureType         = TextureImporterType.Sprite;
                importer.spriteImportMode    = SpriteImportMode.Single;
                importer.mipmapEnabled       = false;
                importer.alphaIsTransparency = false;
                importer.wrapMode            = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(BACKGROUND_PATH);
        }
    }
}
#endif
