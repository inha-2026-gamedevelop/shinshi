#if UNITY_EDITOR
// System
using System;
using System.IO;
using System.Text;

// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Data;

namespace Shinshi.UI.Editor
{
    /// <summary>Dev_Training 씬에서 신체 강화(수련) 창을 오각형 배분 레이아웃으로 구성하고 재사용 프리팹으로 저장한다</summary>
    public static class TrainingWindowBuilder
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SCENE_PATH         = "Assets/00.Scenes/Dev_HUD/Dev_Training.unity";
        private const string PREFAB_PATH        = "Assets/02.Prefab/UI/Training/PF_TrainingWindow.prefab";
        private const string FONT_FOLDER        = "Assets/01.Scripts/06.UI/Training/Fonts";
        private const string SOURCE_FONT_FOLDER = "Assets/01.Scripts/06.UI/Hud/Fonts/NanumGothic/Fonts";
        private const string SKIN_FOLDER        = "Assets/01.Scripts/05.Art/UI/GameStyle/Textures/";
        private const string ESSENCE_ICON_PATH  = "Assets/01.Scripts/05.Art/UI/Icons/Textures/Currency/T_Icon_Currency_Essence_White.png";
        private const string BODY_MIST_PATH     = "Assets/01.Scripts/05.Art/UI/Training/Textures/T_UiTraining_BodyMist.png";

        private const string CANVAS_NAME = "TrainingCanvas";
        private const string ROOT_NAME   = "TrainingWindowRoot";

        private const float WINDOW_WIDTH  = 1140f;
        private const float WINDOW_HEIGHT = 720f;
        private const float HEADER_HEIGHT = 72f;

        private const float LEFT_X      = 24f;
        private const float LEFT_Y      = 96f;
        private const float LEFT_WIDTH  = 560f;
        private const float LEFT_HEIGHT = 596f;

        private const float RIGHT_X      = 604f;
        private const float RIGHT_Y      = 96f;
        private const float RIGHT_WIDTH  = 512f;
        private const float RIGHT_HEIGHT = 596f;

        private const float AXIS_BUTTON_WIDTH  = 128f;
        private const float AXIS_BUTTON_HEIGHT = 56f;

        private const float REFERENCE_WIDTH        = 1920f;
        private const float REFERENCE_HEIGHT       = 1080f;
        private const float MATCH_WIDTH_OR_HEIGHT  = 0.5f;

        private const int STAT_COUNT = 5;

        private static readonly Color G_Window  = new Color(0.065f, 0.083f, 0.102f, 1f);
        private static readonly Color G_Control = new Color(0.055f, 0.075f, 0.095f, 1f);
        private static readonly Color G_Border  = new Color(0.29f, 0.32f, 0.34f, 1f);
        private static readonly Color G_Gold    = new Color(0.68f, 0.6f, 0.43f, 1f);
        private static readonly Color G_Accent  = new Color(0.84f, 0.75f, 0.57f, 1f);
        private static readonly Color G_Text    = new Color(0.9f, 0.92f, 0.92f, 1f);
        private static readonly Color G_Muted   = new Color(0.64f, 0.69f, 0.71f, 1f);

        // ETrainingStat 순서: 공격, 체력, 방어, 치명타, 기력. 위에서 시계 방향으로 배치한다
        private static readonly string[]  G_StatKeys    = { "Attack", "Health", "Defense", "Crit", "Energy" };
        private static readonly string[]  G_StatLabels  = { "공격", "체력", "방어", "치명타", "기력" };
        private static readonly Vector2[] G_AxisCenters =
        {
            new Vector2(280f, 72f),
            new Vector2(478f, 208f),
            new Vector2(405f, 420f),
            new Vector2(155f, 420f),
            new Vector2(82f, 208f),
        };

        private static TMP_FontAsset G_Regular;
        private static TMP_FontAsset G_Bold;
        private static TrainingView  G_View;

        /****************************************
        *                 Menu
        ****************************************/

        [MenuItem("Shinshi/UI/신체 강화 창 만들기")]
        private static void BuildMenu()
        {
            Build();
        }

