// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary> 캐릭터 상세 창을 공통 어두운 스타일로 한 번 다시 배치한다. 값 글자는 CharDetailView 직렬화 참조 그대로 채워지고 이름표 글자만 장식으로 더한다 </summary>
    public static class CharDetailModernLayout
    {
        private const float WIDTH    = 1500f;
        private const float HEIGHT   = 820f;
        private const float PANEL_Y  = 84f;
        private const float PANEL_H  = 640f;

        private static readonly string[] STAT_FIELDS =
        {
            "_attackLabel", "_healthLabel", "_defenseLabel", "_critLabel",
            "_energyLabel", "_speedLabel", "_lifeStealLabel", "_combatPowerLabel",
        };

        private static readonly string[] STAT_NAMES =
        {
            "공격력", "최대 체력", "방어력", "치명타 확률",
            "최대 기력", "이동 속도", "생명력 흡수", "전투력",
        };

        private static readonly string[] SLOT_NAMES = { "무기", "머리", "몸통", "손", "발", "정기" };

        public static void Apply(CharDetailView view)
        {
            var window = view.transform as RectTransform;
            TMP_Text name = ModernDarkKit.Field<TMP_Text>(view, "_nameLabel");
            if (!ModernDarkKit.TryBegin(view, window, name, out ModernDarkKit kit))
            {
                return;
            }

            foreach (EquipmentGrowthSurface surface in window.GetComponentsInChildren<EquipmentGrowthSurface>(true))
            {
                surface.enabled = false;
            }

            kit.Frame(new Vector2(WIDTH, HEIGHT), "캐릭터 정보");

            const float leftX = 32f, leftW = 380f;
            const float midX = 432f, midW = 420f;
            const float rightX = 872f, rightW = 596f;

            kit.Inset("LeftPanel", leftX, PANEL_Y, leftW, PANEL_H);
            kit.Inset("MidPanel", midX, PANEL_Y, midW, PANEL_H);
            kit.Inset("StatPanel", rightX, PANEL_Y, rightW, 290f);
            kit.Inset("EquipPanel", rightX, PANEL_Y + 310f, rightW, 330f);

            Button close = ModernDarkKit.Field<Button>(view, "_closeButton");
            kit.Place(close, WIDTH - 72f, 8f, 44f, 44f);
            kit.StyleClose(close);

            LayoutIdentity(view, kit, leftX, leftW);
            LayoutPreview(view, kit, midX, midW);
            LayoutStats(view, kit, rightX, rightW);
            LayoutEquipment(view, kit, rightX, rightW);

            kit.HideLegacy();
            KeepPreviewVisible(view);
        }

        // HideLegacy가 렌더 뷰포트 가지를 꺼 버리면 3D 칸이 비므로 다시 켠다
        private static void KeepPreviewVisible(CharDetailView view)
        {
            RawImage render = view.GetComponentInChildren<RawImage>(true);
            for (Transform t = (render != null) ? render.transform : null; (t != null) && (t != view.transform); t = t.parent)
            {
                t.gameObject.SetActive(true);
            }

            if (render != null)
            {
                render.enabled = true;
            }
        }

        private static void LayoutIdentity(CharDetailView view, ModernDarkKit kit, float x, float w)
        {
            TMP_Text name  = ModernDarkKit.Field<TMP_Text>(view, "_nameLabel");
            TMP_Text klass = ModernDarkKit.Field<TMP_Text>(view, "_classLabel");
            kit.Place(name, x + 24f, PANEL_Y + 24f, w - 48f, 48f);
            kit.Place(klass, x + 24f, PANEL_Y + 78f, w - 48f, 32f);
            Text(name, 36f, UiModernDark.TextGold, TextAlignmentOptions.Left);
            Text(klass, 24f, UiModernDark.TextMain, TextAlignmentOptions.Left);

            kit.Line("IdentityLine", x + 24f, PANEL_Y + 126f, w - 48f);

            TMP_Text talisman = ModernDarkKit.Field<TMP_Text>(view, "_talismanLabel");
            kit.Label("TalismanCaption", "부적", 20f, UiModernDark.TextGold, TextAlignmentOptions.Left, true, x + 24f, PANEL_Y + 146f, 120f, 30f);
            kit.Place(talisman, x + 24f, PANEL_Y + 180f, w - 48f, 32f);
            Text(talisman, 20f, UiModernDark.TextMuted, TextAlignmentOptions.Left);

            kit.Line("TrainingLine", x + 24f, PANEL_Y + 232f, w - 48f);
            kit.Label("TrainingCaption", "수련", 20f, UiModernDark.TextGold, TextAlignmentOptions.Left, true, x + 24f, PANEL_Y + 252f, 120f, 30f);

            var training = ModernDarkKit.Field<TMP_Text[]>(view, "_trainingLabels");
            int count = (training != null) ? training.Length : 0;
            for (int i = 0; i < count; ++i)
            {
                kit.Place(training[i], x + 24f, PANEL_Y + 292f + (i * 44f), w - 48f, 38f);
                Text(training[i], 22f, UiModernDark.TextMain, TextAlignmentOptions.Left);
            }
        }

        private static void LayoutPreview(CharDetailView view, ModernDarkKit kit, float x, float w)
        {
            GameObject stage = ModernDarkKit.Field<GameObject>(view, "_previewStage");
            if ((stage != null) && (stage.transform is RectTransform))
            {
                kit.Place(stage, x + 16f, PANEL_Y + 16f, w - 32f, PANEL_H - 32f);
            }

            // 프리팹은 _previewStage를 비워 두고 렌더 텍스처를 담은 RawImage만 뷰포트 안에 둔다. 가려지지 않게 중간 칸에 놓는다
            RawImage render = view.GetComponentInChildren<RawImage>(true);
            if ((render == null) || !(render.transform.parent is RectTransform viewport))
            {
                return;
            }

            if (viewport == view.transform)
            {
                kit.Place(render, x + 16f, PANEL_Y + 16f, w - 32f, PANEL_H - 32f);
                return;
            }

            kit.Place(viewport, x + 16f, PANEL_Y + 16f, w - 32f, PANEL_H - 32f);
            render.rectTransform.anchorMin        = Vector2.zero;
            render.rectTransform.anchorMax        = Vector2.one;
            render.rectTransform.offsetMin        = Vector2.zero;
            render.rectTransform.offsetMax        = Vector2.zero;
            render.raycastTarget                  = false;
        }

        private static void LayoutStats(CharDetailView view, ModernDarkKit kit, float x, float w)
        {
            kit.Label("StatCaption", "능력치", 22f, UiModernDark.TextGold, TextAlignmentOptions.Left, true, x + 24f, PANEL_Y + 14f, 200f, 34f);
            kit.Line("StatLine", x + 24f, PANEL_Y + 54f, w - 48f);

            float cellW = (w - 72f) * 0.5f;
            for (int i = 0; i < STAT_FIELDS.Length; ++i)
            {
                float cx = x + 24f + ((i % 2) * (cellW + 24f));
                float cy = PANEL_Y + 70f + ((i / 2) * 54f);
                TMP_Text value = ModernDarkKit.Field<TMP_Text>(view, STAT_FIELDS[i]);
                if (i == STAT_FIELDS.Length - 1)
                {
                    // 전투력 글자는 "전투력 18,420"처럼 이름을 이미 품고 있어 이름표를 따로 달지 않고 칸 전체를 쓴다
                    kit.Place(value, cx, cy, cellW, 40f);
                    Text(value, 24f, UiModernDark.Cyan, TextAlignmentOptions.Right);
                    if (value != null)
                    {
                        value.overflowMode = TextOverflowModes.Overflow;
                    }

                    continue;
                }

                kit.Label("StatName" + i, STAT_NAMES[i], 20f, UiModernDark.TextMuted, TextAlignmentOptions.Left, false, cx, cy, cellW * 0.5f, 40f);
                kit.Place(value, cx + (cellW * 0.45f), cy, cellW * 0.55f, 40f);
                Text(value, 24f, UiModernDark.TextMain, TextAlignmentOptions.Right);
            }
        }

        private static void LayoutEquipment(CharDetailView view, ModernDarkKit kit, float x, float w)
        {
            float top = PANEL_Y + 310f;
            kit.Label("EquipCaption", "장비", 22f, UiModernDark.TextGold, TextAlignmentOptions.Left, true, x + 24f, top + 14f, 200f, 34f);
            kit.Line("EquipLine", x + 24f, top + 54f, w - 48f);

            var names    = ModernDarkKit.Field<TMP_Text[]>(view, "_slotNameLabels");
            var enhances = ModernDarkKit.Field<TMP_Text[]>(view, "_slotEnhanceLabels");
            for (int i = 0; i < SLOT_NAMES.Length; ++i)
            {
                float y = top + 66f + (i * 42f);
                kit.Label("SlotName" + i, SLOT_NAMES[i], 19f, UiModernDark.TextMuted, TextAlignmentOptions.Left, false, x + 24f, y, 90f, 36f);

                TMP_Text itemName = ((names != null) && (i < names.Length)) ? names[i] : null;
                kit.Place(itemName, x + 120f, y, w - 120f - 150f, 36f);
                Text(itemName, 21f, UiModernDark.TextMain, TextAlignmentOptions.Left);

                TMP_Text enhance = ((enhances != null) && (i < enhances.Length)) ? enhances[i] : null;
                kit.Place(enhance, x + w - 24f - 110f, y, 110f, 36f);
                Text(enhance, 21f, UiModernDark.TextGold, TextAlignmentOptions.Right);
            }
        }

        private static void Text(TMP_Text text, float size, Color color, TextAlignmentOptions align)
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
    }
}
