#if UNITY_EDITOR
// System
using System;
using System.Collections.Generic;
using System.IO;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Data;
using Shinshi.Common;

namespace Shinshi.UI.Editor
{
    /// <summary> 생성 원본을 64픽셀 런타임 아이콘으로 축소하고 정의 ID 표에 연결한다 </summary>
    public static class ItemIcon64Builder
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string OUTPUT_FOLDER = "Assets/01.Scripts/05.Art/UI/Icons/Textures/Items64";
        public const string LIBRARY_PATH  = "Assets/01.Scripts/05.Art/UI/Icons/UiIconLibrary.asset";
        private const string SOURCE_FOLDER = ".claude/docs/2026-10-15/item-icons";
        private const string EQUIPMENT_PATH = "Assets/09.Data/Growth/EquipmentCatalog.asset";
        private const string REWARD_PATH    = "Assets/09.Data/Reward/RewardCatalog.asset";
        private const string LIFE_PATH      = "Assets/09.Data/Life/LifeConfig.asset";
        private const string TALISMAN_PATH  = "Assets/09.Data/Combat/TalismanCatalog.asset";
        private const int ICON_SIZE          = 64;
        private const int CHANNEL_MAX        = 255;
        private const int TALISMAN_PREFIX_LENGTH = 3;
        private const string VARIANT_NAME = "TalismanVariant";
        private const float BADGE_WIDTH = 25f;
        private const float BADGE_HEIGHT = 17f;
        private const float BADGE_INSET = 3f;
        private const float BADGE_FONT_SIZE = 12f;
        private static readonly Color G_BadgeBackground = new Color(0.03f, 0.07f, 0.11f, 0.92f);
        private static readonly Color G_BadgeText = new Color(0.85f, 0.82f, 1f, 1f);
        private static readonly string[] G_SlotKeys = { "weapon", "head", "body", "hands", "feet", "accessory" };
        private static readonly string[] G_SlotPrefabPaths =
        {
            "Assets/02.Prefab/UI/PF_ItemSlot.prefab",
            "Assets/02.Prefab/UI/PF_ItemSlot_Flat.prefab"
        };

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem("Shinshi/UI/아이템 64px 전체 적용")]
        public static void ApplyAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("아이템 아이콘 연결은 편집 모드에서 실행한다");
            }
            ImportAvailable();
            ApplyLibrary();
            ApplyVariantBadges();
            Shinshi.Editor.EquipmentEnhanceWindowBuilder.BuildPrefabOnly();
            EquipmentOptionWindowBuilder.Build();
            Verify();
        }

        [MenuItem("Shinshi/UI/아이템 64px 원본 임포트")]
        public static void ImportAvailable()
        {
            Directory.CreateDirectory(OUTPUT_FOLDER);
            string[] groups = { "equipment", "materials", "talismans" };
            foreach (string group in groups)
            {
                string folder = Path.Combine(SOURCE_FOLDER, group);
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                foreach (string source in Directory.GetFiles(folder, "*.png", SearchOption.TopDirectoryOnly))
                {
                    string key = Path.GetFileNameWithoutExtension(source);
                    if (group == "equipment")
                    {
                        key = "equip_" + key;
                    }

                    ImportOne(source, OUTPUT_FOLDER + "/T_Item64_" + key + ".png");
                }
            }
            foreach (string target in Directory.GetFiles(OUTPUT_FOLDER, "T_Item64_*.png", SearchOption.TopDirectoryOnly))
            {
                ConfigureImporter(target.Replace('\\', '/'));
            }
        }

        [MenuItem("Shinshi/UI/아이템 64px 라이브러리 연결")]
        public static void ApplyLibrary()
        {
            Dictionary<string, Sprite> icons = CollectIcons();
            int definitionCount = icons.Count;
            foreach (string slot in G_SlotKeys)
            {
                icons.Add("equip_" + slot, LoadIcon("equip_" + slot));
            }
            UiIconLibrarySO library = AssetDatabase.LoadAssetAtPath<UiIconLibrarySO>(LIBRARY_PATH);
            var serialized = new SerializedObject(library);
            SerializedProperty entries = serialized.FindProperty("_entries");
            foreach (KeyValuePair<string, Sprite> pair in icons)
            {
                int index = FindEntry(entries, pair.Key);
                if (index < 0)
                {
                    index = entries.arraySize;
                    ++entries.arraySize;
                }

                SerializedProperty entry = entries.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("_id").stringValue = pair.Key;
                entry.FindPropertyRelative("_sprite").objectReferenceValue = pair.Value;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssetIfDirty(library);
            Debug.Log("아이템 아이콘 연결: " + definitionCount + "개 정의, 장비 공통 키 " + G_SlotKeys.Length + "개");
        }

        public static Dictionary<string, Sprite> CollectIcons()
        {
            var icons = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            EquipmentCatalogSO equipment = AssetDatabase.LoadAssetAtPath<EquipmentCatalogSO>(EQUIPMENT_PATH);
            foreach (EquipmentCatalogSO.EquipmentDefinition item in equipment.Equipment)
            {
                icons.Add(item.DefinitionId, LoadIcon("equip_" + G_SlotKeys[(int)item.Slot]));
            }

            RewardCatalogSO rewards = AssetDatabase.LoadAssetAtPath<RewardCatalogSO>(REWARD_PATH);
            foreach (RewardCatalogSO.ItemDefinition item in rewards.Items)
            {
                icons.Add(item.DefinitionId, LoadIcon(item.DefinitionId));
            }

            LifeConfigSO life = AssetDatabase.LoadAssetAtPath<LifeConfigSO>(LIFE_PATH);
            foreach (LifeConfigSO.HerbDefinition item in life.Herbs)
            {
                icons.Add(item.DefinitionId, LoadIcon(item.DefinitionId));
            }
            foreach (LifeConfigSO.PotionDefinition item in life.Potions)
            {
                icons.Add(item.DefinitionId, LoadIcon(item.DefinitionId));
            }

            TalismanCatalogSO talismans = AssetDatabase.LoadAssetAtPath<TalismanCatalogSO>(TALISMAN_PATH);
            foreach (TalismanCatalogSO.TalismanEffectDefinition item in talismans.Effects)
            {
                icons.Add(item.DefinitionId, LoadIcon(item.DefinitionId.Substring(0, TALISMAN_PREFIX_LENGTH)));
            }
            return icons;
        }

        [MenuItem("Shinshi/UI/아이템 칸 부적 구분 배지 연결")]
        public static void ApplyVariantBadges()
        {
            foreach (string path in G_SlotPrefabPaths)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    ItemSlotView view = root.GetComponent<ItemSlotView>();
                    var serialized = new SerializedObject(view);
                    TMP_Text count = serialized.FindProperty("_countLabel").objectReferenceValue as TMP_Text;
                    Transform existing = root.transform.Find(VARIANT_NAME);
                    GameObject badge = (existing != null) ? existing.gameObject
                        : new GameObject(VARIANT_NAME, typeof(RectTransform), typeof(Image));
                    badge.transform.SetParent(root.transform, false);
                    RectTransform rect = (RectTransform)badge.transform;
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.zero;
                    rect.pivot = Vector2.zero;
                    rect.anchoredPosition = new Vector2(BADGE_INSET, BADGE_INSET);
                    rect.sizeDelta = new Vector2(BADGE_WIDTH, BADGE_HEIGHT);
                    Image background = badge.GetComponent<Image>();
                    background.color = G_BadgeBackground;
                    background.raycastTarget = false;
                    TMP_Text label = badge.GetComponentInChildren<TMP_Text>(true);
                    if (label == null)
                    {
                        var textObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                        textObject.transform.SetParent(badge.transform, false);
                        RectTransform textRect = (RectTransform)textObject.transform;
                        textRect.anchorMin = Vector2.zero;
                        textRect.anchorMax = Vector2.one;
                        textRect.offsetMin = Vector2.zero;
                        textRect.offsetMax = Vector2.zero;
                        label = textObject.GetComponent<TextMeshProUGUI>();
                    }
                    label.font = count.font;
                    label.fontSize = BADGE_FONT_SIZE;
                    label.color = G_BadgeText;
                    label.alignment = TextAlignmentOptions.Center;
                    label.raycastTarget = false;
                    label.text = "1A";
                    serialized.FindProperty("_variantLabel").objectReferenceValue = label;
                    serialized.FindProperty("_variantBadge").objectReferenceValue = badge;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    badge.SetActive(false);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        [MenuItem("Shinshi/UI/아이템 64px 연결 검증")]
        public static void Verify()
        {
            Dictionary<string, Sprite> expected = CollectIcons();
            UiIconLibrarySO library = AssetDatabase.LoadAssetAtPath<UiIconLibrarySO>(LIBRARY_PATH);
            foreach (KeyValuePair<string, Sprite> pair in expected)
            {
                if (library.FindIcon(pair.Key) != pair.Value)
                {
                    throw new InvalidOperationException("아이콘 연결 불일치: " + pair.Key);
                }
                var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(pair.Value));
                if (importer.mipmapEnabled || importer.isReadable || !importer.alphaIsTransparency
                    || (importer.textureCompression != TextureImporterCompression.Uncompressed))
                {
                    throw new InvalidOperationException("아이콘 임포트 설정 불일치: " + pair.Key);
                }
            }

            foreach (string path in G_SlotPrefabPaths)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    ItemSlotView view = root.GetComponent<ItemSlotView>();
                    Transform badge = root.transform.Find(VARIANT_NAME);
                    if (badge == null)
                    {
                        throw new InvalidOperationException("부적 배지 없음: " + path);
                    }
                    TMP_Text label = badge.GetComponentInChildren<TMP_Text>(true);
                    string[] cards = { "T13-1A", "T13-3B" };
                    foreach (string id in cards)
                    {
                        view.SetData(new ItemSlotData(id, EItemGrade.Rare, 1, id, ""));
                        if (!badge.gameObject.activeSelf || (label.text != id.Substring(4))
                            || (view.IconSprite != expected[id]))
                        {
                            throw new InvalidOperationException("부적 칸 표시 불일치: " + path + " / " + id);
                        }
                    }
                    view.SetData(new ItemSlotData("item_herb", EItemGrade.Common, 12, "약초", "", true));
                    if (badge.gameObject.activeSelf || (view.IconSprite != expected["item_herb"]))
                    {
                        throw new InvalidOperationException("일반 아이템 재사용 표시 불일치: " + path);
                    }
                    view.SetData(default(ItemSlotData));
                    if (badge.gameObject.activeSelf || (view.IconSprite != null))
                    {
                        throw new InvalidOperationException("빈 칸 재사용 표시 불일치: " + path);
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            Debug.Log("아이콘 검증 통과: " + expected.Count + "개 정의 / 64px / 임포트 설정 / 기본·Flat 칸 부적 전환과 재사용");
        }

        private static int FindEntry(SerializedProperty entries, string id)
        {
            for (int i = 0; i < entries.arraySize; ++i)
            {
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("_id").stringValue == id)
                {
                    return i;
                }
            }
            return -1;
        }

        private static Sprite LoadIcon(string key)
        {
            string path = OUTPUT_FOLDER + "/T_Item64_" + key + ".png";
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if ((sprite == null) || (sprite.rect.width != ICON_SIZE) || (sprite.rect.height != ICON_SIZE))
            {
                throw new InvalidOperationException("64px 아이콘이 준비되지 않았습니다: " + path);
            }
            return sprite;
        }

        private static void ImportOne(string sourcePath, string targetPath)
        {
            // 원본은 Assets 밖에 보존하고 런타임에는 축소본만 넣는다.
            if (!File.Exists(targetPath) || (File.GetLastWriteTimeUtc(sourcePath) > File.GetLastWriteTimeUtc(targetPath)))
            {
                string preparedPath = Path.Combine(SOURCE_FOLDER, "prepared64", Path.GetFileName(targetPath));
                if (File.Exists(preparedPath) && (File.GetLastWriteTimeUtc(preparedPath) >= File.GetLastWriteTimeUtc(sourcePath)))
                {
                    File.Copy(preparedPath, targetPath, true);
                }
                else
                {
                    var source = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    var target = new Texture2D(ICON_SIZE, ICON_SIZE, TextureFormat.RGBA32, false);
                    try
                    {
                        if (!ImageConversion.LoadImage(source, File.ReadAllBytes(sourcePath), false))
                        {
                            throw new InvalidOperationException("PNG 읽기 실패: " + sourcePath);
                        }
                        if (source.width != source.height)
                        {
                            throw new InvalidOperationException("정사각 원본이 필요합니다: " + sourcePath);
                        }

                        target.SetPixels32(Downsample(source.GetPixels32(), source.width));
                        target.Apply(false, false);
                        File.WriteAllBytes(targetPath, ImageConversion.EncodeToPNG(target));
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(source);
                        UnityEngine.Object.DestroyImmediate(target);
                    }
                }
            }

        }

        private static void ConfigureImporter(string targetPath)
        {
            AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(targetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.sRGBTexture = true;
            importer.maxTextureSize = ICON_SIZE;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
        }

        private static Color32[] Downsample(Color32[] source, int size)
        {
            var result = new Color32[ICON_SIZE * ICON_SIZE];
            float step = (float)size / ICON_SIZE;
            for (int y = 0; y < ICON_SIZE; ++y)
            {
                for (int x = 0; x < ICON_SIZE; ++x)
                {
                    double red = 0d, green = 0d, blue = 0d, alpha = 0d, weightSum = 0d;
                    float xMin = x * step, xMax = (x + 1) * step;
                    float yMin = y * step, yMax = (y + 1) * step;
                    for (int sy = Mathf.FloorToInt(yMin); sy < Mathf.CeilToInt(yMax); ++sy)
                    {
                        for (int sx = Mathf.FloorToInt(xMin); sx < Mathf.CeilToInt(xMax); ++sx)
                        {
                            float weight = (Mathf.Min(sx + 1, xMax) - Mathf.Max(sx, xMin))
                                * (Mathf.Min(sy + 1, yMax) - Mathf.Max(sy, yMin));
                            Color32 pixel = source[Mathf.Min(sy, size - 1) * size + Mathf.Min(sx, size - 1)];
                            double opacity = (double)pixel.a / CHANNEL_MAX * weight;
                            red += pixel.r * opacity;
                            green += pixel.g * opacity;
                            blue += pixel.b * opacity;
                            alpha += opacity;
                            weightSum += weight;
                        }
                    }

                    // 투명 여백의 RGB를 섞지 않아 축소한 실루엣에 검은 테두리가 생기지 않게 한다.
                    result[y * ICON_SIZE + x] = alpha > 0d
                        ? new Color32((byte)Math.Round(red / alpha), (byte)Math.Round(green / alpha),
                            (byte)Math.Round(blue / alpha), (byte)Math.Round(alpha / weightSum * CHANNEL_MAX))
                        : new Color32(0, 0, 0, 0);
                }
            }
            return result;
        }
    }
}
#endif
