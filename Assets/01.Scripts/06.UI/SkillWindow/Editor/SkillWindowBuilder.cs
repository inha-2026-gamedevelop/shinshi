#if UNITY_EDITOR
// System
using System;
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Systems;

namespace Shinshi.UI.Editor
{
    /// <summary> 스킬창 프리팹을 공통 창 스킨과 글꼴로 만들고, 마을 출시 씬에 창, 바인더, 단축키 토글, HUD 메뉴 버튼을 연결한다 </summary>
    public static class SkillWindowBuilder
    {
        // 배치 JSON이 없는 창이라 코드로 계층을 만든다. 다시 실행하면 프리팹을 통째로 새로 쓰므로 프리팹을 손으로 고친 내용은 사라진다

        /****************************************
        *                Fields
        ****************************************/

        public const string PREFAB_PATH = "Assets/02.Prefab/UI/Windows/PF_SkillWindow.prefab";

        private const string SKIN_FOLDER     = "Assets/01.Scripts/05.Art/UI/GameStyle/Textures/";
        private const string FONT_REGULAR    = "Assets/01.Scripts/06.UI/Hud/Fonts/HudQuestKorean-Regular.asset";
        private const string FONT_BOLD       = "Assets/01.Scripts/06.UI/Hud/Fonts/HudQuestKorean-Bold.asset";
        private const string ICON_LIBRARY    = "Assets/01.Scripts/05.Art/UI/Icons/UiIconLibrary.asset";
        private const string TOWN_SCENE_PATH    = "Assets/00.Scenes/Release/ShinshiTown.unity";
        private const string DUNGEON_SCENE_PATH = "Assets/00.Scenes/Release/DungeonForest.unity";
        private const string UI_LAYER           = "UI";
        private const string MENU_BUTTON        = "스킬";
        private const string MENU_TEMPLATE      = "옵션 재설정";
        private const string MENU_SECTION       = "제목 경제";

        private const float WINDOW_WIDTH  = 1140f;
        private const float WINDOW_HEIGHT = 780f;
        private const float TITLE_HEIGHT  = 72f;
        private const float MARGIN        = 24f;
        private const float CLOSE_SIZE    = 28f;
        private const float LEFT_WIDTH    = 520f;
        private const float RIGHT_LEFT    = 568f;
        private const float RIGHT_WIDTH   = 548f;
        private const float TAB_TOP       = 120f;
        private const float TAB_WIDTH     = 96f;
        private const float TAB_HEIGHT    = 88f;
        private const float TAB_GAP       = 104f;
        private const float ROW_TOP       = 164f;
        private const float ROW_GAP       = 96f;
        private const float CHOICE_LEFT   = 700f;
        private const float CHOICE_GAP    = 208f;
        private const float CHOICE_WIDTH  = 196f;
        private const float CHOICE_HEIGHT = 80f;
        private const float BUTTON_HEIGHT = 48f;
        private const float ICON_SIZE     = 48f;
        private const float MENU_ROW      = 52f;  // HUD 메뉴 버튼 한 줄 간격
        private const float MENU_SKILL_X  = 276f;
        private const float MENU_SKILL_Y  = -400f;

        private static readonly Color G_Text   = EquipmentGrowthStyle.Text;
        private static readonly Color G_Muted  = EquipmentGrowthStyle.Muted;
        private static readonly Color G_Panel  = new Color(1f, 1f, 1f, 0.9f);
        private static readonly Color G_Line   = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color G_Shade  = new Color(0f, 0f, 0f, 0.5f);

