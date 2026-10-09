// System
using System;
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.UI;

namespace Shinshi.Editor
{
    /// <summary> 테스트 배치용 UI 조각을 코드로 만들고 View의 직렬화 필드를 연결하는 도우미, 좌표는 부모 왼쪽 위 기준이며 아래로 갈수록 y가 크다 </summary>
    public static class EconomyUiBuilder
    {
        /****************************************
        *                Fields
        ****************************************/

        public const float FONT_SIZE      = 16f;
        public const float SMALL_FONT     = 13f;
        public const float TITLE_FONT     = 22f;
        public const float ROW_HEIGHT     = 34f;
        public const float BUTTON_HEIGHT  = 34f;
        public const float LABEL_HEIGHT   = 26f;
        public const float MARGIN         = 16f;
        public const float GAP            = 6f;

        private const string KOREAN_FONT_NAME   = "HudQuestKorean-Regular";
        private const string EVENT_SYSTEM_NAME  = "UiSandboxFixture_EventSystem";
        public const int     FIXTURE_SORT_ORDER = 500;
        private const float  REFERENCE_WIDTH    = 1920f;
        private const float  REFERENCE_HEIGHT   = 1080f;
        private const float  GRADE_BAR_WIDTH    = 6f;
        private const float  TEXT_PADDING       = 6f;
        private const float  DROPDOWN_LIST_HEIGHT = 150f;
        private const float  CHECK_INSET        = 3f;

        private static readonly Vector2 TOP_LEFT = new Vector2(0f, 1f);
        private static readonly Vector2 CENTER   = new Vector2(0.5f, 0.5f);

        private static readonly Color WINDOW_COLOR = new Color(0.10f, 0.12f, 0.16f, 0.97f);
        private static readonly Color PANEL_COLOR  = new Color(0.16f, 0.19f, 0.25f, 1f);
        private static readonly Color BUTTON_COLOR = new Color(0.25f, 0.32f, 0.45f, 1f);
        private static readonly Color INPUT_COLOR  = new Color(0.07f, 0.08f, 0.11f, 1f);
        private static readonly Color ROW_COLOR    = new Color(0.20f, 0.23f, 0.30f, 1f);
        private static readonly Color CHECK_COLOR  = new Color(0.30f, 0.70f, 0.45f, 0.55f);
        private static readonly Color SELECT_COLOR = new Color(1f, 0.85f, 0.25f, 0.30f);
        private static readonly Color HINT_COLOR   = new Color(1f, 1f, 1f, 0.40f);
        private static readonly Color MARK_COLOR   = new Color(0.95f, 0.75f, 0.20f, 1f);

        private static readonly Color[] GRADE_COLORS =
        {
            new Color(0.70f, 0.70f, 0.70f, 1f),
            new Color(0.30f, 0.60f, 1.00f, 1f),
            new Color(0.70f, 0.35f, 0.95f, 1f),
            new Color(1.00f, 0.60f, 0.15f, 1f)
        };

        private static TMP_FontAsset _font;
        private static bool          _fontSearched;

        /****************************************
        *              Properties
        ****************************************/

        public static Color WindowColor => WINDOW_COLOR;
        public static Color PanelColor  => PANEL_COLOR;
        public static Color MarkColor   => MARK_COLOR;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 꺼진 채로 만든 캔버스 루트, 자식과 View 필드를 모두 연결한 뒤에 켜야 Awake가 연결된 참조를 본다 </summary>
        public static GameObject CreateCanvasRoot(string name)
        {
            GameObject root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.SetActive(false);
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = FIXTURE_SORT_ORDER;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(REFERENCE_WIDTH, REFERENCE_HEIGHT);
            scaler.matchWidthOrHeight  = 0.5f;
            return root;
        }

        /// <summary> 장면에 EventSystem이 없을 때만 시험 루트의 자식으로 하나 만든다. 루트와 함께 사라지므로 주인 없는 전역 오브젝트가 남지 않는다 </summary>
        public static void EnsureEventSystem(Transform fixtureRoot)
        {
            if (HasEventSystem(fixtureRoot.gameObject.scene))
            {
                return;
            }

            GameObject go = new GameObject(EVENT_SYSTEM_NAME, typeof(EventSystem), typeof(StandaloneInputModule));
            go.transform.SetParent(fixtureRoot, false);
        }

        // 미리보기 씬은 Find 계열에 잡히지 않으므로 그 씬의 루트만 직접 훑는다
        private static bool HasEventSystem(Scene scene)
        {
            if (!EditorSceneManager.IsPreviewScene(scene))
            {
                return UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; ++i)
            {
                if (roots[i].GetComponentInChildren<EventSystem>(true) != null)
                {
                    return true;
                }
            }

            return false;
        }

        public static RectTransform CreateRect(Transform parent, string name, float x, float y, float width, float height)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin        = TOP_LEFT;
            rect.anchorMax        = TOP_LEFT;
            rect.pivot            = TOP_LEFT;
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta        = new Vector2(width, height);
            return rect;
        }

