// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary> 던전 선택 창을 공통 어두운 스타일로 한 번 다시 배치한다. 지도 그림 같은 실제 자료가 없는 것은 만들지 않고 목록 · 설명 · 입장 버튼은 DungeonEntryView 직렬화 참조 그대로다 </summary>
    public static class DungeonEntryModernLayout
    {
        private const float WIDTH    = 1500f;
        private const float HEIGHT   = 820f;
        private const float PANEL_Y  = 84f;
        private const float PANEL_H  = 640f;

        private const float LIST_X   = 32f;
        private const float LIST_W   = 420f;
        private const float DETAIL_X = 472f;
        private const float DETAIL_W = 996f;

        private const float ROW_GAP  = 8f;
        private const float ROW_MAX  = 96f;
        private const float SCENE_H  = 340f;

        public static void Apply(DungeonEntryView view)
        {
            var window = view.transform as RectTransform;
            TMP_Text notice = ModernDarkKit.Field<TMP_Text>(view, "_noticeLabel");
            if (!ModernDarkKit.TryBegin(view, window, notice, out ModernDarkKit kit))
            {
                return;
            }

            foreach (EquipmentGrowthSurface surface in window.GetComponentsInChildren<EquipmentGrowthSurface>(true))
            {
                surface.enabled = false;
            }

            kit.Frame(new Vector2(WIDTH, HEIGHT), "던전 선택");
            Image windowImage = window.GetComponent<Image>();
            if (windowImage != null)
            {
                windowImage.enabled = true;
            }

            kit.Inset("ListPanel", LIST_X, PANEL_Y, LIST_W, PANEL_H);
            kit.Inset("DetailPanel", DETAIL_X, PANEL_Y, DETAIL_W, PANEL_H);
            AddScenery(kit);

            Button close = ModernDarkKit.Field<Button>(view, "_closeButton");
            kit.Place(close, WIDTH - 72f, 8f, 44f, 44f);
            ModernDarkKit.StyleButton(close, false);
            SkillWindowModernLayout.AddGlyph(kit, close, "×");

            LayoutRows(view, kit);
            LayoutDetail(view, kit);
            LayoutFooter(view, kit, notice);

            kit.HideLegacy();
        }

        /// <summary> 카탈로그 배경 그림을 설명 뒤에 깔고 어두운 막을 덮어 글을 읽기 쉽게 한다. 그림이 없으면 아무것도 만들지 않는다 </summary>
        private static void AddScenery(ModernDarkKit kit)
        {
            ModernDarkUiCatalog catalog = ModernDarkUiCatalog.Load();
            Sprite background = (catalog != null) ? catalog.Background : null;
            if (background == null)
            {
                return;
            }

            RectTransform art = kit.Inset("DetailArt", DETAIL_X + 6f, PANEL_Y + 6f, DETAIL_W - 12f, SCENE_H);
            Image artImage = art.GetComponent<Image>();
            artImage.sprite        = background;
            artImage.type          = Image.Type.Simple;
            artImage.preserveAspect = false;
            artImage.color         = Color.white;
            artImage.raycastTarget = false;

            RectTransform veil = kit.Inset("DetailVeil", DETAIL_X + 6f, PANEL_Y + 6f, DETAIL_W - 12f, SCENE_H);
            Image veilImage = veil.GetComponent<Image>();
            veilImage.sprite        = null;
            veilImage.type          = Image.Type.Simple;
            veilImage.color         = new Color(0.02f, 0.035f, 0.055f, 0.72f);
            veilImage.raycastTarget = false;
        }

        private static void LayoutRows(DungeonEntryView view, ModernDarkKit kit)
        {
            var buttons    = ModernDarkKit.Field<Button[]>(view, "_rowButtons");
            var titles     = ModernDarkKit.Field<TMP_Text[]>(view, "_rowTitles");
            var categories = ModernDarkKit.Field<TMP_Text[]>(view, "_rowCategories");
            var marks      = ModernDarkKit.Field<GameObject[]>(view, "_rowMarks");

            int count = (buttons != null) ? buttons.Length : 0;
            float avail = PANEL_H - 24f;
            float rowH  = (count > 0) ? Mathf.Min(ROW_MAX, (avail / count) - ROW_GAP) : ROW_MAX;
            float x = LIST_X + 12f;
            float w = LIST_W - 24f;
            for (int i = 0; i < count; ++i)
            {
                float y = PANEL_Y + 12f + (i * (rowH + ROW_GAP));
                kit.Place(buttons[i], x, y, w, rowH);
                ModernDarkKit.StyleButton(buttons[i], false);

                float top = y + Mathf.Max(4f, (rowH - 68f) * 0.5f);
                Text(kit, At(titles, i), x + 20f, top, w - 40f, 38f, 26f, UiModernDark.TextMain, TextAlignmentOptions.Left);
                Text(kit, At(categories, i), x + 20f, top + 38f, w - 40f, 28f, 19f, UiModernDark.TextMuted, TextAlignmentOptions.Left);

                GameObject mark = At(marks, i);
                kit.Place(mark, x, y, w, rowH);
                if ((mark != null) && (mark.TryGetComponent(out Image image)))
                {
                    ModernDarkKit.SetSliced(image, UiModernDark.SPRITE_SELECTED, Color.white);
                }
            }

            GameObject empty = ModernDarkKit.Field<GameObject>(view, "_emptyRoot");
            kit.PlaceGroup(empty, x, PANEL_Y + 40f, w, 90f, false);
        }

        private static void LayoutDetail(DungeonEntryView view, ModernDarkKit kit)
        {
            GameObject root = ModernDarkKit.Field<GameObject>(view, "_detailRoot");
            if (root == null)
            {
                return;
            }

            kit.PlaceGroup(root, DETAIL_X, PANEL_Y, DETAIL_W, PANEL_H, false);
            ModernDarkKit.Quiet(root, false);

            float x = DETAIL_X + 40f;
            float w = DETAIL_W - 80f;
            Text(kit, ModernDarkKit.Field<TMP_Text>(view, "_detailCategory"), x, PANEL_Y + 28f, w, 32f, 22f, UiModernDark.Cyan, TextAlignmentOptions.Left);
            Text(kit, ModernDarkKit.Field<TMP_Text>(view, "_detailTitle"), x, PANEL_Y + 64f, w, 64f, 48f, UiModernDark.TextGold, TextAlignmentOptions.Left);
            Text(kit, ModernDarkKit.Field<TMP_Text>(view, "_detailSummary"), x, PANEL_Y + 148f, w, 180f, 24f, UiModernDark.TextMain, TextAlignmentOptions.TopLeft, true);

            float halfW = (w - 24f) * 0.5f;
            // InsetIn은 묶음 기준 좌표라 묶음이 놓인 왼쪽 위(DETAIL_X, PANEL_Y)를 뺀 값을 준다
            kit.InsetIn(root, "RequirementCard", x - DETAIL_X, 360f, halfW, 220f);
            kit.InsetIn(root, "CostCard", x - DETAIL_X + halfW + 24f, 360f, halfW, 220f);
            TMP_Text requirement = ModernDarkKit.Field<TMP_Text>(view, "_detailRequirement");
            TMP_Text cost        = ModernDarkKit.Field<TMP_Text>(view, "_detailCost");
            Text(kit, requirement, x + 24f, PANEL_Y + 412f, halfW - 48f, 150f, 22f, UiModernDark.TextMain, TextAlignmentOptions.TopLeft, true);
            Text(kit, cost, x + halfW + 48f, PANEL_Y + 412f, halfW - 48f, 150f, 22f, UiModernDark.TextGold, TextAlignmentOptions.TopLeft, true);

            // 옛 작은 제목("선행 조건", "입장 비용")은 직렬화 참조가 없으니 글자로 찾아 카드 안 윗줄에 맞춘다
            foreach (TMP_Text caption in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if ((caption == requirement) || (caption == cost) || (caption.name.StartsWith(UiModernDark.DECOR_PREFIX, System.StringComparison.Ordinal)))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(caption.text))
                {
                    continue;
                }

                string value = caption.text.Trim();
                if (value == "선행 조건")
                {
                    Text(kit, caption, x + 24f, PANEL_Y + 372f, halfW - 48f, 32f, 20f, UiModernDark.Cyan, TextAlignmentOptions.Left);
                }
                else if (value == "입장 비용")
                {
                    Text(kit, caption, x + halfW + 48f, PANEL_Y + 372f, halfW - 48f, 32f, 20f, UiModernDark.Cyan, TextAlignmentOptions.Left);
                }
            }
        }

        private static void LayoutFooter(DungeonEntryView view, ModernDarkKit kit, TMP_Text notice)
        {
            Text(kit, notice, LIST_X + 8f, PANEL_Y + PANEL_H + 24f, 960f, 56f, 20f, UiModernDark.TextMuted, TextAlignmentOptions.Left, true);

            Button enter = ModernDarkKit.Field<Button>(view, "_enterButton");
            kit.Place(enter, WIDTH - 32f - 420f, PANEL_Y + PANEL_H + 20f, 420f, 64f);
            ModernDarkKit.StyleButton(enter, true);
            SkillWindowModernLayout.FitButtonLabel(enter, 28f);
            TMP_Text label = ModernDarkKit.Field<TMP_Text>(view, "_enterLabel");
            if (label != null)
            {
                foreach (Shadow effect in label.GetComponents<Shadow>())
                {
                    effect.enabled = false;
                }

                ModernDarkKit.StyleText(label, 0f, UiModernDark.TextMain);
                label.alignment = TextAlignmentOptions.Center;
            }
        }

        private static void Text(ModernDarkKit kit, TMP_Text text, float x, float y, float w, float h, float size, Color color, TextAlignmentOptions align, bool wrap = false)
        {
            if (text == null)
            {
                return;
            }

            kit.Place(text, x, y, w, h);
            ModernDarkKit.StyleText(text, size, color);
            text.alignment        = align;
            text.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            text.overflowMode     = wrap ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
        }

        private static T At<T>(T[] values, int index) where T : class
        {
            return ((values != null) && (index >= 0) && (index < values.Length)) ? values[index] : null;
        }
    }
}
