#if UNITY_EDITOR
// System
using System;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI.Editor
{
    /// <summary> 친구, 길드 창 빌더가 함께 쓰는 공통 창 스킨, 글꼴, 입력 칸, 목록 줄, 확인 상자 만들기 </summary>
    public static class SocialWindowKit
    {
        // 배치 JSON이 없는 창이라 코드로 계층을 만든다. 좌표는 창 왼쪽 위 기준이고 y는 아래로 갈수록 커진다

        /****************************************
        *                Fields
        ****************************************/

        public const float MARGIN             = 24f;
        public const float TITLE_HEIGHT       = 72f;
        public const float BUTTON_HEIGHT      = 44f;
        public const float ROW_HEIGHT         = 44f;
        public const float ROW_GAP            = 48f;
        public const float ROW_BUTTON_W       = 84f;
        public const float ROW_BUTTON_H       = 34f;
        public const int   ROW_BUTTONS        = 3;
        public const float ROW_BUTTON_SPACING = 8f;

        private const string SKIN_FOLDER  = "Assets/01.Scripts/05.Art/UI/GameStyle/Textures/";
        private const string FONT_REGULAR = "Assets/01.Scripts/06.UI/Hud/Fonts/HudQuestKorean-Regular.asset";
        private const string FONT_BOLD    = "Assets/01.Scripts/06.UI/Hud/Fonts/HudQuestKorean-Bold.asset";
        private const string UI_LAYER     = "UI";
        private const float  CLOSE_SIZE   = 28f;
        private const float  DOT_SIZE     = 12f;
        private const float  TEXT_PADDING = 12f;
        private const float  CONFIRM_W    = 520f;
        private const float  CONFIRM_H    = 200f;

        public static readonly Color G_Text  = EquipmentGrowthStyle.Text;
        public static readonly Color G_Muted = EquipmentGrowthStyle.Muted;

        private static readonly Color G_Panel = new Color(1f, 1f, 1f, 0.9f);
        private static readonly Color G_Line  = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color G_Shade = new Color(0f, 0f, 0f, 0.5f);
        private static readonly Color G_Input = new Color(0.07f, 0.09f, 0.12f, 1f);
        private static readonly Color G_Hint  = new Color(0.55f, 0.6f, 0.66f, 1f);

        private static TMP_FontAsset G_Regular;
        private static TMP_FontAsset G_Bold;

        /****************************************
        *                Methods
        ****************************************/

        public static void LoadFonts()
        {
            G_Regular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_REGULAR);
            G_Bold    = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_BOLD);
            if ((G_Regular == null) || (G_Bold == null))
            {
                throw new InvalidOperationException("소셜 창 글꼴을 찾을 수 없습니다");
            }
        }

        /// <summary> 창 바탕, 끌기 제목줄, 제목, 오른쪽 수 글자, 닫기 버튼을 만든다 </summary>
        public static Button Frame(RectTransform root, float width, float height, string title, out TMP_Text titleLabel,
            out TMP_Text countLabel)
        {
            root.anchorMin        = new Vector2(0.5f, 0.5f);
            root.anchorMax        = new Vector2(0.5f, 0.5f);
            root.pivot            = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = Vector2.zero;
            root.sizeDelta        = new Vector2(width, height);

            Image frame = root.gameObject.AddComponent<Image>();
            EquipmentGrowthStyle.Surface(frame, EquipmentGrowthStyle.Canvas);
            frame.raycastTarget = true;

            Image titleBar = Panel(root, "제목줄", 0f, 0f, width, TITLE_HEIGHT, EquipmentGrowthStyle.Panel);
            titleBar.raycastTarget = true;
            var drag = titleBar.gameObject.AddComponent<UiWindowDragHandle>();
            SetReference(drag, "_window", root);
            titleLabel = Label(root, "제목", title, MARGIN, 18f, 420f, 36f, 28f, G_Text, true, TextAlignmentOptions.MidlineLeft);
            countLabel = Label(root, "수", string.Empty, width - MARGIN - CLOSE_SIZE - 176f, 22f, 160f, 30f, 20f,
                EquipmentGrowthStyle.Gold, true, TextAlignmentOptions.MidlineRight);
            Button close = MakeButton(root, "닫기 버튼", width - MARGIN - CLOSE_SIZE, 22f, CLOSE_SIZE, CLOSE_SIZE, "T_UiIcon_Close",
                false);
            Line(root, "제목 구분선", MARGIN, TITLE_HEIGHT, width - (MARGIN * 2f));
            return close;
        }

        /// <summary> 글자가 들어간 버튼, 주 버튼이면 민트 강조 </summary>
        public static Button TextButton(RectTransform parent, string name, string text, float x, float y, float width, float height,
            bool primary)
        {
            Button button = MakeButton(parent, name, x, y, width, height, primary ? "T_UiButton_Primary" : "T_UiButton_Secondary", true);
            Label((RectTransform)button.transform, "글자", text, 4f, 0f, width - 8f, height, 16f,
                primary ? EquipmentGrowthStyle.Canvas : G_Text, true, TextAlignmentOptions.Center);
            if (primary)
            {
                EquipmentGrowthStyle.Primary(button);
            }

            return button;
        }

        /// <summary> 탭 버튼 하나와 선택 표시 막대 </summary>
        public static Button Tab(RectTransform parent, string label, float x, float y, float width, float height,
            out TMP_Text text, out GameObject selected)
        {
            Button button = MakeButton(parent, "탭 " + label, x, y, width, height, "T_UiPanel_Inset", true);
            EquipmentGrowthStyle.Surface(button.image, EquipmentGrowthStyle.Panel);
            var rect = (RectTransform)button.transform;
            text = Label(rect, "글자", label, 0f, 0f, width, height - 4f, 17f, G_Text, true, TextAlignmentOptions.Center);
            Image mark = Panel(rect, "선택 표시", 0f, height - 4f, width, 4f, EquipmentGrowthStyle.Mint);
            mark.gameObject.SetActive(false);
            selected = mark.gameObject;
            return button;
        }

        /// <summary> 한 줄 입력 칸. 이름 입력이라 글자 수 상한을 둔다 </summary>
        public static TMP_InputField Input(RectTransform parent, string name, string placeholder, float x, float y, float width,
            float height, int characterLimit)
        {
            Image image = Panel(parent, name, x, y, width, height, G_Input);
            EquipmentGrowthStyle.Surface(image, G_Input);
            image.raycastTarget = true;
            var self = (RectTransform)image.transform;
            RectTransform area = Rect(self, "글자 영역", TEXT_PADDING, 0f, width - (TEXT_PADDING * 2f), height);
            area.gameObject.AddComponent<RectMask2D>();
            TMP_Text hint = Label(area, "안내", placeholder, 0f, 0f, width - (TEXT_PADDING * 2f), height, 16f, G_Hint, false,
                TextAlignmentOptions.MidlineLeft);
            TMP_Text text = Label(area, "글자", string.Empty, 0f, 0f, width - (TEXT_PADDING * 2f), height, 16f, G_Text, false,
                TextAlignmentOptions.MidlineLeft);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            hint.textWrappingMode = TextWrappingModes.NoWrap;
            TMP_InputField field = image.gameObject.AddComponent<TMP_InputField>();
            field.targetGraphic  = image;
            field.textViewport   = area;
            field.textComponent  = text;
            field.placeholder    = hint;
            field.fontAsset      = G_Regular;
            field.pointSize      = 16f;
            field.characterLimit = characterLimit;
            field.lineType       = TMP_InputField.LineType.SingleLine;
            return field;
        }

        /// <summary> 목록 한 줄. 접속 점, 이름, 보조 글자 둘, 오른쪽 끝에 버튼 3칸 </summary>
        public static SocialRowView Row(RectTransform parent, int index, float x, float y, float width)
        {
            Image background = Panel(parent, "줄 " + (index + 1), x, y, width, ROW_HEIGHT, EquipmentGrowthStyle.Panel);
            EquipmentGrowthStyle.Surface(background, EquipmentGrowthStyle.Panel);
            var rect = (RectTransform)background.transform;
            Image dot = Panel(rect, "접속 점", 12f, (ROW_HEIGHT - DOT_SIZE) * 0.5f, DOT_SIZE, DOT_SIZE, Color.white);
            dot.type = Image.Type.Simple;
            TMP_Text nameLabel = RowLabel(rect, "이름", 32f, 168f, 17f, G_Text, true);
            TMP_Text detail    = RowLabel(rect, "직업", 204f, 132f, 15f, G_Muted, false);
            float buttonsWidth = (ROW_BUTTONS * ROW_BUTTON_W) + ((ROW_BUTTONS - 1) * ROW_BUTTON_SPACING);
            float buttonsLeft  = width - ROW_BUTTON_SPACING - buttonsWidth;
            TMP_Text info      = RowLabel(rect, "상태", 340f, buttonsLeft - 348f, 15f, EquipmentGrowthStyle.Mint, false);

            // 숨긴 버튼은 배치에서 빠지므로 남은 버튼이 오른쪽 끝에 붙는다
            RectTransform group = Rect(rect, "버튼", buttonsLeft, 0f, buttonsWidth, ROW_HEIGHT);
            HorizontalLayoutGroup layout = group.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment         = TextAnchor.MiddleRight;
            layout.spacing                = ROW_BUTTON_SPACING;
            layout.childControlWidth      = false;
            layout.childControlHeight     = false;
            layout.childForceExpandWidth  = false;
            layout.childForceExpandHeight = false;

            var buttons = new UnityEngine.Object[ROW_BUTTONS];
            var labels  = new UnityEngine.Object[ROW_BUTTONS];
            for (int i = 0; i < ROW_BUTTONS; ++i)
            {
                Button button = MakeButton(group, "버튼 " + (i + 1), 0f, 0f, ROW_BUTTON_W, ROW_BUTTON_H, "T_UiButton_Secondary", true);
                labels[i] = Label((RectTransform)button.transform, "글자", string.Empty, 2f, 0f, ROW_BUTTON_W - 4f, ROW_BUTTON_H, 14f,
                    G_Text, true, TextAlignmentOptions.Center);
                buttons[i] = button;
            }

            SocialRowView row = background.gameObject.AddComponent<SocialRowView>();
            SetReference(row, "_presenceDot", dot);
            SetReference(row, "_nameLabel", nameLabel);
            SetReference(row, "_detailLabel", detail);
            SetReference(row, "_infoLabel", info);
            SetArray(row, "_buttons", buttons);
            SetArray(row, "_buttonLabels", labels);
            background.gameObject.SetActive(false);
            return row;
        }

        /// <summary> 창 전체를 덮는 확인 상자. 글자와 확인, 취소 버튼 </summary>
        public static GameObject Confirm(RectTransform root, float width, float height, out TMP_Text text, out Button accept,
            out Button cancel)
        {
            Image shade = Panel(root, "확인", 0f, 0f, width, height, G_Shade);
            shade.raycastTarget = true;
            var rect = (RectTransform)shade.transform;
            float left = (width - CONFIRM_W) * 0.5f;
            float top  = (height - CONFIRM_H) * 0.5f;
            EquipmentGrowthStyle.Card(rect, "확인 패널", left, top, CONFIRM_W, CONFIRM_H, EquipmentGrowthStyle.Panel);
            text = Label(rect, "확인 글자", string.Empty, left + 24f, top + 24f, CONFIRM_W - 48f, 80f, 18f, G_Text, false,
                TextAlignmentOptions.Center);
            accept = TextButton(rect, "확인 버튼", "확인", left + 40f, top + 124f, 200f, BUTTON_HEIGHT, true);
            cancel = TextButton(rect, "취소 버튼", "취소", left + 280f, top + 124f, 200f, BUTTON_HEIGHT, false);
            shade.gameObject.SetActive(false);
            return shade.gameObject;
        }

        // 줄 글자는 한 줄로 두고 넘치면 말줄임으로 자른다
        private static TMP_Text RowLabel(RectTransform row, string name, float x, float width, float size, Color color, bool bold)
        {
            TMP_Text label = Label(row, name, string.Empty, x, 0f, width, ROW_HEIGHT, size, color, bold, TextAlignmentOptions.MidlineLeft);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode     = TextOverflowModes.Ellipsis;
            return label;
        }

        public static Button MakeButton(RectTransform parent, string name, float x, float y, float width, float height, string skin,
            bool sliced)
        {
            Image image = Panel(parent, name, x, y, width, height, G_Panel);
            Skin(image, skin, G_Panel);
            if (!sliced)
            {
                image.type = Image.Type.Simple;
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

        public static void Line(RectTransform parent, string name, float x, float y, float width)
        {
            Image line = Panel(parent, name, x, y, width, 1f, G_Line);
            Skin(line, "T_UiLine_Fade", G_Line);
            line.type = Image.Type.Simple;
        }

        public static TMP_Text Label(RectTransform parent, string name, string value, float x, float y, float width, float height,
            float size, Color color, bool bold, TextAlignmentOptions alignment)
        {
            TextMeshProUGUI text = Rect(parent, name, x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
            text.font             = bold ? G_Bold : G_Regular;
            text.text             = value;
            text.fontSize         = size;
            text.color            = color;
            text.alignment        = alignment;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget    = false;
            return text;
        }

        public static Image Panel(RectTransform parent, string name, float x, float y, float width, float height, Color color)
        {
            Image image = Rect(parent, name, x, y, width, height).gameObject.AddComponent<Image>();
            image.color         = color;
            image.raycastTarget = false;
            return image;
        }

        public static void Skin(Image image, string name, Color tint)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SKIN_FOLDER + name + ".png");
            if (sprite == null)
            {
                throw new InvalidOperationException("소셜 창 스킨을 찾을 수 없습니다: " + name);
            }

            image.sprite = sprite;
            image.type   = Image.Type.Sliced;
            image.color  = tint;
        }

        public static RectTransform Rect(RectTransform parent, string name, float x, float y, float width, float height)
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

        public static void SetReference(UnityEngine.Object target, string name, UnityEngine.Object value)
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

        public static void SetArray(UnityEngine.Object target, string name, UnityEngine.Object[] values)
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

        /// <summary> 미리보기 장면에서 루트를 만들고 build로 채운 뒤 꺼진 채 프리팹으로 저장한다 </summary>
        public static void SavePrefab(string rootName, string path, Action<RectTransform> build)
        {
            LoadFonts();
            UnityEngine.SceneManagement.Scene preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            GameObject root = new GameObject(rootName, typeof(RectTransform));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
            try
            {
                root.SetActive(false);
                root.layer = LayerMask.NameToLayer(UI_LAYER);
                build((RectTransform)root.transform);

                // 창은 단축키와 메뉴가 켠다. 기존 창 프리팹과 같이 꺼진 채로 저장한다
                root.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
            }
        }
    }
}
#endif