        /// <summary> 화면 가운데에 놓는 창 바탕 </summary>
        public static Image CreateWindow(Transform parent, string name, float width, float height)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin        = CENTER;
            rect.anchorMax        = CENTER;
            rect.pivot            = CENTER;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta        = new Vector2(width, height);
            Image image = go.GetComponent<Image>();
            image.color = WINDOW_COLOR;
            return image;
        }

        public static Image CreatePanel(Transform parent, string name, float x, float y, float width, float height, Color color)
        {
            RectTransform rect = CreateRect(parent, name, x, y, width, height);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        public static Image CreateSection(Transform parent, string name, float x, float y, float width, float height)
        {
            Image image = CreatePanel(parent, name, x, y, width, height, PANEL_COLOR);
            image.raycastTarget = false;
            return image;
        }

        public static TMP_Text CreateLabel(Transform parent, string name, string text, float x, float y, float width, float height,
            float fontSize = FONT_SIZE, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            RectTransform rect = CreateRect(parent, name, x, y, width, height);
            TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            Font(label);
            label.text           = text;
            label.fontSize       = fontSize;
            label.color          = Color.white;
            label.alignment      = alignment;
            label.overflowMode   = TextOverflowModes.Ellipsis;
            label.raycastTarget  = false;
            return label;
        }

        public static Button CreateButton(Transform parent, string name, string text, float x, float y, float width, float height)
        {
            Image image = CreatePanel(parent, name, x, y, width, height, BUTTON_COLOR);
            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            CreateLabel(image.transform, "Label", text, 0f, 0f, width, height, FONT_SIZE, TextAlignmentOptions.Center);
            return button;
        }

        public static Toggle CreateToggle(Transform parent, string name, string text, float x, float y, float width, float height,
            ToggleGroup group = null)
        {
            Image background = CreatePanel(parent, name, x, y, width, height, BUTTON_COLOR);
            Image check = CreatePanel(background.transform, "Checkmark", CHECK_INSET, CHECK_INSET, width - (CHECK_INSET * 2f), height - (CHECK_INSET * 2f), CHECK_COLOR);
            check.raycastTarget = false;
            Toggle toggle = background.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.graphic       = check;
            toggle.group         = group;
            toggle.SetIsOnWithoutNotify(false);
            CreateLabel(background.transform, "Label", text, 0f, 0f, width, height, FONT_SIZE, TextAlignmentOptions.Center);
            return toggle;
        }

        public static TMP_InputField CreateInput(Transform parent, string name, string placeholder, float x, float y, float width, float height)
        {
            Image image = CreatePanel(parent, name, x, y, width, height, INPUT_COLOR);
            float innerWidth  = width - (TEXT_PADDING * 2f);
            float innerHeight = height - (TEXT_PADDING * 0.5f);
            RectTransform area = CreateRect(image.transform, "TextArea", TEXT_PADDING, TEXT_PADDING * 0.25f, innerWidth, innerHeight);
            area.gameObject.AddComponent<RectMask2D>();
            TMP_Text hint = CreateLabel(area, "Placeholder", placeholder, 0f, 0f, innerWidth, innerHeight);
            hint.color = HINT_COLOR;
            TMP_Text text = CreateLabel(area, "Text", string.Empty, 0f, 0f, innerWidth, innerHeight);
            TMP_InputField field = image.gameObject.AddComponent<TMP_InputField>();
            field.targetGraphic = image;
            field.textViewport  = area;
            field.textComponent = text;
            field.placeholder   = hint;
            return field;
        }

        public static TMP_Dropdown CreateDropdown(Transform parent, string name, IList<string> options, float x, float y, float width, float height)
        {
            Image image = CreatePanel(parent, name, x, y, width, height, INPUT_COLOR);
            TMP_Text caption = CreateLabel(image.transform, "Caption", string.Empty, TEXT_PADDING, 0f, width - (TEXT_PADDING * 2f), height);

            Image templateImage = CreatePanel(image.transform, "Template", 0f, 0f, width, DROPDOWN_LIST_HEIGHT, PANEL_COLOR);
            RectTransform template = templateImage.rectTransform;
            template.anchorMin        = new Vector2(0f, 0f);
            template.anchorMax        = new Vector2(1f, 0f);
            template.pivot            = new Vector2(0.5f, 1f);
            template.anchoredPosition = Vector2.zero;
            template.sizeDelta        = new Vector2(0f, DROPDOWN_LIST_HEIGHT);

            ScrollRect scroll = templateImage.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            RectTransform viewport = CreateRect(template, "Viewport", 0f, 0f, width, DROPDOWN_LIST_HEIGHT);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = CreateRect(viewport, "Content", 0f, 0f, width, height);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot     = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, height);
            scroll.viewport = viewport;
            scroll.content  = content;

            Image itemBackground = CreatePanel(content, "Item", 0f, 0f, width, height, ROW_COLOR);
            RectTransform item = itemBackground.rectTransform;
            item.anchorMin = new Vector2(0f, 0.5f);
            item.anchorMax = new Vector2(1f, 0.5f);
            item.pivot     = new Vector2(0.5f, 0.5f);
            item.sizeDelta = new Vector2(0f, height);
            Toggle itemToggle = itemBackground.gameObject.AddComponent<Toggle>();
            itemToggle.targetGraphic = itemBackground;
            TMP_Text itemLabel = CreateLabel(item, "Item Label", string.Empty, TEXT_PADDING, 0f, width - (TEXT_PADDING * 2f), height);
            templateImage.gameObject.SetActive(false);

            TMP_Dropdown dropdown = image.gameObject.AddComponent<TMP_Dropdown>();
            dropdown.targetGraphic = image;
            dropdown.template      = template;
            dropdown.captionText   = caption;
            dropdown.itemText      = itemLabel;
            dropdown.AddOptions(new List<string>(options));
            dropdown.SetValueWithoutNotify(0);
            dropdown.RefreshShownValue();
            return dropdown;
        }

        /// <summary> ListRowView를 만들고 단추, 칸, 선택 표시, 등급 막대를 직렬화 필드에 연결한다 </summary>
        public static ListRowView CreateRow(Transform parent, string name, int columnCount, float x, float y, float width, float height)
        {
            Image image = CreatePanel(parent, name, x, y, width, height, ROW_COLOR);
            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            Image gradeBar = CreatePanel(image.transform, "GradeBar", 0f, 0f, GRADE_BAR_WIDTH, height, GRADE_COLORS[0]);
            gradeBar.raycastTarget = false;
            Image selected = CreatePanel(image.transform, "Selected", 0f, 0f, width, height, SELECT_COLOR);
            selected.raycastTarget = false;
            selected.gameObject.SetActive(false);

            TMP_Text[] columns = new TMP_Text[columnCount];
            float columnWidth = (width - GRADE_BAR_WIDTH) / columnCount;
            for (int i = 0; i < columnCount; ++i)
            {
                columns[i] = CreateLabel(image.transform, "Column" + i, string.Empty, GRADE_BAR_WIDTH + (columnWidth * i), 0f,
                    columnWidth, height, SMALL_FONT);
            }

            ListRowView row = image.gameObject.AddComponent<ListRowView>();
            SetRef(row, "_button", button);
            SetRefs(row, "_columns", columns);
            SetRef(row, "_selectedMark", selected.gameObject);
            SetRef(row, "_gradeBar", gradeBar);
            SetColors(row, "_gradeColors", GRADE_COLORS);
            return row;
        }

        public static ListRowView[] CreateRows(Transform parent, string namePrefix, int count, int columnCount,
            float x, float y, float width, float rowHeight, float gap)
        {
            ListRowView[] rows = new ListRowView[count];
            for (int i = 0; i < count; ++i)
            {
                rows[i] = CreateRow(parent, namePrefix + i, columnCount, x, y + ((rowHeight + gap) * i), width, rowHeight);
            }

            return rows;
        }

        /// <summary> 눌러도 아무것도 하지 않는 표시용 글자 상자, 켜고 끄는 표시 GameObject로 쓴다 </summary>
        public static GameObject CreateMark(Transform parent, string name, string text, float x, float y, float width, float height)
        {
            TMP_Text label = CreateLabel(parent, name, text, x, y, width, height, SMALL_FONT, TextAlignmentOptions.Center);
            label.color = MARK_COLOR;
            return label.gameObject;
        }

        public static void SetRef(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            FindProperty(serialized, field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetRefs<T>(UnityEngine.Object target, string field, IReadOnlyList<T> values) where T : UnityEngine.Object
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = FindProperty(serialized, field);
            property.arraySize = values.Count;
            for (int i = 0; i < values.Count; ++i)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetColors(UnityEngine.Object target, string field, IReadOnlyList<Color> values)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = FindProperty(serialized, field);
            property.arraySize = values.Count;
            for (int i = 0; i < values.Count; ++i)
            {
                property.GetArrayElementAtIndex(i).colorValue = values[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static T GetRef<T>(UnityEngine.Object target, string field) where T : UnityEngine.Object
        {
            SerializedObject serialized = new SerializedObject(target);
            return FindProperty(serialized, field).objectReferenceValue as T;
        }

        public static T[] GetRefs<T>(UnityEngine.Object target, string field) where T : UnityEngine.Object
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = FindProperty(serialized, field);
            T[] result = new T[property.arraySize];
            for (int i = 0; i < result.Length; ++i)
            {
                result[i] = property.GetArrayElementAtIndex(i).objectReferenceValue as T;
            }

            return result;
        }

        private static SerializedProperty FindProperty(SerializedObject serialized, string field)
        {
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
            {
                throw new InvalidOperationException(serialized.targetObject.GetType().Name + "에 직렬화 필드 " + field + "이(가) 없습니다. View 필드 이름이 바뀌었는지 확인하세요");
            }

            return property;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Font(TMP_Text label)
        {
            if (!_fontSearched)
            {
                _fontSearched = true;
                string[] guids = AssetDatabase.FindAssets(KOREAN_FONT_NAME + " t:TMP_FontAsset");
                if (guids.Length > 0)
                {
                    _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guids[0]));
                }
            }

            if (_font != null)
            {
                label.font = _font;
            }
        }
    }
}
