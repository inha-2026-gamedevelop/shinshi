// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary> 장면에 놓인 인벤토리 창을 넓은 어두운 스타일로 한 번 다시 배치하고, 선택한 아이템의 상세 패널을 붙인다 </summary>
    internal sealed class InventoryModernLayout
    {
        // 장면을 저장하지 않아도 되도록 기존 직렬화 참조에서 부모를 거슬러 올라가 배치한다.
        // 참조가 없는 장식(제목 줄, 밑줄)만 이름으로 한 번 찾는다. 장면 파일에 이 요소들의 참조 필드가 없기 때문이다

        /****************************************
        *                Fields
        ****************************************/

        public const float WIDTH  = 1100f;
        public const float HEIGHT = 680f;

        private const float MARGIN        = 24f;
        private const float HEADER_HEIGHT = 64f;
        private const float TAB_TOP       = 76f;
        private const float TAB_HEIGHT    = 44f;
        private const float TAB_WIDTH     = 104f;
        private const float TAB_SPACING   = 2f;
        private const float GRID_TOP      = 134f;
        private const float FOOTER_HEIGHT = 64f;
        private const float DETAIL_WIDTH  = 360f;
        private const float GAP           = 16f;
        private const float GRID_PADDING  = 6f;
        private const float CELL_SIZE     = 74f;
        private const float CELL_SPACING  = 6f;
        private const float SORT_LEFT     = 300f;
        private const float ARRANGE_LEFT  = 312f;
        private const float ICON_FRAME    = 132f;
        private const float ICON_PADDING  = 12f;
        private const float CORNER_SIZE   = 24f;
        private const float CORNER_INSET  = 4f;

        private static readonly Color DIVIDER_COLOR = new Color(UiModernDark.GoldDim.r, UiModernDark.GoldDim.g, UiModernDark.GoldDim.b, 0.5f);

        // EItemGrade 값 순서와 같아야 한다
        private static readonly Color[] G_GradeColors =
        {
            new Color(0.80f, 0.82f, 0.86f, 1f),
            new Color(0.38f, 0.66f, 0.98f, 1f),
            new Color(0.72f, 0.50f, 0.96f, 1f),
            new Color(0.98f, 0.72f, 0.28f, 1f),
        };

        private static readonly string[] G_GradeNames = { "일반", "희귀", "영웅", "전설" };

        private readonly TMP_FontAsset _regular;
        private readonly TMP_FontAsset _bold;

        private GameObject _detailRoot;
        private GameObject _contentRoot;
        private TMP_Text   _emptyLabel;
        private Image      _iconImage;
        private Image      _iconFrame;
        private TMP_Text   _titleLabel;
        private TMP_Text   _metaLabel;
        private TMP_Text   _stateLabel;
        private TMP_Text   _descriptionLabel;

        /****************************************
        *                Methods
        ****************************************/

        public InventoryModernLayout(TMP_FontAsset regular, TMP_FontAsset bold)
        {
            _regular = regular;
            _bold    = bold;
        }

        /// <summary> 창, 분류 탭, 칸 목록, 아래 줄을 넓은 배치로 옮기고 상세 패널을 만든다 </summary>
        public void Apply(RectTransform window, Toggle[] tabs, ItemSlotGridView grid, TMP_Dropdown sortDropdown, Button orderButton,
            Button arrangeButton, TMP_Text capacityLabel)
        {
            StyleWindow(window);
            StyleHeader(window);
            StyleTabs(window, tabs);
            StyleFooter(window, capacityLabel, sortDropdown, orderButton, arrangeButton);
            StyleGrid(window, grid);
            BuildDetail(window);
        }

        /// <summary> 선택한 칸의 실제 값으로 상세 패널을 채운다. 빈 선택이면 안내 문구를 보인다 </summary>
        public void ShowItem(bool hasItem, in ItemSlotData slot, string categoryLabel, Sprite icon)
        {
            if (_detailRoot == null)
            {
                return;
            }

            _contentRoot.SetActive(hasItem);
            _emptyLabel.gameObject.SetActive(!hasItem);
            if (!hasItem)
            {
                return;
            }

            int grade = Mathf.Clamp((int)slot.Grade, 0, G_GradeNames.Length - 1);
            Color gradeColor = G_GradeColors[grade];

            _iconImage.sprite  = icon;
            _iconImage.enabled = (icon != null);
            _iconFrame.color   = gradeColor;
            _titleLabel.text   = slot.Title;
            _titleLabel.color  = gradeColor;
            _metaLabel.text    = string.IsNullOrEmpty(categoryLabel)
                ? string.Format("{0} · {1}개", G_GradeNames[grade], slot.Count)
                : string.Format("{0} · {1} · {2}개", categoryLabel, G_GradeNames[grade], slot.Count);
            _stateLabel.text   = slot.IsEquipped ? (slot.IsLocked ? "착용 중 · 잠김" : "착용 중") : (slot.IsLocked ? "잠김" : string.Empty);
            _descriptionLabel.text = slot.Description;
        }

        private void StyleWindow(RectTransform window)
        {
            window.anchorMin        = new Vector2(0.5f, 0.5f);
            window.anchorMax        = new Vector2(0.5f, 0.5f);
            window.pivot            = new Vector2(0.5f, 0.5f);
            window.anchoredPosition = Vector2.zero;
            window.sizeDelta        = new Vector2(WIDTH, HEIGHT);

            if (window.TryGetComponent(out Image image))
            {
                SetSliced(image, UiModernDark.SPRITE_PANEL, Color.white);
            }

            AddCorner(window, "TL", new Vector2(0f, 1f), new Vector2(1f, 1f));
            AddCorner(window, "TR", new Vector2(1f, 1f), new Vector2(-1f, 1f));
            AddCorner(window, "BL", new Vector2(0f, 0f), new Vector2(1f, -1f));
            AddCorner(window, "BR", new Vector2(1f, 0f), new Vector2(-1f, -1f));
        }

        private void StyleHeader(RectTransform window)
        {
            Transform header = window.Find("Header");
            if (header == null)
            {
                return;
            }

            ((RectTransform)header).sizeDelta = new Vector2(0f, HEADER_HEIGHT);

            // 옛 제목 띠는 새 패널 테두리와 겹쳐 가린다
            Transform band = header.Find("Band");
            if ((band != null) && band.TryGetComponent(out Image bandImage))
            {
                bandImage.enabled = false;
            }

            Transform titleGroup = header.Find("TitleGroup");
            if (titleGroup != null)
            {
                var rect = (RectTransform)titleGroup;
                rect.anchorMin        = new Vector2(0.5f, 0.5f);
                rect.anchorMax        = new Vector2(0.5f, 0.5f);
                rect.pivot            = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;

                if (titleGroup.TryGetComponent(out HorizontalLayoutGroup layout))
                {
                    layout.childAlignment = TextAnchor.MiddleCenter;
                }

                Transform title = titleGroup.Find("Title");
                if ((title != null) && title.TryGetComponent(out TMP_Text titleText))
                {
                    titleText.fontSize = 26f;
                    titleText.color    = UiModernDark.TextGold;
                }
            }

            AddDiamond(header, "L", -110f);
            AddDiamond(header, "R", 110f);
        }

        private void StyleTabs(RectTransform window, Toggle[] tabs)
        {
            if ((tabs == null) || (tabs.Length == 0) || (tabs[0] == null))
            {
                return;
            }

            var tabsRect = (RectTransform)tabs[0].transform.parent;
            tabsRect.anchorMin        = new Vector2(0f, 1f);
            tabsRect.anchorMax        = new Vector2(0f, 1f);
            tabsRect.pivot            = new Vector2(0f, 1f);
            tabsRect.anchoredPosition = new Vector2(MARGIN, -TAB_TOP);
            tabsRect.sizeDelta        = new Vector2((tabs.Length * TAB_WIDTH) + ((tabs.Length - 1) * TAB_SPACING), TAB_HEIGHT);

            if (tabsRect.TryGetComponent(out HorizontalLayoutGroup row))
            {
                row.spacing = TAB_SPACING;
            }

            for (int i = 0; i < tabs.Length; ++i)
            {
                if (tabs[i] == null)
                {
                    continue;
                }

                if (tabs[i].TryGetComponent(out LayoutElement element))
                {
                    element.preferredWidth = TAB_WIDTH;
                }

                if (tabs[i].targetGraphic is Image image)
                {
                    SetSliced(image, UiModernDark.SPRITE_TAB, Color.white);
                }

                ColorBlock colors = tabs[i].colors;
                colors.normalColor      = Color.white;
                colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
                colors.pressedColor     = new Color(0.8f, 0.8f, 0.8f, 1f);
                colors.selectedColor    = Color.white;
                tabs[i].colors          = colors;
            }

            Transform line = window.Find("TabsLine");
            if (line != null)
            {
                ((RectTransform)line).anchoredPosition = new Vector2(0f, -(TAB_TOP + TAB_HEIGHT + 2f));
            }
        }

        private void StyleFooter(RectTransform window, TMP_Text capacityLabel, TMP_Dropdown sortDropdown, Button orderButton,
            Button arrangeButton)
        {
            // 용량 글자 → ValueGroup → CapacityGroup → Footer 순으로 올라간다
            if (capacityLabel != null)
            {
                var footer = (RectTransform)capacityLabel.transform.parent.parent.parent;
                footer.sizeDelta = new Vector2(footer.sizeDelta.x, FOOTER_HEIGHT);
            }

            Transform footerLine = window.Find("FooterLine");
            if (footerLine != null)
            {
                ((RectTransform)footerLine).anchoredPosition = new Vector2(0f, FOOTER_HEIGHT);
            }

            if (sortDropdown != null)
            {
                // 드롭다운 → SortGroup → SortRow
                var sortRow = (RectTransform)sortDropdown.transform.parent.parent;
                sortRow.anchorMin        = new Vector2(0f, 0f);
                sortRow.anchorMax        = new Vector2(0f, 0f);
                sortRow.pivot            = new Vector2(0f, 0.5f);
                sortRow.anchoredPosition = new Vector2(SORT_LEFT, FOOTER_HEIGHT * 0.5f);
                sortRow.sizeDelta        = new Vector2(480f, 29f);

                if (sortDropdown.targetGraphic is Image dropdownImage)
                {
                    SetSliced(dropdownImage, UiModernDark.SPRITE_BUTTON, Color.white);
                }
            }

            if ((orderButton != null) && orderButton.targetGraphic is Image orderImage)
            {
                SetSliced(orderImage, UiModernDark.SPRITE_BUTTON, Color.white);
            }

            if (arrangeButton != null)
            {
                var rect = (RectTransform)arrangeButton.transform;
                rect.anchorMin        = new Vector2(0f, 0.5f);
                rect.anchorMax        = new Vector2(0f, 0.5f);
                rect.pivot            = new Vector2(0f, 0.5f);
                rect.anchoredPosition = new Vector2(ARRANGE_LEFT, 0f);

                if (arrangeButton.targetGraphic is Image arrangeImage)
                {
                    SetSliced(arrangeImage, UiModernDark.SPRITE_BUTTON_PRIMARY, Color.white);
                }
            }
        }

        private void StyleGrid(RectTransform window, ItemSlotGridView grid)
        {
            if (grid == null)
            {
                return;
            }

            // 칸 목록 → Viewport → GridArea
            var gridArea = (RectTransform)grid.transform.parent.parent;
            float right = MARGIN + DETAIL_WIDTH + GAP;

            var inset = new GameObject(UiModernDark.DECOR_PREFIX + "GridInset", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            inset.layer = window.gameObject.layer;
            inset.transform.SetParent(window, false);
            inset.transform.SetSiblingIndex(gridArea.GetSiblingIndex());
            Stretch((RectTransform)inset.transform, MARGIN, FOOTER_HEIGHT + 8f, right, GRID_TOP);
            Image insetImage = inset.GetComponent<Image>();
            SetSliced(insetImage, UiModernDark.SPRITE_INSET, Color.white);
            insetImage.raycastTarget = false;

            Stretch(gridArea, MARGIN + GRID_PADDING, FOOTER_HEIGHT + 8f + GRID_PADDING, right + GRID_PADDING, GRID_TOP + GRID_PADDING);

            if (grid.TryGetComponent(out GridLayoutGroup layout))
            {
                layout.cellSize = new Vector2(CELL_SIZE, CELL_SIZE);
                layout.spacing  = new Vector2(CELL_SPACING, CELL_SPACING);
            }
        }

        private void BuildDetail(RectTransform window)
        {
            _detailRoot = new GameObject(UiModernDark.DECOR_PREFIX + "Detail", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _detailRoot.layer = window.gameObject.layer;
            _detailRoot.transform.SetParent(window, false);
            var root = (RectTransform)_detailRoot.transform;
            root.anchorMin        = new Vector2(1f, 0f);
            root.anchorMax        = new Vector2(1f, 1f);
            root.pivot            = new Vector2(1f, 0.5f);
            root.sizeDelta        = new Vector2(DETAIL_WIDTH, -(TAB_TOP + FOOTER_HEIGHT + 8f));
            root.anchoredPosition = new Vector2(-MARGIN, ((FOOTER_HEIGHT + 8f) - TAB_TOP) * 0.5f);
            Image rootImage = _detailRoot.GetComponent<Image>();
            SetSliced(rootImage, UiModernDark.SPRITE_INSET, Color.white);
            rootImage.raycastTarget = false;

            _emptyLabel = NewText(root, "Empty", "아이템을 선택하면 상세 정보가 보입니다", 18f, UiModernDark.TextMuted, TextAlignmentOptions.Center, false);
            _emptyLabel.textWrappingMode = TextWrappingModes.Normal;
            Stretch((RectTransform)_emptyLabel.transform, 16f, 16f, 16f, 16f);

            var content = new GameObject("Content", typeof(RectTransform));
            content.layer = window.gameObject.layer;
            content.transform.SetParent(root, false);
            _contentRoot = content;
            var contentRect = (RectTransform)content.transform;
            Stretch(contentRect, 0f, 0f, 0f, 0f);

            GameObject frame = NewImage(contentRect, "IconFrame", UiModernDark.SPRITE_SLOT, Color.white);
            _iconFrame = frame.GetComponent<Image>();
            var frameRect = (RectTransform)frame.transform;
            frameRect.anchorMin        = new Vector2(0.5f, 1f);
            frameRect.anchorMax        = new Vector2(0.5f, 1f);
            frameRect.pivot            = new Vector2(0.5f, 1f);
            frameRect.anchoredPosition = new Vector2(0f, -24f);
            frameRect.sizeDelta        = new Vector2(ICON_FRAME, ICON_FRAME);

            var icon = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            icon.layer = window.gameObject.layer;
            icon.transform.SetParent(frameRect, false);
            Stretch((RectTransform)icon.transform, ICON_PADDING, ICON_PADDING, ICON_PADDING, ICON_PADDING);
            _iconImage                = icon.GetComponent<Image>();
            _iconImage.preserveAspect = true;
            _iconImage.raycastTarget  = false;

            float textTop = 24f + ICON_FRAME + 16f;
            _titleLabel = NewText(contentRect, "Title", string.Empty, 26f, UiModernDark.TextGold, TextAlignmentOptions.Center, true);
            PlaceTop((RectTransform)_titleLabel.transform, textTop, 36f);
            _metaLabel = NewText(contentRect, "Meta", string.Empty, 16f, UiModernDark.TextMuted, TextAlignmentOptions.Center, false);
            PlaceTop((RectTransform)_metaLabel.transform, textTop + 40f, 24f);
            _stateLabel = NewText(contentRect, "State", string.Empty, 16f, UiModernDark.Cyan, TextAlignmentOptions.Center, false);
            PlaceTop((RectTransform)_stateLabel.transform, textTop + 66f, 24f);

            GameObject divider = NewImage(contentRect, "Divider", null, DIVIDER_COLOR);
            var dividerRect = (RectTransform)divider.transform;
            dividerRect.anchorMin        = new Vector2(0f, 1f);
            dividerRect.anchorMax        = new Vector2(1f, 1f);
            dividerRect.pivot            = new Vector2(0.5f, 1f);
            dividerRect.anchoredPosition = new Vector2(0f, -(textTop + 98f));
            dividerRect.sizeDelta        = new Vector2(-32f, 1f);

            _descriptionLabel = NewText(contentRect, "Description", string.Empty, 17f, UiModernDark.TextMain, TextAlignmentOptions.TopLeft, false);
            _descriptionLabel.textWrappingMode = TextWrappingModes.Normal;
            Stretch((RectTransform)_descriptionLabel.transform, 20f, 16f, 20f, textTop + 108f);

            ShowItem(false, default, null, null);
        }

        private TMP_Text NewText(RectTransform parent, string name, string text, float size, Color color, TextAlignmentOptions alignment, bool bold)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
            TMP_FontAsset font = bold ? _bold : _regular;
            if (font != null)
            {
                label.font = font;
            }

            label.text             = text;
            label.fontSize         = size;
            label.color            = color;
            label.alignment        = alignment;
            label.raycastTarget    = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode     = TextOverflowModes.Ellipsis;
            return label;
        }

        private static GameObject NewImage(RectTransform parent, string name, string spriteName, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
            if (spriteName != null)
            {
                SetSliced(image, spriteName, color);
            }
            else
            {
                image.color = color;
            }

            return go;
        }

        private static void SetSliced(Image image, string spriteName, Color color)
        {
            image.sprite = UiModernDarkSprites.Get(spriteName);
            image.type   = Image.Type.Sliced;
            image.color  = color;
        }

        private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot     = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void PlaceTop(RectTransform rect, float top, float height)
        {
            rect.anchorMin        = new Vector2(0f, 1f);
            rect.anchorMax        = new Vector2(1f, 1f);
            rect.pivot            = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -top);
            rect.sizeDelta        = new Vector2(-32f, height);
        }

        private static void AddCorner(RectTransform window, string name, Vector2 anchor, Vector2 scale)
        {
            GameObject go = NewImage(window, UiModernDark.DECOR_PREFIX + "Corner" + name, null, Color.white);
            Image image = go.GetComponent<Image>();
            image.sprite = UiModernDarkSprites.Get(UiModernDark.SPRITE_CORNER);
            image.type   = Image.Type.Simple;
            var rect = (RectTransform)go.transform;
            rect.anchorMin        = anchor;
            rect.anchorMax        = anchor;
            rect.pivot            = new Vector2(0.5f, 0.5f);
            rect.sizeDelta        = new Vector2(CORNER_SIZE, CORNER_SIZE);
            float center = CORNER_INSET + (CORNER_SIZE * 0.5f);
            rect.anchoredPosition = new Vector2((anchor.x > 0.5f) ? -center : center, (anchor.y > 0.5f) ? -center : center);
            rect.localScale       = new Vector3(scale.x, scale.y, 1f);
        }

        private static void AddDiamond(Transform header, string name, float x)
        {
            GameObject go = NewImage((RectTransform)header, UiModernDark.DECOR_PREFIX + "Diamond" + name, null, Color.white);
            Image image = go.GetComponent<Image>();
            image.sprite = UiModernDarkSprites.Get(UiModernDark.SPRITE_DIAMOND);
            image.type   = Image.Type.Simple;
            image.SetNativeSize();
            var rect = (RectTransform)go.transform;
            rect.anchorMin        = new Vector2(0.5f, 0.5f);
            rect.anchorMax        = new Vector2(0.5f, 0.5f);
            rect.pivot            = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, 0f);
        }
    }
}