        /// <summary>Dev_Training 씬에 신체 강화 창을 구성하고 프리팹으로 저장한다, 결과 요약을 돌려준다</summary>
        public static string Build()
        {
            ValidateContext();

            GameObject canvasObject = GameObject.Find(CANVAS_NAME);
            if (canvasObject == null)
            {
                throw new InvalidOperationException("TrainingCanvas를 찾을 수 없다");
            }

            ConfigureCanvasScaler(canvasObject);

            EnsureFolder(FONT_FOLDER);
            EnsureFolder(Path.GetDirectoryName(PREFAB_PATH).Replace('\\', '/'));
            G_Regular = EnsureFont("Regular");
            G_Bold    = EnsureFont("Bold");

            RectTransform canvas  = (RectTransform)canvasObject.transform;
            GameObject    existing = GameObject.Find(ROOT_NAME);
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
            }

            RectTransform window = Rect(canvas, ROOT_NAME, 0f, 0f, WINDOW_WIDTH, WINDOW_HEIGHT);
            window.anchorMin = new Vector2(0.5f, 0.5f);
            window.anchorMax = new Vector2(0.5f, 0.5f);
            window.pivot     = new Vector2(0.5f, 0.5f);
            window.anchoredPosition = Vector2.zero;
            window.localScale = Vector3.one;

            // ExecuteAlways인 TrainingView는 AddComponent 즉시 Awake가 돈다. 모든 참조를 연결하기 전에 돌면
            // 배열 필드가 비어 있어 NullReferenceException이 난다. 구성을 마칠 때까지 비활성 상태로 둔다
            window.gameObject.SetActive(false);

            G_View = window.gameObject.AddComponent<TrainingView>();

            BuildFrame(window);
            BuildHeader(window);
            BuildLeftPanel(window);
            BuildRightPanel(window);

            PreloadCharacters(window);

            window.gameObject.SetActive(true);

            EnsurePrefabFolder();
            PrefabUtility.SaveAsPrefabAsset(window.gameObject, PREFAB_PATH);

            TrainingPreviewHelper.CreateOrUpdate(window.gameObject, G_View);
            PreloadCharacters(window);

            UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssetIfDirty(G_Regular);
            AssetDatabase.SaveAssetIfDirty(G_Bold);
            Selection.activeGameObject = window.gameObject;

