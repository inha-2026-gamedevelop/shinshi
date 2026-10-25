// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary> 장비 옵션 재설정 창을 장비 강화 · 신체 강화와 같은 공통 어두운 스타일로 한 번 다시 배치한다. 글자와 단추는 EquipmentOptionView 직렬화 참조 그대로다 </summary>
    public static class EquipmentOptionModernLayout
    {
        private const float WIDTH   = 1500f;
        private const float HEIGHT  = 820f;
        private const float PANEL_Y = 84f;
        private const float PANEL_H = 640f;

        private const float LEFT_X  = 32f;
        private const float LEFT_W  = 380f;
        private const float MID_X   = 432f;
        private const float MID_W   = 636f;
        private const float RIGHT_X = 1088f;
        private const float RIGHT_W = 380f;

        private const float SLOT_TOP = 72f;
        private const float SLOT_H   = 86f;
        private const float SLOT_GAP = 6f;
        private const float ART_SIZE = 210f;
        private const float LINE_TOP = 358f;
        private const float LINE_H   = 100f;
        private const float LINE_GAP = 12f;

        private const string DRAG_NAME    = "제목줄";
        private const string ORBIT_NAME   = "재설정 문양";
        private const string CRYSTAL_NAME = "재설정 결정 장식";
        private const string SLOT_ICON    = "부위 아이콘";
        private const float  HALF         = 0.5f;

        private static readonly Vector2 G_CenterPivot = new Vector2(HALF, HALF);

        public static void Apply(EquipmentOptionView view)
        {
            var window = view.transform as RectTransform;
            TMP_Text status = ModernDarkKit.Field<TMP_Text>(view, "_statusLabel");
            if (!ModernDarkKit.TryBegin(view, window, status, out ModernDarkKit kit))
            {
                return;
            }

            // 옛 마감 효과가 메시를 덮어 새 바탕과 단추 그림을 어둡게 가리므로 먼저 끈다
            foreach (EquipmentGrowthSurface surface in window.GetComponentsInChildren<EquipmentGrowthSurface>(true))
            {
                surface.enabled = false;
            }

            kit.Frame(new Vector2(WIDTH, HEIGHT), "장비 옵션 재설정");
            kit.Inset("SlotPanel", LEFT_X, PANEL_Y, LEFT_W, PANEL_H);
            kit.Inset("ItemPanel", MID_X, PANEL_Y, MID_W, PANEL_H);
            kit.Inset("CostPanel", RIGHT_X, PANEL_Y, RIGHT_W, PANEL_H);

            Button close = ModernDarkKit.Field<Button>(view, "_closeButton");
            kit.Place(close, WIDTH - 72f, 8f, 44f, 44f);
            kit.StyleClose(close);
            PlaceDrag(kit);

            LayoutSlots(view, kit);
            LayoutItem(view, kit);
            LayoutLines(view, kit);
            LayoutReroll(view, kit);

            kit.HideLegacy();
        }

        // 제목줄은 창 끌기 손잡이라 남기고 그림만 투명하게 해 새 제목 아래에 깐다
        private static void PlaceDrag(ModernDarkKit kit)
        {
            Transform drag = kit.Window.Find(DRAG_NAME);
            if (drag == null)
            {
                return;
            }

            kit.Place(drag.gameObject, 0f, 0f, WIDTH - 84f, 56f);
            if (drag.TryGetComponent(out Image image))
            {
                image.sprite = null;
                image.color  = Color.clear;
            }
        }

        private static void LayoutSlots(EquipmentOptionView view, ModernDarkKit kit)
        {
            kit.Label("SlotCaption", "재설정할 장비 선택", 22f, UiModernDark.TextGold, TextAlignmentOptions.Left, true, LEFT_X + 24f, PANEL_Y + 16f, LEFT_W - 48f, 34f);
            kit.Line("SlotLine", LEFT_X + 24f, PANEL_Y + 58f, LEFT_W - 48f);

            var buttons = ModernDarkKit.Field<Button[]>(view, "_slotButtons");
            var labels  = ModernDarkKit.Field<TMP_Text[]>(view, "_slotLabels");
            var marks   = ModernDarkKit.Field<GameObject[]>(view, "_slotSelectedMarks");

            float x = LEFT_X + 12f;
            float w = LEFT_W - 24f;
            for (int i = 0; i < Len(buttons); ++i)
            {
                Button button = buttons[i];
                if (button == null)
                {
                    continue;
                }

                float y = PANEL_Y + SLOT_TOP + (i * (SLOT_H + SLOT_GAP));
                kit.Place(button, x, y, w, SLOT_H);
                ModernDarkKit.StyleButton(button, false);

                // 단추 안쪽 위젯은 Place가 건너뛰므로 단추 기준 좌표로 직접 놓는다
                Transform icon = button.transform.Find(SLOT_ICON);
                if (icon != null)
                {
                    ModernDarkKit.PlaceChild(icon, 14f, 11f, 64f, 64f);
                }

                TMP_Text label = At(labels, i);
                if (label != null)
                {
                    ModernDarkKit.PlaceChild(label, 92f, 6f, w - 104f, SLOT_H - 12f);
                    ModernDarkKit.StyleText(label, 21f, UiModernDark.TextMain);
                    label.alignment        = TextAlignmentOptions.MidlineLeft;
                    label.textWrappingMode = TextWrappingModes.Normal;
                    label.overflowMode     = TextOverflowModes.Ellipsis;
                }

                // 선택 표시는 단추 바탕 바로 위, 아이콘과 글자 아래에 칸 전체 테두리로 깐다
                GameObject mark = At(marks, i);
                if ((mark != null) && (mark.transform is RectTransform markRect))
                {
                    markRect.SetAsFirstSibling();
                    ModernDarkKit.PlaceRect(markRect, 0f, 0f, w, SLOT_H);
                    if (mark.TryGetComponent(out Image image))
                    {
                        ModernDarkKit.SetSliced(image, UiModernDark.SPRITE_SELECTED, Color.white);
                        image.raycastTarget = false;
                    }
                }
            }
        }

        private static void LayoutItem(EquipmentOptionView view, ModernDarkKit kit)
        {
            kit.Label("ItemCaption", "재설정 대상", 20f, UiModernDark.TextMuted, TextAlignmentOptions.Center, false, MID_X + 24f, PANEL_Y + 14f, MID_W - 48f, 28f);
            Text(kit, ModernDarkKit.Field<TMP_Text>(view, "_itemLabel"), MID_X + 24f, PANEL_Y + 42f, MID_W - 48f, 44f, 32f, UiModernDark.TextGold, TextAlignmentOptions.Center);
            kit.Line("ItemLine", MID_X + 120f, PANEL_Y + 94f, MID_W - 240f);

            // 옛 창의 재설정 문양과 결정 그림은 가운데 장식으로 그대로 쓴다
            float artX = MID_X + ((MID_W - ART_SIZE) * 0.5f);
            float artY = PANEL_Y + 104f;
            PlaceArt(kit, ORBIT_NAME, artX, artY);
            PlaceArt(kit, CRYSTAL_NAME, artX, artY);
        }

        // 결정 그림은 세로로 길어 비율을 지키며 줄어드는데, 줄인 그림은 기준점 쪽에 붙는다.
        // Place가 기준점을 왼쪽 위로 두므로 같은 상자에서 기준점만 가운데로 옮겨 문양 원 가운데에 맞춘다
        private static void PlaceArt(ModernDarkKit kit, string name, float x, float y)
        {
            Transform art = kit.Window.Find(name);
            if (art == null)
            {
                return;
            }

            kit.Place(art.gameObject, x, y, ART_SIZE, ART_SIZE);
            var rect = (RectTransform)art;
            rect.pivot             = G_CenterPivot;
            rect.anchoredPosition += new Vector2(ART_SIZE * HALF, -ART_SIZE * HALF);
        }

        private static void LayoutLines(EquipmentOptionView view, ModernDarkKit kit)
        {
            kit.Label("LineCaption", "현재 장비 옵션", 22f, UiModernDark.TextGold, TextAlignmentOptions.Left, true, MID_X + 24f, PANEL_Y + 322f, MID_W - 48f, 30f);

            var lines  = ModernDarkKit.Field<TMP_Text[]>(view, "_lineLabels");
            var ranges = ModernDarkKit.Field<TMP_Text[]>(view, "_rangeLabels");
            int count  = Mathf.Max(Len(lines), Len(ranges));
            float x = MID_X + 24f;
            float w = MID_W - 48f;
            for (int i = 0; i < count; ++i)
            {
                float y = PANEL_Y + LINE_TOP + (i * (LINE_H + LINE_GAP));
                kit.Inset("LineCard" + i, x, y, w, LINE_H, UiModernDark.SPRITE_BUTTON);
                kit.Label("LineNo" + i, (i + 1).ToString("00"), 26f, UiModernDark.TextGold, TextAlignmentOptions.Center, true, x + 12f, y, 56f, LINE_H);

                // 줄 글자색은 상급 여부에 따라 SetLine이 칠하므로 지금 색을 그대로 두고 크기와 자리만 맞춘다
                TMP_Text line = At(lines, i);
                if (line != null)
                {
                    Text(kit, line, x + 80f, y + 12f, w - 96f, 42f, 26f, line.color, TextAlignmentOptions.Left);
                }

                Text(kit, At(ranges, i), x + 80f, y + 58f, w - 96f, 30f, 19f, UiModernDark.TextMuted, TextAlignmentOptions.Left);
            }

            kit.Label("LineNote", "옵션 2줄을 함께 재설정합니다", 19f, UiModernDark.TextMuted, TextAlignmentOptions.Center, false,
                MID_X + 24f, PANEL_Y + PANEL_H - 52f, MID_W - 48f, 32f);
        }

        private static void LayoutReroll(EquipmentOptionView view, ModernDarkKit kit)
        {
            kit.Label("CostCaption", "필요 재료", 22f, UiModernDark.TextGold, TextAlignmentOptions.Left, true, RIGHT_X + 24f, PANEL_Y + 16f, RIGHT_W - 48f, 34f);
            kit.Line("CostLine", RIGHT_X + 24f, PANEL_Y + 58f, RIGHT_W - 48f);

            float x = RIGHT_X + 20f;
            float w = RIGHT_W - 40f;
            kit.Inset("CostCard", x, PANEL_Y + 72f, w, 120f);
            kit.Label("CostHint", "필요 / 보유", 17f, UiModernDark.TextMuted, TextAlignmentOptions.Right, false, x + 16f, PANEL_Y + 80f, w - 32f, 24f);
            Text(kit, ModernDarkKit.Field<TMP_Text>(view, "_costLabel"), x + 16f, PANEL_Y + 106f, w - 32f, 78f, 23f, UiModernDark.TextMain, TextAlignmentOptions.Left, true);

            kit.Label("PityCaption", "상급 확정", 20f, UiModernDark.TextMuted, TextAlignmentOptions.Center, false, x, PANEL_Y + 214f, w, 28f);
            Text(kit, ModernDarkKit.Field<TMP_Text>(view, "_pityLabel"), x, PANEL_Y + 246f, w, 64f, 24f, UiModernDark.TextGold, TextAlignmentOptions.Center, true);
            kit.Line("PityLine", RIGHT_X + 24f, PANEL_Y + 326f, RIGHT_W - 48f);

            Text(kit, ModernDarkKit.Field<TMP_Text>(view, "_statusLabel"), x, PANEL_Y + 344f, w, 180f, 20f, UiModernDark.TextGold, TextAlignmentOptions.Center, true);

            Button reroll = ModernDarkKit.Field<Button>(view, "_rerollButton");
            kit.Place(reroll, x, PANEL_Y + PANEL_H - 76f, w, 64f);
            ModernDarkKit.StyleButton(reroll, true);

            // 단추 글자는 옛 단추 크기로 놓여 있어 새 단추 칸에 꽉 채운다
            TMP_Text rerollLabel = ModernDarkKit.Field<TMP_Text>(view, "_rerollLabel");
            if (rerollLabel != null)
            {
                var rect = (RectTransform)rerollLabel.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                ModernDarkKit.StyleText(rerollLabel, 26f, UiModernDark.TextMain);
                rerollLabel.alignment = TextAlignmentOptions.Center;
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
