// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary> SocialRowView 한 줄 안쪽을 새 줄 크기에 맞춰 놓는다. 접속 점 색, 버튼 클릭 연결은 건드리지 않고 자리와 글자 모양만 바꾼다 </summary>
    public static class SocialRowModernLayout
    {
        private const float BUTTON_W   = 112f;
        private const float BUTTON_GAP = 8f;
        private const float PAD        = 16f;

        public static void Style(SocialRowView row, float width, float height)
        {
            ModernDarkKit.StyleInset(row.GetComponent<Image>(), UiModernDark.SPRITE_BUTTON);

            var dot    = ModernDarkKit.Field<Image>(row, "_presenceDot");
            var name   = ModernDarkKit.Field<TMP_Text>(row, "_nameLabel");
            var detail = ModernDarkKit.Field<TMP_Text>(row, "_detailLabel");
            var info   = ModernDarkKit.Field<TMP_Text>(row, "_infoLabel");
            var buttons = ModernDarkKit.Field<Button[]>(row, "_buttons");

            float dotSize = 14f;
            if (dot != null)
            {
                ModernDarkKit.PlaceRect((RectTransform)dot.transform, PAD, (height - dotSize) * 0.5f, dotSize, dotSize);
            }

            int buttonCount = (buttons != null) ? buttons.Length : 0;
            float buttonsW  = buttonCount * (BUTTON_W + BUTTON_GAP);
            float buttonH   = Mathf.Min(44f, height - 12f);

            // 줄이 낮아도 이름과 보조 글이 겹치지 않게 칸 높이에 맞춰 글자 크기를 줄인다. 충분히 높으면 22 / 17 그대로다
            float nameH   = Mathf.Floor(height * 0.52f);
            float detailH = height - nameH - 2f;
            Text(name, PAD + 28f, 2f, width * 0.40f, nameH, Mathf.Min(22f, nameH * 0.8f), UiModernDark.TextMain, TextAlignmentOptions.Left);
            Text(detail, PAD + 28f, nameH, width * 0.40f, detailH, Mathf.Min(17f, detailH * 0.75f), UiModernDark.TextMuted, TextAlignmentOptions.Left);
            Text(info, width * 0.45f, 0f, width - (width * 0.45f) - buttonsW - PAD, height, 19f, UiModernDark.Cyan, TextAlignmentOptions.Right);

            // 버튼들이 줄 바로 아래가 아닌 '버튼' 묶음 안에 있으면 묶음 자체를 줄 오른쪽 예약 칸에 놓는다.
            // 묶음의 가로 배치(오른쪽 정렬, 간격 8)는 그대로 두고 버튼 크기만 정해 숨김 · 표시에도 정보 글자와 겹치지 않게 한다
            RectTransform group = SharedGroup(buttons, (RectTransform)row.transform);
            float groupW = (buttonCount * BUTTON_W) + (Mathf.Max(0, buttonCount - 1) * BUTTON_GAP);
            if (group != null)
            {
                ModernDarkKit.PlaceRect(group, width - PAD - groupW, 0f, groupW, height);
                var layout = group.GetComponent<HorizontalLayoutGroup>();
                if (layout != null)
                {
                    layout.padding                = new RectOffset(0, 0, 0, 0);
                    layout.spacing                = BUTTON_GAP;
                    layout.childAlignment         = TextAnchor.MiddleRight;
                    layout.childControlWidth      = false;
                    layout.childControlHeight     = false;
                    layout.childForceExpandWidth  = false;
                    layout.childForceExpandHeight = false;
                }
            }

            for (int i = 0; i < buttonCount; ++i)
            {
                if (buttons[i] == null)
                {
                    continue;
                }

                var rect = (RectTransform)buttons[i].transform;
                if (group == null)
                {
                    float x = width - PAD - ((buttonCount - i) * (BUTTON_W + BUTTON_GAP)) + BUTTON_GAP;
                    ModernDarkKit.PlaceRect(rect, x, (height - buttonH) * 0.5f, BUTTON_W, buttonH);
                }
                else if (group.GetComponent<HorizontalLayoutGroup>() != null)
                {
                    rect.localScale = Vector3.one;
                    rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, BUTTON_W);
                    rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, buttonH);
                }
                else
                {
                    ModernDarkKit.PlaceRect(rect, i * (BUTTON_W + BUTTON_GAP), (height - buttonH) * 0.5f, BUTTON_W, buttonH);
                }

                ModernDarkKit.StyleButton(buttons[i], i == 0);
            }

            if (group != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(group);
            }
        }

        private static RectTransform SharedGroup(Button[] buttons, RectTransform row)
        {
            Transform parent = null;
            int count = (buttons != null) ? buttons.Length : 0;
            for (int i = 0; i < count; ++i)
            {
                if (buttons[i] == null)
                {
                    continue;
                }

                if ((parent != null) && (buttons[i].transform.parent != parent))
                {
                    return null;
                }

                parent = buttons[i].transform.parent;
            }

            return ((parent != null) && (parent != row) && (parent is RectTransform rect)) ? rect : null;
        }

        private static void Text(TMP_Text text, float x, float y, float w, float h, float size, Color color, TextAlignmentOptions align)
        {
            if (text == null)
            {
                return;
            }

            ModernDarkKit.PlaceRect((RectTransform)text.transform, x, y, w, h);
            ModernDarkKit.StyleText(text, size, color);
            text.alignment        = align;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode     = TextOverflowModes.Ellipsis;
        }
    }
}