        private static TMP_FontAsset G_Regular;
        private static TMP_FontAsset G_Bold;

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem("Shinshi/UI/스킬창 만들기")]
        public static void Build()
        {
            G_Regular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_REGULAR);
            G_Bold    = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_BOLD);
            if ((G_Regular == null) || (G_Bold == null))
            {
                throw new InvalidOperationException("스킬창 글꼴을 찾을 수 없습니다");
            }

            UnityEngine.SceneManagement.Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject root = new GameObject("PF_SkillWindow", typeof(RectTransform));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
            try
            {
                root.SetActive(false);
                root.layer = LayerMask.NameToLayer(UI_LAYER);
                BuildWindow((RectTransform)root.transform);

                // 창은 단축키와 메뉴가 켠다. 기존 창 프리팹과 같이 꺼진 채로 저장한다
                root.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(preview);
            }

            Debug.Log("[UI] 스킬창 프리팹을 만들었다: " + PREFAB_PATH);
        }

        /// <summary> 마을 출시 씬에 스킬창과 바인더, 단축키 토글, HUD 메뉴 버튼을 놓는다. 이미 있으면 아무것도 하지 않는다 </summary>
        [MenuItem("Shinshi/UI/스킬창을 마을 씬에 연결")]
        public static void WireTownScene()
        {
            WireScene(TOWN_SCENE_PATH, true);
        }

        /// <summary> 던전 출시 씬에 스킬창과 바인더, 단축키 토글을 놓는다. 던전 HUD에는 메뉴가 없어 단축키로만 연다 </summary>
        [MenuItem("Shinshi/UI/스킬창을 던전 씬에 연결")]
        public static void WireDungeonScene()
        {
            WireScene(DUNGEON_SCENE_PATH, false);
        }

        private static void WireScene(string scenePath, bool addMenuButton)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            UiIconLibrarySO icons = AssetDatabase.LoadAssetAtPath<UiIconLibrarySO>(ICON_LIBRARY);
            if ((prefab == null) || (icons == null))
            {
                throw new InvalidOperationException("스킬창 프리팹이나 아이콘 표가 없습니다. 먼저 창을 만드세요");
            }

            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (UnityEngine.Object.FindObjectsByType<SkillWindowBinder>(FindObjectsInactive.Include).Length > 0)
            {
                Debug.Log("[UI] 스킬창이 이미 연결돼 있다");
                return;
            }

            CharDetailBinder[] anchors = UnityEngine.Object.FindObjectsByType<CharDetailBinder>(FindObjectsInactive.Include);
            CharDetailView[] views = UnityEngine.Object.FindObjectsByType<CharDetailView>(FindObjectsInactive.Include);
            HotkeyRouter[] routers = UnityEngine.Object.FindObjectsByType<HotkeyRouter>(FindObjectsInactive.Include);
            HudMenuDropdown[] menus = UnityEngine.Object.FindObjectsByType<HudMenuDropdown>(FindObjectsInactive.Include);
            if ((anchors.Length == 0) || (views.Length == 0) || (routers.Length == 0) || ((addMenuButton) && (menus.Length == 0)))
            {
                throw new InvalidOperationException("씬에서 캐릭터 상세창 연결, 단축키 라우터, HUD 메뉴를 찾지 못했습니다: " + scenePath);
            }

            // 캐릭터 상세창의 캔버스 설정을 따르고 정렬은 이미 놓인 성장창보다 하나 위에 둔다
            Canvas referenceCanvas = views[0].GetComponentInParent<Canvas>(true);
            CanvasScaler referenceScaler = (referenceCanvas != null) ? referenceCanvas.GetComponent<CanvasScaler>() : null;
            if ((referenceCanvas == null) || (referenceScaler == null))
            {
                throw new InvalidOperationException("캐릭터 상세창의 캔버스 설정을 찾지 못했습니다");
            }

            int sortingOrder = referenceCanvas.sortingOrder;
            EquipmentOptionView[] optionViews = UnityEngine.Object.FindObjectsByType<EquipmentOptionView>(FindObjectsInactive.Include);
            for (int i = 0; i < optionViews.Length; ++i)
            {
                Canvas optionCanvas = optionViews[i].GetComponentInParent<Canvas>(true);
                if (optionCanvas != null)
                {
                    sortingOrder = Mathf.Max(sortingOrder, optionCanvas.sortingOrder);
                }
            }

            GameObject canvasObject = new GameObject("SkillWindowCanvas", typeof(RectTransform));
            canvasObject.layer = LayerMask.NameToLayer(UI_LAYER);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode   = referenceCanvas.renderMode;
            canvas.sortingOrder = sortingOrder + 1;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = referenceScaler.uiScaleMode;
            scaler.referenceResolution = referenceScaler.referenceResolution;
            scaler.screenMatchMode     = referenceScaler.screenMatchMode;
            scaler.matchWidthOrHeight  = referenceScaler.matchWidthOrHeight;
            canvasObject.AddComponent<GraphicRaycaster>();

            GameObject window = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvasObject.transform);
            window.name = "SkillWindow";

            GameObject bindingObject = anchors[0].gameObject;
            SkillWindowBinder binder = bindingObject.AddComponent<SkillWindowBinder>();
            HotkeyWindowToggle toggle = bindingObject.AddComponent<HotkeyWindowToggle>();
            SetReference(binder, "_view", window.GetComponent<SkillWindowView>());
            SetReference(binder, "_toggle", toggle);
            SetReference(binder, "_icons", icons);
            SetReference(toggle, "_router", routers[0]);
            SetReference(toggle, "_window", window);
            SetReference(toggle, "_closeGuard", binder);
            var serializedToggle = new SerializedObject(toggle);
            serializedToggle.FindProperty("_action").intValue = (int)EHotkeyAction.SkillWindow;
            serializedToggle.ApplyModifiedPropertiesWithoutUndo();

            if (addMenuButton)
            {
                AddMenuButton(menus[0], toggle);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[UI] 스킬창을 씬에 연결했다: " + scenePath);
        }

        // 캐릭터 묶음의 빈 줄에 "스킬"을 넣고, 아래 묶음(경제, 시스템)을 한 줄 내린다
        private static void AddMenuButton(HudMenuDropdown menu, HotkeyWindowToggle toggle)
        {
            var panel = (RectTransform)new SerializedObject(menu).FindProperty("_panel").objectReferenceValue;
            Button template = FindButton(panel, MENU_TEMPLATE);
            RectTransform section = FindChild(panel, MENU_SECTION);
            if ((template == null) || (section == null))
            {
                throw new InvalidOperationException("HUD 메뉴의 옵션 재설정 버튼이나 경제 제목을 찾지 못했습니다");
            }

            if (FindButton(panel, MENU_BUTTON) != null)
            {
                return;
            }

            Transform body = template.transform.parent;
            float sectionTop = section.anchoredPosition.y;
            var lowered = new List<RectTransform>();
            foreach (Transform child in body)
            {
                var rect = (RectTransform)child;
                if (rect.anchoredPosition.y <= sectionTop)
                {
                    lowered.Add(rect);
                }
            }

            for (int i = 0; i < lowered.Count; ++i)
            {
                lowered[i].anchoredPosition += new Vector2(0f, -MENU_ROW);
                PrefabUtility.RecordPrefabInstancePropertyModifications(lowered[i]);
            }

            Button button = UnityEngine.Object.Instantiate(template, body);
            button.name = MENU_BUTTON;
            ((RectTransform)button.transform).anchoredPosition = new Vector2(MENU_SKILL_X, MENU_SKILL_Y);
            button.GetComponentInChildren<TMP_Text>(true).text = MENU_BUTTON;
            while (button.onClick.GetPersistentEventCount() > 0)
            {
                UnityEventTools.RemovePersistentListener(button.onClick, 0);
            }

            UnityEventTools.AddPersistentListener(button.onClick, toggle.Toggle);
            button.gameObject.SetActive(true);
            button.interactable = true;
            EditorUtility.SetDirty(button);
        }

        private static Button FindButton(Transform parent, string name)
        {
            foreach (Button button in parent.GetComponentsInChildren<Button>(true))
            {
                if (button.name == name)
                {
                    return button;
                }
            }

            return null;
        }

        private static RectTransform FindChild(Transform parent, string name)
        {
            foreach (RectTransform child in parent.GetComponentsInChildren<RectTransform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }

        private static void BuildWindow(RectTransform root)
        {
            root.anchorMin        = new Vector2(0.5f, 0.5f);
            root.anchorMax        = new Vector2(0.5f, 0.5f);
            root.pivot            = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = Vector2.zero;
            root.sizeDelta        = new Vector2(WINDOW_WIDTH, WINDOW_HEIGHT);

            Image frame = root.gameObject.AddComponent<Image>();
            EquipmentGrowthStyle.Surface(frame, EquipmentGrowthStyle.Canvas);
            frame.raycastTarget = true;
            var view = root.gameObject.AddComponent<SkillWindowView>();

            Image titleBar = Panel(root, "제목줄", 0f, 0f, WINDOW_WIDTH, TITLE_HEIGHT, EquipmentGrowthStyle.Panel);
            titleBar.raycastTarget = true;
            var drag = titleBar.gameObject.AddComponent<UiWindowDragHandle>();
            SetReference(drag, "_window", root);
            Label(root, "제목", "스킬", MARGIN, 18f, 120f, 36f, 28f, G_Text, true, TextAlignmentOptions.MidlineLeft);
            TMP_Text classLabel = Label(root, "직업", string.Empty, 150f, 22f, 300f, 30f, 20f, EquipmentGrowthStyle.Gold, true,
                TextAlignmentOptions.MidlineLeft);
            Label(root, "분류", "기술  /  부적", 770f, 24f, 260f, 24f, 14f, G_Muted, false, TextAlignmentOptions.MidlineRight);
            Button close = MakeButton(root, "닫기 버튼", WINDOW_WIDTH - MARGIN - CLOSE_SIZE, 22f, CLOSE_SIZE, CLOSE_SIZE,
                "T_UiIcon_Close", false);
            Line(root, "제목 구분선", MARGIN, TITLE_HEIGHT, WINDOW_WIDTH - (MARGIN * 2f));
            TMP_Text stackRule = Label(root, "스택 규칙", string.Empty, MARGIN, 82f, WINDOW_WIDTH - (MARGIN * 2f), 28f, 15f,
                EquipmentGrowthStyle.Soul, false, TextAlignmentOptions.MidlineLeft);

            BuildTabs(root, view);

            EquipmentGrowthStyle.Card(root, "기술 정보 패널", MARGIN, 224f, LEFT_WIDTH, 220f, EquipmentGrowthStyle.Panel);
            TMP_Text skillName = Label(root, "기술 이름", string.Empty, 40f, 236f, LEFT_WIDTH - 32f, 32f, 24f, G_Text, true,
                TextAlignmentOptions.MidlineLeft);
            TMP_Text skillStats = Label(root, "기술 수치", string.Empty, 40f, 272f, LEFT_WIDTH - 32f, 92f, 16f,
                EquipmentGrowthStyle.Mint, false, TextAlignmentOptions.TopLeft);
            TMP_Text skillDescription = Label(root, "기술 설명", string.Empty, 40f, 368f, LEFT_WIDTH - 32f, 68f, 15f, G_Muted,
                false, TextAlignmentOptions.TopLeft);

            TMP_Text notice = Label(root, "부적 안내", string.Empty, RIGHT_LEFT + 16f, 250f, RIGHT_WIDTH - 32f, 80f, 18f, G_Muted,
                false, TextAlignmentOptions.Center);

            RectTransform group = Rect(root, "부적 영역", 0f, 0f, WINDOW_WIDTH, WINDOW_HEIGHT);
            BuildTalismans(group, view);

            TMP_Text status = Label(root, "상태", string.Empty, MARGIN, 680f, 700f, 64f, 15f, EquipmentGrowthStyle.Soul, false,
                TextAlignmentOptions.MidlineLeft);
            Button retry = MakeButton(root, "다시 확인 버튼", 736f, 690f, 140f, BUTTON_HEIGHT, "T_UiButton_Secondary", true);
            Label((RectTransform)retry.transform, "글자", "다시 확인", 0f, 0f, 140f, BUTTON_HEIGHT, 17f, G_Text, true,
                TextAlignmentOptions.Center);
            retry.gameObject.SetActive(false);

            BuildConfirm(root, view);

            SetReference(view, "_closeButton", close);
            SetReference(view, "_classLabel", classLabel);
            SetReference(view, "_stackRuleLabel", stackRule);
            SetReference(view, "_skillNameLabel", skillName);
            SetReference(view, "_skillStatsLabel", skillStats);
            SetReference(view, "_skillDescription", skillDescription);
            SetReference(view, "_talismanGroup", group.gameObject);
            SetReference(view, "_talismanNotice", notice);
            SetReference(view, "_statusLabel", status);
            SetReference(view, "_retryButton", retry);
            EquipmentGrowthStyle.Finish(root, EquipmentGrowthStyle.Soul);
        }

        private static void BuildTabs(RectTransform root, SkillWindowView view)
        {
            int count = SkillWindowViewRules.TAB_COUNT;
            var buttons  = new UnityEngine.Object[count];
            var icons    = new UnityEngine.Object[count];
            var labels   = new UnityEngine.Object[count];
            var selected = new UnityEngine.Object[count];
            for (int i = 0; i < count; ++i)
            {
                Button button = MakeButton(root, "탭 " + SkillWindowViewRules.TabLabel(i), MARGIN + (TAB_GAP * i), TAB_TOP,
                    TAB_WIDTH, TAB_HEIGHT, "T_UiPanel_Inset", true);
                EquipmentGrowthStyle.Surface(button.image, EquipmentGrowthStyle.Canvas);
                var rect = (RectTransform)button.transform;
                Image icon = IconSlot(rect, 24f, 8f);
                icon.enabled        = false;
                labels[i] = Label(rect, "키", SkillWindowViewRules.TabLabel(i), 0f, 58f, TAB_WIDTH, 26f, 18f, G_Text, true,
                    TextAlignmentOptions.Center);
                Image mark = Panel(rect, "선택 표시", 0f, TAB_HEIGHT - 4f, TAB_WIDTH, 4f, EquipmentGrowthStyle.Mint);
                mark.gameObject.SetActive(false);
                buttons[i]  = button;
                icons[i]    = icon;
                selected[i] = mark.gameObject;
            }

            SetArray(view, "_tabButtons", buttons);
            SetArray(view, "_tabIcons", icons);
            SetArray(view, "_tabLabels", labels);
            SetArray(view, "_tabSelected", selected);
        }

        private static void BuildTalismans(RectTransform group, SkillWindowView view)
        {
            EquipmentGrowthStyle.Card(group, "부적 패널", RIGHT_LEFT, TAB_TOP, RIGHT_WIDTH, 336f, EquipmentGrowthStyle.Panel);
            Label(group, "부적 제목", "부적", RIGHT_LEFT + 16f, 128f, 200f, 28f, 18f, G_Text, true, TextAlignmentOptions.MidlineLeft);
            Label(group, "부적 안내 줄", "단계마다 하나를 장착합니다", RIGHT_LEFT + 220f, 128f, 312f, 28f, 13f, G_Muted, false,
                TextAlignmentOptions.MidlineRight);

            int count = SkillWindowViewRules.TIERS_PER_SKILL * SkillWindowViewRules.CHOICES_PER_TIER;
            var roots    = new UnityEngine.Object[count];
            var buttons  = new UnityEngine.Object[count];
            var icons    = new UnityEngine.Object[count];
            var names    = new UnityEngine.Object[count];
            var states   = new UnityEngine.Object[count];
            var selected = new UnityEngine.Object[count];
            var equipped = new UnityEngine.Object[count];
            for (int tier = 0; tier < SkillWindowViewRules.TIERS_PER_SKILL; ++tier)
            {
                float y = ROW_TOP + (ROW_GAP * tier);
                Label(group, "단계 " + (tier + 1), SkillWindowViewRules.TierLabel(tier), RIGHT_LEFT + 16f, y, 112f, CHOICE_HEIGHT, 16f,
                    (tier == (SkillWindowViewRules.TIERS_PER_SKILL - 1)) ? EquipmentGrowthStyle.Gold : G_Text, true,
                    TextAlignmentOptions.MidlineLeft);
                for (int choice = 0; choice < SkillWindowViewRules.CHOICES_PER_TIER; ++choice)
                {
                    int index = (tier * SkillWindowViewRules.CHOICES_PER_TIER) + choice;
                    Button button = MakeButton(group, "후보 " + (tier + 1) + ((choice == 0) ? "A" : "B"),
                        CHOICE_LEFT + (CHOICE_GAP * choice), y, CHOICE_WIDTH, CHOICE_HEIGHT, "T_UiPanel_Inset", true);
                    EquipmentGrowthStyle.Surface(button.image, EquipmentGrowthStyle.Canvas);
                    var rect = (RectTransform)button.transform;
                    Image icon = IconSlot(rect, 8f, 16f);
                    icon.enabled        = false;
                    TMP_Text effectName = Label(rect, "이름", string.Empty, 64f, 8f, CHOICE_WIDTH - 72f, 34f, 15f, G_Text, true,
                        TextAlignmentOptions.MidlineLeft);
                    effectName.overflowMode = TextOverflowModes.Ellipsis;
                    TMP_Text state = Label(rect, "상태", string.Empty, 64f, 44f, CHOICE_WIDTH - 72f, 28f, 13f, G_Muted, false,
                        TextAlignmentOptions.MidlineLeft);
                    Image mark = Panel(rect, "선택 표시", 0f, 0f, 4f, CHOICE_HEIGHT, EquipmentGrowthStyle.Mint);
                    mark.gameObject.SetActive(false);
                    TMP_Text badge = Label(rect, "장착 표시", "장착", CHOICE_WIDTH - 52f, 4f, 48f, 20f, 12f,
                        EquipmentGrowthStyle.Gold, true, TextAlignmentOptions.MidlineRight);
                    badge.gameObject.SetActive(false);
                    roots[index]    = button.gameObject;
                    buttons[index]  = button;
                    icons[index]    = icon;
                    names[index]    = effectName;
                    states[index]   = state;
                    selected[index] = mark.gameObject;
                    equipped[index] = badge.gameObject;
                }
            }

            EquipmentGrowthStyle.Card(group, "효과 상세 패널", MARGIN, 460f, LEFT_WIDTH, 196f, EquipmentGrowthStyle.Canvas);
            TMP_Text effectTitle = Label(group, "효과 이름", string.Empty, 40f, 472f, LEFT_WIDTH - 32f, 30f, 20f, G_Text, true,
                TextAlignmentOptions.MidlineLeft);
            TMP_Text effectBonus = Label(group, "효과 보너스", string.Empty, 40f, 506f, LEFT_WIDTH - 32f, 28f, 16f,
                EquipmentGrowthStyle.Mint, true, TextAlignmentOptions.MidlineLeft);
            TMP_Text effectDescription = Label(group, "효과 설명", string.Empty, 40f, 538f, LEFT_WIDTH - 32f, 72f, 14f, G_Muted,
                false, TextAlignmentOptions.TopLeft);
            TMP_Text effectWarning = Label(group, "효과 경고", string.Empty, 40f, 614f, LEFT_WIDTH - 32f, 28f, 14f,
                EquipmentGrowthStyle.Gold, true, TextAlignmentOptions.MidlineLeft);

            EquipmentGrowthStyle.Card(group, "동작 패널", RIGHT_LEFT, 470f, RIGHT_WIDTH, 186f, EquipmentGrowthStyle.Canvas);
            Button register = ActionButton(group, "등록 버튼", RIGHT_LEFT + 16f, 482f, 252f, true, out TMP_Text registerLabel);
            Button upgrade  = ActionButton(group, "강화 버튼", RIGHT_LEFT + 280f, 482f, 252f, true, out TMP_Text upgradeLabel);
            Button down     = ActionButton(group, "수량 감소", RIGHT_LEFT + 16f, 542f, 44f, false, out TMP_Text downLabel);
            downLabel.text = "-";
            Button disassemble = ActionButton(group, "분해 버튼", RIGHT_LEFT + 68f, 542f, 164f, false, out TMP_Text disassembleLabel);
            Button up       = ActionButton(group, "수량 증가", RIGHT_LEFT + 240f, 542f, 44f, false, out TMP_Text upLabel);
            upLabel.text = "+";
            Button equip    = ActionButton(group, "장착 버튼", RIGHT_LEFT + 296f, 542f, 236f, true, out TMP_Text equipLabel);
            TMP_Text footer = Label(group, "보유 줄", string.Empty, RIGHT_LEFT + 16f, 604f, RIGHT_WIDTH - 32f, 40f, 15f, G_Text,
                false, TextAlignmentOptions.MidlineLeft);

            Button save = MakeButton(group, "조합 저장 버튼", WINDOW_WIDTH - MARGIN - 228f, 690f, 228f, 52f, "T_UiButton_Primary", true);
            Label((RectTransform)save.transform, "글자", "조합 저장", 0f, 0f, 228f, 52f, 20f, EquipmentGrowthStyle.Canvas, true,
                TextAlignmentOptions.Center);
            EquipmentGrowthStyle.Primary(save);

            SetArray(view, "_choiceRoots", roots);
            SetArray(view, "_choiceButtons", buttons);
            SetArray(view, "_choiceIcons", icons);
            SetArray(view, "_choiceNames", names);
            SetArray(view, "_choiceStates", states);
            SetArray(view, "_choiceSelected", selected);
            SetArray(view, "_choiceEquipped", equipped);
            SetReference(view, "_effectTitle", effectTitle);
            SetReference(view, "_effectBonus", effectBonus);
            SetReference(view, "_effectDescription", effectDescription);
            SetReference(view, "_effectWarning", effectWarning);
            SetReference(view, "_registerButton", register);
            SetReference(view, "_registerLabel", registerLabel);
            SetReference(view, "_upgradeButton", upgrade);
            SetReference(view, "_upgradeLabel", upgradeLabel);
            SetReference(view, "_disassembleButton", disassemble);
            SetReference(view, "_disassembleLabel", disassembleLabel);
            SetReference(view, "_quantityDown", down);
            SetReference(view, "_quantityUp", up);
            SetReference(view, "_equipButton", equip);
            SetReference(view, "_equipLabel", equipLabel);
            SetReference(view, "_footerLabel", footer);
            SetReference(view, "_saveButton", save);
        }

        private static void BuildConfirm(RectTransform root, SkillWindowView view)
        {
            Image shade = Panel(root, "닫기 확인", 0f, 0f, WINDOW_WIDTH, WINDOW_HEIGHT, G_Shade);
            shade.raycastTarget = true;
            var rect = (RectTransform)shade.transform;
            float left = (WINDOW_WIDTH - 520f) * 0.5f;
            float top  = (WINDOW_HEIGHT - 200f) * 0.5f;
            EquipmentGrowthStyle.Card(rect, "확인 패널", left, top, 520f, 200f, EquipmentGrowthStyle.Panel);
            Label(rect, "확인 글자", "저장하지 않은 조합 변경이 있습니다.\n저장하지 않고 닫을까요?", left + 24f, top + 24f, 472f, 80f, 18f,
                G_Text, false, TextAlignmentOptions.Center);
            Button accept = MakeButton(rect, "닫기", left + 40f, top + 124f, 200f, BUTTON_HEIGHT, "T_UiButton_Secondary", true);
            Label((RectTransform)accept.transform, "글자", "저장하지 않고 닫기", 0f, 0f, 200f, BUTTON_HEIGHT, 16f, G_Text, true,
                TextAlignmentOptions.Center);
            Button cancel = MakeButton(rect, "취소", left + 280f, top + 124f, 200f, BUTTON_HEIGHT, "T_UiButton_Primary", true);
            Label((RectTransform)cancel.transform, "글자", "계속 편집", 0f, 0f, 200f, BUTTON_HEIGHT, 16f, EquipmentGrowthStyle.Canvas,
                true, TextAlignmentOptions.Center);
            EquipmentGrowthStyle.Primary(cancel);
            shade.gameObject.SetActive(false);
            SetReference(view, "_confirmPanel", shade.gameObject);
            SetReference(view, "_confirmAccept", accept);
            SetReference(view, "_confirmCancel", cancel);
        }

        // 빈 칸 그림을 먼저 넣어 둔다. 그림 없는 큰 Image는 창 마감 단계가 판 표면으로 바꾼다
        private static Image IconSlot(RectTransform parent, float x, float y)
        {
            Image icon = Panel(parent, "아이콘", x, y, ICON_SIZE, ICON_SIZE, Color.white);
            Skin(icon, "T_UiSlot_Empty", Color.white);
            icon.type           = Image.Type.Simple;
            icon.preserveAspect = true;
            return icon;
        }

        private static Button ActionButton(RectTransform parent, string name, float x, float y, float width, bool primary,
            out TMP_Text label)
        {
            Button button = MakeButton(parent, name, x, y, width, BUTTON_HEIGHT, primary ? "T_UiButton_Primary" : "T_UiButton_Secondary",
                true);
            label = Label((RectTransform)button.transform, "글자", string.Empty, 4f, 0f, width - 8f, BUTTON_HEIGHT, 14f,
                primary ? EquipmentGrowthStyle.Canvas : G_Text, true, TextAlignmentOptions.Center);
            if (primary)
            {
                EquipmentGrowthStyle.Primary(button);
            }

            return button;
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
                throw new InvalidOperationException("스킬창 스킨을 찾을 수 없습니다: " + name);
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
