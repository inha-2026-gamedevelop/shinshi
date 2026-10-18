// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary> 신체 강화 · 수련 창을 시안 구성으로 한 번 다시 배치한다. 두 시안은 같은 성장 데이터를 다르게 보여 주는 표시 모드이고 서버 처리 · 초안 · 잠금은 TrainingView 그대로다 </summary>
    public static class TrainingModernLayout
    {
        private const int   STAT_COUNT = 5;
        private const float WIDTH      = 1500f;
        private const float HEIGHT     = 840f;
        private const float CARD_X     = 32f;
        private const float CARD_Y     = 136f;
        private const float CARD_W     = 380f;
        private const float CARD_H     = 104f;
        private const float CARD_GAP   = 10f;
        private const float BAR_X      = 20f;
        private const float BAR_Y      = 76f;
        private const float BAR_W      = 340f;
        private const float BAR_H      = 10f;
        private const float RIGHT_X    = 1072f;
        private const float RIGHT_W    = 396f;
        private const float TOP_BAR_Y  = 72f;
        private const float BOTTOM_Y   = 712f;
        private const float ART_W      = 300f;
        private const float ART_H      = 450f;
        private const string ART_BODY  = "ModernDarkGrowth/BodyMeridian";
        private const string ART_DUMMY = "ModernDarkGrowth/TrainingDummy";

        private const string ALT_PREFIX    = UiModernDark.DECOR_PREFIX + "A_";
        private const string NORMAL_PREFIX = UiModernDark.DECOR_PREFIX + "B_";
        private const string TITLE_ALT     = "신체 강화";
        private const string TITLE_NORMAL  = "수련";
        private const string TRAIN_ALT     = "강화";
        private const string TRAIN_NORMAL  = "수련 시작";

        public static void Apply(TrainingView view)
        {
            var window = view.transform as RectTransform;
            TMP_Text status = ModernDarkKit.Field<TMP_Text>(view, "_statusText");
            if (!ModernDarkKit.TryBegin(view, window, status, out ModernDarkKit kit))
            {
                return;
            }

            kit.Frame(new Vector2(WIDTH, HEIGHT), null);

            kit.Label("A_Title", TITLE_ALT, 30f, UiModernDark.TextGold, TextAlignmentOptions.Center, true, 0f, 6f, WIDTH, 48f);
            kit.Label("B_Title", TITLE_NORMAL, 30f, UiModernDark.TextGold, TextAlignmentOptions.Center, true, 0f, 6f, WIDTH, 48f);
            kit.Line("TitleLine", 24f, 60f, WIDTH - 48f);

            PlaceCommon(kit, view);
            PlaceTopBar(kit, view);
            PlaceStatCards(kit, view);
            PlaceCenter(kit, view);
            PlaceRightPanel(kit, view);
            PlaceBottom(kit, view);

            kit.HideLegacy();
            Present(view, view.ModernAlternate);
        }

        /// <summary> 두 표시 모드의 장식 묶음과 행동 글자만 바꾼다. 데이터와 초안은 건드리지 않는다 </summary>
        public static void Present(TrainingView view, bool alternate)
        {
            Transform window = view.transform;
            if (window.Find(ModernDarkKit.APPLIED_NAME) == null)
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

            Button train = ModernDarkKit.Field<Button>(view, "_trainButton");
            SetButtonLabel(train, alternate ? TRAIN_ALT : TRAIN_NORMAL);
        }

        /// <summary> 선택 표시 · 현재 다음 단계 · 진행 막대를 현재 초안 값으로 맞춘다. 레이아웃이 적용되지 않았으면 아무것도 하지 않는다 </summary>
        public static void Refresh(TrainingView view, int selected, int confirmedStage, int workingStage, int[] working, int statCap, int workingTotal, int totalCap)
        {
            Transform window = view.transform;
            if (window.Find(ModernDarkKit.APPLIED_NAME) == null)
            {
                return;
            }

            for (int i = 0; i < STAT_COUNT; ++i)
            {
                Transform sel = window.Find(UiModernDark.DECOR_PREFIX + "Sel" + i);
                if (sel != null)
                {
                    sel.gameObject.SetActive(i == selected);
                }

                int value = ((working != null) && (i < working.Length)) ? working[i] : 0;
                SetFill(window.Find(NORMAL_PREFIX + "Fill" + i) as RectTransform, BAR_W, value, statCap);
            }

            SetFill(window.Find(NORMAL_PREFIX + "TotalFill") as RectTransform, 560f, workingTotal, totalCap);
            SetNumber(window, "CurNum", confirmedStage);
            SetNumber(window, "NextNum", workingStage);

            Button train = ModernDarkKit.Field<Button>(view, "_trainButton");
            SetButtonLabel(train, view.ModernAlternate ? TRAIN_ALT : TRAIN_NORMAL);
        }

        private static void PlaceCommon(ModernDarkKit kit, TrainingView view)
        {
            Button close = ModernDarkKit.Field<Button>(view, "_closeButton");
            kit.Place(close, WIDTH - 72f, 8f, 44f, 44f);
            ModernDarkKit.StyleButton(close, false);

            GameObject preview = ModernDarkKit.Field<GameObject>(view, "_previewTag");
            kit.Place(preview, 24f, 14f, 180f, 32f);

            Transform drag = kit.Window.Find("DragSurface");
            if (drag != null)
            {
                kit.Place(drag.gameObject, 0f, 0f, WIDTH - 84f, 56f);
            }
        }

        private static void PlaceTopBar(ModernDarkKit kit, TrainingView view)
        {
            TMP_Text total   = ModernDarkKit.Field<TMP_Text>(view, "_totalInvestText");
            TMP_Text pool    = ModernDarkKit.Field<TMP_Text>(view, "_poolText");
            TMP_Text essence = ModernDarkKit.Field<TMP_Text>(view, "_essenceText");

            kit.Place(total, 32f, TOP_BAR_Y, 420f, 36f);
            kit.Place(pool, 470f, TOP_BAR_Y, 460f, 36f);
            kit.Label("EssenceCaption", "보유 정기", 22f, UiModernDark.TextMuted, TextAlignmentOptions.Right, false, 1000f, TOP_BAR_Y, 130f, 36f);
            kit.Place(essence, 1140f, TOP_BAR_Y, 328f, 36f);

            Style(total, 24f, UiModernDark.TextMuted, TextAlignmentOptions.Left);
            Style(pool, 22f, UiModernDark.TextGold, TextAlignmentOptions.Center);
            Style(essence, 26f, UiModernDark.TextGold, TextAlignmentOptions.Right);

            kit.Line("TopLine", 24f, 116f, WIDTH - 48f);

            // 수련 모드의 전체 진행 막대. 값은 전체 강화 합계 / 상한으로 Refresh가 채운다
            kit.Inset("B_TotalBar", 470f, 108f, 560f, 6f);
            RectTransform fill = kit.Inset("B_TotalFill", 470f, 108f, 0f, 6f, UiModernDark.SPRITE_BUTTON_PRIMARY);
            fill.name = NORMAL_PREFIX + "TotalFill";
        }

        private static void PlaceStatCards(ModernDarkKit kit, TrainingView view)
        {
            var buttons = ModernDarkKit.Field<Button[]>(view, "_statButtons");
            var stages  = ModernDarkKit.Field<TMP_Text[]>(view, "_stageLabels");
            for (int i = 0; i < STAT_COUNT; ++i)
            {
                float y = CARD_Y + (i * (CARD_H + CARD_GAP));
                kit.Inset("Card" + i, CARD_X, y, CARD_W, CARD_H, UiModernDark.SPRITE_BUTTON);
                kit.Inset("Sel" + i, CARD_X, y, CARD_W, CARD_H, UiModernDark.SPRITE_SELECTED);
                kit.Inset("B_Bar" + i, CARD_X + BAR_X, y + BAR_Y, BAR_W, BAR_H);
                kit.Inset("B_Fill" + i, CARD_X + BAR_X, y + BAR_Y, 0f, BAR_H, UiModernDark.SPRITE_BUTTON_PRIMARY);

                Button button = ((buttons != null) && (i < buttons.Length)) ? buttons[i] : null;
                if (button != null)
                {
                    kit.Place(button, CARD_X, y, CARD_W, CARD_H);
                    Transform label = button.transform.Find("Label");
                    if (label is RectTransform labelRect)
                    {
                        ModernDarkKit.PlaceRect(labelRect, 20f, 14f, 200f, 52f);
                        Style(labelRect.GetComponent<TMP_Text>(), 28f, UiModernDark.TextMain, TextAlignmentOptions.Left);
                    }
                }

                TMP_Text stage = ((stages != null) && (i < stages.Length)) ? stages[i] : null;
                if (stage != null)
                {
                    ModernDarkKit.PlaceRect((RectTransform)stage.transform, 220f, 14f, 140f, 52f);
                    Style(stage, 24f, UiModernDark.TextGold, TextAlignmentOptions.Right);
                }
            }
        }

        private static void PlaceCenter(ModernDarkKit kit, TrainingView view)
        {
            kit.Inset("CenterBox", 432f, CARD_Y, 620f, 560f);

            // 몸 그림(신체 강화)과 수련 허수아비(수련)는 같은 자리를 모드별로 나눠 쓴다. 그림이 있으면 5각 그래프는 구석에 작게 둔다
            bool bodyArt  = AddArt(kit, "A_BodyArt", ART_BODY);
            bool dummyArt = AddArt(kit, "B_DummyArt", ART_DUMMY);
            var radar = ModernDarkKit.Field<TrainingRadarGraphic>(view, "_radar");
            if (bodyArt || dummyArt)
            {
                kit.Place(radar, 896f, CARD_Y + 14f, 150f, 150f);
            }
            else
            {
                kit.Place(radar, 512f, CARD_Y + 14f, 460f, 440f);
            }

            TMP_Text title = ModernDarkKit.Field<TMP_Text>(view, "_selectedTitleText");
            TMP_Text stage = ModernDarkKit.Field<TMP_Text>(view, "_selectedStageText");
            kit.Place(title, 432f, CARD_Y + 468f, 620f, 44f);
            kit.Place(stage, 432f, CARD_Y + 512f, 620f, 36f);
            Style(title, 32f, UiModernDark.TextGold, TextAlignmentOptions.Center);
            Style(stage, 22f, UiModernDark.TextMuted, TextAlignmentOptions.Center);
        }

        /// <summary> Resources의 실제 그림을 가운데 상자에 비율을 지켜 넣는다. 그림이 없으면 아무것도 만들지 않고 false를 돌려준다 </summary>
        private static bool AddArt(ModernDarkKit kit, string name, string resourcePath)
        {
            var texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null)
            {
                return false;
            }

            float scale = Mathf.Min(ART_W / texture.width, ART_H / texture.height);
            float w     = texture.width * scale;
            float h     = texture.height * scale;
            RectTransform rect = kit.Inset(name, 432f + ((620f - w) * 0.5f), CARD_Y + 8f + ((ART_H - h) * 0.5f), w, h);
            Image legacy = rect.GetComponent<Image>();
            if (legacy != null)
            {
                Object.DestroyImmediate(legacy);
            }

            RawImage raw = rect.gameObject.AddComponent<RawImage>();
            raw.texture       = texture;
            raw.color         = Color.white;
            raw.raycastTarget = false;
            return true;
        }

        private static void PlaceRightPanel(ModernDarkKit kit, TrainingView view)
        {
            kit.Inset("RightBox", RIGHT_X, CARD_Y, RIGHT_W, 560f);

            // 신체 강화: 현재 단계와 다음 단계를 원으로 보여 준다
            kit.Label("A_CurCaption", "현재 단계", 20f, UiModernDark.TextMuted, TextAlignmentOptions.Center, false, 1092f, 152f, 150f, 28f);
            kit.Label("A_NextCaption", "다음 단계", 20f, UiModernDark.TextMuted, TextAlignmentOptions.Center, false, 1298f, 152f, 150f, 28f);
            kit.Inset("A_CurCircle", 1119f, 186f, 96f, 96f, UiModernDark.SPRITE_SLOT);
            kit.Inset("A_NextCircle", 1325f, 186f, 96f, 96f, UiModernDark.SPRITE_SLOT);
            kit.Label("A_CurNum", "0", 40f, UiModernDark.TextMain, TextAlignmentOptions.Center, true, 1119f, 206f, 96f, 56f);
            kit.Label("A_NextNum", "0", 40f, UiModernDark.TextGold, TextAlignmentOptions.Center, true, 1325f, 206f, 96f, 56f);
            kit.Label("A_Arrow", ">>", 32f, UiModernDark.TextGold, TextAlignmentOptions.Center, true, 1228f, 208f, 90f, 52f);

            // 수련: 선택한 항목의 수련 효과 머리말
            kit.Label("B_Header", "수련 효과", 26f, UiModernDark.TextGold, TextAlignmentOptions.Left, true, 1092f, 152f, 356f, 36f);
            kit.Line("B_HeaderLine", 1092f, 192f, 356f);

            kit.Inset("EffectBox", 1092f, 300f, 356f, 150f);
            TMP_Text before = ModernDarkKit.Field<TMP_Text>(view, "_selectedBeforeText");
            TMP_Text after  = ModernDarkKit.Field<TMP_Text>(view, "_selectedAfterText");
            kit.Label("EffectBeforeCaption", "현재", 18f, UiModernDark.TextMuted, TextAlignmentOptions.Left, false, 1108f, 308f, 120f, 24f);
            kit.Place(before, 1108f, 332f, 324f, 36f);
            kit.Label("EffectArrow", ">", 28f, UiModernDark.TextGold, TextAlignmentOptions.Center, true, 1108f, 366f, 324f, 32f);
            kit.Label("EffectAfterCaption", "다음", 18f, UiModernDark.TextMuted, TextAlignmentOptions.Left, false, 1108f, 396f, 120f, 24f);
            kit.Place(after, 1108f, 418f, 324f, 28f);
            Style(before, 22f, UiModernDark.TextMain, TextAlignmentOptions.Left);
            Style(after, 22f, UiModernDark.TextGold, TextAlignmentOptions.Left);

            TMP_Text cost    = ModernDarkKit.Field<TMP_Text>(view, "_costText");
            TMP_Text balance = ModernDarkKit.Field<TMP_Text>(view, "_balanceText");
            kit.Label("MaterialCaption", "필요 재료", 22f, UiModernDark.TextGold, TextAlignmentOptions.Left, true, 1092f, 466f, 356f, 28f);
            kit.Place(cost, 1092f, 498f, 356f, 32f);
            kit.Place(balance, 1092f, 530f, 356f, 32f);
            Style(cost, 22f, UiModernDark.TextMain, TextAlignmentOptions.Left);
            Style(balance, 20f, UiModernDark.TextMuted, TextAlignmentOptions.Left);

            Button minus  = ModernDarkKit.Field<Button>(view, "_minusButton");
            Button plus   = ModernDarkKit.Field<Button>(view, "_plusButton");
            TMP_Text qty  = ModernDarkKit.Field<TMP_Text>(view, "_quantityText");
            kit.Place(minus, 1092f, 584f, 80f, 52f);
            kit.Place(qty, 1180f, 584f, 176f, 52f);
            kit.Place(plus, 1364f, 584f, 84f, 52f);
            ModernDarkKit.StyleButton(minus, false);
            ModernDarkKit.StyleButton(plus, false);
            SkillWindowModernLayout.FitButtonLabel(minus, 30f);
            SkillWindowModernLayout.FitButtonLabel(plus, 30f);
            Style(qty, 28f, UiModernDark.TextMain, TextAlignmentOptions.Center);
        }

        private static void PlaceBottom(ModernDarkKit kit, TrainingView view)
        {
            Button respec = ModernDarkKit.Field<Button>(view, "_respecButton");
            Button revert = ModernDarkKit.Field<Button>(view, "_revertButton");
            Button train  = ModernDarkKit.Field<Button>(view, "_trainButton");
            TMP_Text status = ModernDarkKit.Field<TMP_Text>(view, "_statusText");

            kit.Place(respec, CARD_X, BOTTOM_Y, CARD_W, 56f);
            kit.Place(status, 432f, BOTTOM_Y, 620f, 56f);
            kit.Place(revert, RIGHT_X, BOTTOM_Y, 150f, 56f);
            kit.Place(train, 1236f, BOTTOM_Y, 232f, 56f);
            ModernDarkKit.StyleButton(respec, false);
            ModernDarkKit.StyleButton(revert, false);
            ModernDarkKit.StyleButton(train, true);
            SkillWindowModernLayout.FitButtonLabel(respec, 20f);
            SkillWindowModernLayout.FitButtonLabel(revert, 20f);
            SkillWindowModernLayout.FitButtonLabel(train, 22f);
            Style(status, 20f, UiModernDark.TextMuted, TextAlignmentOptions.Center);
        }

        private static void Style(TMP_Text text, float size, Color color, TextAlignmentOptions align)
        {
            if (text == null)
            {
                return;
            }

            ModernDarkKit.StyleText(text, size, color);
            text.alignment        = align;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode     = TextOverflowModes.Ellipsis;
        }

        private static void SetButtonLabel(Button button, string text)
        {
            if (button == null)
            {
                return;
            }

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = text;
            }
        }

        private static void SetNumber(Transform window, string name, int value)
        {
            Transform number = window.Find(ALT_PREFIX + name);
            if (number != null)
            {
                var label = number.GetComponent<TMP_Text>();
                if (label != null)
                {
                    label.text = value.ToString();
                }
            }
        }

        private static void SetFill(RectTransform fill, float fullWidth, int value, int cap)
        {
            if (fill == null)
            {
                return;
            }

            float ratio = (cap > 0) ? Mathf.Clamp01((float)value / cap) : 0f;
            fill.sizeDelta = new Vector2(fullWidth * ratio, fill.sizeDelta.y);
            var image = fill.GetComponent<Image>();
            if (image != null)
            {
                image.enabled = ratio > 0f;
            }
        }
    }
}
