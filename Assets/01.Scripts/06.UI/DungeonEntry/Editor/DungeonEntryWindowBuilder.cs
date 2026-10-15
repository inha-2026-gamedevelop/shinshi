#if UNITY_EDITOR
// System
using System;
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI.Editor
{
    /// <summary> 카오스 던전 / 보스 맵 입장 창 프리팹을 공통 창 스킨과 한글 글꼴로 만든다 </summary>
    // 배치 JSON이 없는 창이라 코드로 계층을 만든다. 다시 실행하면 프리팹을 통째로 새로 쓰므로 프리팹을 손으로 고친 내용은 사라진다.
    // 씬에 넣는 일은 RiftGuideNpcBuilder가 한다
    public static class DungeonEntryWindowBuilder
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string PREFAB_PATH = "Assets/02.Prefab/UI/Windows/PF_DungeonEntryWindow.prefab";

        private const string SKIN_FOLDER = "Assets/01.Scripts/05.Art/UI/GameStyle/Textures/";
        private const string UI_LAYER    = "UI";

        private const float WINDOW_WIDTH  = 960f;
        private const float WINDOW_HEIGHT = 640f;
        private const float FRAME_EDGE    = 2f;
        private const float TITLE_HEIGHT  = 72f;
        private const float MARGIN        = 24f;
        private const float CLOSE_SIZE    = 28f;

        private const float LIST_X      = 24f;
        private const float LIST_Y      = 92f;
        private const float LIST_WIDTH  = 296f;
        private const float LIST_HEIGHT = 452f;
        private const float ROW_X       = 12f;
        private const float ROW_TOP     = 12f;
        private const float ROW_WIDTH   = 272f;
        private const float ROW_HEIGHT  = 50f;
        private const float ROW_GAP     = 4f;
        private const int   ROW_COUNT   = 8;
        private const float MARK_WIDTH  = 4f;

        private const float DETAIL_X      = 340f;
        private const float DETAIL_Y      = 92f;
        private const float DETAIL_WIDTH  = 596f;
        private const float DETAIL_HEIGHT = 452f;
        private const float DETAIL_INSET  = 24f;

        private const float BOTTOM_Y      = 556f;
        private const float BUTTON_WIDTH  = 220f;
        private const float BUTTON_HEIGHT = 52f;

        private static readonly Color G_Canvas = Hex("#10151D");
        private static readonly Color G_Panel  = Hex("#202936");
        private static readonly Color G_Mint   = Hex("#76E6BD");
        private static readonly Color G_Gold   = Hex("#E8BC68");
        private static readonly Color G_Text   = Hex("#EAF2F8");
        private static readonly Color G_Muted  = Hex("#A6BBCB");
        private static readonly Color G_Line   = new Color(1f, 1f, 1f, 0.35f);

        private static TMP_FontAsset G_Regular;
        private static TMP_FontAsset G_Bold;

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem("Shinshi/UI/입장 창 만들기")]
        public static string Build()
        {
            G_Regular = DungeonEntryFonts.Ensure("Regular");
            G_Bold    = DungeonEntryFonts.Ensure("Bold");

            GameObject root = new GameObject("PF_DungeonEntryWindow", typeof(RectTransform));
            try
            {
                // 직렬화 참조를 모두 연결한 뒤에 켜야 Awake가 입력을 등록한다. 창은 NPC와 단축키가 켜므로 꺼진 채로 저장한다
                root.SetActive(false);
                root.layer = LayerMask.NameToLayer(UI_LAYER);
                BuildWindow((RectTransform)root.transform);

                DungeonEntryFonts.Preload(G_Regular, G_Bold, CollectTexts(root));
                PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            string result = "[UI] 입장 창 프리팹을 만들었다: " + PREFAB_PATH;
            Debug.Log(result);
            return result;
        }

        private static IEnumerable<string> CollectTexts(GameObject root)
        {
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                yield return text.text;
            }
        }

        private static void BuildWindow(RectTransform root)
        {
            root.anchorMin        = new Vector2(0.5f, 0.5f);
            root.anchorMax        = new Vector2(0.5f, 0.5f);
            root.pivot            = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = Vector2.zero;
            root.sizeDelta        = new Vector2(WINDOW_WIDTH, WINDOW_HEIGHT);

            Image frame = root.gameObject.AddComponent<Image>();
            Skin(frame, "T_UiWindow_Frame", Color.white);
            frame.raycastTarget = true;
            var view = root.gameObject.AddComponent<DungeonEntryView>();

            Image titleBar = Panel(root, "제목줄", FRAME_EDGE, FRAME_EDGE, WINDOW_WIDTH - (FRAME_EDGE * 2f), TITLE_HEIGHT - FRAME_EDGE,
                Color.white);
            Skin(titleBar, "T_UiWindow_TitleBar", Color.white);
            titleBar.raycastTarget = true;
            var drag = titleBar.gameObject.AddComponent<UiWindowDragHandle>();
            SetReference(drag, "_window", root);
            Label(root, "제목", "균열 입장", MARGIN, 18f, 420f, 36f, 28f, G_Text, true, TextAlignmentOptions.MidlineLeft);
            Label(root, "분류", "카오스 던전  /  보스 맵", 600f, 24f, 280f, 24f, 14f, G_Muted, false, TextAlignmentOptions.MidlineRight);
            Button close = MakeButton(root, "닫기 버튼", WINDOW_WIDTH - MARGIN - CLOSE_SIZE, 22f, CLOSE_SIZE, CLOSE_SIZE, "T_UiIcon_Close",
                false);
            Line(root, "제목 구분선", MARGIN, TITLE_HEIGHT, WINDOW_WIDTH - (MARGIN * 2f));

            // 목록
            Card(root, "목록 패널", LIST_X, LIST_Y, LIST_WIDTH, LIST_HEIGHT);
            var rowButtons    = new UnityEngine.Object[ROW_COUNT];
            var rowTitles     = new UnityEngine.Object[ROW_COUNT];
            var rowCategories = new UnityEngine.Object[ROW_COUNT];
            var rowMarks      = new UnityEngine.Object[ROW_COUNT];
            for (int i = 0; i < ROW_COUNT; ++i)
            {
                float y = LIST_Y + ROW_TOP + ((ROW_HEIGHT + ROW_GAP) * i);
                Button row = MakeButton(root, "목록 줄 " + i, LIST_X + ROW_X, y, ROW_WIDTH, ROW_HEIGHT, "T_UiPanel_Inset", true);
                RectTransform rect = (RectTransform)row.transform;
                rowButtons[i]    = row;
                rowTitles[i]     = Label(rect, "이름", "항목", 16f, 5f, ROW_WIDTH - 32f, 26f, 19f, G_Text, true,
                    TextAlignmentOptions.MidlineLeft);
                rowCategories[i] = Label(rect, "분류", "분류", 16f, 29f, ROW_WIDTH - 32f, 18f, 12f, G_Muted, false,
                    TextAlignmentOptions.MidlineLeft);
                Image mark = Panel(rect, "선택 표시", 0f, 0f, MARK_WIDTH, ROW_HEIGHT, G_Mint);
                mark.gameObject.SetActive(false);
                rowMarks[i] = mark.gameObject;
            }

            RectTransform emptyRoot = Rect(root, "빈 목록 안내", LIST_X, LIST_Y, LIST_WIDTH, LIST_HEIGHT);
            Label(emptyRoot, "글자", "입장할 수 있는 곳이 없습니다", 0f, 0f, LIST_WIDTH, LIST_HEIGHT, 16f, G_Muted, false,
                TextAlignmentOptions.Center);
            emptyRoot.gameObject.SetActive(false);

            // 선택한 항목
            Card(root, "상세 패널", DETAIL_X, DETAIL_Y, DETAIL_WIDTH, DETAIL_HEIGHT);
            RectTransform detail = Rect(root, "상세", DETAIL_X, DETAIL_Y, DETAIL_WIDTH, DETAIL_HEIGHT);
            float inner = DETAIL_WIDTH - (DETAIL_INSET * 2f);
            TMP_Text category = Label(detail, "분류", "카오스 던전", DETAIL_INSET, 22f, inner, 22f, 15f, G_Mint, true,
                TextAlignmentOptions.MidlineLeft);
            TMP_Text title = Label(detail, "이름", "숲", DETAIL_INSET, 46f, inner, 48f, 34f, G_Text, true,
                TextAlignmentOptions.MidlineLeft);
            Line(detail, "상세 구분선", DETAIL_INSET, 104f, inner);
            TMP_Text summary = Label(detail, "설명", string.Empty, DETAIL_INSET, 120f, inner, 140f, 18f, G_Text, false,
                TextAlignmentOptions.TopLeft);
            summary.lineSpacing = 8f;
            Label(detail, "선행 조건 제목", "선행 조건", DETAIL_INSET, 276f, inner, 22f, 14f, G_Muted, true,
                TextAlignmentOptions.MidlineLeft);
            TMP_Text requirement = Label(detail, "선행 조건", string.Empty, DETAIL_INSET, 300f, inner, 52f, 18f, G_Gold, false,
                TextAlignmentOptions.TopLeft);
            Label(detail, "입장 비용 제목", "입장 비용", DETAIL_INSET, 364f, inner, 22f, 14f, G_Muted, true,
                TextAlignmentOptions.MidlineLeft);
            TMP_Text cost = Label(detail, "입장 비용", string.Empty, DETAIL_INSET, 388f, inner, 28f, 18f, G_Text, false,
                TextAlignmentOptions.MidlineLeft);

            // 하단
            Label(root, "닫기 안내", "Esc 키나 X로 닫을 수 있습니다", LIST_X, BOTTOM_Y + 4f, LIST_WIDTH, 44f, 13f, G_Muted, false,
                TextAlignmentOptions.MidlineLeft);
            TMP_Text notice = Label(root, "안내", string.Empty, DETAIL_X, BOTTOM_Y + 4f, DETAIL_WIDTH - BUTTON_WIDTH - 20f, 44f, 15f,
                G_Gold, false, TextAlignmentOptions.MidlineLeft);
            Button enter = MakeButton(root, "입장 버튼", WINDOW_WIDTH - MARGIN - BUTTON_WIDTH, BOTTOM_Y, BUTTON_WIDTH, BUTTON_HEIGHT, null,
                false);
            enter.image.color = Color.white;
            ColorBlock colors = enter.colors;
            colors.normalColor      = G_Mint;
            colors.highlightedColor = Color.Lerp(G_Mint, Color.white, 0.3f);
            colors.selectedColor    = colors.highlightedColor;
            colors.pressedColor     = Color.Lerp(G_Mint, G_Canvas, 0.2f);
            colors.disabledColor    = Hex("#536776");
            colors.fadeDuration     = 0.15f;
            enter.colors            = colors;
            TMP_Text enterLabel = Label((RectTransform)enter.transform, "글자", "입장", 0f, 0f, BUTTON_WIDTH, BUTTON_HEIGHT, 22f, G_Canvas,
                true, TextAlignmentOptions.Center);

            SetReference(view, "_closeButton", close);
            SetArray(view, "_rowButtons", rowButtons);
            SetArray(view, "_rowTitles", rowTitles);
            SetArray(view, "_rowCategories", rowCategories);
            SetArray(view, "_rowMarks", rowMarks);
            SetReference(view, "_emptyRoot", emptyRoot.gameObject);
            SetReference(view, "_detailRoot", detail.gameObject);
            SetReference(view, "_detailCategory", category);
            SetReference(view, "_detailTitle", title);
            SetReference(view, "_detailSummary", summary);
            SetReference(view, "_detailRequirement", requirement);
            SetReference(view, "_detailCost", cost);
            SetReference(view, "_enterButton", enter);
            SetReference(view, "_enterLabel", enterLabel);
            SetReference(view, "_noticeLabel", notice);
        }

        private static Button MakeButton(RectTransform parent, string name, float x, float y, float width, float height, string skin,
            bool sliced)
        {
            Image image = Panel(parent, name, x, y, width, height, Color.white);
            if (skin != null)
            {
                Skin(image, skin, Color.white);
                if (!sliced)
                {
                    image.type = Image.Type.Simple;
                }
            }

            image.raycastTarget = true;
            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor      = Color.white;
            colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
            colors.selectedColor    = colors.highlightedColor;
            colors.disabledColor    = new Color(0.5f, 0.5f, 0.5f, 0.55f);
            button.colors = colors;
            return button;
        }

        private static void Card(RectTransform parent, string name, float x, float y, float width, float height)
        {
            Image card = Panel(parent, name, x, y, width, height, Color.white);
            Skin(card, "T_UiPanel_Inset", Color.white);
        }

        private static void Line(RectTransform parent, string name, float x, float y, float width)
        {
            Image line = Panel(parent, name, x, y, width, 1f, G_Line);
            Skin(line, "T_UiLine_Fade", G_Line);
            line.type = Image.Type.Simple;
        }

        private static TMP_Text Label(RectTransform parent, string name, string value, float x, float y, float width, float height,
            float size, Color color, bool bold, TextAlignmentOptions alignment)
        {
            TextMeshProUGUI text = Rect(parent, name, x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
            text.font             = bold ? G_Bold : G_Regular;
            text.text             = value;
            text.fontSize         = size;
            text.color            = color;
            text.alignment        = alignment;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode     = TextOverflowModes.Ellipsis;
            text.raycastTarget    = false;
            return text;
        }

        private static Image Panel(RectTransform parent, string name, float x, float y, float width, float height, Color color)
        {
            Image image = Rect(parent, name, x, y, width, height).gameObject.AddComponent<Image>();
            image.color         = color;
            image.raycastTarget = false;
            return image;
        }

        private static void Skin(Image image, string name, Color tint)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SKIN_FOLDER + name + ".png");
            if (sprite == null)
            {
                throw new InvalidOperationException("입장 창 스킨을 찾을 수 없습니다: " + name);
            }

            image.sprite = sprite;
            image.type   = Image.Type.Sliced;
            image.color  = tint;
        }

        // 좌상단 기준 좌표로 놓는다. y는 아래로 갈수록 커진다
        private static RectTransform Rect(RectTransform parent, string name, float x, float y, float width, float height)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.layer = LayerMask.NameToLayer(UI_LAYER);
            RectTransform rect = (RectTransform)gameObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin        = Vector2.up;
            rect.anchorMax        = Vector2.up;
            rect.pivot            = Vector2.up;
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta        = new Vector2(width, height);
            return rect;
        }

        private static void SetReference(UnityEngine.Object target, string name, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(name);
            if (property == null)
            {
                throw new InvalidOperationException("연결할 필드가 없습니다: " + name);
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(UnityEngine.Object target, string name, UnityEngine.Object[] values)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(name);
            if (property == null)
            {
                throw new InvalidOperationException("연결할 필드가 없습니다: " + name);
            }

            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; ++i)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Color Hex(string value)
        {
            ColorUtility.TryParseHtmlString(value, out Color color);
            return color;
        }
    }
}
#endif
