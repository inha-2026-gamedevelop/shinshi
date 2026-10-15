#if UNITY_EDITOR
// System
using System;
using System.IO;
using System.Text;

// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Data;
using Shinshi.UI;

namespace Shinshi.Editor
{
    /// <summary> Dev_EquipmentEnhance 씬에 장비 강화 창을 구성하고 재사용 프리팹으로 저장한다 </summary>
    public static class EquipmentEnhanceWindowBuilder
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SCENE_PATH     = "Assets/00.Scenes/Dev_HUD/Dev_EquipmentEnhance.unity";
        private const string PREFAB_FOLDER  = "Assets/02.Prefab/UI/EquipmentEnhance";
        private const string PREFAB_PATH    = PREFAB_FOLDER + "/PF_EquipmentEnhanceWindow.prefab";
        private const string CATALOG_PATH   = "Assets/09.Data/Growth/EquipmentCatalog.asset";
        private const string SKIN_FOLDER    = "Assets/01.Scripts/05.Art/UI/GameStyle/Textures/";
        private const string ICON_FOLDER    = "Assets/01.Scripts/05.Art/UI/Icons/Textures/";
        private const string ESSENCE_ICON   = ICON_FOLDER + "Currency/T_Icon_Currency_Essence_White.png";
        private const string SHARD_ICON     = ICON_FOLDER + "T_Icon_item_refine_shard.png";
        private const string CHEVRON_ICON   = SKIN_FOLDER + "T_UiIcon_Chevron.png";

        // 셰브런 원본은 아래를 가리킨다. 오른쪽(현재 -> 강화 후)을 가리키도록 돌린다
        private const float CHEVRON_RIGHT_ANGLE = 90f;

        // 신체 강화 창과 같은 나눔고딕 동적 TMP 폰트를 함께 쓴다
        private const string REGULAR_FONT_PATH = "Assets/01.Scripts/06.UI/Training/Fonts/TrainingKorean-Regular.asset";
        private const string BOLD_FONT_PATH    = "Assets/01.Scripts/06.UI/Training/Fonts/TrainingKorean-Bold.asset";

        private const string CANVAS_NAME = "EquipmentEnhanceCanvas";
        private const string ROOT_NAME   = "EquipmentEnhanceWindowRoot";
        private const string HELPER_NAME = "EquipmentEnhancePreviewHelper";

        private const float WINDOW_WIDTH  = 1140f;
        private const float WINDOW_HEIGHT = 720f;
        private const float HEADER_HEIGHT = 72f;

        private const float LEFT_X      = 24f;
        private const float LEFT_Y      = 96f;
        private const float LEFT_WIDTH  = 372f;
        private const float LEFT_HEIGHT = 596f;

        private const float RIGHT_X      = 416f;
        private const float RIGHT_Y      = 96f;
        private const float RIGHT_WIDTH  = 700f;
        private const float RIGHT_HEIGHT = 596f;

        private const float ROW_TOP    = 60f;
        private const float ROW_HEIGHT = 64f;
        private const float ROW_GAP    = 7f;

        private const float STAT_TOP      = 346f;
        private const float STAT_ROW_STEP = 30f;

        private const float REFERENCE_WIDTH       = 1920f;
        private const float REFERENCE_HEIGHT      = 1080f;
        private const float MATCH_WIDTH_OR_HEIGHT = 0.5f;

        private const int SLOT_COUNT     = 6;
        private const int STAT_ROW_COUNT = 3;

        private static readonly Color G_Window  = new Color(0.065f, 0.083f, 0.102f, 1f);
        private static readonly Color G_Control = new Color(0.055f, 0.075f, 0.095f, 1f);
        private static readonly Color G_Row     = new Color(0.085f, 0.105f, 0.13f, 1f);
        private static readonly Color G_Border  = new Color(0.29f, 0.32f, 0.34f, 1f);
        private static readonly Color G_Gold    = new Color(0.68f, 0.6f, 0.43f, 1f);
        private static readonly Color G_Accent  = new Color(0.84f, 0.75f, 0.57f, 1f);
        private static readonly Color G_Text    = new Color(0.9f, 0.92f, 0.92f, 1f);
        private static readonly Color G_Muted   = new Color(0.64f, 0.69f, 0.71f, 1f);
        private static readonly Color G_Shade   = new Color(0.02f, 0.03f, 0.04f, 0.95f);