            string result = $"TrainingWindowBuilder: {PREFAB_PATH} 저장, 창 {WINDOW_WIDTH}x{WINDOW_HEIGHT}, 루트 {ROOT_NAME}";
            Debug.Log(result);
            return result;
        }

        /****************************************
        *               Validation
        ****************************************/

        private static void ValidateContext()
        {
            if (EditorApplication.isPlaying)
            {
                throw new InvalidOperationException("신체 강화 창 제작은 편집 모드에서 실행한다");
            }

            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != SCENE_PATH)
            {
                throw new InvalidOperationException("Dev_Training 씬을 열고 실행한다");
            }
        }

        private static void ConfigureCanvasScaler(GameObject canvasObject)
        {
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                throw new InvalidOperationException("TrainingCanvas에 CanvasScaler가 없다");
            }

            scaler.uiScaleMode          = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution  = new Vector2(REFERENCE_WIDTH, REFERENCE_HEIGHT);
            scaler.matchWidthOrHeight   = MATCH_WIDTH_OR_HEIGHT;
            canvasObject.transform.localScale = Vector3.one;
        }

        /****************************************
        *                Frame
        ****************************************/

        private static void BuildFrame(RectTransform window)
        {
            Image shadow = Panel(window, "WindowShadow", -12f, -12f, WINDOW_WIDTH + 24f, WINDOW_HEIGHT + 24f, new Color(0f, 0f, 0f, 0.35f));
            shadow.transform.SetAsFirstSibling();

            Image background = Panel(window, "Background", 0f, 0f, WINDOW_WIDTH, WINDOW_HEIGHT, G_Window);
            background.raycastTarget = true;
            Skin(background, "T_UiWindow_Frame", Color.white);
        }

        private static void BuildHeader(RectTransform window)
        {
            Image header = Panel(window, "HeaderSurface", 1f, 1f, WINDOW_WIDTH - 2f, HEADER_HEIGHT - 1f, Color.white);
            Skin(header, "T_UiWindow_TitleBar", new Color(0.7f, 0.8f, 0.9f, 0.8f));

            Text(Rect(window, "Title", 32f, 20f, 200f, 32f), "신체 강화", 30f, G_Text, true, TextAlignmentOptions.Left);
            GameObject previewTag = Text(Rect(window, "PreviewTag", 220f, 28f, 140f, 20f), "미리보기", 13f, G_Muted, false, TextAlignmentOptions.Left).gameObject;

            RectTransform dragArea = Rect(window, "DragSurface", 0f, 0f, WINDOW_WIDTH - 96f, HEADER_HEIGHT);
            Image dragImage = dragArea.gameObject.AddComponent<Image>();
            dragImage.color = Color.clear;
            dragImage.raycastTarget = true;
            UiWindowDragHandle drag = dragArea.gameObject.AddComponent<UiWindowDragHandle>();
            SetReference(drag, "_window", window);

            Button close = CloseButton(window, WINDOW_WIDTH - 36f - 28f, 20f);

            Panel(window, "HeaderDivider", 24f, HEADER_HEIGHT, WINDOW_WIDTH - 48f, 1f, new Color(G_Border.r, G_Border.g, G_Border.b, 0.6f));

            SetReference(G_View, "_closeButton", close);
            SetReference(G_View, "_previewTag", previewTag);
        }

        /****************************************
        *              Left Panel
        ****************************************/

        private static void BuildLeftPanel(RectTransform window)
        {
            RectTransform panel = Rect(window, "LeftPanel", LEFT_X, LEFT_Y, LEFT_WIDTH, LEFT_HEIGHT);
            Image panelBackground = panel.gameObject.AddComponent<Image>();
            panelBackground.color = G_Control;
            Skin(panelBackground, "T_UiPanel_Inset", new Color(0.8f, 0.9f, 1f, 1f));
            Border(panel, new Color(G_Border.r, G_Border.g, G_Border.b, 0.5f), 1f);

            Text(Rect(panel, "LeftTitle", 24f, 18f, 300f, 26f), "신체 수련", 20f, G_Gold, true, TextAlignmentOptions.Left);
            TMP_Text totalInvest = Text(Rect(panel, "TotalInvest", 360f, 18f, 176f, 26f), string.Empty, 16f, G_Muted, false, TextAlignmentOptions.Right);

            // 안개 그림을 먼저 배치해 뒤에 놓고, 오각형 Radar를 뒤이어 배치해 얇은 선이 그림 앞에서 보이게 한다
            RectTransform bodyRect = Rect(panel, "BodyMistImage", 145f, 120f, 270f, 270f);
            Image bodyImage = bodyRect.gameObject.AddComponent<Image>();
            bodyImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BODY_MIST_PATH);
            bodyImage.color  = new Color(0.74f, 0.85f, 0.94f, 0.18f);
            bodyImage.preserveAspect = true;
            bodyImage.raycastTarget  = false;
            bodyRect.SetAsFirstSibling();
            AddBodyMistDrift(panel.gameObject, bodyImage);

            RectTransform radarRect = Rect(panel, "Radar", 100f, 80f, 360f, 360f);
            TrainingRadarGraphic radar = radarRect.gameObject.AddComponent<TrainingRadarGraphic>();
            radar.raycastTarget = false;

            Button[]  statButtons = new Button[STAT_COUNT];
            TMP_Text[] stageLabels = new TMP_Text[STAT_COUNT];
            for (int i = 0; i < STAT_COUNT; ++i)
            {
                Vector2 center = G_AxisCenters[i];
                float   x = center.x - (AXIS_BUTTON_WIDTH * 0.5f);
                float   y = center.y - (AXIS_BUTTON_HEIGHT * 0.5f);

                Button button = TextButton(panel, "Stat" + G_StatKeys[i], x, y, AXIS_BUTTON_WIDTH, AXIS_BUTTON_HEIGHT);
                TMP_Text nameLabel = Text(Rect((RectTransform)button.transform, "Label", 0f, 6f, AXIS_BUTTON_WIDTH, 22f),
                    G_StatLabels[i], 16f, G_Text, true, TextAlignmentOptions.Center);
                AddFeedback(button.gameObject, nameLabel, G_Text);
                TMP_Text label = Text(Rect((RectTransform)button.transform, "StageLabel", 0f, 30f, AXIS_BUTTON_WIDTH, 20f),
                    string.Empty, 14f, G_Muted, false, TextAlignmentOptions.Center);

                statButtons[i] = button;
                stageLabels[i] = label;
            }

            TMP_Text poolText = Text(Rect(panel, "PoolText", 24f, 468f, 512f, 30f), string.Empty, 16f, G_Muted, false, TextAlignmentOptions.Left);
            Panel(panel, "FooterDivider", 24f, 512f, 512f, 1f, G_Border);
            Image essenceIcon = Rect(panel, "EssenceIcon", 24f, 540f, 32f, 36f).gameObject.AddComponent<Image>();
            essenceIcon.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ESSENCE_ICON_PATH);
            essenceIcon.color = G_Accent;
            essenceIcon.preserveAspect = true;
            essenceIcon.raycastTarget = false;
            TMP_Text essenceText = Text(Rect(panel, "EssenceText", 68f, 540f, 236f, 36f), string.Empty, 24f, G_Gold, true, TextAlignmentOptions.Left);

            Button respec = Button(panel, "RespecButton", 396f, 538f, 140f, 42f, G_Control, "T_UiButton_Secondary");
            Text(Rect((RectTransform)respec.transform, "Label", 0f, 0f, 140f, 42f), "재분배", 18f, G_Text, true);

            SetReference(G_View, "_statButtons", statButtons);
            SetReference(G_View, "_stageLabels", stageLabels);
            SetReference(G_View, "_radar", radar);
            SetReference(G_View, "_totalInvestText", totalInvest);
            SetReference(G_View, "_poolText", poolText);
            SetReference(G_View, "_essenceText", essenceText);
            SetReference(G_View, "_respecButton", respec);
        }

        /****************************************
        *              Right Panel
        ****************************************/

        private static void BuildRightPanel(RectTransform window)
        {
            RectTransform panel = Rect(window, "RightPanel", RIGHT_X, RIGHT_Y, RIGHT_WIDTH, RIGHT_HEIGHT);
            Image panelBackground = panel.gameObject.AddComponent<Image>();
            panelBackground.color = G_Control;
            Skin(panelBackground, "T_UiPanel_Inset", new Color(0.8f, 0.9f, 1f, 1f));
            Border(panel, new Color(G_Border.r, G_Border.g, G_Border.b, 0.5f), 1f);

            TMP_Text selectedTitle = Text(Rect(panel, "SelectedTitle", 24f, 18f, 424f, 40f), G_StatLabels[0], 28f, G_Text, true, TextAlignmentOptions.Left);
            Panel(panel, "DividerTop", 24f, 96f, RIGHT_WIDTH - 48f, 1f, G_Border);

            Text(Rect(panel, "StageCaption", 24f, 100f, 200f, 18f), "투자 단계", 14f, G_Muted, false, TextAlignmentOptions.Left);
            TMP_Text selectedStage = Text(Rect(panel, "SelectedStage", 24f, 116f, 464f, 54f), string.Empty, 40f, G_Accent, true, TextAlignmentOptions.Left);

            TMP_Text before = Text(Rect(panel, "BeforeText", 24f, 190f, 200f, 48f), string.Empty, 18f, G_Muted, false, TextAlignmentOptions.Left);
            Text(Rect(panel, "EffectArrow", 232f, 190f, 40f, 48f), "→", 30f, G_Accent, false, TextAlignmentOptions.Center);
            TMP_Text after = Text(Rect(panel, "AfterText", 280f, 190f, 208f, 48f), string.Empty, 20f, G_Text, true, TextAlignmentOptions.Left);

            Text(Rect(panel, "AdjustTitle", 24f, 268f, 200f, 24f), "투자 조정", 16f, G_Gold, true, TextAlignmentOptions.Left);

            Button minus = TextButton(panel, "MinusButton", 154f, 278f, 44f, 44f);
            TMP_Text minusLabel = Text(Rect((RectTransform)minus.transform, "Label", 0f, 0f, 44f, 44f), "-", 22f, G_Text, true);
            AddFeedback(minus.gameObject, minusLabel, G_Text);
            TMP_Text quantity = Text(Rect(panel, "QuantityText", 204f, 278f, 100f, 44f), "0", 24f, G_Text, true, TextAlignmentOptions.Center);
            Button plus = TextButton(panel, "PlusButton", 310f, 278f, 44f, 44f);
            TMP_Text plusLabel = Text(Rect((RectTransform)plus.transform, "Label", 0f, 0f, 44f, 44f), "+", 22f, G_Text, true);
            AddFeedback(plus.gameObject, plusLabel, G_Text);

            Panel(panel, "DividerBottom", 24f, 348f, RIGHT_WIDTH - 48f, 1f, G_Border);

            TMP_Text cost    = Text(Rect(panel, "CostText", 24f, 370f, 464f, 32f), string.Empty, 22f, G_Gold, false, TextAlignmentOptions.Left);
            TMP_Text balance = Text(Rect(panel, "BalanceText", 24f, 412f, 464f, 28f), string.Empty, 17f, G_Muted, false, TextAlignmentOptions.Left);
            TMP_Text status  = Text(Rect(panel, "StatusText", 24f, 456f, 464f, 56f), string.Empty, 15f, G_Muted, false, TextAlignmentOptions.TopLeft);

            Button revert = Button(panel, "RevertButton", 178f, 530f, 142f, 44f, G_Control, "T_UiButton_Secondary");
            Text(Rect((RectTransform)revert.transform, "Label", 0f, 0f, 142f, 44f), "되돌리기", 18f, G_Text, false);
            Button train = Button(panel, "TrainButton", 336f, 530f, 152f, 44f, G_Control, "T_UiButton_Primary");
            Text(Rect((RectTransform)train.transform, "Label", 0f, 0f, 152f, 44f), "강화", 20f, G_Accent, true);
            Border((RectTransform)train.transform, new Color(G_Gold.r, G_Gold.g, G_Gold.b, 0.55f), 1f);

            SetReference(G_View, "_selectedTitleText", selectedTitle);
            SetReference(G_View, "_selectedStageText", selectedStage);
            SetReference(G_View, "_selectedBeforeText", before);
            SetReference(G_View, "_selectedAfterText", after);
            SetReference(G_View, "_quantityText", quantity);
            SetReference(G_View, "_costText", cost);
            SetReference(G_View, "_balanceText", balance);
            SetReference(G_View, "_statusText", status);
            SetReference(G_View, "_minusButton", minus);
            SetReference(G_View, "_plusButton", plus);
            SetReference(G_View, "_revertButton", revert);
            SetReference(G_View, "_trainButton", train);
        }

        /****************************************
        *           Scene Preview Helper
        ****************************************/

        // 미리보기 바인더는 프리팹과 수명이 다른 개발 씬의 별도 오브젝트에 둔다
        private static class TrainingPreviewHelper
        {
            private const string GROWTH_CONFIG_PATH = "Assets/09.Data/Growth/GrowthConfig.asset";
            private const long   ESSENCE_SAMPLE      = 18400;

            private static readonly int[] G_TrainingSample = { 14, 9, 6, 3, 2 };

            public static void CreateOrUpdate(GameObject window, TrainingView view)
            {
                GrowthConfigSO growth = AssetDatabase.LoadAssetAtPath<GrowthConfigSO>(GROWTH_CONFIG_PATH);
                if (growth == null)
                {
                    throw new InvalidOperationException("GrowthConfigSO 에셋을 찾을 수 없다: " + GROWTH_CONFIG_PATH);
                }

                GameObject helper = null;
                foreach (Transform child in window.transform.parent)
                {
                    if (child.name == "TrainingPreviewHelper")
                    {
                        helper = child.gameObject;
                        break;
                    }
                }
                if (helper == null)
                {
                    helper = new GameObject("TrainingPreviewHelper");
                    helper.transform.SetParent(window.transform.parent, false);
                }
                helper.SetActive(false);
                if (!helper.TryGetComponent(out TrainingPreviewBinder binder))
                {
                    binder = helper.AddComponent<TrainingPreviewBinder>();
                }

                SetReference(binder, "_view", view);
                SetReference(binder, "_growthConfig", growth);
                helper.SetActive(true);

                view.ApplyPreviewSnapshot(ESSENCE_SAMPLE, G_TrainingSample, growth);
            }
        }

        /****************************************
        *               Helpers
        ****************************************/

        private static void AddBodyMistDrift(GameObject parent, Graphic graphic)
        {
            UiAmbientDrift drift = parent.AddComponent<UiAmbientDrift>();
            SerializedObject serialized = new SerializedObject(drift);
            SerializedProperty motes = serialized.FindProperty("_motes");
            motes.arraySize = 1;
            SerializedProperty mote = motes.GetArrayElementAtIndex(0);
            mote.FindPropertyRelative("_graphic").objectReferenceValue = graphic;
            mote.FindPropertyRelative("_travel").vector2Value = Vector2.zero;
            mote.FindPropertyRelative("_duration").floatValue = 10f;
            mote.FindPropertyRelative("_delay").floatValue = 0f;
            mote.FindPropertyRelative("_minAlpha").floatValue = 0.14f;
            mote.FindPropertyRelative("_maxAlpha").floatValue = 0.22f;
            mote.FindPropertyRelative("_yoyo").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Button TextButton(RectTransform parent, string name, float x, float y, float width, float height)
        {
            RectTransform rect = Rect(parent, name, x, y, width, height);
            Image hitArea = rect.gameObject.AddComponent<Image>();
            hitArea.color = Color.clear;
            hitArea.raycastTarget = true;
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = hitArea;
            button.transition = Selectable.Transition.None;
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            return button;
        }

        private static Button Button(RectTransform parent, string name, float x, float y, float width, float height, Color color, string skin)
        {
            RectTransform rect = Rect(parent, name, x, y, width, height);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = Color.white;
            if (!string.IsNullOrEmpty(skin))
            {
                Skin(image, skin, Color.white);
            }
            else
            {
                image.color = color;
            }

            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition    = Selectable.Transition.None;
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            AddFeedback(rect.gameObject, image, image.color);
            return button;
        }

        // UiButtonFeedback은 자신의 기본 _normalColor(밝은 회색)로 그래픽 색을 덮어쓴다.
        // 스킨 틴트를 그대로 유지하려면 지금 색을 기준으로 호버/누름 색까지 함께 채워 둬야 한다
        private static void AddFeedback(GameObject target, Graphic graphic, Color normal)
        {
            UiButtonFeedback feedback = target.AddComponent<UiButtonFeedback>();
            SetReference(feedback, "_graphic", graphic);
            SetColor(feedback, "_normalColor", normal);
            SetColor(feedback, "_hoverColor", Color.Lerp(normal, Color.white, 0.35f));
            SetColor(feedback, "_pressedColor", Color.Lerp(normal, Color.black, 0.2f));
        }

        // 닫기 버튼은 9-슬라이스 배경이 아니라 아이콘 한 장을 그대로 보여준다
        private static Button CloseButton(RectTransform parent, float x, float y)
        {
            RectTransform rect = Rect(parent, "CloseButton", x, y, 36f, 36f);
            Image frame = rect.gameObject.AddComponent<Image>();
            frame.color = G_Control;
            Border(rect, G_Border, 1f);

            Sprite iconSprite = AssetDatabase.LoadAssetAtPath<Sprite>(SKIN_FOLDER + "T_UiIcon_Close.png");
            RectTransform iconRect = Rect(rect, "Icon", 8f, 8f, 20f, 20f);
            Image icon = iconRect.gameObject.AddComponent<Image>();
            icon.sprite = iconSprite;
            icon.color = G_Muted;
            icon.raycastTarget = false;

            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = frame;
            button.transition    = Selectable.Transition.None;
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            AddFeedback(rect.gameObject, icon, icon.color);
            return button;
        }

        private static TMP_Text Text(RectTransform rect, string value, float size, Color color, bool bold = false, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = bold ? G_Bold : G_Regular;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            return text;
        }

        private static Image Panel(RectTransform parent, string name, float x, float y, float width, float height, Color color)
        {
            Image image = Rect(parent, name, x, y, width, height).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void Skin(Image image, string name, Color tint)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SKIN_FOLDER + name + ".png");
            if (sprite == null)
            {
                throw new InvalidOperationException("신체 강화 창 스킨을 찾을 수 없다: " + name);
            }

            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = tint;
        }

        private static void Border(RectTransform parent, Color color, float width)
        {
            RectTransform border = Rect(parent, "Border", 0f, 0f, 0f, 0f);
            Stretch(border);
            RectTransform top = Panel(border, "Top", 0f, 0f, 0f, width, color).rectTransform;
            top.anchorMax = Vector2.one;
            top.offsetMin = new Vector2(0f, -width);
            top.offsetMax = Vector2.zero;
            RectTransform bottom = Panel(border, "Bottom", 0f, 0f, 0f, width, color).rectTransform;
            bottom.anchorMin = Vector2.zero;
            bottom.anchorMax = Vector2.right;
            bottom.offsetMin = Vector2.zero;
            bottom.offsetMax = new Vector2(0f, width);
            RectTransform left = Panel(border, "Left", 0f, 0f, width, 0f, color).rectTransform;
            left.anchorMin = Vector2.zero;
            left.anchorMax = Vector2.up;
            left.offsetMin = Vector2.zero;
            left.offsetMax = new Vector2(width, 0f);
            RectTransform right = Panel(border, "Right", 0f, 0f, width, 0f, color).rectTransform;
            right.anchorMin = Vector2.right;
            right.anchorMax = Vector2.one;
            right.offsetMin = new Vector2(-width, 0f);
            right.offsetMax = Vector2.zero;
        }

        private static RectTransform Rect(RectTransform parent, string name, float x, float y, float width, float height)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.layer = LayerMask.NameToLayer("UI");
            RectTransform rect = (RectTransform)gameObject.transform;
            rect.SetParent(parent, false);
            SetRect(rect, x, y, width, height);
            return rect;
        }

        private static void SetRect(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = Vector2.up;
            rect.anchorMax = Vector2.up;
            rect.pivot = Vector2.up;
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetReference(UnityEngine.Object target, string name, UnityEngine.Object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(name);
            if (property == null)
            {
                throw new InvalidOperationException($"{target.GetType().Name}에 필드가 없다: {name}");
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetReference(UnityEngine.Object target, string name, UnityEngine.Object[] values)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(name);
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; ++i)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetColor(UnityEngine.Object target, string name, Color value)
        {
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty(name).colorValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static void EnsurePrefabFolder()
        {
            EnsureFolder("Assets/02.Prefab");
            EnsureFolder("Assets/02.Prefab/UI");
            EnsureFolder("Assets/02.Prefab/UI/Training");
        }

        private static TMP_FontAsset EnsureFont(string weight)
        {
            string path = FONT_FOLDER + "/TrainingKorean-" + weight + ".asset";
            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null)
            {
                return existing;
            }

            Font source = AssetDatabase.LoadAssetAtPath<Font>(SOURCE_FONT_FOLDER + "/NanumGothic-" + weight + ".ttf");
            TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(source, 48, 6, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            font.name = "TrainingKorean-" + weight;
            AssetDatabase.CreateAsset(font, path);
            AssetDatabase.AddObjectToAsset(font.material, font);
            AssetDatabase.AddObjectToAsset(font.atlasTextures[0], font);
            return font;
        }

        private static void PreloadCharacters(RectTransform root)
        {
            StringBuilder characters = new StringBuilder("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789%+-/.,: ()");
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                characters.Append(text.text);
            }

            characters.Append("체력치명타");
            G_Regular.TryAddCharacters(characters.ToString());
            G_Bold.TryAddCharacters(characters.ToString());
            EditorUtility.SetDirty(G_Regular);
            EditorUtility.SetDirty(G_Bold);
        }
    }
}
#endif
