// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary> Restyles the result window once, reward data, save lock and return wiring stay on ResultScreenView </summary>
    public static class ResultScreenModernLayout
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float WIDTH     = 1200f;
        private const float HEIGHT    = 780f;
        private const float PAD       = 32f;
        private const float BOX_Y     = 150f;
        private const float BOX_H     = 470f;
        private const float ESSENCE_W = 300f;
        private const float REWARD_W  = 400f;
        private const float BOX_GAP   = 16f;

        private const float SLOT_SIZE = 64f;
        private const float SLOT_GAP  = 8f;

        private const float BACKDROP_PAD   = 8f;
        private const float BACKDROP_ALPHA = 0.4f;

        private static readonly Color LOST_COLOR = new Color(0.86f, 0.40f, 0.36f, 1f);

        /****************************************
        *                Methods
        ****************************************/

        public static void Apply(ResultScreenView view)
        {
            var window = ModernDarkKit.Field<RectTransform>(view, "_window");
            if (window == null)
            {
                window = view.transform as RectTransform;
            }

            TMP_Text title = ModernDarkKit.Field<TMP_Text>(view, "_titleLabel");
            if (!ModernDarkKit.TryBegin(view, window, title, out ModernDarkKit kit))
            {
                return;
            }

            kit.Frame(new Vector2(WIDTH, HEIGHT), null);
            AddBackground(kit);
            DisableEffects(view);

            float essenceX = PAD;
            float confirmX = essenceX + ESSENCE_W + BOX_GAP;
            float chanceX  = confirmX + REWARD_W + BOX_GAP;
            float chanceW  = WIDTH - PAD - chanceX;

            var region = ModernDarkKit.Field<TMP_Text>(view, "_regionLabel");
            kit.Place(title, PAD, 28f, WIDTH - (PAD * 2f), 64f);
            kit.Place(region, PAD, 96f, WIDTH - (PAD * 2f), 36f);
            ModernDarkKit.StyleText(title, 48f, UiModernDark.TextGold);
            ModernDarkKit.StyleText(region, 24f, UiModernDark.TextMuted);
            Align(title, TextAlignmentOptions.Center);
            Align(region, TextAlignmentOptions.Center);
            kit.Line("TitleLine", PAD, 140f, WIDTH - (PAD * 2f));

            kit.Inset("EssenceBox", essenceX, BOX_Y, ESSENCE_W, BOX_H);
            kit.Inset("ConfirmedBox", confirmX, BOX_Y, REWARD_W, BOX_H);
            kit.Inset("ChanceBox", chanceX, BOX_Y, chanceW, BOX_H);
            kit.Label("EssenceHead", "정수", 24f, UiModernDark.TextGold, TextAlignmentOptions.Left, true, essenceX + 20f, BOX_Y + 14f, ESSENCE_W - 40f, 36f);
            kit.Label("ConfirmedHead", "확정 보상", 24f, UiModernDark.TextGold, TextAlignmentOptions.Left, true, confirmX + 20f, BOX_Y + 14f, REWARD_W - 40f, 36f);
            kit.Label("ChanceHead", "확률 보상", 24f, UiModernDark.TextGold, TextAlignmentOptions.Left, true, chanceX + 20f, BOX_Y + 14f, chanceW - 40f, 36f);

            PlaceEssence(kit, view, essenceX);
            PlaceReward(kit, view, "_confirmedGrid", "_confirmedEmpty", confirmX, REWARD_W);
            PlaceReward(kit, view, "_chanceGrid", "_chanceEmpty", chanceX, chanceW);
            PlaceBottom(kit, view);

            kit.HideLegacy();
        }

        // 카탈로그 배경이 있을 때만 깐다. 장식이라 입력을 막지 않고 다른 상자보다 아래에 그려진다
        private static void AddBackground(ModernDarkKit kit)
        {
            ModernDarkUiCatalog catalog = ModernDarkUiCatalog.Load();
            if ((catalog == null) || (catalog.Background == null))
            {
                return;
            }

            RectTransform rect = kit.Inset("Backdrop", BACKDROP_PAD, BACKDROP_PAD, WIDTH - (BACKDROP_PAD * 2f), HEIGHT - (BACKDROP_PAD * 2f));
            var image = rect.GetComponent<Image>();
            if (image == null)
            {
                return;
            }

            image.sprite        = catalog.Background;
            image.type          = Image.Type.Simple;
            image.color         = new Color(1f, 1f, 1f, BACKDROP_ALPHA);
            image.raycastTarget = false;
        }

        private static void PlaceEssence(ModernDarkKit kit, ResultScreenView view, float x)
        {
            var baseLabel  = ModernDarkKit.Field<TMP_Text>(view, "_baseLabel");
            var extraLabel = ModernDarkKit.Field<TMP_Text>(view, "_extraLabel");
            var lostLabel  = ModernDarkKit.Field<TMP_Text>(view, "_lostLabel");
            kit.Label("BaseName", "기본", 22f, UiModernDark.TextMuted, TextAlignmentOptions.Left, false, x + 20f, BOX_Y + 70f, 110f, 40f);
            kit.Label("ExtraName", "추가", 22f, UiModernDark.TextMuted, TextAlignmentOptions.Left, false, x + 20f, BOX_Y + 120f, 110f, 40f);
            kit.Place(baseLabel, x + 130f, BOX_Y + 70f, ESSENCE_W - 150f, 40f);
            kit.Place(extraLabel, x + 130f, BOX_Y + 120f, ESSENCE_W - 150f, 40f);
            kit.Place(lostLabel, x + 20f, BOX_Y + 176f, ESSENCE_W - 40f, 40f);
            ModernDarkKit.StyleText(baseLabel, 28f, UiModernDark.TextMain);
            ModernDarkKit.StyleText(extraLabel, 28f, UiModernDark.TextGold);
            ModernDarkKit.StyleText(lostLabel, 22f, LOST_COLOR);
            Align(baseLabel, TextAlignmentOptions.Right);
            Align(extraLabel, TextAlignmentOptions.Right);
            Align(lostLabel, TextAlignmentOptions.Right);
        }

        private static void PlaceReward(ModernDarkKit kit, ResultScreenView view, string gridName, string emptyName, float x, float width)
        {
            var grid  = ModernDarkKit.Field<ItemSlotGridView>(view, gridName);
            var empty = ModernDarkKit.Field<TMP_Text>(view, emptyName);
            kit.Place(grid, x + 20f, BOX_Y + 64f, width - 40f, BOX_H - 84f);
            WrapGrid(grid);
            kit.Place(empty, x + 20f, BOX_Y + 200f, width - 40f, 40f);
            ModernDarkKit.StyleText(empty, 22f, UiModernDark.TextMuted);
            Align(empty, TextAlignmentOptions.Center);
        }

        // 한 줄로 늘어서는 배치를 칸 폭에 맞춰 줄바꿈하는 격자로 바꾼다. 칸 수와 표시 규칙은 그대로 둔다
        private static void WrapGrid(ItemSlotGridView grid)
        {
            if (grid == null)
            {
                return;
            }

            GameObject host = grid.gameObject;
            var fitter = host.GetComponent<ContentSizeFitter>();
            if (fitter != null)
            {
                fitter.enabled = false;
            }

            var layout = host.GetComponent<GridLayoutGroup>();
            if (layout == null)
            {
                var old = host.GetComponent<LayoutGroup>();
                if (old != null)
                {
                    Object.DestroyImmediate(old);
                }

                layout = host.AddComponent<GridLayoutGroup>();
            }

            layout.cellSize        = new Vector2(SLOT_SIZE, SLOT_SIZE);
            layout.spacing         = new Vector2(SLOT_GAP, SLOT_GAP);
            layout.startCorner     = GridLayoutGroup.Corner.UpperLeft;
            layout.startAxis       = GridLayoutGroup.Axis.Horizontal;
            layout.childAlignment  = TextAnchor.UpperLeft;
            layout.constraint      = GridLayoutGroup.Constraint.Flexible;
            layout.padding         = new RectOffset(0, 0, 0, 0);
        }

        private static void PlaceBottom(ModernDarkKit kit, ResultScreenView view)
        {
            var save   = ModernDarkKit.Field<SaveStatusView>(view, "_saveStatus");
            var button = ModernDarkKit.Field<Button>(view, "_returnButton");
            var hint   = ModernDarkKit.Field<TMP_Text>(view, "_hintLabel");
            kit.Place(save, PAD, 648f, 360f, 40f);
            kit.Place(hint, PAD, 696f, 560f, 32f);
            kit.Place(button, WIDTH - PAD - 280f, 640f, 280f, 72f);
            ModernDarkKit.StyleButton(button, true);
            ModernDarkKit.StyleText(hint, 20f, UiModernDark.TextMuted);
        }

        private static void DisableEffects(ResultScreenView view)
        {
            var accents = ModernDarkKit.Field<Graphic[]>(view, "_accents");
            if (accents != null)
            {
                for (int i = 0; i < accents.Length; ++i)
                {
                    if ((accents[i] != null) && !(accents[i] is TMP_Text))
                    {
                        accents[i].enabled = false;
                    }
                }
            }

            var glow = ModernDarkKit.Field<Graphic>(view, "_glow");
            if (glow != null)
            {
                glow.enabled = false;
            }
        }

        private static void Align(TMP_Text text, TextAlignmentOptions alignment)
        {
            if (text != null)
            {
                text.alignment = alignment;
            }
        }
    }
}
