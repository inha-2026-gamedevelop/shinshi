#if UNITY_EDITOR
// System
using System.Collections.Generic;
using System.IO;
using System.Text;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Data;

namespace Shinshi.UI.Editor
{
    /// <summary> 입장 창과 NPC 이름표가 쓰는 한글 동적 글꼴 에셋을 만든다 </summary>
    // 다른 창의 글꼴 에셋은 건드리지 않고 이 기능 전용 에셋을 둔다(신체 강화 창과 같은 방식). 원본은 HUD 글꼴 폴더의 나눔고딕이다
    public static class DungeonEntryFonts
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string FONT_FOLDER = "Assets/01.Scripts/06.UI/DungeonEntry/Fonts";

        private const string SOURCE_FONT_FOLDER = "Assets/01.Scripts/06.UI/Hud/Fonts/NanumGothic/Fonts";
        private const string CATALOG_PATH       = "Assets/09.Data/Progression/ProgressionCatalog.asset";
        private const string BASE_CHARACTERS    = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789%+-/.,:()[] ";

        private const int FONT_POINT_SIZE = 48;
        private const int FONT_PADDING    = 6;
        private const int FONT_ATLAS_SIZE = 1024;

        /****************************************
        *                Methods
        ****************************************/

        public static TMP_FontAsset Ensure(string weight)
        {
            if (!AssetDatabase.IsValidFolder(FONT_FOLDER))
            {
                AssetDatabase.CreateFolder("Assets/01.Scripts/06.UI/DungeonEntry", "Fonts");
            }

            string        path     = FONT_FOLDER + "/DungeonEntryKorean-" + weight + ".asset";
            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null)
            {
                return existing;
            }

            Font source = AssetDatabase.LoadAssetAtPath<Font>(SOURCE_FONT_FOLDER + "/NanumGothic-" + weight + ".ttf");
            if (source == null)
            {
                throw new FileNotFoundException("나눔고딕 원본 글꼴을 찾을 수 없습니다: " + weight);
            }

            TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(source, FONT_POINT_SIZE, FONT_PADDING, GlyphRenderMode.SDFAA,
                FONT_ATLAS_SIZE, FONT_ATLAS_SIZE, AtlasPopulationMode.Dynamic, true);
            font.name = "DungeonEntryKorean-" + weight;
            AssetDatabase.CreateAsset(font, path);
            AssetDatabase.AddObjectToAsset(font.material, font);
            AssetDatabase.AddObjectToAsset(font.atlasTextures[0], font);
            return font;
        }

        /// <summary> 창과 이름표에 쓰는 글자를 미리 아틀라스에 넣는다. 카탈로그에서 만드는 목록 글자도 함께 넣는다 </summary>
        public static void Preload(TMP_FontAsset regular, TMP_FontAsset bold, IEnumerable<string> texts)
        {
            var characters = new StringBuilder(BASE_CHARACTERS);
            foreach (string text in texts)
            {
                characters.Append(text);
            }

            var catalog = AssetDatabase.LoadAssetAtPath<ProgressionCatalogSO>(CATALOG_PATH);
            if (catalog != null)
            {
                var entries = new List<DungeonEntryData>();
                DungeonEntryRules.Build(catalog, entries);
                for (int i = 0; i < entries.Count; ++i)
                {
                    characters.Append(entries[i].Title).Append(entries[i].Category).Append(entries[i].Summary)
                        .Append(entries[i].Requirement).Append(entries[i].Cost).Append(entries[i].UnavailableReason);
                }
            }

            string all = characters.ToString();
            regular.TryAddCharacters(all);
            bold.TryAddCharacters(all);
            EditorUtility.SetDirty(regular);
            EditorUtility.SetDirty(bold);
            AssetDatabase.SaveAssetIfDirty(regular);
            AssetDatabase.SaveAssetIfDirty(bold);
        }
    }
}
#endif
