// System
using System;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.UI
{
    /// <summary> Restyles the sortie prep window once, server ready rules and button wiring stay on SortiePrepView </summary>
    public static class SortiePrepModernLayout
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float WIDTH      = 1400f;
        private const float HEIGHT     = 820f;
        private const float LEFT_X     = 32f;
        private const float TOP_Y      = 84f;
        private const float LEFT_W     = 420f;
        private const float LEFT_H     = 704f;
        private const float ART_X      = 48f;
        private const float ART_Y      = 100f;
        private const float ART_W      = 388f;
        private const float ART_H      = 232f;
        private const float CARD_X     = 476f;
        private const float CARD_Y     = 124f;
        private const float CARD_W     = 892f;
        private const float CARD_H     = 124f;
        private const float CARD_GAP   = 10f;
        private const float BUTTON_Y   = 724f;
        private const float BUTTON_H   = 64f;
        private const float BUTTON_W   = 220f;
        private const float DOT_SIZE   = 20f;
        private const float DOT_STEP   = 36f;
        private const int   CARD_COUNT = 4;

        private const float INVITE_GAP         = 20f;
        private const float INVITE_GAP_SMALL   = 12f;
        private const float INVITE_H           = 48f;
        private const float INVITE_INPUT_W     = 270f;
        private const float INVITE_BUTTON_W    = 130f;

        /****************************************
        *                Methods
        ****************************************/

        public static void Apply(SortiePrepView view)
        {
            var window = view.transform as RectTransform;
            TMP_Text nameLabel = ModernDarkKit.Field<TMP_Text>(view, "_dungeonNameLabel");
            if (!ModernDarkKit.TryBegin(view, window, nameLabel, out ModernDarkKit kit))
            {
                return;
            }

            kit.Frame(new Vector2(WIDTH, HEIGHT), "출정 준비");

            kit.Inset("ContentBox", LEFT_X, TOP_Y, LEFT_W, LEFT_H);
            PlaceContent(kit, view, nameLabel);
            PlaceMembers(kit, view);
            PlaceBottom(kit, view);

            kit.HideLegacy();
        }

        private static void PlaceContent(ModernDarkKit kit, SortiePrepView view, TMP_Text nameLabel)
        {
            var art         = ModernDarkKit.Field<Image>(view, "_dungeonArt");
            var placeholder = ModernDarkKit.Field<GameObject>(view, "_artPlaceholder");
            kit.Inset("ArtBox", ART_X, ART_Y, ART_W, ART_H);
            kit.Place(art, ART_X, ART_Y, ART_W, ART_H);
            kit.PlaceGroup(placeholder, ART_X, ART_Y, ART_W, ART_H);
            kit.Place(ModernDarkKit.Field<GameObject>(view, "_hardFx"), ART_X, ART_Y, ART_W, ART_H);

            var summary = ModernDarkKit.Field<TMP_Text>(view, "_dungeonSummaryLabel");
            kit.Place(nameLabel, ART_X, 348f, ART_W, 44f);
            kit.Place(summary, ART_X, 400f, ART_W, 190f);
            ModernDarkKit.StyleText(nameLabel, 30f, UiModernDark.TextGold);
            ModernDarkKit.StyleText(summary, 20f, UiModernDark.TextMuted);
            if (summary != null)
            {
                summary.textWrappingMode = TextWrappingModes.Normal;
                summary.alignment        = TextAlignmentOptions.TopLeft;
                summary.overflowMode     = TextOverflowModes.Ellipsis;
            }

            kit.Line("ContentLine", ART_X, 600f, ART_W);

            var prev  = ModernDarkKit.Field<Button>(view, "_prevDifficultyButton");
            var next  = ModernDarkKit.Field<Button>(view, "_nextDifficultyButton");
            var label = ModernDarkKit.Field<TMP_Text>(view, "_difficultyLabel");
            kit.Place(prev, ART_X, 616f, 48f, 48f);
            kit.Place(label, ART_X + 56f, 616f, ART_W - 112f, 48f);
            kit.Place(next, ART_X + ART_W - 48f, 616f, 48f, 48f);
            ModernDarkKit.StyleButton(prev, false);
            ModernDarkKit.StyleButton(next, false);
            AddGlyph(prev, label, "<");
            AddGlyph(next, label, ">");
            ModernDarkKit.StyleText(label, 26f, UiModernDark.TextGold);
            if (label != null)
            {
                label.alignment = TextAlignmentOptions.Center;
            }

            var dots = ModernDarkKit.Field<Image[]>(view, "_difficultyDots");
            if (dots == null)
            {
                return;
            }

            float dotsWidth = (dots.Length - 1) * DOT_STEP;
            float dotX      = ART_X + ((ART_W - dotsWidth) * 0.5f);
            for (int i = 0; i < dots.Length; ++i)
            {
                kit.Place(dots[i], dotX + (i * DOT_STEP) - (DOT_SIZE * 0.5f), 680f, DOT_SIZE, DOT_SIZE);
            }
        }

        private static void PlaceMembers(ModernDarkKit kit, SortiePrepView view)
        {
            var ready = ModernDarkKit.Field<TMP_Text>(view, "_readyCountLabel");
            kit.Place(ready, CARD_X, TOP_Y, CARD_W, 32f);
            ModernDarkKit.StyleText(ready, 22f, UiModernDark.TextGold);
            if (ready != null)
            {
                ready.alignment = TextAlignmentOptions.Right;
            }

            var members = ModernDarkKit.Field<Array>(view, "_members");
            int count   = (members != null) ? Mathf.Min(members.Length, CARD_COUNT) : 0;
            for (int i = 0; i < count; ++i)
            {
                object row = members.GetValue(i);
                if (row == null)
                {
                    continue;
                }

                float y = CARD_Y + (i * (CARD_H + CARD_GAP));
                kit.Inset("Card" + i, CARD_X, y, CARD_W, CARD_H);
                PlaceCard(kit, row, y);
            }
        }

        private static void PlaceCard(ModernDarkKit kit, object row, float y)
        {
            var icon   = ModernDarkKit.Field<Image>(row, "_classIcon");
            var title  = ModernDarkKit.Field<TMP_Text>(row, "_nameLabel");
            var power  = ModernDarkKit.Field<TMP_Text>(row, "_powerLabel");
            var state  = ModernDarkKit.Field<TMP_Text>(row, "_stateLabel");
            var leader = ModernDarkKit.Field<GameObject>(row, "_leaderIcon");
            var fx     = ModernDarkKit.Field<GameObject>(row, "_readyFx");
            var tint   = ModernDarkKit.Field<GameObject>(row, "_problemTint");
            var empty  = ModernDarkKit.Field<GameObject>(row, "_emptyRoot");
            var parts  = ModernDarkKit.Field<GameObject[]>(row, "_filledParts");

            // 묶음을 먼저 놓아야 그 안의 위젯이 창 좌표로 환산된다
            kit.PlaceGroup(empty, CARD_X, y, CARD_W, CARD_H);
            if (parts != null)
            {
                for (int i = 0; i < parts.Length; ++i)
                {
                    if (IsWidget(parts[i], icon, title, power, state, leader, fx, tint))
                    {
                        continue;
                    }

                    kit.PlaceGroup(parts[i], CARD_X, y, CARD_W, CARD_H);
                }
            }

            kit.Place(fx, CARD_X, y, CARD_W, CARD_H);
            kit.Place(tint, CARD_X, y, CARD_W, CARD_H);
            kit.Place(icon, CARD_X + 20f, y + 22f, 80f, 80f);
            kit.Place(leader, CARD_X + 84f, y + 8f, 32f, 32f);
            kit.Place(title, CARD_X + 124f, y + 18f, 520f, 44f);
            kit.Place(power, CARD_X + 124f, y + 68f, 360f, 36f);
            kit.Place(state, CARD_X + CARD_W - 244f, y + 40f, 220f, 44f);
            ModernDarkKit.StyleText(title, 26f, UiModernDark.TextMain);
            ModernDarkKit.StyleText(power, 20f, UiModernDark.TextMuted);
            ModernDarkKit.StyleText(state, 24f, UiModernDark.TextMain);
            if (state != null)
            {
                state.alignment = TextAlignmentOptions.Right;
            }

            ModernDarkKit.Quiet(empty);
        }

        private static bool IsWidget(GameObject part, Image icon, TMP_Text title, TMP_Text power, TMP_Text state, GameObject leader, GameObject fx, GameObject tint)
        {
            if (part == null)
            {
                return true;
            }

            return (part == leader) || (part == fx) || (part == tint)
                || ((icon != null) && (part == icon.gameObject))
                || ((title != null) && (part == title.gameObject))
                || ((power != null) && (part == power.gameObject))
                || ((state != null) && (part == state.gameObject));
        }

        private static void PlaceBottom(ModernDarkKit kit, SortiePrepView view)
        {
            var notice = ModernDarkKit.Field<TMP_Text>(view, "_noticeLabel");
            if (notice == null)
            {
                notice = CreateNotice(view);
            }

            kit.Place(notice, CARD_X, CARD_Y + (CARD_COUNT * (CARD_H + CARD_GAP)) + 4f, CARD_W, 48f);
            ModernDarkKit.StyleText(notice, 20f, UiModernDark.TextMain);
            if (notice != null)
            {
                notice.alignment        = TextAlignmentOptions.TopLeft;
                notice.textWrappingMode = TextWrappingModes.Normal;
                notice.overflowMode     = TextOverflowModes.Ellipsis;
                notice.raycastTarget    = false;
            }

            var leave = ModernDarkKit.Field<Button>(view, "_leaveButton");
            var ready = ModernDarkKit.Field<Button>(view, "_readyButton");
            var start = ModernDarkKit.Field<Button>(view, "_startButton");
            var close = ModernDarkKit.Field<Button>(view, "_closeButton");
            kit.Place(leave, CARD_X, BUTTON_Y, BUTTON_W, BUTTON_H);
            kit.Place(ready, CARD_X + CARD_W - BUTTON_W, BUTTON_Y, BUTTON_W, BUTTON_H);
            kit.Place(start, CARD_X + CARD_W - BUTTON_W, BUTTON_Y, BUTTON_W, BUTTON_H);
            kit.Place(close, WIDTH - 72f, 8f, 44f, 44f);
            ModernDarkKit.StyleButton(leave, false);
            ModernDarkKit.StyleButton(ready, true);
            ModernDarkKit.StyleButton(start, true);
            ModernDarkKit.StyleButton(close, false);
            AddGlyph(close, notice, "X");
            CreateInviteRow(view);
        }

        // 나가기와 준비 사이 빈 자리에 대상 이름 입력과 초대 단추를 실행 중에만 만든다. 씬과 프리팹은 저장하지 않는다
        private static void CreateInviteRow(SortiePrepView view)
        {
            const string INPUT_NAME  = UiModernDark.DECOR_PREFIX + "InviteInput";
            const string BUTTON_NAME = UiModernDark.DECOR_PREFIX + "InviteButton";

            Transform inputExisting  = view.transform.Find(INPUT_NAME);
            Transform buttonExisting = view.transform.Find(BUTTON_NAME);
            if ((inputExisting != null) && (buttonExisting != null))
            {
                view.AdoptInviteRow(inputExisting.GetComponent<TMP_InputField>(), buttonExisting.GetComponent<Button>());
                return;
            }

            TMP_Text fontSource = ModernDarkKit.Field<TMP_Text>(view, "_dungeonNameLabel");
            TMP_FontAsset font  = (fontSource != null) ? fontSource.font : null;
            float rowX          = CARD_X + BUTTON_W + INVITE_GAP;
            float rowY          = BUTTON_Y + ((BUTTON_H - INVITE_H) * 0.5f);

            TMP_InputField input = PartyRecruitView.NewInput(view.transform, INPUT_NAME, font, "초대할 캐릭터 이름", rowX, rowY, INVITE_INPUT_W,
                INVITE_H, TMP_InputField.ContentType.Standard, CharacterNameRules.MAX_INPUT_LENGTH);
            Button button = PartyRecruitView.NewButton(view.transform, BUTTON_NAME, font, "초대", rowX + INVITE_INPUT_W + INVITE_GAP_SMALL, rowY,
                INVITE_BUTTON_W, INVITE_H, false, null);
            view.AdoptInviteRow(input, button);
            view.SetInviteState(false, false);
        }

        // 원본에 안내 글자 참조가 없으면 실행 중에만 만들어 뷰에 연결한다. 씬과 프리팹은 저장하지 않는다
        private static TMP_Text CreateNotice(SortiePrepView view)
        {
            const string NOTICE_NAME = UiModernDark.DECOR_PREFIX + "Notice";
            Transform existing = view.transform.Find(NOTICE_NAME);
            TMP_Text text = (existing != null) ? existing.GetComponent<TMP_Text>() : null;
            if (text == null)
            {
                var go = new GameObject(NOTICE_NAME, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                go.layer = view.gameObject.layer;
                go.transform.SetParent(view.transform, false);
                text = go.GetComponent<TextMeshProUGUI>();

                TMP_Text fontSource = ModernDarkKit.Field<TMP_Text>(view, "_dungeonNameLabel");
                if (fontSource != null)
                {
                    text.font = fontSource.font;
                }
            }

            view.AdoptNoticeLabel(text);
            return text;
        }

        // 옛 단추 글자는 폰트에 없는 기호라 비어 보인다. 끄고 한글 폰트로 안전한 글자를 새 자식에 얹는다. 단추와 클릭 연결은 그대로다
        private static void AddGlyph(Button button, TMP_Text fontSource, string glyph)
        {
            if (button == null)
            {
                return;
            }

            const string GLYPH_NAME = "Glyph";
            foreach (TMP_Text old in button.GetComponentsInChildren<TMP_Text>(true))
            {
                if (old.gameObject.name != (UiModernDark.DECOR_PREFIX + GLYPH_NAME))
                {
                    old.gameObject.SetActive(false);
                }
            }

            Transform existing = button.transform.Find(UiModernDark.DECOR_PREFIX + GLYPH_NAME);
            if (existing != null)
            {
                return;
            }

            var go = new GameObject(UiModernDark.DECOR_PREFIX + GLYPH_NAME, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.layer = button.gameObject.layer;
            go.transform.SetParent(button.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var text = go.GetComponent<TextMeshProUGUI>();
            if (fontSource != null)
            {
                text.font = fontSource.font;
            }

            text.text             = glyph;
            text.fontSize         = 28f;
            text.color            = UiModernDark.TextMain;
            text.alignment        = TextAlignmentOptions.Center;
            text.raycastTarget    = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
        }
    }
}
