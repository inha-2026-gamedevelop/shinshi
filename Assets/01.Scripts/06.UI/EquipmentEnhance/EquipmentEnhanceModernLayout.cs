// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary> 장비 강화 창을 공통 어두운 스타일로 한 번 다시 배치한다. 장비 · 확률 · 비용 글자와 결과 연출은 EquipmentEnhanceView 직렬화 참조 그대로다 </summary>
    public static class EquipmentEnhanceModernLayout
    {
        private const float WIDTH    = 1500f;
        private const float HEIGHT   = 748f;
        private const float PANEL_Y  = 84f;
        private const float PANEL_H  = 640f;

        private const float LEFT_X   = 32f;
        private const float LEFT_W   = 380f;
        private const float MID_X    = 432f;
        private const float MID_W    = 636f;
        private const float RIGHT_X  = 1088f;
        private const float RIGHT_W  = 380f;

        private const float SLOT_TOP = 140f;
        private const float SLOT_H   = 90f;
        private const float SLOT_GAP = 6f;
        private const float STAT_ROW_H = 48f;

        public static void Apply(EquipmentEnhanceView view)
        {
            var window = view.transform as RectTransform;
            TMP_Text status = ModernDarkKit.Field<TMP_Text>(view, "_statusText");
            if (!ModernDarkKit.TryBegin(view, window, status, out ModernDarkKit kit))
            {
                return;
            }

            // 옛 마감 효과가 메시를 덮어 창 바탕과 강화 단추 그림을 어둡게 가리므로 먼저 끈다. 창 바탕 Image의 raycast는 건드리지 않는다
            foreach (EquipmentGrowthSurface surface in window.GetComponentsInChildren<EquipmentGrowthSurface>(true))
            {
                surface.enabled = false;
            }

            kit.Frame(new Vector2(WIDTH, HEIGHT), "장비 강화");
            kit.Inset("SlotPanel", LEFT_X, PANEL_Y, LEFT_W, PANEL_H);
            kit.Inset("ItemPanel", MID_X, PANEL_Y, MID_W, PANEL_H);
            kit.Inset("CostPanel", RIGHT_X, PANEL_Y, RIGHT_W, PANEL_H);
            kit.Inset("StatPanel", MID_X + 16f, PANEL_Y + 420f, MID_W - 32f, 200f);
            kit.Inset("PityTrack", RIGHT_X + 20f, PANEL_Y + 380f, RIGHT_W - 40f, 16f);

            Button close = ModernDarkKit.Field<Button>(view, "_closeButton");
            kit.Place(close, WIDTH - 72f, 8f, 44f, 44f);
            kit.StyleClose(close);

            LayoutSlots(view, kit);
            LayoutItem(view, kit);
            LayoutStats(view, kit);
            LayoutCosts(view, kit);
            LayoutResult(view, kit);

            kit.HideLegacy();
        }

        private static void LayoutSlots(EquipmentEnhanceView view, ModernDarkKit kit)
        {
            kit.Label("SlotCaption", "강화할 장비 선택", 22f, UiModernDark.TextGold, TextAlignmentOptions.Left, true, LEFT_X + 24f, PANEL_Y + 16f, LEFT_W - 48f, 34f);
            kit.Line("SlotLine", LEFT_X + 24f, PANEL_Y + 58f, LEFT_W - 48f);

            var buttons    = ModernDarkKit.Field<Button[]>(view, "_slotButtons");
            var frames     = ModernDarkKit.Field<Image[]>(view, "_slotFrames");
            var icons      = ModernDarkKit.Field<Image[]>(view, "_slotIcons");
            var names      = ModernDarkKit.Field<TMP_Text[]>(view, "_slotNameTexts");
            var levels     = ModernDarkKit.Field<TMP_Text[]>(view, "_slotLevelTexts");
            var selections = ModernDarkKit.Field<GameObject[]>(view, "_slotSelections");

            int count = Mathf.Max(Len(buttons), Len(names));
            float x = LEFT_X + 12f;
            float w = LEFT_W - 24f;
            for (int i = 0; i < count; ++i)
            {
                float y = PANEL_Y + SLOT_TOP - 62f + (i * (SLOT_H + SLOT_GAP));
                Button button = At(buttons, i);
                kit.Place(button, x, y, w, SLOT_H);
                ModernDarkKit.StyleButton(button, false);

                // 단추 안쪽 위젯은 Place가 건너뛰므로 단추 기준 좌표로 직접 놓는다. 아이콘이 틀 안에 있으면 틀 기준이다
                Image frame = At(frames, i);
                Image icon  = At(icons, i);
                ModernDarkKit.PlaceChild(frame, 12f, 9f, 72f, 72f);
                bool iconInFrame = (icon != null) && (frame != null) && (icon.transform.parent == frame.transform);
                ModernDarkKit.PlaceChild(icon, iconInFrame ? 8f : 20f, iconInFrame ? 8f : 17f, 56f, 56f);
                Child(At(names, i), 100f, 12f, w - 100f - 84f, 34f, 23f, UiModernDark.TextMain, TextAlignmentOptions.Left);
                Child(At(levels, i), w - 84f, 12f, 72f, 34f, 22f, UiModernDark.TextGold, TextAlignmentOptions.Right);

                // 선택 표시는 단추 안쪽이라 Place가 건너뛰므로 단추 칸 전체에 직접 맞춘다
                GameObject selection = At(selections, i);
                if ((selection != null) && (selection.TryGetComponent(out Image image)))
                {
                    ModernDarkKit.PlaceChild(image, 0f, 0f, w, SLOT_H);
                    ModernDarkKit.SetSliced(image, UiModernDark.SPRITE_SELECTED, Color.white);
                }
            }
        }

        private static void LayoutItem(EquipmentEnhanceView view, ModernDarkKit kit)
        {
            Text(kit, ModernDarkKit.Field<TMP_Text>(view, "_itemNameText"), MID_X + 24f, PANEL_Y + 14f, MID_W - 48f, 44f, 34f, UiModernDark.TextGold, TextAlignmentOptions.Center);
            Text(kit, ModernDarkKit.Field<TMP_Text>(view, "_itemGradeText"), MID_X + 24f, PANEL_Y + 60f, MID_W - 48f, 30f, 21f, UiModernDark.TextMuted, TextAlignmentOptions.Center);
            kit.Line("ItemLine", MID_X + 120f, PANEL_Y + 96f, MID_W - 240f);

            float iconX = MID_X + ((MID_W - 240f) * 0.5f);
            var itemFrame = ModernDarkKit.Field<Image>(view, "_itemFrame");
            var itemIcon  = ModernDarkKit.Field<Image>(view, "_itemIcon");
            kit.Place(itemFrame, iconX, PANEL_Y + 110f, 240f, 240f);
            if ((itemIcon != null) && (itemFrame != null) && (itemIcon.transform.parent == itemFrame.transform))
            {
                ModernDarkKit.PlaceChild(itemIcon, 24f, 24f, 192f, 192f);
            }
            else
            {
                kit.Place(itemIcon, iconX + 24f, PANEL_Y + 134f, 192f, 192f);
            }

            float levelY = PANEL_Y + 356f;
            Text(kit, ModernDarkKit.Field<TMP_Text>(view, "_levelFromText"), MID_X + 100f, levelY, 190f, 56f, 42f, UiModernDark.TextMain, TextAlignmentOptions.Right);
            GameObject levelArrow = ModernDarkKit.Field<GameObject>(view, "_levelArrow");
            kit.Place(levelArrow, MID_X + ((MID_W - 48f) * 0.5f), levelY + 4f, 48f, 48f);
            FitArrow(levelArrow);
            Text(kit, ModernDarkKit.Field<TMP_Text>(view, "_levelToText"), MID_X + MID_W - 100f - 190f, levelY, 190f, 56f, 42f, UiModernDark.TextGold, TextAlignmentOptions.Left);

            GameObject tag = ModernDarkKit.Field<GameObject>(view, "_previewTag");
            kit.PlaceGroup(tag, MID_X + MID_W - 196f, PANEL_Y + PANEL_H - 40f, 180f, 28f, false);
            if (tag != null)
            {
                foreach (TMP_Text label in tag.GetComponentsInChildren<TMP_Text>(true))
                {
                    var rect = (RectTransform)label.transform;
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.offsetMin = Vector2.zero;
                    rect.offsetMax = Vector2.zero;
                    ModernDarkKit.StyleText(label, 18f, UiModernDark.TextGold);
                    label.alignment        = TextAlignmentOptions.Center;
                    label.textWrappingMode = TextWrappingModes.NoWrap;
                    label.overflowMode     = TextOverflowModes.Overflow;
                }
            }
        }

        // 화살표 묶음 안쪽 글자 · 그림은 이미 옮긴 위젯의 자식이라 Place가 건너뛰므로 묶음 칸에 꽉 채우고 가운데로 맞춘다
        private static void FitArrow(GameObject arrow)
        {
            if (arrow == null)
            {
                return;
            }

            // 화살표는 90도 돌린 그림이라 왼쪽 위 기준점 그대로면 자기 높이만큼 위로 돌아간다. 놓인 자리는 두고 기준점만 가운데로 옮긴다
            if (arrow.transform is RectTransform own)
            {
                Vector2 size  = own.sizeDelta;
                Vector2 shift = (new Vector2(0.5f, 0.5f) - own.pivot) * size;
                own.pivot             = new Vector2(0.5f, 0.5f);
                own.anchoredPosition += shift;
            }

            foreach (Graphic graphic in arrow.GetComponentsInChildren<Graphic>(true))
            {
                if ((graphic.transform == arrow.transform) && !(graphic is TMP_Text))
                {
                    continue;
                }

                if (graphic.transform != arrow.transform)
                {
                    var rect = (RectTransform)graphic.transform;
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.offsetMin = Vector2.zero;
                    rect.offsetMax = Vector2.zero;
                }

                if (graphic is TMP_Text text)
                {
                    text.alignment        = TextAlignmentOptions.Center;
                    text.textWrappingMode = TextWrappingModes.NoWrap;
                    text.overflowMode     = TextOverflowModes.Overflow;
                }

                graphic.raycastTarget = false;
            }
        }

        private static void LayoutStats(EquipmentEnhanceView view, ModernDarkKit kit)
        {
            var rows    = ModernDarkKit.Field<GameObject[]>(view, "_statRows");
            var names   = ModernDarkKit.Field<TMP_Text[]>(view, "_statNameTexts");
            var befores = ModernDarkKit.Field<TMP_Text[]>(view, "_statBeforeTexts");
            var arrows  = ModernDarkKit.Field<GameObject[]>(view, "_statArrows");
            var afters  = ModernDarkKit.Field<TMP_Text[]>(view, "_statAfterTexts");

            float top = PANEL_Y + 436f;
            for (int i = 0; i < Len(rows); ++i)
            {
                float y = top + (i * (STAT_ROW_H + 8f));
                kit.PlaceGroup(rows[i], MID_X + 32f, y, MID_W - 64f, STAT_ROW_H, false);
                Text(kit, At(names, i), MID_X + 60f, y, 150f, STAT_ROW_H, 22f, UiModernDark.TextMain, TextAlignmentOptions.Left);
                Text(kit, At(befores, i), MID_X + 230f, y, 110f, STAT_ROW_H, 24f, UiModernDark.TextMuted, TextAlignmentOptions.Right);
                kit.Place(At(arrows, i), MID_X + 352f, y + 6f, 36f, 36f);
                FitArrow(At(arrows, i));
                Text(kit, At(afters, i), MID_X + 400f, y, 196f, STAT_ROW_H, 24f, UiModernDark.TextGold, TextAlignmentOptions.Left);
            }
        }

        private static void LayoutCosts(EquipmentEnhanceView view, ModernDarkKit kit)
        {
            kit.Label("CostCaption", "필요 재료", 22f, UiModernDark.TextGold, TextAlignmentOptions.Left, true, RIGHT_X + 24f, PANEL_Y + 16f, RIGHT_W - 48f, 34f);
            kit.Line("CostLine", RIGHT_X + 24f, PANEL_Y + 58f, RIGHT_W - 48f);

            Cost(view, kit, "Essence", "정기", "_essenceCostText", "_essenceText", PANEL_Y + 72f);
            Cost(view, kit, "Shard", "정련편", "_shardCostText", "_shardText", PANEL_Y + 176f);

            float x = RIGHT_X + 20f;
            float w = RIGHT_W - 40f;
            Text(kit, ModernDarkKit.Field<TMP_Text>(view, "_rateCaption"), x, PANEL_Y + 288f, w, 28f, 20f, UiModernDark.TextMuted, TextAlignmentOptions.Center);
            Text(kit, ModernDarkKit.Field<TMP_Text>(view, "_rateText"), x, PANEL_Y + 318f, w, 56f, 44f, UiModernDark.TextGold, TextAlignmentOptions.Center);
            kit.Place(ModernDarkKit.Field<Image>(view, "_pityFill"), x, PANEL_Y + 380f, w, 16f);
            Text(kit, ModernDarkKit.Field<TMP_Text>(view, "_pityText"), x, PANEL_Y + 402f, w, 48f, 18f, UiModernDark.TextMuted, TextAlignmentOptions.Center, true);

            Text(kit, ModernDarkKit.Field<TMP_Text>(view, "_noteText"), x, PANEL_Y + 456f, w, 44f, 17f, UiModernDark.TextMuted, TextAlignmentOptions.Center, true);
            Text(kit, ModernDarkKit.Field<TMP_Text>(view, "_statusText"), x, PANEL_Y + 506f, w, 56f, 20f, UiModernDark.TextGold, TextAlignmentOptions.Center, true);

            Button enhance = ModernDarkKit.Field<Button>(view, "_enhanceButton");
            kit.Place(enhance, x, PANEL_Y + PANEL_H - 76f, w, 64f);
            ModernDarkKit.StyleButton(enhance, true);
            ModernDarkKit.StyleText(ModernDarkKit.Field<TMP_Text>(view, "_enhanceLabel"), 26f, UiModernDark.TextMain);
        }

        private static void Cost(EquipmentEnhanceView view, ModernDarkKit kit, string key, string caption, string costField, string heldField, float y)
        {
            float x = RIGHT_X + 20f;
            float w = RIGHT_W - 40f;
            kit.Inset(key + "Card", x, y, w, 96f);
            kit.Label(key + "Caption", caption, 22f, UiModernDark.TextMain, TextAlignmentOptions.Left, false, x + 16f, y + 8f, 130f, 38f);
            Text(kit, ModernDarkKit.Field<TMP_Text>(view, costField), x + 150f, y + 8f, w - 166f, 38f, 26f, UiModernDark.TextMain, TextAlignmentOptions.Right);
            kit.Label(key + "Held", "보유", 19f, UiModernDark.TextMuted, TextAlignmentOptions.Left, false, x + 16f, y + 50f, 130f, 36f);
            Text(kit, ModernDarkKit.Field<TMP_Text>(view, heldField), x + 150f, y + 50f, w - 166f, 36f, 22f, UiModernDark.TextMuted, TextAlignmentOptions.Right);
        }

        private static void LayoutResult(EquipmentEnhanceView view, ModernDarkKit kit)
        {
            var group = ModernDarkKit.Field<CanvasGroup>(view, "_resultGroup");
            if (group != null)
            {
                kit.PlaceGroup(group.gameObject, MID_X + 118f, PANEL_Y + 150f, 400f, 180f, false);
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

        private static void Child(TMP_Text text, float x, float y, float w, float h, float size, Color color, TextAlignmentOptions align)
        {
            if (text == null)
            {
                return;
            }

            ModernDarkKit.PlaceChild(text, x, y, w, h);
            ModernDarkKit.StyleText(text, size, color);
            text.alignment        = align;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode     = TextOverflowModes.Ellipsis;
        }

        private static int Len<T>(T[] values)
        {
            return (values != null) ? values.Length : 0;
        }

        private static T At<T>(T[] values, int index) where T : class
        {
            return ((values != null) && (index >= 0) && (index < values.Length)) ? values[index] : null;
        }
    }
}
