#if UNITY_EDITOR
// System
using System.Collections.Generic;
using System.Text;

// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Network;

namespace Shinshi.Editor
{
    // P09 전환 뒤 씬과 프리팹의 참조를 다시 연결한다. 클래스 필드 타입이 바뀌어 기존 참조가 끊긴 곳(미리보기, 인게임 외형)을 새 카탈로그로 잇고,
    // 생성 씬에는 기존 외형 탭 대신 커스터마이징 행 패널을 화면 왼쪽에 만든다. 버튼과 글자는 기존 화면 오브젝트를 복제해 모양을 맞춘다
    /// <summary> 생성, 선택 씬과 PF_NetPlayer를 P09 외형에 연결한다 </summary>
    public static class PlayerP09SceneWiring
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string MENU_PATH         = "Shinshi/Art/P09/Wire Scenes";
        private const string CLASS_MENU_PATH   = "Shinshi/Art/P09/Wire NetPlayer To Class Assets";
        private const string CREATE_SCENE      = "Assets/00.Scenes/Release/CharacterCreate.unity";
        private const string SELECT_SCENE      = "Assets/00.Scenes/Release/CharacterSelect.unity";
        private const string NET_PLAYER_PREFAB = "Assets/02.Prefab/Network/NetPlayer/PF_NetPlayer.prefab";
        private const string CANVAS_NAME       = "CreateCanvas";
        private const string CREATE_PANEL_NAME = "생성 패널";
        private const string BUTTON_TEMPLATE   = "중복 확인 버튼";
        private const string LABEL_TEMPLATE    = "이름 제목";
        private const string PANEL_NAME        = "외형 패널";
        private const string LOOK_TAB_PREFIX   = "외형 탭";
        private const string LOOK_PANEL_PREFIX = "외형 옵션 패널";
        private const string ALIVE_PARAMETER   = "Alive";

        private const float PANEL_X       = -620f;
        private const float PANEL_WIDTH   = 520f;
        private const float PANEL_HEIGHT  = 800f;
        private const float TITLE_HEIGHT  = 56f;
        private const float ROW_HEIGHT    = 46f;
        private const float ROW_SPACING   = 4f;
        private const int   PADDING       = 20;
        private const float LABEL_WIDTH   = 170f;
        private const float BUTTON_WIDTH  = 56f;
        private const float VALUE_WIDTH   = 150f;
        private const float FONT_SIZE     = 24f;
        private const int   EMOTION_COUNT = 10;

