#if UNITY_EDITOR
// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI.Editor
{
    /// <summary> 친구 창 프리팹에 공통 어두운 스타일을 제자리에서 입힌다. 길드 등 같은 창 도구를 쓰는 다른 창은 건드리지 않는다 </summary>
    public static class FriendsWindowRestyle
    {
        // 프리팹을 새로 만들지 않아 GUID, FriendsView 연결 필드, 버튼 이벤트가 그대로 남는다.
        // 여러 번 실행해도 장식은 먼저 지우고 다시 만들므로 결과가 같다

        /****************************************
        *                Fields
        ****************************************/

        private const float TITLE_FONT_SIZE = 30f;
        private const float COUNT_FONT_SIZE = 20f;
        private const float FRAME_INSET     = 6f;
        private const float DIVIDER_ALPHA   = 0.8f;
        private const float SHADE_ALPHA     = 0.65f;
        private const float MARK_THICKNESS  = 3f;

        private const float WIDE_INPUT_TOP     = 92f;
        private const float WIDE_REQUEST_WIDTH = 200f;
        private const float WIDE_TAB_TOP       = 152f;
        private const float WIDE_TAB_WIDTH     = 200f;
        private const float WIDE_TAB_GAP       = 208f;
        private const float WIDE_LIST_TOP      = 212f;
        private const float WIDE_ROW_GAP       = 52f;
        private const float WIDE_PAGE_BUTTON   = 44f;
        private const float WIDE_FOOTER_HEIGHT = 36f;

        private const string TAB_PREFIX = "탭 ";
        private const string ROW_PREFIX = "줄 ";

        private static readonly string[] PAGE_BUTTONS = { "이전 쪽", "다음 쪽" };

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem("Shinshi/UI/ModernDark/친구창 스타일 적용")]
        public static void Apply()
        {
            ModernDarkTextures.EnsureAll(false);
            ModernDarkStyle.EditPrefab(FriendsWindowBuilder.PREFAB_PATH, ApplyToRoot);
            Debug.Log("[UI] 친구창 프리팹에 공통 스타일을 적용했다: " + FriendsWindowBuilder.PREFAB_PATH);
        }

        public static void ApplyToRoot(RectTransform root)
        {
            ModernDarkStyle.StripLegacySurface(root);
            ModernDarkStyle.ClearDecor(root);
            LayoutWide(root);

            Image frame = root.GetComponent<Image>();
            if (frame != null)
            {
                ModernDarkStyle.ApplyPanel(frame);
            }

            StyleTitle(root);
            StyleInput(root);
            StyleTabs(root);
            StyleRows(root);
            StyleFooter(root);
            StyleConfirm(root);

            ModernDarkStyle.AddCornerDecor(root, FRAME_INSET);

            // 확인 상자가 맨 위에 그려지도록 마지막 자식으로 유지한다
            Transform confirm = root.Find("확인");
            if (confirm != null)
            {
                confirm.SetAsLastSibling();
            }
        }

        // 창 폭을 넓힌 배치다. 모두 왼쪽 위 기준 절대 좌표라 여러 번 실행해도 결과가 같다
        private static void LayoutWide(RectTransform root)
        {
            float w     = FriendsView.WINDOW_WIDTH;
            float h     = FriendsView.WINDOW_HEIGHT;
            float m     = SocialWindowKit.MARGIN;
            float listW = w - (m * 2f);
            root.sizeDelta = new Vector2(w, h);

            Place(Direct<RectTransform>(root, "제목줄"), 0f, 0f, w, SocialWindowKit.TITLE_HEIGHT);
            Place(Direct<RectTransform>(root, "수"), w - m - 28f - 176f, 22f, 160f, 30f);
            Place(Direct<RectTransform>(root, "닫기 버튼"), w - m - 28f, 22f, 28f, 28f);
            Place(Direct<RectTransform>(root, "제목 구분선"), m, SocialWindowKit.TITLE_HEIGHT, listW, 1f);

            float inputW = listW - WIDE_REQUEST_WIDTH - 12f;
            RectTransform input = Direct<RectTransform>(root, "이름 입력");
            Place(input, m, WIDE_INPUT_TOP, inputW, SocialWindowKit.BUTTON_HEIGHT);
            RectTransform area = (input == null) ? null : input.Find("글자 영역") as RectTransform;
            if (area != null)
            {
                Place(area, 12f, 0f, inputW - 24f, SocialWindowKit.BUTTON_HEIGHT);
                foreach (RectTransform text in area)
                {
                    Place(text, 0f, 0f, inputW - 24f, SocialWindowKit.BUTTON_HEIGHT);
                }
            }

            Place(Direct<RectTransform>(root, "친구 요청 버튼"), w - m - WIDE_REQUEST_WIDTH, WIDE_INPUT_TOP, WIDE_REQUEST_WIDTH,
                SocialWindowKit.BUTTON_HEIGHT);
            FitChildText(Direct<RectTransform>(root, "친구 요청 버튼"), WIDE_REQUEST_WIDTH, SocialWindowKit.BUTTON_HEIGHT);

            int tabIndex = 0;
            int rowIndex = 0;
            for (int i = 0; i < root.childCount; ++i)
            {
                var child = (RectTransform)root.GetChild(i);
                if (child.name.StartsWith(TAB_PREFIX, System.StringComparison.Ordinal))
                {
                    Place(child, m + (WIDE_TAB_GAP * tabIndex), WIDE_TAB_TOP, WIDE_TAB_WIDTH, SocialWindowKit.BUTTON_HEIGHT);
                    FitChildText(child, WIDE_TAB_WIDTH, SocialWindowKit.BUTTON_HEIGHT - MARK_THICKNESS);
                    ++tabIndex;
                }
                else if (child.name.StartsWith(ROW_PREFIX, System.StringComparison.Ordinal))
                {
                    Place(child, m, WIDE_LIST_TOP + (WIDE_ROW_GAP * rowIndex), listW, SocialWindowKit.ROW_HEIGHT);
                    LayoutRow(child, listW);
                    ++rowIndex;
                }
            }

            Place(Direct<RectTransform>(root, "빈 목록"), m, WIDE_LIST_TOP + 120f, listW, 60f);

            float pageTop    = h - 16f - WIDE_FOOTER_HEIGHT;
            float statusLeft = m + (WIDE_PAGE_BUTTON * 2f) + 116f;
            Place(Direct<RectTransform>(root, "이전 쪽"), m, pageTop, WIDE_PAGE_BUTTON, WIDE_FOOTER_HEIGHT);
            Place(Direct<RectTransform>(root, "쪽"), m + WIDE_PAGE_BUTTON + 4f, pageTop, 92f, WIDE_FOOTER_HEIGHT);
            Place(Direct<RectTransform>(root, "다음 쪽"), m + WIDE_PAGE_BUTTON + 100f, pageTop, WIDE_PAGE_BUTTON, WIDE_FOOTER_HEIGHT);
            Place(Direct<RectTransform>(root, "상태"), statusLeft, pageTop, w - statusLeft - m, WIDE_FOOTER_HEIGHT);

            LayoutConfirm(root, w, h);

            if (root.TryGetComponent(out FriendsView view))
            {
                var serialized = new SerializedObject(view);
                serialized.FindProperty("_windowSize").vector2Value = new Vector2(w, h);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void LayoutRow(RectTransform row, float width)
        {
            const float buttonsWidth = (SocialWindowKit.ROW_BUTTONS * SocialWindowKit.ROW_BUTTON_W)
                + ((SocialWindowKit.ROW_BUTTONS - 1) * SocialWindowKit.ROW_BUTTON_SPACING);
            float buttonsLeft = width - SocialWindowKit.ROW_BUTTON_SPACING - buttonsWidth;
            Place(row.Find("접속 점") as RectTransform, 16f, (SocialWindowKit.ROW_HEIGHT - 12f) * 0.5f, 12f, 12f);
            Place(row.Find("이름") as RectTransform, 40f, 0f, 240f, SocialWindowKit.ROW_HEIGHT);
            Place(row.Find("직업") as RectTransform, 300f, 0f, 200f, SocialWindowKit.ROW_HEIGHT);
            Place(row.Find("상태") as RectTransform, 520f, 0f, buttonsLeft - 528f, SocialWindowKit.ROW_HEIGHT);
            Place(row.Find("버튼") as RectTransform, buttonsLeft, 0f, buttonsWidth, SocialWindowKit.ROW_HEIGHT);
        }

        private static void LayoutConfirm(RectTransform root, float w, float h)
        {
            const float cardW = 520f;
            const float cardH = 200f;
            Transform confirm = root.Find("확인");
            if (confirm == null)
            {
                return;
            }

            float left = (w - cardW) * 0.5f;
            float top  = (h - cardH) * 0.5f;
            Place((RectTransform)confirm, 0f, 0f, w, h);
            Place(confirm.Find("확인 패널") as RectTransform, left, top, cardW, cardH);
            Place(confirm.Find("확인 글자") as RectTransform, left + 24f, top + 24f, cardW - 48f, 80f);
            Place(confirm.Find("확인 버튼") as RectTransform, left + 40f, top + 124f, 200f, SocialWindowKit.BUTTON_HEIGHT);
            Place(confirm.Find("취소 버튼") as RectTransform, left + 280f, top + 124f, 200f, SocialWindowKit.BUTTON_HEIGHT);
        }

        private static void FitChildText(RectTransform parent, float width, float height)
        {
            if (parent == null)
            {
                return;
            }

            foreach (RectTransform child in parent)
            {
                if (child.TryGetComponent(out TMP_Text _))
                {
                    Place(child, 0f, 0f, width, height);
                }
            }
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin        = Vector2.up;
            rect.anchorMax        = Vector2.up;
            rect.pivot            = Vector2.up;
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta        = new Vector2(width, height);
        }

        private static T Direct<T>(RectTransform root, string name) where T : Component
        {
            Transform child = root.Find(name);
            return (child == null) ? null : child.GetComponent<T>();
        }

        private static void StyleTitle(RectTransform root)
        {
            Image bar = Direct<Image>(root, "제목줄");
            if (bar != null)
            {
                bar.sprite = null;
                bar.type   = Image.Type.Simple;
                bar.color  = new Color(0f, 0f, 0f, 0f);
            }

            TMP_Text title = Direct<TMP_Text>(root, "제목");
            if (title != null)
            {
                title.color     = UiModernDark.TextGold;
                title.fontSize  = TITLE_FONT_SIZE;
                title.alignment = TextAlignmentOptions.Center;
                RectTransform rect = title.rectTransform;
                rect.anchoredPosition = new Vector2((root.sizeDelta.x - rect.sizeDelta.x) * 0.5f, rect.anchoredPosition.y);
            }

            TMP_Text count = Direct<TMP_Text>(root, "수");
            if (count != null)
            {
                count.color    = UiModernDark.Cyan;
                count.fontSize = COUNT_FONT_SIZE;
            }

            Image close = Direct<Image>(root, "닫기 버튼");
            if (close != null)
            {
                close.color = UiModernDark.TextMain;
            }

            Image divider = Direct<Image>(root, "제목 구분선");
            if (divider != null)
            {
                divider.color = new Color(UiModernDark.Gold.r, UiModernDark.Gold.g, UiModernDark.Gold.b, DIVIDER_ALPHA);
            }
        }

        private static void StyleInput(RectTransform root)
        {
            Image input = Direct<Image>(root, "이름 입력");
            if (input != null)
            {
                ModernDarkStyle.ApplyInset(input);
                foreach (TMP_Text text in input.GetComponentsInChildren<TMP_Text>(true))
                {
                    text.color = (text.name == "안내") ? UiModernDark.TextMuted : UiModernDark.TextMain;
                }
            }

            Button request = Direct<Button>(root, "친구 요청 버튼");
            if (request != null)
            {
                ModernDarkStyle.ApplyButton(request, true);
            }
        }

        private static void StyleTabs(RectTransform root)
        {
            for (int i = 0; i < root.childCount; ++i)
            {
                Transform tab = root.GetChild(i);
                if ((!tab.name.StartsWith(TAB_PREFIX, System.StringComparison.Ordinal)) || (!tab.TryGetComponent(out Button button)))
                {
                    continue;
                }

                ModernDarkStyle.ApplyTab(button);
                TMP_Text label = tab.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                {
                    label.color = UiModernDark.TextMain;
                }

                Transform mark = tab.Find("선택 표시");
                if ((mark != null) && (mark.TryGetComponent(out Image markImage)))
                {
                    RectTransform markRect = markImage.rectTransform;
                    float         tabHeight = ((RectTransform)tab).sizeDelta.y;
                    markRect.anchoredPosition = new Vector2(markRect.anchoredPosition.x, -(tabHeight - MARK_THICKNESS));
                    markRect.sizeDelta        = new Vector2(((RectTransform)tab).sizeDelta.x, MARK_THICKNESS);
                    ModernDarkStyle.ApplyActiveMark(markImage);
                }
            }
        }

        private static void StyleRows(RectTransform root)
        {
            for (int i = 0; i < root.childCount; ++i)
            {
                Transform row = root.GetChild(i);
                if ((!row.name.StartsWith(ROW_PREFIX, System.StringComparison.Ordinal)) || (!row.TryGetComponent(out Image background)))
                {
                    continue;
                }

                ModernDarkStyle.ApplySprite(background, UiModernDark.SPRITE_TAB, Color.white);
                foreach (TMP_Text text in row.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (text.name == "이름")
                    {
                        text.color = UiModernDark.TextMain;
                    }
                    else if (text.name == "직업")
                    {
                        text.color = UiModernDark.TextMuted;
                    }
                    else if (text.name == "상태")
                    {
                        text.color = UiModernDark.Cyan;
                    }
                }

                foreach (Button button in row.GetComponentsInChildren<Button>(true))
                {
                    ModernDarkStyle.ApplyButton(button, false);
                }
            }
        }

        private static void StyleFooter(RectTransform root)
        {
            for (int i = 0; i < PAGE_BUTTONS.Length; ++i)
            {
                Button page = Direct<Button>(root, PAGE_BUTTONS[i]);
                if (page != null)
                {
                    ModernDarkStyle.ApplyButton(page, false);
                }
            }

            TMP_Text pageLabel = Direct<TMP_Text>(root, "쪽");
            if (pageLabel != null)
            {
                pageLabel.color = UiModernDark.TextMain;
            }

            TMP_Text status = Direct<TMP_Text>(root, "상태");
            if (status != null)
            {
                status.color = UiModernDark.TextGold;
            }

            TMP_Text empty = Direct<TMP_Text>(root, "빈 목록");
            if (empty != null)
            {
                empty.color = UiModernDark.TextMuted;
            }
        }

        private static void StyleConfirm(RectTransform root)
        {
            Transform confirm = root.Find("확인");
            if (confirm == null)
            {
                return;
            }

            if (confirm.TryGetComponent(out Image shade))
            {
                shade.sprite = null;
                shade.type   = Image.Type.Simple;
                shade.color  = new Color(0f, 0f, 0f, SHADE_ALPHA);
            }

            Image card = confirm.Find("확인 패널") == null ? null : confirm.Find("확인 패널").GetComponent<Image>();
            if (card != null)
            {
                ModernDarkStyle.ApplyPanel(card);
            }

            TMP_Text message = ModernDarkStyle.FindText(confirm, "확인 글자");
            if (message != null)
            {
                message.color = UiModernDark.TextMain;
            }

            Transform accept = confirm.Find("확인 버튼");
            if ((accept != null) && (accept.TryGetComponent(out Button acceptButton)))
            {
                ModernDarkStyle.ApplyButton(acceptButton, true);
            }

            Transform cancel = confirm.Find("취소 버튼");
            if ((cancel != null) && (cancel.TryGetComponent(out Button cancelButton)))
            {
                ModernDarkStyle.ApplyButton(cancelButton, false);
            }
        }
    }
}
#endif