        // EEquipmentSlot, EItemGrade 순서
        private static readonly string[] G_SlotKeys   = { "Weapon", "Head", "Body", "Hands", "Feet", "Accessory" };
        private static readonly string[] G_SlotIcons  = { "weapon", "head", "body", "hands", "feet", "accessory" };
        private static readonly string[] G_GradeSkins = { "Common", "Rare", "Epic", "Legendary" };

        private static TMP_FontAsset        G_Regular;
        private static TMP_FontAsset        G_Bold;
        private static EquipmentEnhanceView G_View;

        /****************************************
        *                 Menu
        ****************************************/

        [MenuItem("Shinshi/UI/장비 강화 창 만들기")]
        private static void BuildMenu()
        {
            Build();
        }

        /// <summary> Dev_EquipmentEnhance 씬에 장비 강화 창을 구성하고 프리팹으로 저장한다, 결과 요약을 돌려준다 </summary>
        public static string Build()
        {
            ValidateContext();

            GameObject canvasObject = GameObject.Find(CANVAS_NAME);
            if (canvasObject == null)
            {
                throw new InvalidOperationException("EquipmentEnhanceCanvas를 찾을 수 없다");
            }

            ConfigureCanvasScaler(canvasObject);
            G_Regular = LoadAsset<TMP_FontAsset>(REGULAR_FONT_PATH);
            G_Bold    = LoadAsset<TMP_FontAsset>(BOLD_FONT_PATH);

            RectTransform canvas   = (RectTransform)canvasObject.transform;
            GameObject    existing = GameObject.Find(ROOT_NAME);
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
            }

            RectTransform window = Rect(canvas, ROOT_NAME, 0f, 0f, WINDOW_WIDTH, WINDOW_HEIGHT);
            window.anchorMin        = new Vector2(0.5f, 0.5f);
            window.anchorMax        = new Vector2(0.5f, 0.5f);
            window.pivot            = new Vector2(0.5f, 0.5f);
            window.anchoredPosition = Vector2.zero;
            window.localScale       = Vector3.one;

            // ExecuteAlways인 뷰는 AddComponent 즉시 Awake가 돈다. 참조를 다 연결할 때까지 비활성으로 둔다
            window.gameObject.SetActive(false);
            G_View = window.gameObject.AddComponent<EquipmentEnhanceView>();

            BuildFrame(window);
            BuildHeader(window);
            BuildLeftPanel(window);
            RectTransform right = BuildRightPanel(window);
            BuildResultOverlay(right);
            AssignSprites();

            PreloadCharacters(window);
            window.gameObject.SetActive(true);

            EnsureFolder(PREFAB_FOLDER);
            PrefabUtility.SaveAsPrefabAsset(window.gameObject, PREFAB_PATH);

            PreviewHelper.CreateOrUpdate(window.gameObject, G_View);
            PreloadCharacters(window);

            UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssetIfDirty(G_Regular);
            AssetDatabase.SaveAssetIfDirty(G_Bold);
            Selection.activeGameObject = window.gameObject;

