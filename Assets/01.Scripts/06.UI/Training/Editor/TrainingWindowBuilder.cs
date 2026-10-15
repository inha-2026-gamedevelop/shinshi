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
using Shinshi.Common;
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

        private const float REFERENCE_WIDTH       = 1920f;
        private const float REFERENCE_HEIGHT      = 1080f;
        private const float MATCH_WIDTH_OR_HEIGHT = 0.5f;

        private const int STAT_COUNT           = Constants.Combat.TRAINING_STAT_COUNT;
        private const float ZERO_DIMENSION     = 0f;
        private const float DIVIDER_BOTTOM_Y   = 348f;
        private const float MIST_DELAY         = 0f;
        private const float PANEL_INNER_MARGIN = 24f;
        private const float DIVIDER_TOP_Y      = 96f;

        private const float TITLE_X                      = 32f;
        private const float TITLE_Y                      = 20f;
        private const float TITLE_WIDTH                  = 200f;
        private const float TITLE_HEIGHT                 = 32f;
        private const float PREVIEW_TAG_X                = 220f;
        private const float PREVIEW_TAG_Y                = 28f;
        private const float PREVIEW_TAG_WIDTH            = 140f;
        private const float PREVIEW_TAG_HEIGHT           = 20f;
        private const float LEFT_TITLE_X                 = 24f;
        private const float LEFT_TITLE_Y                 = 18f;
        private const float LEFT_TITLE_WIDTH             = 300f;
        private const float LEFT_TITLE_HEIGHT            = 26f;
        private const float TOTAL_INVEST_X               = 360f;
        private const float TOTAL_INVEST_Y               = 18f;
        private const float TOTAL_INVEST_WIDTH           = 176f;
        private const float TOTAL_INVEST_HEIGHT          = 26f;
        private const float BODY_MIST_IMAGE_X            = 145f;
        private const float BODY_MIST_IMAGE_Y            = 120f;
        private const float BODY_MIST_IMAGE_WIDTH        = 270f;
        private const float BODY_MIST_IMAGE_HEIGHT       = 270f;
        private const float RADAR_X                      = 100f;
        private const float RADAR_Y                      = 80f;
        private const float RADAR_WIDTH                  = 360f;
        private const float RADAR_HEIGHT                 = 360f;
        private const float BUTTON_LABEL_X               = 0f;
        private const float BUTTON_LABEL_Y               = 6f;
        private const float BUTTON_LABEL_HEIGHT          = 22f;
        private const float BUTTON_STAGE_LABEL_X         = 0f;
        private const float BUTTON_STAGE_LABEL_Y         = 30f;
        private const float BUTTON_STAGE_LABEL_HEIGHT    = 20f;
        private const float POOL_TEXT_X                  = 24f;
        private const float POOL_TEXT_Y                  = 468f;
        private const float POOL_TEXT_WIDTH              = 512f;
        private const float POOL_TEXT_HEIGHT             = 30f;
        private const float ESSENCE_ICON_X               = 24f;
        private const float ESSENCE_ICON_Y               = 540f;
        private const float ESSENCE_ICON_WIDTH           = 32f;
        private const float ESSENCE_ICON_HEIGHT          = 36f;
        private const float ESSENCE_TEXT_X               = 68f;
        private const float ESSENCE_TEXT_Y               = 540f;
        private const float ESSENCE_TEXT_WIDTH           = 236f;
        private const float ESSENCE_TEXT_HEIGHT          = 36f;
        private const float RESPEC_LABEL_X               = 0f;
        private const float RESPEC_LABEL_Y               = 0f;
        private const float RESPEC_LABEL_WIDTH           = 140f;
        private const float RESPEC_LABEL_HEIGHT          = 42f;
        private const float SELECTED_TITLE_X             = 24f;
        private const float SELECTED_TITLE_Y             = 18f;
        private const float SELECTED_TITLE_WIDTH         = 424f;
        private const float SELECTED_TITLE_HEIGHT        = 40f;
        private const float STAGE_CAPTION_X              = 24f;
        private const float STAGE_CAPTION_Y              = 100f;
        private const float STAGE_CAPTION_WIDTH          = 200f;
        private const float STAGE_CAPTION_HEIGHT         = 18f;
        private const float SELECTED_STAGE_X             = 24f;
        private const float SELECTED_STAGE_Y             = 116f;
        private const float SELECTED_STAGE_WIDTH         = 464f;
        private const float SELECTED_STAGE_HEIGHT        = 54f;
        private const float BEFORE_TEXT_X                = 24f;
        private const float BEFORE_TEXT_Y                = 190f;
        private const float BEFORE_TEXT_WIDTH            = 200f;
        private const float BEFORE_TEXT_HEIGHT           = 48f;
        private const float EFFECT_ARROW_X               = 232f;
        private const float EFFECT_ARROW_Y               = 190f;
        private const float EFFECT_ARROW_WIDTH           = 40f;
        private const float EFFECT_ARROW_HEIGHT          = 48f;
        private const float AFTER_TEXT_X                 = 280f;
        private const float AFTER_TEXT_Y                 = 190f;
        private const float AFTER_TEXT_WIDTH             = 208f;
        private const float AFTER_TEXT_HEIGHT            = 48f;
        private const float ADJUST_TITLE_X               = 24f;
        private const float ADJUST_TITLE_Y               = 268f;
        private const float ADJUST_TITLE_WIDTH           = 200f;
        private const float ADJUST_TITLE_HEIGHT          = 24f;
        private const float MINUS_BUTTON_X               = 154f;
        private const float MINUS_BUTTON_Y               = 278f;
        private const float MINUS_BUTTON_WIDTH           = 44f;
        private const float MINUS_BUTTON_HEIGHT          = 44f;
        private const float MINUS_LABEL_X                = 0f;
        private const float MINUS_LABEL_Y                = 0f;
        private const float MINUS_LABEL_WIDTH            = 44f;
        private const float MINUS_LABEL_HEIGHT           = 44f;
        private const float QUANTITY_TEXT_X              = 204f;
        private const float QUANTITY_TEXT_Y              = 278f;
        private const float QUANTITY_TEXT_WIDTH          = 100f;
        private const float QUANTITY_TEXT_HEIGHT         = 44f;
        private const float PLUS_BUTTON_X                = 310f;
        private const float PLUS_BUTTON_Y                = 278f;
        private const float PLUS_BUTTON_WIDTH            = 44f;
        private const float PLUS_BUTTON_HEIGHT           = 44f;
        private const float PLUS_LABEL_X                 = 0f;
        private const float PLUS_LABEL_Y                 = 0f;
        private const float PLUS_LABEL_WIDTH             = 44f;
        private const float PLUS_LABEL_HEIGHT            = 44f;
        private const float COST_TEXT_X                  = 24f;
        private const float COST_TEXT_Y                  = 370f;
        private const float COST_TEXT_WIDTH              = 464f;
        private const float COST_TEXT_HEIGHT             = 32f;
        private const float BALANCE_TEXT_X               = 24f;
        private const float BALANCE_TEXT_Y               = 412f;
        private const float BALANCE_TEXT_WIDTH           = 464f;
        private const float BALANCE_TEXT_HEIGHT          = 28f;
        private const float STATUS_TEXT_X                = 24f;
        private const float STATUS_TEXT_Y                = 456f;
        private const float STATUS_TEXT_WIDTH            = 464f;
        private const float STATUS_TEXT_HEIGHT           = 56f;
        private const float REVERT_LABEL_X               = 0f;
        private const float REVERT_LABEL_Y               = 0f;
        private const float REVERT_LABEL_WIDTH           = 142f;
        private const float REVERT_LABEL_HEIGHT          = 44f;
        private const float TRAIN_LABEL_X                = 0f;
        private const float TRAIN_LABEL_Y                = 0f;
        private const float TRAIN_LABEL_WIDTH            = 152f;
        private const float TRAIN_LABEL_HEIGHT           = 44f;
        private const float CLOSE_ICON_X                 = 8f;
        private const float CLOSE_ICON_Y                 = 8f;
        private const float CLOSE_ICON_WIDTH             = 20f;
        private const float CLOSE_ICON_HEIGHT            = 20f;
        private const float BORDER_X                     = 0f;
        private const float BORDER_Y                     = 0f;
        private const float BORDER_WIDTH                 = 0f;
        private const float BORDER_HEIGHT                = 0f;
        private const float BACKGROUND_X                 = 0f;
        private const float BACKGROUND_Y                 = 0f;
        private const float FOOTER_DIVIDER_X             = 24f;
        private const float FOOTER_DIVIDER_Y             = 512f;
        private const float FOOTER_DIVIDER_WIDTH         = 512f;
        private const float FOOTER_DIVIDER_HEIGHT        = 1f;
        private const float RESPEC_BUTTON_X              = 396f;
        private const float RESPEC_BUTTON_Y              = 538f;
        private const float RESPEC_BUTTON_WIDTH          = 140f;
        private const float RESPEC_BUTTON_HEIGHT         = 42f;
        private const float REVERT_BUTTON_X              = 178f;
        private const float REVERT_BUTTON_Y              = 530f;
        private const float REVERT_BUTTON_WIDTH          = 142f;
        private const float REVERT_BUTTON_HEIGHT         = 44f;
        private const float TRAIN_BUTTON_X               = 336f;
        private const float TRAIN_BUTTON_Y               = 530f;
        private const float TRAIN_BUTTON_WIDTH           = 152f;
        private const float TRAIN_BUTTON_HEIGHT          = 44f;
        private const float TITLE_FONT_SIZE              = 30f;
        private const float PREVIEW_TAG_FONT_SIZE        = 13f;
        private const float LEFT_TITLE_FONT_SIZE         = 20f;
        private const float TOTAL_INVEST_FONT_SIZE       = 16f;
        private const float BUTTON_LABEL_FONT_SIZE       = 16f;
        private const float BUTTON_STAGE_LABEL_FONT_SIZE = 14f;
        private const float POOL_TEXT_FONT_SIZE          = 16f;
        private const float ESSENCE_TEXT_FONT_SIZE       = 24f;
        private const float RESPEC_LABEL_FONT_SIZE       = 18f;
        private const float SELECTED_TITLE_FONT_SIZE     = 28f;
        private const float STAGE_CAPTION_FONT_SIZE      = 14f;
        private const float SELECTED_STAGE_FONT_SIZE     = 40f;
        private const float BEFORE_TEXT_FONT_SIZE        = 18f;
        private const float EFFECT_ARROW_FONT_SIZE       = 30f;
        private const float AFTER_TEXT_FONT_SIZE         = 20f;
        private const float ADJUST_TITLE_FONT_SIZE       = 16f;
        private const float MINUS_LABEL_FONT_SIZE        = 22f;
        private const float QUANTITY_TEXT_FONT_SIZE      = 24f;
        private const float PLUS_LABEL_FONT_SIZE         = 22f;
        private const float COST_TEXT_FONT_SIZE          = 22f;
        private const float BALANCE_TEXT_FONT_SIZE       = 17f;
        private const float STATUS_TEXT_FONT_SIZE        = 15f;
        private const float REVERT_LABEL_FONT_SIZE       = 18f;
        private const float TRAIN_LABEL_FONT_SIZE        = 20f;
        private const float WINDOW_COLOR_RED             = 0.065f;
        private const float WINDOW_COLOR_GREEN           = 0.083f;
        private const float WINDOW_COLOR_BLUE            = 0.102f;
        private const float WINDOW_COLOR_ALPHA           = 1f;
        private const float CONTROL_COLOR_RED            = 0.055f;
        private const float CONTROL_COLOR_GREEN          = 0.075f;
        private const float CONTROL_COLOR_BLUE           = 0.095f;
        private const float CONTROL_COLOR_ALPHA          = 1f;
        private const float BORDER_COLOR_RED             = 0.29f;
        private const float BORDER_COLOR_GREEN           = 0.32f;
        private const float BORDER_COLOR_BLUE            = 0.34f;
        private const float BORDER_COLOR_ALPHA           = 1f;
        private const float GOLD_COLOR_RED               = 0.68f;
        private const float GOLD_COLOR_GREEN             = 0.6f;
        private const float GOLD_COLOR_BLUE              = 0.43f;
        private const float GOLD_COLOR_ALPHA             = 1f;
        private const float ACCENT_COLOR_RED             = 0.84f;
        private const float ACCENT_COLOR_GREEN           = 0.75f;
        private const float ACCENT_COLOR_BLUE            = 0.57f;
        private const float ACCENT_COLOR_ALPHA           = 1f;
        private const float TEXT_COLOR_RED               = 0.9f;
        private const float TEXT_COLOR_GREEN             = 0.92f;
        private const float TEXT_COLOR_BLUE              = 0.92f;
        private const float TEXT_COLOR_ALPHA             = 1f;
        private const float MUTED_COLOR_RED              = 0.64f;
        private const float MUTED_COLOR_GREEN            = 0.69f;
        private const float MUTED_COLOR_BLUE             = 0.71f;
        private const float MUTED_COLOR_ALPHA            = 1f;
        private const float SHADOW_RED                   = 0f;
        private const float SHADOW_GREEN                 = 0f;
        private const float SHADOW_BLUE                  = 0f;
        private const float SHADOW_ALPHA                 = 0.35f;
        private const float HEADER_TINT_RED              = 0.7f;
        private const float HEADER_TINT_GREEN            = 0.8f;
        private const float HEADER_TINT_BLUE             = 0.9f;
        private const float HEADER_TINT_ALPHA            = 0.8f;
        private const float INSET_TINT_RED               = 0.8f;
        private const float INSET_TINT_GREEN             = 0.9f;
        private const float INSET_TINT_BLUE              = 1f;
        private const float INSET_TINT_ALPHA             = 1f;
        private const float BODY_MIST_TINT_RED           = 0.74f;
        private const float BODY_MIST_TINT_GREEN         = 0.85f;
        private const float BODY_MIST_TINT_BLUE          = 0.94f;
        private const float BODY_MIST_TINT_ALPHA         = 0.18f;
        private const float AXIS_ATTACK_X                = 280f;
        private const float AXIS_ATTACK_Y                = 72f;
        private const float AXIS_HEALTH_X                = 478f;
        private const float AXIS_HEALTH_Y                = 208f;
        private const float AXIS_DEFENSE_X               = 405f;
        private const float AXIS_DEFENSE_Y               = 420f;
        private const float AXIS_CRIT_X                  = 155f;
        private const float AXIS_CRIT_Y                  = 420f;
        private const float AXIS_ENERGY_X                = 82f;
        private const float AXIS_ENERGY_Y                = 208f;
        private const float SHADOW_EXTRA_SIZE            = 24f;
        private const float SHADOW_MARGIN                = 12f;
        private const float FRAME_EDGE_SUM               = 2f;
        private const float FRAME_EDGE                   = 1f;
        private const float DRAG_CLOSE_EXCLUSION         = 96f;
        private const float CLOSE_SIZE                   = 36f;
        private const float CLOSE_RIGHT_MARGIN           = 28f;
        private const float CLOSE_TOP                    = 20f;
        private const float PANEL_HORIZONTAL_PADDING     = 48f;
        private const float HEADER_DIVIDER_ALPHA         = 0.6f;
        private const float PANEL_BORDER_ALPHA           = 0.5f;
        private const float TRAIN_BORDER_ALPHA           = 0.55f;
        private const float HALF                         = 0.5f;
        private const float BORDER_LINE_WIDTH            = 1f;
        private const int MIST_LAYER_COUNT               = 1;
        private const float MIST_DURATION                = 10f;
        private const float MIST_MIN_ALPHA               = 0.14f;
        private const float MIST_MAX_ALPHA               = 0.22f;
        private const float HOVER_BLEND                  = 0.35f;
        private const float PRESSED_BLEND                = 0.2f;
        private const int FONT_POINT_SIZE                = 48;
        private const int FONT_PADDING                   = 6;
        private const int FONT_ATLAS_SIZE                = 1024;

        private static readonly Color _windowColor  = new Color(WINDOW_COLOR_RED, WINDOW_COLOR_GREEN, WINDOW_COLOR_BLUE, WINDOW_COLOR_ALPHA);
        private static readonly Color _controlColor = new Color(CONTROL_COLOR_RED, CONTROL_COLOR_GREEN, CONTROL_COLOR_BLUE, CONTROL_COLOR_ALPHA);
        private static readonly Color _borderColor  = new Color(BORDER_COLOR_RED, BORDER_COLOR_GREEN, BORDER_COLOR_BLUE, BORDER_COLOR_ALPHA);
        private static readonly Color _goldColor    = new Color(GOLD_COLOR_RED, GOLD_COLOR_GREEN, GOLD_COLOR_BLUE, GOLD_COLOR_ALPHA);
        private static readonly Color _accentColor  = new Color(ACCENT_COLOR_RED, ACCENT_COLOR_GREEN, ACCENT_COLOR_BLUE, ACCENT_COLOR_ALPHA);
        private static readonly Color _textColor    = new Color(TEXT_COLOR_RED, TEXT_COLOR_GREEN, TEXT_COLOR_BLUE, TEXT_COLOR_ALPHA);
        private static readonly Color _mutedColor   = new Color(MUTED_COLOR_RED, MUTED_COLOR_GREEN, MUTED_COLOR_BLUE, MUTED_COLOR_ALPHA);

        // ETrainingStat 순서: 공격, 체력, 방어, 치명타, 기력. 위에서 시계 방향으로 배치한다
        private static readonly string[]  G_StatKeys    = { "Attack", "Health", "Defense", "Crit", "Energy" };
        private static readonly string[]  G_StatLabels  = { "공격", "체력", "방어", "치명타", "기력" };
        private static readonly Vector2[] G_AxisCenters =
        {
            new Vector2(AXIS_ATTACK_X, AXIS_ATTACK_Y),
            new Vector2(AXIS_HEALTH_X, AXIS_HEALTH_Y),
            new Vector2(AXIS_DEFENSE_X, AXIS_DEFENSE_Y),
            new Vector2(AXIS_CRIT_X, AXIS_CRIT_Y),
            new Vector2(AXIS_ENERGY_X, AXIS_ENERGY_Y),
        };

        private static TMP_FontAsset _regularFont;
        private static TMP_FontAsset _boldFont;
        private static TrainingView  _view;

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
            _regularFont = EnsureFont("Regular");
            _boldFont    = EnsureFont("Bold");

            RectTransform canvas  = (RectTransform)canvasObject.transform;
            GameObject    existing = GameObject.Find(ROOT_NAME);
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
            }

            RectTransform window = Rect(canvas, ROOT_NAME, ZERO_DIMENSION, ZERO_DIMENSION, WINDOW_WIDTH, WINDOW_HEIGHT);
            window.anchorMin = new Vector2(HALF, HALF);
            window.anchorMax = new Vector2(HALF, HALF);
            window.pivot     = new Vector2(HALF, HALF);
            window.anchoredPosition = Vector2.zero;
            window.localScale = Vector3.one;

            // ExecuteAlways인 TrainingView는 AddComponent 즉시 Awake가 돈다. 모든 참조를 연결하기 전에 돌면
            // 배열 필드가 비어 있어 NullReferenceException이 난다. 구성을 마칠 때까지 비활성 상태로 둔다
            window.gameObject.SetActive(false);

            _view = window.gameObject.AddComponent<TrainingView>();

            BuildFrame(window);
            BuildHeader(window);
            BuildLeftPanel(window);
            BuildRightPanel(window);

            PreloadCharacters(window);

            window.gameObject.SetActive(true);

            EnsurePrefabFolder();
            PrefabUtility.SaveAsPrefabAsset(window.gameObject, PREFAB_PATH);

            TrainingPreviewHelper.CreateOrUpdate(window.gameObject, _view);
            PreloadCharacters(window);

            UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssetIfDirty(_regularFont);
            AssetDatabase.SaveAssetIfDirty(_boldFont);
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
            Image shadow = Panel(window, "WindowShadow", -SHADOW_MARGIN, -SHADOW_MARGIN, WINDOW_WIDTH + SHADOW_EXTRA_SIZE,
                WINDOW_HEIGHT + SHADOW_EXTRA_SIZE, new Color(SHADOW_RED, SHADOW_GREEN, SHADOW_BLUE, SHADOW_ALPHA));
            shadow.transform.SetAsFirstSibling();

            Image background = Panel(window, "Background", BACKGROUND_X, BACKGROUND_Y, WINDOW_WIDTH, WINDOW_HEIGHT, _windowColor);
            background.raycastTarget = true;
            Skin(background, "T_UiWindow_Frame", Color.white);
        }

        private static void BuildHeader(RectTransform window)
        {
            Image header = Panel(window, "HeaderSurface", FRAME_EDGE, FRAME_EDGE, WINDOW_WIDTH - FRAME_EDGE_SUM, HEADER_HEIGHT - FRAME_EDGE, Color.white);
            Skin(header, "T_UiWindow_TitleBar", new Color(HEADER_TINT_RED, HEADER_TINT_GREEN, HEADER_TINT_BLUE, HEADER_TINT_ALPHA));

            Text(Rect(window, "Title", TITLE_X, TITLE_Y, TITLE_WIDTH, TITLE_HEIGHT), "신체 강화", TITLE_FONT_SIZE, _textColor, true, TextAlignmentOptions.Left);
            GameObject previewTag = Text(Rect(window, "PreviewTag", PREVIEW_TAG_X, PREVIEW_TAG_Y, PREVIEW_TAG_WIDTH,
                PREVIEW_TAG_HEIGHT), "미리보기", PREVIEW_TAG_FONT_SIZE, _mutedColor, false, TextAlignmentOptions.Left).gameObject;

            RectTransform dragArea = Rect(window, "DragSurface", ZERO_DIMENSION, ZERO_DIMENSION, WINDOW_WIDTH - DRAG_CLOSE_EXCLUSION, HEADER_HEIGHT);
            Image dragImage = dragArea.gameObject.AddComponent<Image>();
            dragImage.color = Color.clear;
            dragImage.raycastTarget = true;
            UiWindowDragHandle drag = dragArea.gameObject.AddComponent<UiWindowDragHandle>();
            SetReference(drag, "_window", window);

            Button close = CloseButton(window, WINDOW_WIDTH - CLOSE_SIZE - CLOSE_RIGHT_MARGIN, CLOSE_TOP);

            Panel(window, "HeaderDivider", PANEL_INNER_MARGIN, HEADER_HEIGHT, WINDOW_WIDTH - PANEL_HORIZONTAL_PADDING, BORDER_LINE_WIDTH,
                new Color(_borderColor.r, _borderColor.g, _borderColor.b, HEADER_DIVIDER_ALPHA));

            SetReference(_view, "_closeButton", close);
            SetReference(_view, "_previewTag", previewTag);
        }

        /****************************************
        *              Left Panel
        ****************************************/

        private static void BuildLeftPanel(RectTransform window)
        {
            RectTransform panel = Rect(window, "LeftPanel", LEFT_X, LEFT_Y, LEFT_WIDTH, LEFT_HEIGHT);
            Image panelBackground = panel.gameObject.AddComponent<Image>();
            panelBackground.color = _controlColor;
            Skin(panelBackground, "T_UiPanel_Inset", new Color(INSET_TINT_RED, INSET_TINT_GREEN, INSET_TINT_BLUE, INSET_TINT_ALPHA));
            Border(panel, new Color(_borderColor.r, _borderColor.g, _borderColor.b, PANEL_BORDER_ALPHA), BORDER_LINE_WIDTH);

            Text(Rect(panel, "LeftTitle", LEFT_TITLE_X, LEFT_TITLE_Y, LEFT_TITLE_WIDTH, LEFT_TITLE_HEIGHT), "신체 수련",
                LEFT_TITLE_FONT_SIZE, _goldColor, true, TextAlignmentOptions.Left);
            TMP_Text totalInvest = Text(Rect(panel, "TotalInvest", TOTAL_INVEST_X, TOTAL_INVEST_Y, TOTAL_INVEST_WIDTH,
                TOTAL_INVEST_HEIGHT), string.Empty, TOTAL_INVEST_FONT_SIZE, _mutedColor, false, TextAlignmentOptions.Right);

            // 안개 그림을 먼저 배치해 뒤에 놓고, 오각형 Radar를 뒤이어 배치해 얇은 선이 그림 앞에서 보이게 한다
            RectTransform bodyRect = Rect(panel, "BodyMistImage", BODY_MIST_IMAGE_X, BODY_MIST_IMAGE_Y, BODY_MIST_IMAGE_WIDTH, BODY_MIST_IMAGE_HEIGHT);
            Image bodyImage = bodyRect.gameObject.AddComponent<Image>();
            bodyImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BODY_MIST_PATH);
            bodyImage.color  = new Color(BODY_MIST_TINT_RED, BODY_MIST_TINT_GREEN, BODY_MIST_TINT_BLUE, BODY_MIST_TINT_ALPHA);
            bodyImage.preserveAspect = true;
            bodyImage.raycastTarget  = false;
            bodyRect.SetAsFirstSibling();
            AddBodyMistDrift(panel.gameObject, bodyImage);

            RectTransform radarRect = Rect(panel, "Radar", RADAR_X, RADAR_Y, RADAR_WIDTH, RADAR_HEIGHT);
            TrainingRadarGraphic radar = radarRect.gameObject.AddComponent<TrainingRadarGraphic>();
            radar.raycastTarget = false;

            Button[]  statButtons = new Button[STAT_COUNT];
            TMP_Text[] stageLabels = new TMP_Text[STAT_COUNT];
            for (int i = 0; i < STAT_COUNT; ++i)
            {
                Vector2 center = G_AxisCenters[i];
                float   x = center.x - (AXIS_BUTTON_WIDTH * HALF);
                float   y = center.y - (AXIS_BUTTON_HEIGHT * HALF);

                Button button = TextButton(panel, "Stat" + G_StatKeys[i], x, y, AXIS_BUTTON_WIDTH, AXIS_BUTTON_HEIGHT);
                TMP_Text nameLabel = Text(Rect((RectTransform)button.transform, "Label", BUTTON_LABEL_X, BUTTON_LABEL_Y,
                    AXIS_BUTTON_WIDTH, BUTTON_LABEL_HEIGHT),
                    G_StatLabels[i], BUTTON_LABEL_FONT_SIZE, _textColor, true, TextAlignmentOptions.Center);
                AddFeedback(button.gameObject, nameLabel, _textColor);
                TMP_Text label = Text(Rect((RectTransform)button.transform, "StageLabel", BUTTON_STAGE_LABEL_X,
                    BUTTON_STAGE_LABEL_Y, AXIS_BUTTON_WIDTH, BUTTON_STAGE_LABEL_HEIGHT),
                    string.Empty, BUTTON_STAGE_LABEL_FONT_SIZE, _mutedColor, false, TextAlignmentOptions.Center);

                statButtons[i] = button;
                stageLabels[i] = label;
            }

            TMP_Text poolText = Text(Rect(panel, "PoolText", POOL_TEXT_X, POOL_TEXT_Y, POOL_TEXT_WIDTH, POOL_TEXT_HEIGHT),
                string.Empty, POOL_TEXT_FONT_SIZE, _mutedColor, false, TextAlignmentOptions.Left);
            Panel(panel, "FooterDivider", FOOTER_DIVIDER_X, FOOTER_DIVIDER_Y, FOOTER_DIVIDER_WIDTH, FOOTER_DIVIDER_HEIGHT, _borderColor);
            Image essenceIcon = Rect(panel, "EssenceIcon", ESSENCE_ICON_X, ESSENCE_ICON_Y, ESSENCE_ICON_WIDTH, ESSENCE_ICON_HEIGHT).gameObject.AddComponent<Image>();
            essenceIcon.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ESSENCE_ICON_PATH);
            essenceIcon.color = _accentColor;
            essenceIcon.preserveAspect = true;
            essenceIcon.raycastTarget = false;
            TMP_Text essenceText = Text(Rect(panel, "EssenceText", ESSENCE_TEXT_X, ESSENCE_TEXT_Y, ESSENCE_TEXT_WIDTH,
                ESSENCE_TEXT_HEIGHT), string.Empty, ESSENCE_TEXT_FONT_SIZE, _goldColor, true, TextAlignmentOptions.Left);

            Button respec = Button(panel, "RespecButton", RESPEC_BUTTON_X, RESPEC_BUTTON_Y, RESPEC_BUTTON_WIDTH,
                RESPEC_BUTTON_HEIGHT, _controlColor, "T_UiButton_Secondary");
            Text(Rect((RectTransform)respec.transform, "Label", RESPEC_LABEL_X, RESPEC_LABEL_Y, RESPEC_LABEL_WIDTH,
                RESPEC_LABEL_HEIGHT), "재분배", RESPEC_LABEL_FONT_SIZE, _textColor, true);

            SetReference(_view, "_statButtons", statButtons);
            SetReference(_view, "_stageLabels", stageLabels);
            SetReference(_view, "_radar", radar);
            SetReference(_view, "_totalInvestText", totalInvest);
            SetReference(_view, "_poolText", poolText);
            SetReference(_view, "_essenceText", essenceText);
            SetReference(_view, "_respecButton", respec);
        }

        /****************************************
        *              Right Panel
        ****************************************/

        private static void BuildRightPanel(RectTransform window)
        {
            RectTransform panel = Rect(window, "RightPanel", RIGHT_X, RIGHT_Y, RIGHT_WIDTH, RIGHT_HEIGHT);
            Image panelBackground = panel.gameObject.AddComponent<Image>();
            panelBackground.color = _controlColor;
            Skin(panelBackground, "T_UiPanel_Inset", new Color(INSET_TINT_RED, INSET_TINT_GREEN, INSET_TINT_BLUE, INSET_TINT_ALPHA));
            Border(panel, new Color(_borderColor.r, _borderColor.g, _borderColor.b, PANEL_BORDER_ALPHA), BORDER_LINE_WIDTH);

            TMP_Text selectedTitle = Text(Rect(panel, "SelectedTitle", SELECTED_TITLE_X, SELECTED_TITLE_Y, SELECTED_TITLE_WIDTH,
                SELECTED_TITLE_HEIGHT), G_StatLabels[0], SELECTED_TITLE_FONT_SIZE, _textColor, true, TextAlignmentOptions.Left);
            Panel(panel, "DividerTop", PANEL_INNER_MARGIN, DIVIDER_TOP_Y, RIGHT_WIDTH - PANEL_HORIZONTAL_PADDING, BORDER_LINE_WIDTH, _borderColor);

            Text(Rect(panel, "StageCaption", STAGE_CAPTION_X, STAGE_CAPTION_Y, STAGE_CAPTION_WIDTH, STAGE_CAPTION_HEIGHT),
                "투자 단계", STAGE_CAPTION_FONT_SIZE, _mutedColor, false, TextAlignmentOptions.Left);
            TMP_Text selectedStage = Text(Rect(panel, "SelectedStage", SELECTED_STAGE_X, SELECTED_STAGE_Y, SELECTED_STAGE_WIDTH,
                SELECTED_STAGE_HEIGHT), string.Empty, SELECTED_STAGE_FONT_SIZE, _accentColor, true, TextAlignmentOptions.Left);

            TMP_Text before = Text(Rect(panel, "BeforeText", BEFORE_TEXT_X, BEFORE_TEXT_Y, BEFORE_TEXT_WIDTH, BEFORE_TEXT_HEIGHT),
                string.Empty, BEFORE_TEXT_FONT_SIZE, _mutedColor, false, TextAlignmentOptions.Left);
            Text(Rect(panel, "EffectArrow", EFFECT_ARROW_X, EFFECT_ARROW_Y, EFFECT_ARROW_WIDTH, EFFECT_ARROW_HEIGHT), "→",
                EFFECT_ARROW_FONT_SIZE, _accentColor, false, TextAlignmentOptions.Center);
            TMP_Text after = Text(Rect(panel, "AfterText", AFTER_TEXT_X, AFTER_TEXT_Y, AFTER_TEXT_WIDTH, AFTER_TEXT_HEIGHT),
                string.Empty, AFTER_TEXT_FONT_SIZE, _textColor, true, TextAlignmentOptions.Left);

            Text(Rect(panel, "AdjustTitle", ADJUST_TITLE_X, ADJUST_TITLE_Y, ADJUST_TITLE_WIDTH, ADJUST_TITLE_HEIGHT), "투자 조정",
                ADJUST_TITLE_FONT_SIZE, _goldColor, true, TextAlignmentOptions.Left);

            Button minus = TextButton(panel, "MinusButton", MINUS_BUTTON_X, MINUS_BUTTON_Y, MINUS_BUTTON_WIDTH, MINUS_BUTTON_HEIGHT);
            TMP_Text minusLabel = Text(Rect((RectTransform)minus.transform, "Label", MINUS_LABEL_X, MINUS_LABEL_Y,
                MINUS_LABEL_WIDTH, MINUS_LABEL_HEIGHT), "-", MINUS_LABEL_FONT_SIZE, _textColor, true);
            AddFeedback(minus.gameObject, minusLabel, _textColor);
            TMP_Text quantity = Text(Rect(panel, "QuantityText", QUANTITY_TEXT_X, QUANTITY_TEXT_Y, QUANTITY_TEXT_WIDTH,
                QUANTITY_TEXT_HEIGHT), "0", QUANTITY_TEXT_FONT_SIZE, _textColor, true, TextAlignmentOptions.Center);
            Button plus = TextButton(panel, "PlusButton", PLUS_BUTTON_X, PLUS_BUTTON_Y, PLUS_BUTTON_WIDTH, PLUS_BUTTON_HEIGHT);
            TMP_Text plusLabel = Text(Rect((RectTransform)plus.transform, "Label", PLUS_LABEL_X, PLUS_LABEL_Y, PLUS_LABEL_WIDTH,
                PLUS_LABEL_HEIGHT), "+", PLUS_LABEL_FONT_SIZE, _textColor, true);
            AddFeedback(plus.gameObject, plusLabel, _textColor);

            Panel(panel, "DividerBottom", PANEL_INNER_MARGIN, DIVIDER_BOTTOM_Y, RIGHT_WIDTH - PANEL_HORIZONTAL_PADDING, BORDER_LINE_WIDTH, _borderColor);

            TMP_Text cost    = Text(Rect(panel, "CostText", COST_TEXT_X, COST_TEXT_Y, COST_TEXT_WIDTH, COST_TEXT_HEIGHT),
                string.Empty, COST_TEXT_FONT_SIZE, _goldColor, false, TextAlignmentOptions.Left);
            TMP_Text balance = Text(Rect(panel, "BalanceText", BALANCE_TEXT_X, BALANCE_TEXT_Y, BALANCE_TEXT_WIDTH,
                BALANCE_TEXT_HEIGHT), string.Empty, BALANCE_TEXT_FONT_SIZE, _mutedColor, false, TextAlignmentOptions.Left);
            TMP_Text status  = Text(Rect(panel, "StatusText", STATUS_TEXT_X, STATUS_TEXT_Y, STATUS_TEXT_WIDTH,
                STATUS_TEXT_HEIGHT), string.Empty, STATUS_TEXT_FONT_SIZE, _mutedColor, false, TextAlignmentOptions.TopLeft);

            Button revert = Button(panel, "RevertButton", REVERT_BUTTON_X, REVERT_BUTTON_Y, REVERT_BUTTON_WIDTH,
                REVERT_BUTTON_HEIGHT, _controlColor, "T_UiButton_Secondary");
            Text(Rect((RectTransform)revert.transform, "Label", REVERT_LABEL_X, REVERT_LABEL_Y, REVERT_LABEL_WIDTH,
                REVERT_LABEL_HEIGHT), "되돌리기", REVERT_LABEL_FONT_SIZE, _textColor, false);
            Button train = Button(panel, "TrainButton", TRAIN_BUTTON_X, TRAIN_BUTTON_Y, TRAIN_BUTTON_WIDTH, TRAIN_BUTTON_HEIGHT,
                _controlColor, "T_UiButton_Primary");
            Text(Rect((RectTransform)train.transform, "Label", TRAIN_LABEL_X, TRAIN_LABEL_Y, TRAIN_LABEL_WIDTH,
                TRAIN_LABEL_HEIGHT), "강화", TRAIN_LABEL_FONT_SIZE, _accentColor, true);
            Border((RectTransform)train.transform, new Color(_goldColor.r, _goldColor.g, _goldColor.b, TRAIN_BORDER_ALPHA), BORDER_LINE_WIDTH);

            SetReference(_view, "_selectedTitleText", selectedTitle);
            SetReference(_view, "_selectedStageText", selectedStage);
            SetReference(_view, "_selectedBeforeText", before);
            SetReference(_view, "_selectedAfterText", after);
            SetReference(_view, "_quantityText", quantity);
            SetReference(_view, "_costText", cost);
            SetReference(_view, "_balanceText", balance);
            SetReference(_view, "_statusText", status);
            SetReference(_view, "_minusButton", minus);
            SetReference(_view, "_plusButton", plus);
            SetReference(_view, "_revertButton", revert);
            SetReference(_view, "_trainButton", train);
        }

        /****************************************
        *           Scene Preview Helper
        ****************************************/

        // 미리보기 바인더는 프리팹과 수명이 다른 개발 씬의 별도 오브젝트에 둔다
        private static class TrainingPreviewHelper
        {
            private const string GROWTH_CONFIG_PATH = "Assets/09.Data/Growth/GrowthConfig.asset";
            private const long ESSENCE_SAMPLE       = 18400;
            private const int SAMPLE_ATTACK         = 14;
            private const int SAMPLE_HEALTH         = 9;
            private const int SAMPLE_DEFENSE        = 6;
            private const int SAMPLE_CRIT           = 3;
            private const int SAMPLE_ENERGY         = 2;

            private static readonly int[] G_TrainingSample = { SAMPLE_ATTACK, SAMPLE_HEALTH, SAMPLE_DEFENSE, SAMPLE_CRIT, SAMPLE_ENERGY };

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
            motes.arraySize = MIST_LAYER_COUNT;
            SerializedProperty mote = motes.GetArrayElementAtIndex(0);
            mote.FindPropertyRelative("_graphic").objectReferenceValue = graphic;
            mote.FindPropertyRelative("_travel").vector2Value = Vector2.zero;
            mote.FindPropertyRelative("_duration").floatValue = MIST_DURATION;
            mote.FindPropertyRelative("_delay").floatValue = MIST_DELAY;
            mote.FindPropertyRelative("_minAlpha").floatValue = MIST_MIN_ALPHA;
            mote.FindPropertyRelative("_maxAlpha").floatValue = MIST_MAX_ALPHA;
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
            SetColor(feedback, "_hoverColor", Color.Lerp(normal, Color.white, HOVER_BLEND));
            SetColor(feedback, "_pressedColor", Color.Lerp(normal, Color.black, PRESSED_BLEND));
        }

        // 닫기 버튼은 9-슬라이스 배경이 아니라 아이콘 한 장을 그대로 보여준다
        private static Button CloseButton(RectTransform parent, float x, float y)
        {
            RectTransform rect = Rect(parent, "CloseButton", x, y, CLOSE_SIZE, CLOSE_SIZE);
            Image frame = rect.gameObject.AddComponent<Image>();
            frame.color = _controlColor;
            Border(rect, _borderColor, BORDER_LINE_WIDTH);

            Sprite iconSprite = AssetDatabase.LoadAssetAtPath<Sprite>(SKIN_FOLDER + "T_UiIcon_Close.png");
            RectTransform iconRect = Rect(rect, "Icon", CLOSE_ICON_X, CLOSE_ICON_Y, CLOSE_ICON_WIDTH, CLOSE_ICON_HEIGHT);
            Image icon = iconRect.gameObject.AddComponent<Image>();
            icon.sprite = iconSprite;
            icon.color = _mutedColor;
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
            text.font = bold ? _boldFont : _regularFont;
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
            RectTransform border = Rect(parent, "Border", BORDER_X, BORDER_Y, BORDER_WIDTH, BORDER_HEIGHT);
            Stretch(border);
            RectTransform top = Panel(border, "Top", ZERO_DIMENSION, ZERO_DIMENSION, ZERO_DIMENSION, width, color).rectTransform;
            top.anchorMax = Vector2.one;
            top.offsetMin = new Vector2(ZERO_DIMENSION, -width);
            top.offsetMax = Vector2.zero;
            RectTransform bottom = Panel(border, "Bottom", ZERO_DIMENSION, ZERO_DIMENSION, ZERO_DIMENSION, width, color).rectTransform;
            bottom.anchorMin = Vector2.zero;
            bottom.anchorMax = Vector2.right;
            bottom.offsetMin = Vector2.zero;
            bottom.offsetMax = new Vector2(ZERO_DIMENSION, width);
            RectTransform left = Panel(border, "Left", ZERO_DIMENSION, ZERO_DIMENSION, width, ZERO_DIMENSION, color).rectTransform;
            left.anchorMin = Vector2.zero;
            left.anchorMax = Vector2.up;
            left.offsetMin = Vector2.zero;
            left.offsetMax = new Vector2(width, ZERO_DIMENSION);
            RectTransform right = Panel(border, "Right", ZERO_DIMENSION, ZERO_DIMENSION, width, ZERO_DIMENSION, color).rectTransform;
            right.anchorMin = Vector2.right;
            right.anchorMax = Vector2.one;
            right.offsetMin = new Vector2(-width, ZERO_DIMENSION);
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
            rect.pivot = new Vector2(HALF, HALF);
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
            TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(source, FONT_POINT_SIZE, FONT_PADDING, GlyphRenderMode.SDFAA,
                FONT_ATLAS_SIZE, FONT_ATLAS_SIZE, AtlasPopulationMode.Dynamic, true);
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
            _regularFont.TryAddCharacters(characters.ToString());
            _boldFont.TryAddCharacters(characters.ToString());
            EditorUtility.SetDirty(_regularFont);
            EditorUtility.SetDirty(_boldFont);
        }
    }
}
#endif
