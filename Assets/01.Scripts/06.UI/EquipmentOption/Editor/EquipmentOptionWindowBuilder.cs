#if UNITY_EDITOR
// System
using System;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.UI.Editor
{
    /// <summary> 장비 옵션 재설정 창 프리팹을 공통 창 스킨과 글꼴로 만든다 </summary>
    public static class EquipmentOptionWindowBuilder
    {
        // 배치 JSON이 없는 창이라 코드로 계층을 만든다. 다시 실행하면 프리팹을 통째로 새로 쓰므로 프리팹을 손으로 고친 내용은 사라진다.
        // 씬에 넣는 일은 하지 않는다. 출시 씬 배치는 통합 담당이 따로 한다

        /****************************************
        *                Fields
        ****************************************/

        public const string PREFAB_PATH = "Assets/02.Prefab/UI/Windows/PF_EquipmentOptionWindow.prefab";

        private const string SKIN_FOLDER  = "Assets/01.Scripts/05.Art/UI/GameStyle/Textures/";
        private const string FONT_REGULAR = "Assets/01.Scripts/06.UI/Hud/Fonts/HudQuestKorean-Regular.asset";
        private const string FONT_BOLD    = "Assets/01.Scripts/06.UI/Hud/Fonts/HudQuestKorean-Bold.asset";
        private const string UI_LAYER     = "UI";
        private const string TOWN_SCENE_PATH = "Assets/00.Scenes/Release/ShinshiTown.unity";

        private const float WINDOW_WIDTH   = 760f;
        private const float WINDOW_HEIGHT  = 500f;
        private const float TITLE_HEIGHT   = 56f;
        private const float MARGIN         = 24f;
        private const float SLOT_WIDTH     = 250f;
        private const float SLOT_HEIGHT    = 52f;
        private const float SLOT_GAP       = 60f;
        private const float SLOT_TOP       = 76f;
        private const float DETAIL_LEFT    = 304f;
        private const float DETAIL_WIDTH   = 432f;
        private const float LINE_TOP       = 132f;
        private const float LINE_GAP       = 68f;
        private const float CLOSE_SIZE     = 28f;
        private const float BUTTON_WIDTH   = 200f;
        private const float BUTTON_HEIGHT  = 48f;
        private const int   LINE_COUNT     = 2;

        private static readonly Color G_Text   = new Color(0.9f, 0.92f, 0.94f, 1f);
        private static readonly Color G_Muted  = new Color(0.57f, 0.62f, 0.68f, 1f);
        private static readonly Color G_Title  = new Color(0.75f, 0.79f, 0.82f, 1f);
        private static readonly Color G_Accent = new Color(0.94f, 0.82f, 0.55f, 1f);
        private static readonly Color G_Panel  = new Color(1f, 1f, 1f, 0.9f);
        private static readonly Color G_Line   = new Color(1f, 1f, 1f, 0.35f);

        private static TMP_FontAsset G_Regular;
        private static TMP_FontAsset G_Bold;

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem("Shinshi/UI/옵션 재설정 창 만들기")]
        public static void Build()
        {
            G_Regular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_REGULAR);
            G_Bold    = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_BOLD);
            if ((G_Regular == null) || (G_Bold == null))
            {
                throw new InvalidOperationException("옵션 재설정 창 글꼴을 찾을 수 없습니다");
            }

            GameObject root = new GameObject("PF_EquipmentOptionWindow", typeof(RectTransform));
            try
            {
                root.layer = LayerMask.NameToLayer(UI_LAYER);
                BuildWindow((RectTransform)root.transform);

                // 창은 단축키와 메뉴가 켠다. 기존 창 프리팹과 같이 꺼진 채로 저장한다
                root.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[UI] 옵션 재설정 창 프리팹을 만들었다: " + PREFAB_PATH);
        }

        /// <summary> 마을 출시 씬에 옵션 재설정 창과 바인더, 단축키 토글을 놓는다. 이미 있으면 아무것도 하지 않는다 </summary>
        [MenuItem("Shinshi/UI/옵션 재설정 창을 마을 씬에 연결")]
        public static void WireTownScene()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            if (prefab == null)
            {
                throw new InvalidOperationException("옵션 재설정 창 프리팹이 없습니다. 먼저 창을 만드세요");
            }

            UnityEngine.SceneManagement.Scene scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(TOWN_SCENE_PATH,
                UnityEditor.SceneManagement.OpenSceneMode.Single);
            if (UnityEngine.Object.FindObjectsByType<EquipmentOptionBinder>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0)
            {
                Debug.Log("[UI] 옵션 재설정 창이 이미 연결돼 있다");
                return;
            }

            // 캐릭터 상세창의 캔버스 설정과 바인더 자리를 그대로 따른다. 같은 씬의 다른 창과 배율, 입력 처리가 달라지지 않게 한다
            CharDetailBinder[] anchors = UnityEngine.Object.FindObjectsByType<CharDetailBinder>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            CharDetailView[] views = UnityEngine.Object.FindObjectsByType<CharDetailView>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            Shinshi.Systems.HotkeyRouter[] routers = UnityEngine.Object.FindObjectsByType<Shinshi.Systems.HotkeyRouter>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            if ((anchors.Length == 0) || (views.Length == 0) || (routers.Length == 0))
            {
                throw new InvalidOperationException("마을 씬에서 캐릭터 상세창 연결이나 단축키 라우터를 찾지 못했습니다");
            }

            Canvas referenceCanvas = views[0].GetComponentInParent<Canvas>(true);
            CanvasScaler referenceScaler = (referenceCanvas != null) ? referenceCanvas.GetComponent<CanvasScaler>() : null;
            if ((referenceCanvas == null) || (referenceScaler == null))
            {
                throw new InvalidOperationException("캐릭터 상세창의 캔버스 설정을 찾지 못했습니다");
            }

            GameObject canvasObject = new GameObject("EquipmentOptionCanvas", typeof(RectTransform));
            canvasObject.layer = LayerMask.NameToLayer(UI_LAYER);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode   = referenceCanvas.renderMode;
            canvas.sortingOrder = referenceCanvas.sortingOrder + 1;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = referenceScaler.uiScaleMode;
            scaler.referenceResolution = referenceScaler.referenceResolution;
            scaler.screenMatchMode     = referenceScaler.screenMatchMode;
            scaler.matchWidthOrHeight  = referenceScaler.matchWidthOrHeight;
            canvasObject.AddComponent<GraphicRaycaster>();

            GameObject window = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvasObject.transform);
            window.name = "EquipmentOptionWindow";

            GameObject bindingObject = anchors[0].gameObject;
            EquipmentOptionBinder binder = bindingObject.AddComponent<EquipmentOptionBinder>();
            SetReference(binder, "_view", window.GetComponent<EquipmentOptionView>());

            Shinshi.Systems.HotkeyWindowToggle toggle = bindingObject.AddComponent<Shinshi.Systems.HotkeyWindowToggle>();
            SetReference(toggle, "_router", routers[0]);
            SetReference(toggle, "_window", window);
            var serializedToggle = new SerializedObject(toggle);
            serializedToggle.FindProperty("_action").intValue = (int)Shinshi.Systems.EHotkeyAction.EquipmentOption;
            serializedToggle.ApplyModifiedPropertiesWithoutUndo();

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            Debug.Log("[UI] 옵션 재설정 창을 마을 씬에 연결했다");
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

            var view = root.gameObject.AddComponent<EquipmentOptionView>();

            // 제목줄을 끌어 창을 옮긴다. 글자보다 먼저 만들어 글자 아래에 깔리게 한다
            Image titleBar = Panel(root, "제목줄", 0f, 0f, WINDOW_WIDTH, TITLE_HEIGHT, Color.clear);
            titleBar.raycastTarget = true;
            var drag = titleBar.gameObject.AddComponent<UiWindowDragHandle>();
            SetReference(drag, "_window", root);
            Label(root, "제목", "장비 옵션 재설정", MARGIN, 14f, 360f, 30f, 24f, G_Text, true, TextAlignmentOptions.MidlineLeft);

            Button close = MakeButton(root, "닫기 버튼", WINDOW_WIDTH - MARGIN - CLOSE_SIZE, 14f, CLOSE_SIZE, CLOSE_SIZE,
                "T_UiIcon_Close", false);
            Line(root, "제목 구분선", MARGIN, TITLE_HEIGHT, WINDOW_WIDTH - (MARGIN * 2f));

            int slotCount = Constants.Combat.EQUIPMENT_SLOT_COUNT;
            var slotButtons = new UnityEngine.Object[slotCount];
            var slotLabels  = new UnityEngine.Object[slotCount];
            var slotMarks   = new UnityEngine.Object[slotCount];
            for (int i = 0; i < slotCount; ++i)
            {
                string name   = EquipmentOptionViewRules.SlotName(i);
                Button button = MakeButton(root, name + " 줄", MARGIN, SLOT_TOP + (SLOT_GAP * i), SLOT_WIDTH, SLOT_HEIGHT,
                    "T_UiPanel_Inset", true);
                RectTransform rect = (RectTransform)button.transform;
                slotButtons[i] = button;
                TMP_Text slotLabel = Label(rect, "이름", name, 14f, 0f, SLOT_WIDTH - 28f, SLOT_HEIGHT, 15f, G_Text, false,
                    TextAlignmentOptions.MidlineLeft);

                // 장비 이름이 길어도 한 줄에 두고 넘치면 줄임표로 자른다
                slotLabel.textWrappingMode = TextWrappingModes.NoWrap;
                slotLabel.overflowMode     = TextOverflowModes.Ellipsis;
                slotLabels[i] = slotLabel;

                Image mark = Panel(rect, "선택 표시", 0f, 0f, SLOT_WIDTH, SLOT_HEIGHT, G_Accent);
                Skin(mark, "T_UiSelect_Outline", G_Accent);
                mark.gameObject.SetActive(false);
                slotMarks[i] = mark.gameObject;
            }

            Label(root, "선택 장비 제목", "선택한 장비", DETAIL_LEFT, SLOT_TOP - 4f, DETAIL_WIDTH, 24f, 18f, G_Title, true,
                TextAlignmentOptions.MidlineLeft);
            TMP_Text item = Label(root, "장비 이름", string.Empty, DETAIL_LEFT, SLOT_TOP + 22f, DETAIL_WIDTH, 30f, 22f, G_Text, true,
                TextAlignmentOptions.MidlineLeft);

            var lineLabels  = new UnityEngine.Object[LINE_COUNT];
            var rangeLabels = new UnityEngine.Object[LINE_COUNT];
            for (int i = 0; i < LINE_COUNT; ++i)
            {
                float y = LINE_TOP + (LINE_GAP * i);
                Line(root, "옵션 구분선_" + i, DETAIL_LEFT, y, DETAIL_WIDTH);
                lineLabels[i]  = Label(root, "옵션 " + (i + 1), string.Empty, DETAIL_LEFT, y + 8f, DETAIL_WIDTH, 28f, 22f, G_Text, false,
                    TextAlignmentOptions.MidlineLeft);
                rangeLabels[i] = Label(root, "옵션 범위 " + (i + 1), string.Empty, DETAIL_LEFT, y + 36f, DETAIL_WIDTH, 22f, 16f, G_Muted,
                    false, TextAlignmentOptions.MidlineLeft);
            }

            float footerTop = LINE_TOP + (LINE_GAP * LINE_COUNT);
            Line(root, "비용 구분선", DETAIL_LEFT, footerTop, DETAIL_WIDTH);
            TMP_Text cost = Label(root, "비용", string.Empty, DETAIL_LEFT, footerTop + 12f, DETAIL_WIDTH, 24f, 18f, G_Text, false,
                TextAlignmentOptions.MidlineLeft);
            TMP_Text pity = Label(root, "천장", string.Empty, DETAIL_LEFT, footerTop + 40f, DETAIL_WIDTH, 22f, 16f, G_Muted, false,
                TextAlignmentOptions.MidlineLeft);
            TMP_Text status = Label(root, "상태", string.Empty, DETAIL_LEFT, footerTop + 70f, DETAIL_WIDTH, 44f, 16f, G_Accent, false,
                TextAlignmentOptions.TopLeft);

            Button reroll = MakeButton(root, "재설정 버튼", WINDOW_WIDTH - MARGIN - BUTTON_WIDTH,
                WINDOW_HEIGHT - MARGIN - BUTTON_HEIGHT, BUTTON_WIDTH, BUTTON_HEIGHT, "T_UiButton_Primary", true);
            TMP_Text rerollLabel = Label((RectTransform)reroll.transform, "글자", "재설정", 0f, 0f, BUTTON_WIDTH, BUTTON_HEIGHT, 20f,
                G_Text, true, TextAlignmentOptions.Center);

            SetReference(view, "_closeButton", close);
            SetArray(view, "_slotButtons", slotButtons);
            SetArray(view, "_slotLabels", slotLabels);
            SetArray(view, "_slotSelectedMarks", slotMarks);
            SetReference(view, "_itemLabel", item);
            SetArray(view, "_lineLabels", lineLabels);
            SetArray(view, "_rangeLabels", rangeLabels);
            SetReference(view, "_costLabel", cost);
            SetReference(view, "_pityLabel", pity);
            SetReference(view, "_statusLabel", status);
            SetReference(view, "_rerollButton", reroll);
            SetReference(view, "_rerollLabel", rerollLabel);
        }

        private static Button MakeButton(RectTransform parent, string name, float x, float y, float width, float height,
            string skin, bool sliced)
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
                throw new InvalidOperationException("옵션 재설정 창 스킨을 찾을 수 없습니다: " + name);
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
    }
}
#endif