        private static readonly string[] G_RowLabels = { "성별", "얼굴", "헤어", "헤어 색", "눈 색", "피부", "수염", "가슴 크기",
            "머리 장비", "가슴 장비", "팔 장비", "허리 장비", "다리 장비", "표정(미리보기)" };

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem(MENU_PATH)]
        private static void WireMenu()
        {
            Debug.Log(WireAll());
        }

        [MenuItem(CLASS_MENU_PATH)]
        private static void WireClassMenu()
        {
            Debug.Log(WireNetPlayerToClassAssets());
        }

        /// <summary> PF_NetPlayer만 검사, 마검사, 궁수 슈퍼셋 카탈로그와 프로필에 잇는다. 씬은 건드리지 않으며 직접 실행해야만 바뀐다 </summary>
        public static string WireNetPlayerToClassAssets()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PlayerCharacterLookCatalogSO>(PlayerP09AssetBuilder.CLASS_CATALOG_PATH);
            var profile = AssetDatabase.LoadAssetAtPath<PlayerCombatAnimationProfileSO>(PlayerP09AssetBuilder.CLASS_PROFILE_PATH);
            if ((catalog == null) || (profile == null))
            {
                return "[P09] 신규 직업 카탈로그나 프로필이 없다. Shinshi/Art/P09/Build Class Assets를 먼저 실행한다";
            }

            return WireNetPlayer(catalog, profile);
        }

        /// <summary> 프리팹, 선택 씬, 생성 씬 순서로 연결하고 결과 요약을 돌려준다 </summary>
        public static string WireAll()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PlayerCharacterLookCatalogSO>(PlayerP09AssetBuilder.CATALOG_PATH);
            var profile = AssetDatabase.LoadAssetAtPath<PlayerCombatAnimationProfileSO>(PlayerP09AssetBuilder.PROFILE_PATH);
            if ((catalog == null) || (profile == null))
            {
                return "[P09] 카탈로그나 프로필이 없다. Shinshi/Art/P09/Build Assets를 먼저 실행한다";
            }

            var log = new StringBuilder();
            log.AppendLine(WireNetPlayer(catalog, profile));
            log.AppendLine(WireSelectScene(catalog));
            log.AppendLine(WireCreateScene(catalog));
            return log.ToString();
        }

        private static string WireNetPlayer(PlayerCharacterLookCatalogSO catalog, PlayerCombatAnimationProfileSO profile)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(NET_PLAYER_PREFAB);
            try
            {
                var appearance = root.GetComponentInChildren<PlayerFemaleCharacterAppearanceView>(true);
                if (appearance == null)
                {
                    return "[P09] PF_NetPlayer에 외형 뷰가 없다";
                }

                var combatAnimation = appearance.GetComponent<PlayerCharacterCombatAnimationView>();
                if (combatAnimation == null)
                {
                    combatAnimation = appearance.gameObject.AddComponent<PlayerCharacterCombatAnimationView>();
                }

                var so = new SerializedObject(appearance);
                so.FindProperty("_catalog").objectReferenceValue         = catalog;
                so.FindProperty("_combatProfile").objectReferenceValue   = profile;
                so.FindProperty("_combatAnimation").objectReferenceValue = combatAnimation;
                so.ApplyModifiedPropertiesWithoutUndo();

                var presentation = root.GetComponentInChildren<PlayerFemaleCharacterPresentationView>(true);
                if (presentation != null)
                {
                    var presentationSo = new SerializedObject(presentation);
                    presentationSo.FindProperty("_aliveParameter").stringValue = ALIVE_PARAMETER;
                    presentationSo.ApplyModifiedPropertiesWithoutUndo();
                }

                PrefabUtility.SaveAsPrefabAsset(root, NET_PLAYER_PREFAB);
                return "[P09] PF_NetPlayer 연결 완료";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static string WireSelectScene(PlayerCharacterLookCatalogSO catalog)
        {
            Scene scene = EditorSceneManager.OpenScene(SELECT_SCENE, OpenSceneMode.Single);
            int count = SetPreviewCatalogs(scene, catalog);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"[P09] 선택 씬 미리보기 {count}개 연결";
        }

        private static string WireCreateScene(PlayerCharacterLookCatalogSO catalog)
        {
            Scene scene = EditorSceneManager.OpenScene(CREATE_SCENE, OpenSceneMode.Single);
            int count = SetPreviewCatalogs(scene, catalog);

            Transform createPanel = FindCreatePanel(scene);
            if (createPanel == null)
            {
                return "[P09] 생성 패널을 찾지 못했다";
            }

            Transform buttonTemplate = createPanel.Find(BUTTON_TEMPLATE);
            Transform labelTemplate  = createPanel.Find(LABEL_TEMPLATE);
            if ((buttonTemplate == null) || (labelTemplate == null))
            {
                return "[P09] 버튼, 글자 템플릿을 찾지 못했다";
            }

            // 기존 탭과 옵션 패널은 지우지 않고 끈다
            foreach (Transform child in createPanel)
            {
                if (child.name.StartsWith(LOOK_TAB_PREFIX) || child.name.StartsWith(LOOK_PANEL_PREFIX))
                {
                    child.gameObject.SetActive(false);
                }
            }

            Transform canvas = createPanel.parent;
            Transform old    = canvas.Find(PANEL_NAME);
            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }

            CharacterCustomizePanel panel = BuildPanel(canvas, createPanel, buttonTemplate, labelTemplate);

            var screen = Object.FindAnyObjectByType<CharacterCreateScreen>(FindObjectsInactive.Include);
            if (screen == null)
            {
                return "[P09] CharacterCreateScreen을 찾지 못했다";
            }

            var screenSo = new SerializedObject(screen);
            screenSo.FindProperty("_customizePanel").objectReferenceValue = panel;
            screenSo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"[P09] 생성 씬 미리보기 {count}개, 외형 패널 행 {G_RowLabels.Length}개 연결";
        }

        private static CharacterCustomizePanel BuildPanel(Transform canvas, Transform createPanel, Transform buttonTemplate,
            Transform labelTemplate)
        {
            // 생성 패널의 배경 모양을 그대로 쓴다
            var panelObject = new GameObject(PANEL_NAME, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            var panelRect   = (RectTransform)panelObject.transform;
            panelRect.SetParent(canvas, false);
            panelRect.anchorMin        = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax        = new Vector2(0.5f, 0.5f);
            panelRect.pivot            = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = new Vector2(PANEL_X, 0f);
            panelRect.sizeDelta        = new Vector2(PANEL_WIDTH, PANEL_HEIGHT);

            var sourceImage = createPanel.GetComponent<Image>();
            var image       = panelObject.GetComponent<Image>();
            if (sourceImage != null)
            {
                image.sprite = sourceImage.sprite;
                image.type   = sourceImage.type;
                image.color  = sourceImage.color;
            }

            var layout = panelObject.GetComponent<VerticalLayoutGroup>();
            layout.padding                = new RectOffset(PADDING, PADDING, PADDING, PADDING);
            layout.spacing                = ROW_SPACING;
            layout.childAlignment         = TextAnchor.UpperCenter;
            layout.childControlWidth      = true;
            layout.childControlHeight     = false;
            layout.childForceExpandWidth  = true;
            layout.childForceExpandHeight = false;

            TMP_Text title = CloneLabel(labelTemplate, panelRect, "외형 제목", "외형", PANEL_WIDTH - (PADDING * 2f), TITLE_HEIGHT);
            title.alignment = TextAlignmentOptions.Center;

            var rows = new List<CharacterCustomizePanel.Row>();
            for (int i = 0; i < G_RowLabels.Length; ++i)
            {
                rows.Add(BuildRow(panelRect, (CharacterCustomizePanel.ERow)i, G_RowLabels[i], buttonTemplate, labelTemplate));
            }

            var panel = panelObject.AddComponent<CharacterCustomizePanel>();
            panel.EditorSetRows(rows.ToArray(), EMOTION_COUNT);
            return panel;
        }

        private static CharacterCustomizePanel.Row BuildRow(RectTransform parent, CharacterCustomizePanel.ERow kind, string label,
            Transform buttonTemplate, Transform labelTemplate)
        {
            var rowObject = new GameObject("행 " + label, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var rowRect   = (RectTransform)rowObject.transform;
            rowRect.SetParent(parent, false);
            rowRect.sizeDelta = new Vector2(PANEL_WIDTH - (PADDING * 2f), ROW_HEIGHT);
            rowObject.GetComponent<LayoutElement>().preferredHeight = ROW_HEIGHT;

            var layout = rowObject.GetComponent<HorizontalLayoutGroup>();
            layout.spacing                = ROW_SPACING;
            layout.childAlignment         = TextAnchor.MiddleLeft;
            layout.childControlWidth      = false;
            layout.childControlHeight     = true;
            layout.childForceExpandWidth  = false;
            layout.childForceExpandHeight = true;

            TMP_Text name     = CloneLabel(labelTemplate, rowRect, "이름", label, LABEL_WIDTH, ROW_HEIGHT);
            name.alignment    = TextAlignmentOptions.MidlineLeft;
            Button   previous = CloneButton(buttonTemplate, rowRect, "이전", "<");
            TMP_Text value    = CloneLabel(labelTemplate, rowRect, "값", "-", VALUE_WIDTH, ROW_HEIGHT);
            value.alignment   = TextAlignmentOptions.Center;
            Button   next     = CloneButton(buttonTemplate, rowRect, "다음", ">");

            return new CharacterCustomizePanel.Row(kind, rowObject, value, previous, next);
        }

        private static TMP_Text CloneLabel(Transform template, RectTransform parent, string name, string text, float width, float height)
        {
            GameObject clone = Object.Instantiate(template.gameObject, parent, false);
            clone.name = name;
            clone.SetActive(true);
            ((RectTransform)clone.transform).sizeDelta = new Vector2(width, height);

            var label = clone.GetComponentInChildren<TMP_Text>(true);
            label.text     = text;
            label.fontSize = FONT_SIZE;
            return label;
        }

        // 복제한 버튼은 원래 연결된 클릭 이벤트(중복 확인)를 지운다
        private static Button CloneButton(Transform template, RectTransform parent, string name, string text)
        {
            GameObject clone = Object.Instantiate(template.gameObject, parent, false);
            clone.name = name;
            clone.SetActive(true);
            ((RectTransform)clone.transform).sizeDelta = new Vector2(BUTTON_WIDTH, ROW_HEIGHT);

            var button = clone.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            var label = clone.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text     = text;
                label.fontSize = FONT_SIZE;
            }

            return button;
        }

        private static int SetPreviewCatalogs(Scene scene, PlayerCharacterLookCatalogSO catalog)
        {
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (PlayerFemaleLookPreviewView preview in root.GetComponentsInChildren<PlayerFemaleLookPreviewView>(true))
                {
                    var so = new SerializedObject(preview);
                    so.FindProperty("_catalog").objectReferenceValue = catalog;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    ++count;
                }
            }

            return count;
        }

        private static Transform FindCreatePanel(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == CANVAS_NAME)
                {
                    return root.transform.Find(CREATE_PANEL_NAME);
                }
            }

            return null;
        }
    }
}
#endif
