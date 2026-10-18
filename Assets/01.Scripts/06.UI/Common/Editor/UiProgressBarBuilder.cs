#if UNITY_EDITOR
// System
using System.IO;

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


namespace Shinshi.UI.Editor
{
    /// <summary> 진행 막대 프리팹 세 종류와 시험 씬 Dev_ProgressBar를 만든다 </summary>
    public static class UiProgressBarBuilder
    {
        // 막대 모양은 일반전 진행 막대(DungeonProgressView)의 틀, 채움, 끝 번쩍임 그림을 그대로 쓴다.
        // 다시 실행하면 프리팹과 시험 씬을 통째로 새로 쓰므로 손으로 고친 내용은 사라진다. 출시 씬에는 넣지 않는다

        /****************************************
        *                Fields
        ****************************************/

        public const string PREFAB_FOLDER  = "Assets/02.Prefab/UI/Common";
        public const string LOADING_PREFAB = PREFAB_FOLDER + "/PF_ProgressBar_Loading.prefab";
        public const string ACTION_PREFAB  = PREFAB_FOLDER + "/PF_ProgressBar_Action.prefab";
        public const string SKILL_PREFAB   = PREFAB_FOLDER + "/PF_ProgressBar_Skill.prefab";
        public const string SCENE_PATH     = "Assets/00.Scenes/Dev_HUD/Dev_ProgressBar.unity";

        private const string FRAME_SPRITE  = "Assets/01.Scripts/05.Art/UI/DungeonProgress/Textures/T_DungeonProgress_FrameSimple.png";
        private const string EDGE_SPRITE   = "Assets/01.Scripts/05.Art/UI/DungeonProgress/Textures/T_DungeonProgress_EdgeGold.png";
        private const string FILL_SPRITE   = "Assets/01.Scripts/05.Art/UI/GameStyle/Textures/T_UiGauge_Fill.png";
        private const string BUTTON_SPRITE = "Assets/01.Scripts/05.Art/UI/GameStyle/Textures/T_UiButton_Secondary.png";
        private const string FONT_REGULAR  = "Assets/01.Scripts/06.UI/Hud/Fonts/HudQuestKorean-Regular.asset";
        private const string FONT_BOLD     = "Assets/01.Scripts/06.UI/Hud/Fonts/HudQuestKorean-Bold.asset";
        private const string UI_LAYER      = "UI";

        private const float TITLE_BLOCK     = 30f;
        private const float FRAME_INSET     = 6f;
        private const float SWEEP_INSET     = 3f;
        private const float SWEEP_WIDTH     = 0.28f;
        private const float EDGE_WIDTH      = 24f;
        private const float EDGE_HEIGHT_RATIO = 1.9f;
        private const float PERCENT_WIDTH   = 48f;
        private const float PERCENT_HEIGHT  = 20f;
        private const float PERCENT_GAP     = 6f;
        private const float DETAIL_SHARE    = 0.3f;
        private const float BUTTON_WIDTH    = 150f;
        private const float BUTTON_HEIGHT   = 44f;
        private const float BUTTON_GAP      = 12f;
        private const float REFERENCE_WIDTH  = 1920f;
        private const float REFERENCE_HEIGHT = 1080f;

        private static readonly Color G_ActiveGold  = new Color(0.725f, 0.553f, 0.243f, 1f);
        private static readonly Color G_DoneGold    = new Color(0.922f, 0.788f, 0.478f, 1f);
        private static readonly Color G_SkillActive = new Color(0.42f, 0.74f, 0.9f, 1f);
        private static readonly Color G_SkillDone   = new Color(0.8f, 0.95f, 1f, 1f);
        private static readonly Color G_Failed      = new Color(0.86f, 0.42f, 0.38f, 1f);
        private static readonly Color G_Text        = new Color(0.92f, 0.94f, 0.97f, 1f);
        private static readonly Color G_Muted       = new Color(0.6f, 0.64f, 0.7f, 1f);
        private static readonly Color G_Background  = new Color(0.05f, 0.07f, 0.1f, 1f);