            string result = $"EquipmentEnhanceWindowBuilder: {PREFAB_PATH} 저장, 창 {WINDOW_WIDTH}x{WINDOW_HEIGHT}, 루트 {ROOT_NAME}";
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
                throw new InvalidOperationException("장비 강화 창 제작은 편집 모드에서 실행한다");
            }

            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != SCENE_PATH)
            {
                throw new InvalidOperationException("Dev_EquipmentEnhance 씬을 열고 실행한다");
            }
        }

        private static void ConfigureCanvasScaler(GameObject canvasObject)
        {
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                throw new InvalidOperationException("EquipmentEnhanceCanvas에 CanvasScaler가 없다");
            }

            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(REFERENCE_WIDTH, REFERENCE_HEIGHT);
            scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight  = MATCH_WIDTH_OR_HEIGHT;
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

            Text(Rect(window, "Title", 32f, 20f, 200f, 32f), "장비 강화", 30f, G_Text, true, TextAlignmentOptions.Left);
            Text(Rect(window, "Subtitle", 200f, 30f, 260f, 20f), "장인 담우의 대장간", 14f, G_Muted, false, TextAlignmentOptions.Left);
            GameObject previewTag = Text(Rect(window, "PreviewTag", 380f, 30f, 120f, 20f), "미리보기", 13f, G_Gold, false,
                TextAlignmentOptions.Left).gameObject;

            RectTransform dragArea = Rect(window, "DragSurface", 0f, 0f, WINDOW_WIDTH - 96f, HEADER_HEIGHT);
            Image dragImage = dragArea.gameObject.AddComponent<Image>();
            dragImage.color         = Color.clear;
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
            RectTransform panel = InsetPanel(window, "LeftPanel", LEFT_X, LEFT_Y, LEFT_WIDTH, LEFT_HEIGHT);
            Text(Rect(panel, "LeftTitle", 20f, 18f, 200f, 26f), "착용 장비", 20f, G_Gold, true, TextAlignmentOptions.Left);
            Text(Rect(panel, "LeftHint", 180f, 22f, 172f, 20f), "부위를 눌러 선택", 13f, G_Muted, false, TextAlignmentOptions.Right);

            var buttons    = new Button[SLOT_COUNT];
            var frames     = new Image[SLOT_COUNT];
            var icons      = new Image[SLOT_COUNT];
            var names      = new TMP_Text[SLOT_COUNT];
            var levels     = new TMP_Text[SLOT_COUNT];
            var selections = new GameObject[SLOT_COUNT];
            float rowWidth = LEFT_WIDTH - 40f;
            for (int i = 0; i < SLOT_COUNT; ++i)
            {
                float y = ROW_TOP + ((ROW_HEIGHT + ROW_GAP) * i);
                RectTransform row = Rect(panel, "Slot" + G_SlotKeys[i], 20f, y, rowWidth, ROW_HEIGHT);
                Image rowImage = row.gameObject.AddComponent<Image>();
                rowImage.color = G_Row;
                Button button = row.gameObject.AddComponent<Button>();
                button.targetGraphic = rowImage;
                button.transition    = Selectable.Transition.None;
                DisableNavigation(button);
                AddFeedback(row.gameObject, rowImage, G_Row);

                Image selection = Rect(row, "Selection", 0f, 0f, rowWidth, ROW_HEIGHT).gameObject.AddComponent<Image>();
                Skin(selection, "T_UiSelect_Outline", G_Accent);
                selection.raycastTarget = false;

                Image frame = Rect(row, "GradeFrame", 8f, 6f, 52f, 52f).gameObject.AddComponent<Image>();
                frame.raycastTarget = false;
                Image icon = Rect((RectTransform)frame.transform, "Icon", 6f, 6f, 40f, 40f).gameObject.AddComponent<Image>();
                icon.preserveAspect = true;
                icon.raycastTarget  = false;

                TMP_Text name  = Text(Rect(row, "Name", 74f, 12f, 190f, 40f), string.Empty, 17f, G_Text, true, TextAlignmentOptions.Left);
                TMP_Text level = Text(Rect(row, "Level", rowWidth - 82f, 12f, 70f, 40f), string.Empty, 22f, G_Accent, true,
                    TextAlignmentOptions.Right);

                buttons[i]    = button;
                frames[i]     = frame;
                icons[i]      = icon;
                names[i]      = name;
                levels[i]     = level;
                selections[i] = selection.gameObject;
            }

            Panel(panel, "FooterDivider", 20f, 494f, rowWidth, 1f, G_Border);
            TMP_Text essence = Balance(panel, "Essence", 510f, ESSENCE_ICON, "보유 정기");
            TMP_Text shard   = Balance(panel, "Shard", 548f, SHARD_ICON, "보유 정련편");

            SetReference(G_View, "_slotButtons", buttons);
            SetReference(G_View, "_slotFrames", frames);
            SetReference(G_View, "_slotIcons", icons);
            SetReference(G_View, "_slotNameTexts", names);
            SetReference(G_View, "_slotLevelTexts", levels);
            SetReference(G_View, "_slotSelections", selections);
            SetReference(G_View, "_essenceText", essence);
            SetReference(G_View, "_shardText", shard);
        }

        private static TMP_Text Balance(RectTransform panel, string key, float y, string iconPath, string caption)
        {
            Image icon = Rect(panel, key + "Icon", 20f, y, 28f, 30f).gameObject.AddComponent<Image>();
            icon.sprite         = LoadAsset<Sprite>(iconPath);
            icon.color          = G_Accent;
            icon.preserveAspect = true;
            icon.raycastTarget  = false;
            Text(Rect(panel, key + "Caption", 58f, y, 140f, 30f), caption, 16f, G_Muted, false, TextAlignmentOptions.Left);
            return Text(Rect(panel, key + "Text", 180f, y, 172f, 30f), string.Empty, 22f, G_Gold, true, TextAlignmentOptions.Right);
        }

        /****************************************
        *              Right Panel
        ****************************************/

        private static RectTransform BuildRightPanel(RectTransform window)
        {
            RectTransform panel = InsetPanel(window, "RightPanel", RIGHT_X, RIGHT_Y, RIGHT_WIDTH, RIGHT_HEIGHT);
            BuildItemBlock(panel);
            Panel(panel, "DividerItem", 28f, 148f, RIGHT_WIDTH - 56f, 1f, G_Border);
            BuildRateBlock(panel);
            Panel(panel, "DividerRate", 28f, 304f, RIGHT_WIDTH - 56f, 1f, G_Border);
            BuildStatBlock(panel);
            Panel(panel, "DividerStat", 28f, 446f, RIGHT_WIDTH - 56f, 1f, G_Border);
            BuildCostBlock(panel);
            return panel;
        }

        private static void BuildItemBlock(RectTransform panel)
        {
            RectTransform glow = Rect(panel, "ItemGlow", 4f, -4f, 152f, 152f);
            Image glowImage = glow.gameObject.AddComponent<Image>();
            glowImage.sprite        = LoadAsset<Sprite>(SKIN_FOLDER + "T_UiFx_Glow.png");
            glowImage.color         = new Color(G_Gold.r, G_Gold.g, G_Gold.b, 0.22f);
            glowImage.raycastTarget = false;

            Image frame = Rect(panel, "ItemFrame", 28f, 20f, 104f, 104f).gameObject.AddComponent<Image>();
            frame.raycastTarget = false;
            Border((RectTransform)frame.transform, new Color(G_Gold.r, G_Gold.g, G_Gold.b, 0.6f), 1f);
            Image icon = Rect((RectTransform)frame.transform, "Icon", 14f, 14f, 76f, 76f).gameObject.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget  = false;

            TMP_Text name  = Text(Rect(panel, "ItemName", 152f, 34f, 290f, 36f), string.Empty, 26f, G_Text, true, TextAlignmentOptions.Left);
            TMP_Text grade = Text(Rect(panel, "ItemGrade", 152f, 76f, 290f, 22f), string.Empty, 15f, G_Muted, false, TextAlignmentOptions.Left);

            TMP_Text from = Text(Rect(panel, "LevelFrom", 440f, 44f, 92f, 56f), string.Empty, 34f, G_Muted, true, TextAlignmentOptions.Right);
            GameObject arrow = Arrow(panel, "LevelArrow", 540f, 56f, 32f);
            TMP_Text to = Text(Rect(panel, "LevelTo", 580f, 40f, 100f, 64f), string.Empty, 44f, G_Accent, true, TextAlignmentOptions.Left);

            SetReference(G_View, "_itemFrame", frame);
            SetReference(G_View, "_itemIcon", icon);
            SetReference(G_View, "_itemNameText", name);
            SetReference(G_View, "_itemGradeText", grade);
            SetReference(G_View, "_levelFromText", from);
            SetReference(G_View, "_levelToText", to);
            SetReference(G_View, "_levelArrow", arrow);
        }

        private static void BuildRateBlock(RectTransform panel)
        {
            TMP_Text caption = Text(Rect(panel, "RateCaption", 28f, 166f, 270f, 22f), string.Empty, 15f, G_Muted, false, TextAlignmentOptions.Left);
            TMP_Text rate    = Text(Rect(panel, "RateText", 28f, 188f, 270f, 96f), string.Empty, 64f, G_Text, true, TextAlignmentOptions.Left);

            Text(Rect(panel, "PityTitle", 320f, 172f, 352f, 24f), "실패 보정", 17f, G_Gold, true, TextAlignmentOptions.Left);
            Image gaugeFrame = Panel(panel, "PityGaugeFrame", 320f, 206f, 352f, 20f, Color.white);
            Skin(gaugeFrame, "T_UiGauge_Frame", new Color(0.75f, 0.8f, 0.85f, 0.9f));
            Image fill = Rect((RectTransform)gaugeFrame.transform, "Fill", 4f, 4f, 344f, 12f).gameObject.AddComponent<Image>();
            fill.sprite        = LoadAsset<Sprite>(SKIN_FOLDER + "T_UiGauge_Fill.png");
            fill.type          = Image.Type.Filled;
            fill.fillMethod    = Image.FillMethod.Horizontal;
            fill.fillOrigin    = (int)Image.OriginHorizontal.Left;
            fill.color         = G_Accent;
            fill.raycastTarget = false;
            TMP_Text pity = Text(Rect(panel, "PityText", 320f, 236f, 352f, 52f), string.Empty, 15f, G_Muted, false, TextAlignmentOptions.TopLeft);

            SetReference(G_View, "_rateCaption", caption);
            SetReference(G_View, "_rateText", rate);
            SetReference(G_View, "_pityFill", fill);
            SetReference(G_View, "_pityText", pity);
        }

        private static void BuildStatBlock(RectTransform panel)
        {
            Text(Rect(panel, "StatTitle", 28f, 316f, 200f, 24f), "장비 능력치", 17f, G_Gold, true, TextAlignmentOptions.Left);
            Text(Rect(panel, "StatBeforeCaption", 200f, 318f, 120f, 20f), "현재", 13f, G_Muted, false, TextAlignmentOptions.Right);
            Text(Rect(panel, "StatAfterCaption", 380f, 318f, 160f, 20f), "강화 후", 13f, G_Muted, false, TextAlignmentOptions.Left);

            var rows    = new GameObject[STAT_ROW_COUNT];
            var names   = new TMP_Text[STAT_ROW_COUNT];
            var befores = new TMP_Text[STAT_ROW_COUNT];
            var afters  = new TMP_Text[STAT_ROW_COUNT];
            var arrows  = new GameObject[STAT_ROW_COUNT];
            for (int i = 0; i < STAT_ROW_COUNT; ++i)
            {
                RectTransform row = Rect(panel, "StatRow" + i, 28f, STAT_TOP + (STAT_ROW_STEP * i), RIGHT_WIDTH - 56f, 28f);
                names[i]   = Text(Rect(row, "Name", 0f, 0f, 150f, 28f), string.Empty, 17f, G_Muted, false, TextAlignmentOptions.Left);
                befores[i] = Text(Rect(row, "Before", 172f, 0f, 120f, 28f), string.Empty, 18f, G_Text, false, TextAlignmentOptions.Right);
                arrows[i]  = Arrow(row, "Arrow", 310f, 4f, 20f);
                afters[i]  = Text(Rect(row, "After", 352f, 0f, 290f, 28f), string.Empty, 18f, G_Accent, true, TextAlignmentOptions.Left);
                rows[i]    = row.gameObject;
            }

            SetReference(G_View, "_statRows", rows);
            SetReference(G_View, "_statNameTexts", names);
            SetReference(G_View, "_statBeforeTexts", befores);
            SetReference(G_View, "_statAfterTexts", afters);
            SetReference(G_View, "_statArrows", arrows);
        }

        private static void BuildCostBlock(RectTransform panel)
        {
            Text(Rect(panel, "CostTitle", 28f, 458f, 200f, 24f), "강화 비용", 17f, G_Gold, true, TextAlignmentOptions.Left);
            TMP_Text essenceCost = Cost(panel, "Essence", 28f, ESSENCE_ICON, "정기");
            TMP_Text shardCost   = Cost(panel, "Shard", 236f, SHARD_ICON, "정련편");

            TMP_Text note   = Text(Rect(panel, "NoteText", 28f, 528f, 450f, 22f), string.Empty, 14f, G_Muted, false, TextAlignmentOptions.Left);
            TMP_Text status = Text(Rect(panel, "StatusText", 28f, 552f, 450f, 26f), string.Empty, 15f, G_Text, false, TextAlignmentOptions.Left);

            Button enhance = Button(panel, "EnhanceButton", 500f, 516f, 172f, 56f, "T_UiButton_Primary");
            TMP_Text label = Text(Rect((RectTransform)enhance.transform, "Label", 0f, 0f, 172f, 56f), "강화", 24f, G_Accent, true);
            Border((RectTransform)enhance.transform, new Color(G_Gold.r, G_Gold.g, G_Gold.b, 0.6f), 1f);

            SetReference(G_View, "_essenceCostText", essenceCost);
            SetReference(G_View, "_shardCostText", shardCost);
            SetReference(G_View, "_noteText", note);
            SetReference(G_View, "_statusText", status);
            SetReference(G_View, "_enhanceButton", enhance);
            SetReference(G_View, "_enhanceLabel", label);
        }

        private static TMP_Text Cost(RectTransform panel, string key, float x, string iconPath, string caption)
        {
            Image icon = Rect(panel, key + "CostIcon", x, 490f, 26f, 28f).gameObject.AddComponent<Image>();
            icon.sprite         = LoadAsset<Sprite>(iconPath);
            icon.color          = G_Accent;
            icon.preserveAspect = true;
            icon.raycastTarget  = false;
            Text(Rect(panel, key + "CostCaption", x + 34f, 490f, 64f, 28f), caption, 16f, G_Muted, false, TextAlignmentOptions.Left);
            return Text(Rect(panel, key + "CostText", x + 96f, 488f, 96f, 32f), string.Empty, 22f, G_Text, true, TextAlignmentOptions.Left);
        }

        /****************************************
        *            Result Overlay
        ****************************************/

        private static void BuildResultOverlay(RectTransform panel)
        {
            RectTransform overlay = Rect(panel, "ResultOverlay", 0f, 150f, RIGHT_WIDTH, 160f);
            CanvasGroup group = overlay.gameObject.AddComponent<CanvasGroup>();
            group.alpha          = 0f;
            group.blocksRaycasts = false;
            group.interactable   = false;

            Image shade = Panel(overlay, "Shade", 0f, 0f, RIGHT_WIDTH, 160f, G_Shade);
            shade.raycastTarget = false;
            Image band = Panel(overlay, "Band", 0f, 0f, RIGHT_WIDTH, 160f, Color.white);
            band.sprite = LoadAsset<Sprite>(SKIN_FOLDER + "T_UiFx_TitleBand.png");
            band.color  = new Color(G_Gold.r, G_Gold.g, G_Gold.b, 0.35f);

            RectTransform badge = Rect(overlay, "Badge", 0f, 0f, RIGHT_WIDTH, 160f);
            TMP_Text title  = Text(Rect(badge, "Title", 0f, 30f, RIGHT_WIDTH, 64f), string.Empty, 48f, G_Accent, true);
            TMP_Text detail = Text(Rect(badge, "Detail", 0f, 98f, RIGHT_WIDTH, 30f), string.Empty, 18f, G_Text, false);

            SetReference(G_View, "_resultGroup", group);
            SetReference(G_View, "_resultBadge", badge);
            SetReference(G_View, "_resultTitleText", title);
            SetReference(G_View, "_resultDetailText", detail);
        }

        private static void AssignSprites()
        {
            var grades = new Sprite[G_GradeSkins.Length];
            for (int i = 0; i < G_GradeSkins.Length; ++i)
            {
                grades[i] = LoadAsset<Sprite>(SKIN_FOLDER + "T_UiSlot_Bg_" + G_GradeSkins[i] + ".png");
            }

            var slots = new Sprite[SLOT_COUNT];
            for (int i = 0; i < SLOT_COUNT; ++i)
            {
                slots[i] = LoadAsset<Sprite>(ICON_FOLDER + "T_Icon_equip_" + G_SlotIcons[i] + ".png");
            }

            SetReference(G_View, "_gradeFrames", grades);
            SetReference(G_View, "_slotSprites", slots);
        }

        /****************************************
        *           Scene Preview Helper
        ****************************************/

        // 미리보기 바인더는 프리팹과 수명이 다른 개발 씬의 별도 오브젝트에 둔다
        private static class PreviewHelper
        {
            public static void CreateOrUpdate(GameObject window, EquipmentEnhanceView view)
            {
                EquipmentCatalogSO catalog = LoadAsset<EquipmentCatalogSO>(CATALOG_PATH);

                GameObject helper = null;
                foreach (Transform child in window.transform.parent)
                {
                    if (child.name == HELPER_NAME)
                    {
                        helper = child.gameObject;
                        break;
                    }
                }

                if (helper == null)
                {
                    helper = new GameObject(HELPER_NAME);
                    helper.transform.SetParent(window.transform.parent, false);
                }

                helper.SetActive(false);
                if (!helper.TryGetComponent(out EquipmentEnhancePreviewBinder binder))
                {
                    binder = helper.AddComponent<EquipmentEnhancePreviewBinder>();
                }

                SetReference(binder, "_view", view);
                SetReference(binder, "_catalog", catalog);
                helper.SetActive(true);
            }
        }

        /****************************************
        *               Helpers
        ****************************************/

        private static RectTransform InsetPanel(RectTransform parent, string name, float x, float y, float width, float height)
        {
            RectTransform panel = Rect(parent, name, x, y, width, height);
            Image background = panel.gameObject.AddComponent<Image>();
            background.color = G_Control;
            Skin(background, "T_UiPanel_Inset", new Color(0.8f, 0.9f, 1f, 1f));
            Border(panel, new Color(G_Border.r, G_Border.g, G_Border.b, 0.5f), 1f);
            return panel;
        }

        private static Button Button(RectTransform parent, string name, float x, float y, float width, float height, string skin)
        {
            RectTransform rect = Rect(parent, name, x, y, width, height);
            Image image = rect.gameObject.AddComponent<Image>();
            Skin(image, skin, Color.white);

            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition    = Selectable.Transition.None;
            DisableNavigation(button);
            AddFeedback(rect.gameObject, image, image.color);
            return button;
        }

        // 닫기 버튼은 9-슬라이스 배경이 아니라 아이콘 한 장을 그대로 보여준다
        private static Button CloseButton(RectTransform parent, float x, float y)
        {
            RectTransform rect = Rect(parent, "CloseButton", x, y, 36f, 36f);
            Image frame = rect.gameObject.AddComponent<Image>();
            frame.color = G_Control;
            Border(rect, G_Border, 1f);

            Image icon = Rect(rect, "Icon", 8f, 8f, 20f, 20f).gameObject.AddComponent<Image>();
            icon.sprite        = LoadAsset<Sprite>(SKIN_FOLDER + "T_UiIcon_Close.png");
            icon.color         = G_Muted;
            icon.raycastTarget = false;

            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = frame;
            button.transition    = Selectable.Transition.None;
            DisableNavigation(button);
            AddFeedback(rect.gameObject, icon, icon.color);
            return button;
        }

        // 화살표 문자 대신 셰브런 그림을 돌려 쓴다. 코드 문자열에 특수 기호를 넣지 않기 위해서다
        private static GameObject Arrow(RectTransform parent, string name, float x, float y, float size)
        {
            RectTransform rect = Rect(parent, name, x, y, size, size);
            rect.pivot            = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x + (size * 0.5f), -(y + (size * 0.5f)));
            rect.localRotation    = Quaternion.Euler(0f, 0f, CHEVRON_RIGHT_ANGLE);

            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite         = LoadAsset<Sprite>(CHEVRON_ICON);
            image.color          = G_Gold;
            image.preserveAspect = true;
            image.raycastTarget  = false;
            return rect.gameObject;
        }

        private static void DisableNavigation(Button button)
        {
            Navigation navigation = button.navigation;
            navigation.mode   = Navigation.Mode.None;
            button.navigation = navigation;
        }

        // UiButtonFeedback은 자신의 기본 색으로 그래픽 색을 덮어쓴다. 지금 색을 기준으로 호버, 누름 색까지 채워 둔다
        private static void AddFeedback(GameObject target, Graphic graphic, Color normal)
        {
            UiButtonFeedback feedback = target.AddComponent<UiButtonFeedback>();
            SetReference(feedback, "_graphic", graphic);
            SetColor(feedback, "_normalColor", normal);
            SetColor(feedback, "_hoverColor", Color.Lerp(normal, Color.white, 0.18f));
            SetColor(feedback, "_pressedColor", Color.Lerp(normal, Color.black, 0.2f));
        }

        private static TMP_Text Text(RectTransform rect, string value, float size, Color color, bool bold = false,
            TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
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
            image.sprite = LoadAsset<Sprite>(SKIN_FOLDER + name + ".png");
            image.type   = Image.Type.Sliced;
            image.color  = tint;
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
            rect.anchorMin        = Vector2.up;
            rect.anchorMax        = Vector2.up;
            rect.pivot            = Vector2.up;
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta        = new Vector2(width, height);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot     = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static T LoadAsset<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new InvalidOperationException($"장비 강화 창 에셋을 찾을 수 없다: {path}");
            }

            return asset;
        }

        private static void SetReference(UnityEngine.Object target, string name, UnityEngine.Object value)
        {
            SerializedObject   serialized = new SerializedObject(target);
            SerializedProperty property   = serialized.FindProperty(name);
            if (property == null)
            {
                throw new InvalidOperationException($"{target.GetType().Name}에 필드가 없다: {name}");
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetReference(UnityEngine.Object target, string name, UnityEngine.Object[] values)
        {
            SerializedObject   serialized = new SerializedObject(target);
            SerializedProperty property   = serialized.FindProperty(name);
            if (property == null)
            {
                throw new InvalidOperationException($"{target.GetType().Name}에 필드가 없다: {name}");
            }

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

        // 동적 폰트지만 미리 넣어 두면 첫 표시 때 아틀라스 갱신으로 글자가 늦게 뜨지 않는다
        private static void PreloadCharacters(RectTransform root)
        {
            StringBuilder characters = new StringBuilder("0123456789%+-/.,:() ");
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                characters.Append(text.text);
            }

            characters.Append("더할없도전눌러한이번일반희귀영웅전설수련용무기머리몸통손발장신구등급착용한장비없음비어있음공격력생명력방어력최대단계완료확정");
            characters.Append("성공확률이번시도는반드시합니다실패회누적째항상보정이없습니다강화후유지다음능력치가올랐습니다");
            characters.Append("부족한정기정련편경매장에맡긴할수서버의상태를기다리고있습니다마을연결하면미리보기입니다실제재화와저장에는영향");
            G_Regular.TryAddCharacters(characters.ToString());
            G_Bold.TryAddCharacters(characters.ToString());
            EditorUtility.SetDirty(G_Regular);
            EditorUtility.SetDirty(G_Bold);
        }
    }
}
#endif
