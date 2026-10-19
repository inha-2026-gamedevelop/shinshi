// Unity
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary> 스킬창을 시안 구성으로 한 번 다시 배치한다. 스킬(08)과 부적(09)은 같은 SkillWindowView · Binder를 쓰는 두 표시 모드이고 등록 · 강화 · 분해 · 장착 · 조합 저장 연결과 초안은 그대로다 </summary>
    public static class SkillWindowModernLayout
    {
        private const int   TAB_COUNT    = 5;
        private const int   CHOICE_COUNT = 6;
        private const int   TIER_COUNT   = 3;
        private const float WIDTH        = 1500f;
        private const float HEIGHT       = 840f;
        private const float CHOICE_W     = 400f;
        private const float CHOICE_H     = 56f;

        private const string ALT_PREFIX    = UiModernDark.DECOR_PREFIX + "A_";
        private const string NORMAL_PREFIX = UiModernDark.DECOR_PREFIX + "B_";

        public static void Apply(SkillWindowView view)
        {
            var window = view.transform as RectTransform;
            TMP_Text status = ModernDarkKit.Field<TMP_Text>(view, "_statusLabel");
            if (!ModernDarkKit.TryBegin(view, window, status, out ModernDarkKit kit))
            {
                return;
            }

            // 옛 마감 효과가 메시를 덮어 창 바탕 · 제목줄 · 단추 그림을 가리므로 먼저 끈다
            foreach (EquipmentGrowthSurface surface in window.GetComponentsInChildren<EquipmentGrowthSurface>(true))
            {
                surface.enabled = false;
            }

            kit.Frame(new Vector2(WIDTH, HEIGHT), null);
            var back = window.GetComponent<Image>();
            if (back != null)
            {
                back.enabled = true;
            }

            kit.Label("A_Title", "부적", 30f, UiModernDark.TextGold, TextAlignmentOptions.Center, true, 0f, 6f, WIDTH, 48f);
            kit.Label("B_Title", "스킬", 30f, UiModernDark.TextGold, TextAlignmentOptions.Center, true, 0f, 6f, WIDTH, 48f);
            kit.Line("TitleLine", 24f, 60f, WIDTH - 48f);

            PlaceCommon(kit, view);
            DecorateTabs(view);
            DecorateChoices(view);
            PlaceDecor(kit, view);

            GameObject group = ModernDarkKit.Field<GameObject>(view, "_talismanGroup");
            kit.PlaceGroup(group, 0f, 0f, WIDTH, HEIGHT);
            AdoptModeWidgets(kit, view);
            HideGroupLegacy(view, group);

            GameObject confirm = ModernDarkKit.Field<GameObject>(view, "_confirmPanel");
            kit.PlaceGroup(confirm, 0f, 0f, WIDTH, HEIGHT, false);
            if (confirm != null)
            {
                Transform card = confirm.transform.Find("확인 패널");
                var cardImage = (card != null) ? card.GetComponent<Image>() : null;
                if (cardImage != null)
                {
                    ModernDarkKit.SetSliced(cardImage, UiModernDark.SPRITE_PANEL, Color.white);
                }
            }

            var accept = ModernDarkKit.Field<Button>(view, "_confirmAccept");
            var cancel = ModernDarkKit.Field<Button>(view, "_confirmCancel");
            ModernDarkKit.StyleButton(accept, false);
            ModernDarkKit.StyleButton(cancel, true);
            FitButtonLabel(accept, 18f);
            FitButtonLabel(cancel, 18f);

            kit.HideLegacy();
            if (confirm != null)
            {
                confirm.transform.SetAsLastSibling();
            }

            Present(view, view.ModernAlternate);
        }

        /// <summary> 두 표시 모드의 자리와 보이기만 바꾼다. 데이터 · 초안 · 요청 상태와 부적 묶음의 켜기 · 끄기는 건드리지 않는다 </summary>
        public static void Present(SkillWindowView view, bool alternate)
        {
            var window = view.transform as RectTransform;
            if ((window == null) || (window.Find(ModernDarkKit.APPLIED_NAME) == null))
            {
                return;
            }

            for (int i = 0; i < window.childCount; ++i)
            {
                Transform child = window.GetChild(i);
                if (child.name.StartsWith(ALT_PREFIX, System.StringComparison.Ordinal))
                {
                    child.gameObject.SetActive(alternate);
                }
                else if (child.name.StartsWith(NORMAL_PREFIX, System.StringComparison.Ordinal))
                {
                    child.gameObject.SetActive(!alternate);
                }
            }

            PresentTabs(view, window, alternate);
            PresentSkillInfo(view, window, alternate);
            PresentChoices(view, window, alternate);
            PresentEffect(view, window, alternate);
            RefreshIcons(view);
        }

        /// <summary> 고른 기술(08) · 부적(09)의 아이콘을 가운데 자리에 맞춘다. 부적 아이콘이 없으면 기술 아이콘을 쓰고 둘 다 없으면 숨긴다 </summary>
        public static void RefreshIcons(SkillWindowView view)
        {
            Transform window = view.transform;
            if (window.Find(ModernDarkKit.APPLIED_NAME) == null)
            {
                return;
            }

            ApplyIcon(window, "MD_B_IconSlot", "MD_B_SkillIcon", view.SelectedSkillIcon);
            Sprite talisman = (view.SelectedTalismanIcon != null) ? view.SelectedTalismanIcon : view.SelectedSkillIcon;
            ApplyIcon(window, "MD_A_IconSlot", "MD_A_TalismanIcon", talisman);
        }

        private static void ApplyIcon(Transform window, string slotName, string iconName, Sprite sprite)
        {
            Transform slot = window.Find(slotName);
            Transform icon = window.Find(iconName);
            if ((slot != null) && (slot.TryGetComponent(out Image slotImage)))
            {
                slotImage.enabled = sprite != null;
            }

            if ((icon != null) && (icon.TryGetComponent(out Image iconImage)))
            {
                iconImage.sprite         = sprite;
                iconImage.type           = Image.Type.Simple;
                iconImage.preserveAspect = true;
                iconImage.color          = Color.white;
                iconImage.raycastTarget  = false;
                iconImage.enabled        = sprite != null;
            }
        }

        /// <summary> 이름과 비용이 두 칸 띄어 붙은 동작 글자를 이름 / 비용 두 줄로 나눈다. 내용은 그대로이고 줄바꿈만 넣어 글자 중간에서 끊기지 않게 한다 </summary>
        public static string SplitCostLabel(string label)
        {
            return string.IsNullOrEmpty(label) ? label : label.Replace("  ", "\n");
        }

        private static void PlaceCommon(ModernDarkKit kit, SkillWindowView view)
        {
            Button close = ModernDarkKit.Field<Button>(view, "_closeButton");
            kit.Place(close, WIDTH - 72f, 8f, 44f, 44f);
            ModernDarkKit.StyleButton(close, false);
            AddGlyph(kit, close, "×");

            Transform drag = kit.Window.Find("제목줄");
            if (drag != null)
            {
                kit.Place(drag.gameObject, 0f, 0f, WIDTH - 84f, 56f);
                var image = drag.GetComponent<Image>();
                if (image != null)
                {
                    image.color = Color.clear;
                }
            }

            TMP_Text classLabel = ModernDarkKit.Field<TMP_Text>(view, "_classLabel");
            TMP_Text stackRule  = ModernDarkKit.Field<TMP_Text>(view, "_stackRuleLabel");
            kit.Place(classLabel, 32f, 14f, 260f, 32f);
            kit.Place(stackRule, 700f, 14f, 700f, 32f);
            Style(classLabel, 22f, UiModernDark.TextMuted, TextAlignmentOptions.Left, false);
            Style(stackRule, 18f, UiModernDark.TextMuted, TextAlignmentOptions.Right, false);

            TMP_Text status = ModernDarkKit.Field<TMP_Text>(view, "_statusLabel");
            Button retry    = ModernDarkKit.Field<Button>(view, "_retryButton");
            Button save     = ModernDarkKit.Field<Button>(view, "_saveButton");
            kit.Place(status, 32f, 796f, 1000f, 32f);
            kit.Place(retry, 1236f, 790f, 232f, 40f);
            kit.Place(save, 1044f, 716f, 404f, 52f);
            Style(status, 20f, UiModernDark.TextMuted, TextAlignmentOptions.Left, false);
            ModernDarkKit.StyleButton(retry, false);
            ModernDarkKit.StyleButton(save, true);
            FitButtonLabel(retry, 18f);
            FitButtonLabel(save, 22f);
        }

        private static void DecorateTabs(SkillWindowView view)
        {
            var buttons = ModernDarkKit.Field<Button[]>(view, "_tabButtons");
            var marks   = ModernDarkKit.Field<GameObject[]>(view, "_tabSelected");
            for (int i = 0; i < TAB_COUNT; ++i)
            {
                ModernDarkKit.StyleButton(Pick(buttons, i), false);
                StyleMark(Pick(marks, i));
            }
        }

        private static void DecorateChoices(SkillWindowView view)
        {
            var buttons  = ModernDarkKit.Field<Button[]>(view, "_choiceButtons");
            var icons    = ModernDarkKit.Field<Image[]>(view, "_choiceIcons");
            var names    = ModernDarkKit.Field<TMP_Text[]>(view, "_choiceNames");
            var states   = ModernDarkKit.Field<TMP_Text[]>(view, "_choiceStates");
            var selected = ModernDarkKit.Field<GameObject[]>(view, "_choiceSelected");
            var equipped = ModernDarkKit.Field<GameObject[]>(view, "_choiceEquipped");
            for (int i = 0; i < CHOICE_COUNT; ++i)
            {
                ModernDarkKit.StyleButton(Pick(buttons, i), false);
                Image icon = Pick(icons, i);
                if (icon != null)
                {
                    ModernDarkKit.PlaceRect((RectTransform)icon.transform, 8f, 8f, 40f, 40f);
                }

                TMP_Text name = Pick(names, i);
                if (name != null)
                {
                    ModernDarkKit.PlaceRect((RectTransform)name.transform, 58f, 4f, 250f, 28f);
                    Style(name, 22f, UiModernDark.TextMain, TextAlignmentOptions.Left, false);
                }

                TMP_Text state = Pick(states, i);
                if (state != null)
                {
                    ModernDarkKit.PlaceRect((RectTransform)state.transform, 58f, 32f, 250f, 22f);
                    Style(state, 16f, UiModernDark.TextMuted, TextAlignmentOptions.Left, false);
                }

                StyleMark(Pick(selected, i));

                GameObject own = Pick(equipped, i);
                if (own != null)
                {
                    ModernDarkKit.PlaceRect((RectTransform)own.transform, CHOICE_W - 84f, 12f, 76f, 32f);
                }
            }
        }

        private static void StyleMark(GameObject mark)
        {
            if (mark == null)
            {
                return;
            }

            var rect = mark.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            var image = mark.GetComponent<Image>();
            if (image != null)
            {
                ModernDarkKit.SetSliced(image, UiModernDark.SPRITE_SELECTED, Color.white);
                image.raycastTarget = false;
            }
        }

        private static void PlaceDecor(ModernDarkKit kit, SkillWindowView view)
        {
            // 스킬(08): 왼쪽 기술 목록 · 가운데 기술 정보 · 오른쪽 연계 부적
            kit.Inset("B_TabBox", 24f, 76f, 316f, 704f);
            kit.Inset("B_CenterBox", 360f, 76f, 640f, 704f);
            kit.Inset("B_RightBox", 1020f, 76f, 448f, 704f);
            kit.Label("B_ChoiceHeader", "연계 부적", 24f, UiModernDark.TextGold, TextAlignmentOptions.Left, true, 1044f, 92f, 404f, 32f);
            MakeButton(kit, "B_ToTalisman", "부적 관리", 36f, 636f, 292f, 56f, () => view.SetModernPresentation(true));

            // 부적(09): 위쪽 기술 탭 · 왼쪽 후보 목록 · 가운데 효과와 동작 · 오른쪽 기술 정보와 저장
            kit.Inset("A_ListBox", 24f, 148f, 440f, 632f);
            kit.Inset("A_DetailBox", 484f, 148f, 520f, 632f);
            kit.Inset("A_RightBox", 1024f, 148f, 444f, 632f);
            kit.Label("A_ChoiceHeader", "보유 부적", 24f, UiModernDark.TextGold, TextAlignmentOptions.Left, true, 44f, 160f, 400f, 32f);
            kit.Label("A_SkillHeader", "선택한 기술", 24f, UiModernDark.TextGold, TextAlignmentOptions.Left, true, 1044f, 160f, 404f, 32f);
            kit.Inset("A_ActionBox", 500f, 584f, 488f, 180f, UiModernDark.SPRITE_PANEL);

            // 고른 기술 · 부적의 실제 아이콘을 가운데 빈 자리에 크게 보인다. 아이콘이 없으면 RefreshIcons가 숨긴다
            kit.Inset("B_IconSlot", 565f - 8f, 530f - 8f, 246f, 246f, UiModernDark.SPRITE_SLOT);
            kit.Inset("B_SkillIcon", 565f, 530f, 230f, 230f);
            kit.Inset("A_IconSlot", 684f - 6f, 212f - 6f, 132f, 132f, UiModernDark.SPRITE_SLOT);
            kit.Inset("A_TalismanIcon", 684f, 212f, 120f, 120f);
            MakeButton(kit, "A_ToSkill", "스킬 보기", 44f, 726f, 400f, 44f, () => view.SetModernPresentation(false));
        }

        private static void AdoptModeWidgets(ModernDarkKit kit, SkillWindowView view)
        {
            var tabs    = ModernDarkKit.Field<Button[]>(view, "_tabButtons");
            var choices = ModernDarkKit.Field<Button[]>(view, "_choiceButtons");
            for (int i = 0; i < TAB_COUNT; ++i)
            {
                Adopt(kit, Pick(tabs, i));
            }

            for (int i = 0; i < CHOICE_COUNT; ++i)
            {
                Adopt(kit, Pick(choices, i));
            }

            string[] texts =
            {
                "_skillNameLabel", "_skillStatsLabel", "_skillDescription", "_talismanNotice", "_footerLabel",
                "_effectTitle", "_effectBonus", "_effectDescription", "_effectWarning",
            };

            foreach (string field in texts)
            {
                Adopt(kit, ModernDarkKit.Field<TMP_Text>(view, field));
            }

            string[] actions = { "_registerButton", "_upgradeButton", "_disassembleButton", "_quantityDown", "_quantityUp", "_equipButton" };
            foreach (string field in actions)
            {
                var button = ModernDarkKit.Field<Button>(view, field);
                Adopt(kit, button);
                ModernDarkKit.StyleButton(button, field == "_registerButton");
                bool step = (field == "_quantityDown") || (field == "_quantityUp");
                FitButtonLabel(button, step ? 28f : 19f, false);
            }
        }

        private static void HideGroupLegacy(SkillWindowView view, GameObject group)
        {
            if (group == null)
            {
                return;
            }

            var keep = new System.Collections.Generic.HashSet<Transform>();
            ModernDarkKit.CollectReferences(view, keep);
            for (int i = group.transform.childCount - 1; i >= 0; --i)
            {
                Transform child = group.transform.GetChild(i);
                if (child.name.StartsWith(UiModernDark.DECOR_PREFIX, System.StringComparison.Ordinal)
                    || child.name.StartsWith("단계", System.StringComparison.Ordinal))
                {
                    continue;
                }

                if (!ModernDarkKit.ContainsAny(child, keep))
                {
                    child.gameObject.SetActive(false);
                }
                else if (!keep.Contains(child))
                {
                    // 위젯을 품은 옛 패널은 켜 두고 바탕 그림만 끈다
                    Graphic own = child.GetComponent<Graphic>();
                    if (own != null)
                    {
                        own.enabled = false;
                    }
                }
            }
        }

        private static void PresentTabs(SkillWindowView view, RectTransform window, bool alternate)
        {
            var buttons = ModernDarkKit.Field<Button[]>(view, "_tabButtons");
            var icons   = ModernDarkKit.Field<Image[]>(view, "_tabIcons");
            var labels  = ModernDarkKit.Field<TMP_Text[]>(view, "_tabLabels");
            for (int i = 0; i < TAB_COUNT; ++i)
            {
                Button button = Pick(buttons, i);
                if (alternate)
                {
                    Put(window, button, 32f + (i * 188f), 76f, 180f, 56f);
                }
                else
                {
                    Put(window, button, 36f, 88f + (i * 104f), 292f, 96f);
                }

                Image icon = Pick(icons, i);
                if (icon != null)
                {
                    float inset = alternate ? 8f : 12f;
                    float size  = alternate ? 40f : 72f;
                    ModernDarkKit.PlaceRect((RectTransform)icon.transform, inset, inset, size, size);
                }

                TMP_Text label = Pick(labels, i);
                if (label != null)
                {
                    ModernDarkKit.PlaceRect((RectTransform)label.transform, alternate ? 56f : 96f, alternate ? 8f : 24f, alternate ? 116f : 180f, alternate ? 40f : 48f);
                    Style(label, alternate ? 22f : 28f, UiModernDark.TextMain, TextAlignmentOptions.Left, false);
                }
            }
        }

        private static void PresentSkillInfo(SkillWindowView view, RectTransform window, bool alternate)
        {
            TMP_Text name   = ModernDarkKit.Field<TMP_Text>(view, "_skillNameLabel");
            TMP_Text stats  = ModernDarkKit.Field<TMP_Text>(view, "_skillStatsLabel");
            TMP_Text desc   = ModernDarkKit.Field<TMP_Text>(view, "_skillDescription");
            TMP_Text notice = ModernDarkKit.Field<TMP_Text>(view, "_talismanNotice");
            TMP_Text footer = ModernDarkKit.Field<TMP_Text>(view, "_footerLabel");

            if (alternate)
            {
                Put(window, name, 1044f, 200f, 404f, 40f);
                Put(window, stats, 1044f, 244f, 404f, 60f);
                Put(window, desc, 1044f, 312f, 404f, 230f);
                Put(window, notice, 500f, 264f, 488f, 120f);
                Put(window, footer, 1044f, 650f, 404f, 56f);
            }
            else
            {
                Put(window, name, 392f, 100f, 576f, 48f);
                Put(window, stats, 392f, 156f, 576f, 40f);
                Put(window, desc, 392f, 210f, 576f, 300f);
                Put(window, notice, 1044f, 300f, 404f, 120f);
                Put(window, footer, 1044f, 672f, 404f, 32f);
            }

            Style(name, alternate ? 26f : 34f, UiModernDark.TextGold, TextAlignmentOptions.Left, false);
            Style(stats, alternate ? 20f : 22f, UiModernDark.TextMuted, TextAlignmentOptions.TopLeft, true);
            Style(desc, alternate ? 20f : 22f, UiModernDark.TextMain, TextAlignmentOptions.TopLeft, true);
            Style(notice, 20f, UiModernDark.TextMuted, TextAlignmentOptions.TopLeft, true);
            Style(footer, 18f, UiModernDark.TextMuted, TextAlignmentOptions.TopLeft, alternate);
        }

        private static void PresentChoices(SkillWindowView view, RectTransform window, bool alternate)
        {
            var choices = ModernDarkKit.Field<Button[]>(view, "_choiceButtons");
            float baseX = alternate ? 44f : 1044f;
            float baseY = alternate ? 200f : 136f;
            float step  = alternate ? 176f : 188f;

            for (int i = 0; i < CHOICE_COUNT; ++i)
            {
                int tier   = i / 2;
                int choice = i % 2;
                Put(window, Pick(choices, i), baseX, baseY + (tier * step) + 32f + (choice * (CHOICE_H + 6f)), CHOICE_W, CHOICE_H);
            }

            GameObject group = ModernDarkKit.Field<GameObject>(view, "_talismanGroup");
            if (group == null)
            {
                return;
            }

            int tierIndex = 0;
            for (int i = 0; (i < group.transform.childCount) && (tierIndex < TIER_COUNT); ++i)
            {
                Transform child = group.transform.GetChild(i);
                if (!child.name.StartsWith("단계", System.StringComparison.Ordinal))
                {
                    continue;
                }

                Put(window, child, baseX, baseY + (tierIndex * step), CHOICE_W, 28f);
                Style(child.GetComponent<TMP_Text>(), 20f, UiModernDark.TextGold, TextAlignmentOptions.Left, false);
                ++tierIndex;
            }
        }

        private static void PresentEffect(SkillWindowView view, RectTransform window, bool alternate)
        {
            TMP_Text title   = ModernDarkKit.Field<TMP_Text>(view, "_effectTitle");
            TMP_Text bonus   = ModernDarkKit.Field<TMP_Text>(view, "_effectBonus");
            TMP_Text desc    = ModernDarkKit.Field<TMP_Text>(view, "_effectDescription");
            TMP_Text warning = ModernDarkKit.Field<TMP_Text>(view, "_effectWarning");

            Put(window, title, 500f, 164f, 488f, 44f);
            Put(window, bonus, 500f, 344f, 488f, 64f);
            Put(window, desc, 500f, 414f, 488f, 102f);
            Put(window, warning, 500f, 520f, 488f, 56f);
            Style(title, 30f, UiModernDark.TextGold, TextAlignmentOptions.Left, false);
            Style(bonus, 22f, UiModernDark.Cyan, TextAlignmentOptions.TopLeft, true);
            Style(desc, 20f, UiModernDark.TextMain, TextAlignmentOptions.TopLeft, true);
            Style(warning, 20f, UiModernDark.TextMuted, TextAlignmentOptions.TopLeft, true);

            Put(window, ModernDarkKit.Field<Button>(view, "_registerButton"), 512f, 594f, 232f, 60f);
            Put(window, ModernDarkKit.Field<Button>(view, "_upgradeButton"), 756f, 594f, 232f, 60f);
            Put(window, ModernDarkKit.Field<Button>(view, "_equipButton"), 512f, 660f, 476f, 40f);
            Put(window, ModernDarkKit.Field<Button>(view, "_quantityDown"), 512f, 708f, 64f, 48f);
            Put(window, ModernDarkKit.Field<Button>(view, "_disassembleButton"), 586f, 708f, 316f, 48f);
            Put(window, ModernDarkKit.Field<Button>(view, "_quantityUp"), 912f, 708f, 64f, 48f);

            // 스킬 모드는 기술 정보와 연계 부적 목록만 보여 주고, 효과 상세와 동작은 부적 모드에서 다룬다
            string[] fields =
            {
                "_effectTitle", "_effectBonus", "_effectDescription", "_effectWarning",
                "_registerButton", "_upgradeButton", "_equipButton", "_quantityDown", "_disassembleButton", "_quantityUp",
            };

            foreach (string field in fields)
            {
                var target = ModernDarkKit.Field<Component>(view, field);
                if ((target != null) && (target.gameObject.activeSelf != alternate))
                {
                    target.gameObject.SetActive(alternate);
                }
            }
        }

        // 옛 단추 안 글자는 고정 크기 상자라 단추를 다시 놓으면 어긋난다. 단추 전체에 맞추고 가운데 정렬한다
        internal static void FitButtonLabel(Button button, float maxSize, bool wrap = true)
        {
            TMP_Text label = (button != null) ? button.GetComponentInChildren<TMP_Text>(true) : null;
            if (label == null)
            {
                return;
            }

            var rect = (RectTransform)label.transform;
            rect.anchorMin  = Vector2.zero;
            rect.anchorMax  = Vector2.one;
            rect.pivot      = new Vector2(0.5f, 0.5f);
            rect.offsetMin  = new Vector2(6f, 2f);
            rect.offsetMax  = new Vector2(-6f, -2f);
            rect.localScale = Vector3.one;

            label.alignment          = TextAlignmentOptions.Center;
            label.textWrappingMode   = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            label.overflowMode       = TextOverflowModes.Overflow;
            label.enableAutoSizing   = true;
            label.fontSizeMin        = 11f;
            label.fontSizeMax        = maxSize;
            label.raycastTarget      = false;
        }

        // 아이콘 그림만 있던 단추에 글자 모양을 얹는다
        internal static void AddGlyph(ModernDarkKit kit, Button button, string glyph)
        {
            if (button == null)
            {
                return;
            }

            Transform existing = button.transform.Find(UiModernDark.DECOR_PREFIX + "Glyph");
            if (existing != null)
            {
                return;
            }

            var go = new GameObject(UiModernDark.DECOR_PREFIX + "Glyph", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.layer = button.gameObject.layer;
            go.transform.SetParent(button.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
            if (kit.Font != null)
            {
                text.font = kit.Font;
            }

            text.text          = glyph;
            text.fontSize      = 30f;
            text.color         = UiModernDark.TextMain;
            text.alignment     = TextAlignmentOptions.Center;
            text.raycastTarget = false;
        }

        private static void Adopt(ModernDarkKit kit, Component widget)
        {
            kit.Place(widget, 0f, 0f, 10f, 10f);
        }

        private static T Pick<T>(T[] items, int index) where T : class
        {
            return ((items != null) && (index >= 0) && (index < items.Length)) ? items[index] : null;
        }

        private static void Style(TMP_Text text, float size, Color color, TextAlignmentOptions align, bool wrap)
        {
            if (text == null)
            {
                return;
            }

            ModernDarkKit.StyleText(text, size, color);
            text.alignment        = align;
            text.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            text.overflowMode     = wrap ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
        }

        private static void Put(RectTransform window, Component widget, float x, float y, float width, float height)
        {
            if (widget != null)
            {
                Put(window, widget.transform, x, y, width, height);
            }
        }

        // 부모가 창이면 직접 놓고, 보이기 전환 묶음 안이면 부모를 유지한 채 창 좌표를 부모 기준으로 환산한다
        private static void Put(RectTransform window, Transform target, float x, float y, float width, float height)
        {
            var rect = target as RectTransform;
            if (rect == null)
            {
                return;
            }

            var parent = rect.parent as RectTransform;
            if ((parent == null) || (parent == window))
            {
                ModernDarkKit.PlaceRect(rect, x, y, width, height);
                return;
            }

            Vector3 topLeft = window.TransformPoint(new Vector3(window.rect.xMin + x, window.rect.yMax - y, 0f));
            Vector3 local   = parent.InverseTransformPoint(topLeft);
            Vector3 ws      = window.lossyScale;
            Vector3 ps      = parent.lossyScale;
            float scaleX    = (Mathf.Abs(ps.x) > 0.0001f) ? ws.x / ps.x : 1f;
            float scaleY    = (Mathf.Abs(ps.y) > 0.0001f) ? ws.y / ps.y : 1f;

            rect.anchorMin        = new Vector2(0f, 1f);
            rect.anchorMax        = new Vector2(0f, 1f);
            rect.pivot            = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(local.x - parent.rect.xMin, local.y - parent.rect.yMax);
            rect.sizeDelta        = new Vector2(width * scaleX, height * scaleY);
            rect.localScale       = Vector3.one;
        }

        private static void MakeButton(ModernDarkKit kit, string name, string text, float x, float y, float width, float height, UnityAction onClick)
        {
            var go = new GameObject(UiModernDark.DECOR_PREFIX + name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.layer = kit.Window.gameObject.layer;
            go.transform.SetParent(kit.Window, false);
            ModernDarkKit.PlaceRect((RectTransform)go.transform, x, y, width, height);

            var image = go.GetComponent<Image>();
            ModernDarkKit.SetSliced(image, UiModernDark.SPRITE_BUTTON, Color.white);
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.layer = go.layer;
            labelObject.transform.SetParent(go.transform, false);
            var labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            if (kit.Font != null)
            {
                label.font = kit.Font;
            }

            label.text          = text;
            label.fontSize      = 22f;
            label.alignment     = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.color         = UiModernDark.TextMain;
            ModernDarkKit.StyleButton(button, false);
            button.onClick.AddListener(onClick);
        }
    }
}
