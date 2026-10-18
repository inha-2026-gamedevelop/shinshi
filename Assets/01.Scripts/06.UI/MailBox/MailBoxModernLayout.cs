// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary> 우편함 창을 공통 어두운 스타일로 한 번 다시 배치한다. 목록 · 상세 · 수령 연결은 MailBoxView 직렬화 참조 그대로다 </summary>
    public static class MailBoxModernLayout
    {
        private const float WIDTH      = 1500f;
        private const float HEIGHT     = 820f;
        private const float LIST_X     = 32f;
        private const float LIST_Y     = 84f;
        private const float LIST_W     = 700f;
        private const float ROW_MAX_H  = 44f;
        private const float ROW_GAP    = 4f;
        private const float LIST_MAX_H = 592f;
        private const float DETAIL_X   = 752f;
        private const float DETAIL_W   = 716f;
        private const float DETAIL_H   = 512f;

        /// <summary> 행이 많아도 목록 상자가 창 높이를 넘지 않도록 행 높이를 줄인다 </summary>
        public static float RowHeight(int rowCount)
        {
            if (rowCount <= 0)
            {
                return ROW_MAX_H;
            }

            return Mathf.Min(ROW_MAX_H, ((LIST_MAX_H - 16f) / rowCount) - ROW_GAP);
        }

        public static float ListHeight(int rowCount)
        {
            return Mathf.Max(rowCount * (RowHeight(rowCount) + ROW_GAP) + 16f, 300f);
        }

        public static float FooterY(int rowCount)
        {
            return LIST_Y + ListHeight(rowCount) + 12f;
        }

        public static void Apply(MailBoxView view)
        {
            var window = view.transform as RectTransform;
            TMP_Text status = ModernDarkKit.Field<TMP_Text>(view, "_statusLabel");
            if (!ModernDarkKit.TryBegin(view, window, status, out ModernDarkKit kit))
            {
                return;
            }

            foreach (EquipmentGrowthSurface surface in window.GetComponentsInChildren<EquipmentGrowthSurface>(true))
            {
                surface.enabled = false;
            }

            kit.Frame(new Vector2(WIDTH, HEIGHT), "우편");
            Image windowImage = window.GetComponent<Image>();
            if (windowImage != null)
            {
                windowImage.enabled = true;
            }

            var rows        = ModernDarkKit.Field<ListRowView[]>(view, "_rows");
            int rowCount    = (rows != null) ? rows.Length : 0;
            float listH     = ListHeight(rowCount);
            float rowH      = RowHeight(rowCount);
            kit.Inset("ListBox", LIST_X, LIST_Y, LIST_W, listH);
            kit.Inset("DetailBox", DETAIL_X, LIST_Y, DETAIL_W, DETAIL_H);

            for (int i = 0; i < rowCount; ++i)
            {
                if (rows[i] == null)
                {
                    continue;
                }

                kit.Place(rows[i], LIST_X + 16f, LIST_Y + 8f + (i * (rowH + ROW_GAP)), LIST_W - 32f, rowH);
                StyleRow(rows[i], rowH);
            }

            GameObject empty = ModernDarkKit.Field<GameObject>(view, "_emptyMark");
            kit.Place(empty, LIST_X + 16f, LIST_Y + 24f, LIST_W - 32f, 48f);

            Button prev = ModernDarkKit.Field<Button>(view, "_prevButton");
            Button next = ModernDarkKit.Field<Button>(view, "_nextButton");
            TMP_Text page = ModernDarkKit.Field<TMP_Text>(view, "_pageLabel");
            float footY = FooterY(rowCount);
            kit.Place(prev, LIST_X + 200f, footY, 90f, 44f);
            kit.Place(page, LIST_X + 300f, footY, 100f, 44f);
            kit.Place(next, LIST_X + 410f, footY, 90f, 44f);
            ModernDarkKit.StyleButton(prev, false);
            ModernDarkKit.StyleButton(next, false);
            ModernDarkKit.StyleText(page, 20f, UiModernDark.TextMuted);
            StyleArrow(kit, prev, "<");
            StyleArrow(kit, next, ">");

            Button close = ModernDarkKit.Field<Button>(view, "_closeButton");
            kit.Place(close, WIDTH - 72f, 8f, 44f, 44f);
            HideChildGraphics(close, true);
            kit.StyleClose(close);

            GameObject detail = ModernDarkKit.Field<GameObject>(view, "_detailPanel");
            GameObject hint   = ModernDarkKit.Field<GameObject>(view, "_detailHintMark");
            kit.PlaceGroup(detail, DETAIL_X, LIST_Y, DETAIL_W, DETAIL_H);
            ModernDarkKit.Quiet(detail);
            HideUnreferencedDetailChildren(view, detail);
            kit.PlaceGroup(hint, DETAIL_X + 24f, LIST_Y + 240f, DETAIL_W - 48f, 60f, false);

            TMP_Text title  = ModernDarkKit.Field<TMP_Text>(view, "_detailTitle");
            TMP_Text sender = ModernDarkKit.Field<TMP_Text>(view, "_detailSender");
            TMP_Text date   = ModernDarkKit.Field<TMP_Text>(view, "_detailDate");
            TMP_Text body   = ModernDarkKit.Field<TMP_Text>(view, "_detailBody");
            kit.Place(title,  DETAIL_X + 24f, LIST_Y + 20f,  DETAIL_W - 48f, 44f);
            kit.Place(sender, DETAIL_X + 24f, LIST_Y + 72f,  DETAIL_W - 48f, 28f);
            kit.Place(date,   DETAIL_X + 24f, LIST_Y + 102f, DETAIL_W - 48f, 28f);
            kit.Line("DetailLine", DETAIL_X + 24f, LIST_Y + 142f, DETAIL_W - 48f);
            kit.Place(body,   DETAIL_X + 24f, LIST_Y + 158f, DETAIL_W - 48f, 340f);
            ModernDarkKit.StyleText(title, 30f, UiModernDark.TextGold);
            ModernDarkKit.StyleText(sender, 20f, UiModernDark.TextMuted);
            ModernDarkKit.StyleText(date, 20f, UiModernDark.TextMuted);
            ModernDarkKit.StyleText(body, 22f, UiModernDark.TextMain);
            if (body != null)
            {
                body.textWrappingMode = TextWrappingModes.Normal;
                body.alignment        = TextAlignmentOptions.TopLeft;
            }

            Button claim = ModernDarkKit.Field<Button>(view, "_claimButton");
            kit.Place(claim, DETAIL_X, LIST_Y + DETAIL_H + 12f, DETAIL_W, 64f);
            ModernDarkKit.StyleButton(claim, true);
            SkillWindowModernLayout.FitButtonLabel(claim, 26f);

            kit.Place(status, LIST_X, HEIGHT - 52f, WIDTH - 64f, 32f);
            ModernDarkKit.StyleText(status, 20f, UiModernDark.TextMuted);

            kit.HideLegacy();
            HideListHeader(view, detail);
        }

        /// <summary> 목록 위 옛 열 머리글(H_ 로 시작하는 글자와 표 머리글 바탕 · 제목줄)은 새 목록에서 쓰지 않으니 끈다 </summary>
        private static void HideListHeader(MailBoxView view, GameObject detail)
        {
            var keep = new System.Collections.Generic.HashSet<Transform>();
            ModernDarkKit.CollectReferences(view, keep);
            foreach (Transform node in view.GetComponentsInChildren<Transform>(true))
            {
                bool header = node.name.StartsWith("H_", System.StringComparison.Ordinal)
                    || (node.name == "표 헤더 배경") || (node.name == "제목줄");
                if (!header || ModernDarkKit.ContainsAny(node, keep)
                    || ((detail != null) && node.IsChildOf(detail.transform)))
                {
                    continue;
                }

                node.gameObject.SetActive(false);
            }
        }

        /// <summary> 옛 작은 화살표 그림을 끄고 단추 전체에 가운데 맞춘 글자 화살표 하나만 보인다 </summary>
        private static void StyleArrow(ModernDarkKit kit, Button button, string glyph)
        {
            if (button == null)
            {
                return;
            }

            HideChildGraphics(button);
            SkillWindowModernLayout.AddGlyph(kit, button, glyph);
            TMP_Text text = button.transform.Find(UiModernDark.DECOR_PREFIX + "Glyph").GetComponent<TMP_Text>();
            text.fontSize         = 28f;
            text.fontStyle        = FontStyles.Bold;
            text.alignment        = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
        }

        /// <summary> 단추 안쪽의 옛 그림(작은 아이콘 · 화살표 · 닫기 그림)을 끈다. 단추 자신의 바탕과 장식 글자는 건드리지 않는다 </summary>
        private static void HideChildGraphics(Button button, bool keepText = false)
        {
            if (button == null)
            {
                return;
            }

            foreach (Graphic graphic in button.GetComponentsInChildren<Graphic>(true))
            {
                if ((graphic.gameObject == button.gameObject)
                    || (keepText && (graphic is TMP_Text))
                    || graphic.name.StartsWith(UiModernDark.DECOR_PREFIX, System.StringComparison.Ordinal))
                {
                    continue;
                }

                graphic.enabled = false;
            }
        }

        /// <summary> 직렬화 참조가 없는 옛 상세 가지(비어 있는 첨부 보상 머리글 등)를 끈다. 보상 내용은 본문과 목록 요약 열이 이미 보여 준다 </summary>
        private static void HideUnreferencedDetailChildren(MailBoxView view, GameObject detail)
        {
            if (detail == null)
            {
                return;
            }

            var keep = new System.Collections.Generic.HashSet<Transform>();
            ModernDarkKit.CollectReferences(view, keep);
            for (int i = detail.transform.childCount - 1; i >= 0; --i)
            {
                Transform child = detail.transform.GetChild(i);
                if (child.name.StartsWith(UiModernDark.DECOR_PREFIX, System.StringComparison.Ordinal))
                {
                    continue;
                }

                if (!ModernDarkKit.ContainsAny(child, keep))
                {
                    child.gameObject.SetActive(false);
                }
            }
        }

        private static void StyleRow(ListRowView row, float rowH)
        {
            Image back = row.GetComponent<Image>();
            ModernDarkKit.StyleInset(back, UiModernDark.SPRITE_BUTTON);
            var columns = ModernDarkKit.Field<TMP_Text[]>(row, "_columns");
            if ((columns == null) || (columns.Length < 5))
            {
                return;
            }

            // 열 순서: 보낸 사람, 제목, 보상 요약, 날짜, 만료
            float h = rowH - 8f;
            // 행 안쪽 가로 590 이내, 열 사이 12~14 간격. 시각과 남은 기간이 맞닿지 않게 나눈다
            Column(columns[0], 10f, 4f, 70f, h, 17f, UiModernDark.TextMuted, TextAlignmentOptions.Left);
            Column(columns[1], 92f, 4f, 112f, h, 18f, UiModernDark.TextMain, TextAlignmentOptions.Left);
            Column(columns[2], 216f, 4f, 146f, h, 15f, UiModernDark.TextGold, TextAlignmentOptions.Right);
            Column(columns[3], 376f, 4f, 90f, h, 14f, UiModernDark.TextMuted, TextAlignmentOptions.Right);
            Column(columns[4], 480f, 4f, 110f, h, 15f, UiModernDark.TextMuted, TextAlignmentOptions.Right);
        }

        private static void Column(TMP_Text text, float x, float y, float w, float h, float size, Color color, TextAlignmentOptions align)
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