        private static TMP_FontAsset G_Regular;
        private static TMP_FontAsset G_Bold;

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem("Shinshi/UI/Progress Bar/Build Prefabs And Test Scene")]
        public static void BuildAll()
        {
            LoadFonts();
            Directory.CreateDirectory(Path.Combine(Directory.GetParent(Application.dataPath).FullName, PREFAB_FOLDER));
            SavePrefab(LOADING_PREFAB, BuildBar("PF_ProgressBar_Loading", 560f, 22f, true, true, true, G_ActiveGold, G_DoneGold));
            SavePrefab(ACTION_PREFAB, BuildBar("PF_ProgressBar_Action", 340f, 22f, true, true, true, G_ActiveGold, G_DoneGold));
            SavePrefab(SKILL_PREFAB, BuildBar("PF_ProgressBar_Skill", 260f, 14f, true, false, false, G_SkillActive, G_SkillDone));
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            BuildScene();
        }

        private static void LoadFonts()
        {
            G_Regular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_REGULAR);
            G_Bold    = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_BOLD);
        }

        private static void SavePrefab(string path, GameObject root)
        {
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }

        // 계층: 뿌리(CanvasGroup, UiProgressBar) -> 제목, 세부 글자, 틀(Gauge) -> 채움 -> 끝 번쩍임, 퍼센트, 틀 안의 무한 대기 띠
        private static GameObject BuildBar(string name, float width, float barHeight, bool hasTitle, bool hasDetail, bool hasPercent,
            Color activeColor, Color doneColor)
        {
            float height = barHeight + (hasTitle ? TITLE_BLOCK : 0f);
            var rootObject = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup), typeof(UiProgressBar));
            rootObject.layer = LayerMask.NameToLayer(UI_LAYER);
            var root = (RectTransform)rootObject.transform;
            root.pivot     = new Vector2(0f, 1f);
            root.sizeDelta = new Vector2(width, height);

            TMP_Text title = hasTitle ? CreateText(root, "Title", G_Bold, 17f, G_Text, TextAlignmentOptions.TopLeft) : null;
            if (title != null)
            {
                Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(width * (1f - DETAIL_SHARE), TITLE_BLOCK));
            }

            TMP_Text detail = hasDetail ? CreateText(root, "Detail", G_Regular, 14f, G_Muted, TextAlignmentOptions.TopRight) : null;
            if (detail != null)
            {
                Anchor(detail.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(width * DETAIL_SHARE, TITLE_BLOCK));
            }

            var gaugeObject = new GameObject("Gauge", typeof(RectTransform), typeof(Image));
            gaugeObject.layer = rootObject.layer;
            var gauge = (RectTransform)gaugeObject.transform;
            gauge.SetParent(root, false);
            Anchor(gauge, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, barHeight));
            var frameImage = gaugeObject.GetComponent<Image>();
            frameImage.sprite        = AssetDatabase.LoadAssetAtPath<Sprite>(FRAME_SPRITE);
            frameImage.type          = Image.Type.Sliced;
            frameImage.raycastTarget = false;

            var fillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillObject.layer = rootObject.layer;
            var fill = (RectTransform)fillObject.transform;
            fill.SetParent(gauge, false);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.sizeDelta = new Vector2(-FRAME_INSET, -FRAME_INSET);
            var fillImage = fillObject.GetComponent<Image>();
            fillImage.sprite        = AssetDatabase.LoadAssetAtPath<Sprite>(FILL_SPRITE);
            fillImage.type          = Image.Type.Filled;
            fillImage.fillMethod    = Image.FillMethod.Horizontal;
            fillImage.fillOrigin    = (int)Image.OriginHorizontal.Left;
            fillImage.fillAmount    = 0f;
            fillImage.color         = activeColor;
            fillImage.raycastTarget = false;

            var edgeObject = new GameObject("EdgeGlow", typeof(RectTransform), typeof(Image));
            edgeObject.layer = rootObject.layer;
            var edge = (RectTransform)edgeObject.transform;
            edge.SetParent(fill, false);
            Anchor(edge, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(EDGE_WIDTH, barHeight * EDGE_HEIGHT_RATIO));
            var edgeImage = edgeObject.GetComponent<Image>();
            edgeImage.sprite        = AssetDatabase.LoadAssetAtPath<Sprite>(EDGE_SPRITE);
            edgeImage.color         = new Color(1f, 1f, 1f, 0.55f);
            edgeImage.raycastTarget = false;

            TMP_Text percent = CreateText(fill, "Percent", G_Bold, 13f, G_Text, TextAlignmentOptions.MidlineLeft);
            Anchor(percent.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(PERCENT_WIDTH, PERCENT_HEIGHT));
            percent.rectTransform.anchoredPosition = new Vector2(PERCENT_GAP, 0f);
            percent.gameObject.SetActive(hasPercent);

            var sweepObject = new GameObject("Sweep", typeof(RectTransform), typeof(Image));
            sweepObject.layer = rootObject.layer;
            var sweep = (RectTransform)sweepObject.transform;
            sweep.SetParent(gauge, false);
            sweep.anchorMin = Vector2.zero;
            sweep.anchorMax = new Vector2(SWEEP_WIDTH, 1f);
            sweep.offsetMin = new Vector2(SWEEP_INSET, SWEEP_INSET);
            sweep.offsetMax = new Vector2(-SWEEP_INSET, -SWEEP_INSET);
            var sweepImage = sweepObject.GetComponent<Image>();
            sweepImage.sprite        = AssetDatabase.LoadAssetAtPath<Sprite>(FILL_SPRITE);
            sweepImage.color         = new Color(activeColor.r, activeColor.g, activeColor.b, 0.7f);
            sweepImage.raycastTarget = false;
            sweepObject.SetActive(false);

            var gaugeLabel = gaugeObject.AddComponent<UiGaugeFollowLabel>();
            var gaugeSerialized = new SerializedObject(gaugeLabel);
            gaugeSerialized.FindProperty("_fill").objectReferenceValue      = fillImage;
            gaugeSerialized.FindProperty("_label").objectReferenceValue     = percent.rectTransform;
            gaugeSerialized.FindProperty("_labelText").objectReferenceValue = percent;
            gaugeSerialized.FindProperty("_edgeGlow").objectReferenceValue  = edgeImage;
            gaugeSerialized.ApplyModifiedPropertiesWithoutUndo();

            var bar = rootObject.GetComponent<UiProgressBar>();
            var barSerialized = new SerializedObject(bar);
            barSerialized.FindProperty("_gauge").objectReferenceValue       = gaugeLabel;
            barSerialized.FindProperty("_sweep").objectReferenceValue       = sweep;
            barSerialized.FindProperty("_percentRoot").objectReferenceValue = percent.gameObject;
            barSerialized.FindProperty("_title").objectReferenceValue       = title;
            barSerialized.FindProperty("_detail").objectReferenceValue      = detail;
            barSerialized.FindProperty("_group").objectReferenceValue       = rootObject.GetComponent<CanvasGroup>();
            barSerialized.FindProperty("_activeColor").colorValue           = activeColor;
            barSerialized.FindProperty("_completedColor").colorValue        = doneColor;
            barSerialized.FindProperty("_failedColor").colorValue           = G_Failed;
            barSerialized.ApplyModifiedPropertiesWithoutUndo();
            return rootObject;
        }

        private static void BuildScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags      = CameraClearFlags.SolidColor;
            camera.backgroundColor = G_Background;
            camera.orthographic    = true;

            var canvasObject = new GameObject("ProgressBarCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.layer = LayerMask.NameToLayer(UI_LAYER);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(REFERENCE_WIDTH, REFERENCE_HEIGHT);
            scaler.matchWidthOrHeight  = 0.5f;
            var canvasRect = (RectTransform)canvasObject.transform;

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            CreateHeading(canvasRect, "화면 제목", "프로그레스바 테스트", 28f, new Vector2(60f, -40f));

            var loadingBar = PlaceBar(canvasRect, LOADING_PREFAB, new Vector2(60f, -200f));
            CreateHeading(canvasRect, "로딩 제목", "1. 로딩, 통신 - 단계로 오르는 값과 무한 대기", 20f, new Vector2(60f, -150f));
            Button step = CreateButton(canvasRect, "단계 진행", new Vector2(700f, -208f), 0, out _);
            Button indeterminate = CreateButton(canvasRect, "무한 대기", new Vector2(700f, -208f), 1, out _);
            Button fail = CreateButton(canvasRect, "실패", new Vector2(700f, -208f), 2, out _);
            Button reset = CreateButton(canvasRect, "처음부터", new Vector2(700f, -208f), 3, out _);

            var actionBar = PlaceBar(canvasRect, ACTION_PREFAB, new Vector2(60f, -450f));
            CreateHeading(canvasRect, "행동 제목", "2. 시간이 걸리는 행동 - 채집, 시전, 제작", 20f, new Vector2(60f, -400f));
            Button start = CreateButton(canvasRect, "시작 3초", new Vector2(700f, -458f), 0, out _);
            Button cancel = CreateButton(canvasRect, "취소", new Vector2(700f, -458f), 1, out _);
            Button loop = CreateButton(canvasRect, "반복 끔", new Vector2(700f, -458f), 2, out TMP_Text loopLabel);

            var skillBar = PlaceBar(canvasRect, SKILL_PREFAB, new Vector2(60f, -700f));
            CreateHeading(canvasRect, "스킬 제목", "3. 스킬 게이지 - 충전은 차오르고 채널링은 줄어든다", 20f, new Vector2(60f, -650f));
            Button charge = CreateButton(canvasRect, "충전 1.5초", new Vector2(700f, -708f), 0, out _);
            Button channel = CreateButton(canvasRect, "채널링 2초", new Vector2(700f, -708f), 1, out _);
            Button breakButton = CreateButton(canvasRect, "끊기", new Vector2(700f, -708f), 2, out _);

            var demoObject = new GameObject("ProgressBarPreview", typeof(ProgressBarPreviewBinder));
            var demo = new SerializedObject(demoObject.GetComponent<ProgressBarPreviewBinder>());
            demo.FindProperty("_loadingBar").objectReferenceValue = loadingBar;
            demo.FindProperty("_actionBar").objectReferenceValue  = actionBar;
            demo.FindProperty("_skillBar").objectReferenceValue   = skillBar;
            demo.FindProperty("_loadingStepButton").objectReferenceValue          = step;
            demo.FindProperty("_loadingIndeterminateButton").objectReferenceValue = indeterminate;
            demo.FindProperty("_loadingFailButton").objectReferenceValue          = fail;
            demo.FindProperty("_loadingResetButton").objectReferenceValue         = reset;
            demo.FindProperty("_actionStartButton").objectReferenceValue  = start;
            demo.FindProperty("_actionCancelButton").objectReferenceValue = cancel;
            demo.FindProperty("_actionLoopButton").objectReferenceValue   = loop;
            demo.FindProperty("_actionLoopLabel").objectReferenceValue    = loopLabel;
            demo.FindProperty("_skillChargeButton").objectReferenceValue  = charge;
            demo.FindProperty("_skillChannelButton").objectReferenceValue = channel;
            demo.FindProperty("_skillBreakButton").objectReferenceValue   = breakButton;
            demo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, SCENE_PATH);
        }

        private static UiProgressBar PlaceBar(RectTransform parent, string prefabPath, Vector2 position)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            var rect = (RectTransform)instance.transform;
            rect.anchorMin        = rect.anchorMax = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            return instance.GetComponent<UiProgressBar>();
        }

        private static void CreateHeading(RectTransform parent, string objectName, string text, float size, Vector2 position)
        {
            TMP_Text label = CreateText(parent, objectName, G_Bold, size, G_Text, TextAlignmentOptions.TopLeft);
            Anchor(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(900f, size + 10f));
            label.rectTransform.anchoredPosition = position;
            label.text = text;
        }

        private static Button CreateButton(RectTransform parent, string text, Vector2 origin, int index, out TMP_Text label)
        {
            var buttonObject = new GameObject("Button_" + text, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.layer = LayerMask.NameToLayer(UI_LAYER);
            var rect = (RectTransform)buttonObject.transform;
            rect.SetParent(parent, false);
            Anchor(rect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(BUTTON_WIDTH, BUTTON_HEIGHT));
            rect.anchoredPosition = origin + new Vector2(index * (BUTTON_WIDTH + BUTTON_GAP), 0f);
            var image = buttonObject.GetComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BUTTON_SPRITE);
            image.type   = Image.Type.Sliced;
            buttonObject.GetComponent<Button>().targetGraphic = image;

            label = CreateText(rect, "Label", G_Regular, 16f, G_Text, TextAlignmentOptions.Center);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            label.text = text;
            return buttonObject.GetComponent<Button>();
        }

        private static TMP_Text CreateText(RectTransform parent, string objectName, TMP_FontAsset font, float size, Color color,
            TextAlignmentOptions alignment)
        {
            var textObject = new GameObject(objectName, typeof(RectTransform));
            textObject.layer = LayerMask.NameToLayer(UI_LAYER);
            var rect = (RectTransform)textObject.transform;
            rect.SetParent(parent, false);
            var text = textObject.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                text.font = font;
            }

            text.fontSize           = size;
            text.color              = color;
            text.alignment          = alignment;
            text.raycastTarget      = false;
            text.textWrappingMode   = TextWrappingModes.NoWrap;
            return text;
        }

        private static void Anchor(RectTransform rect, Vector2 min, Vector2 max, Vector2 pivot, Vector2 size)
        {
            rect.anchorMin        = min;
            rect.anchorMax        = max;
            rect.pivot            = pivot;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta        = size;
        }
    }
}
#endif
