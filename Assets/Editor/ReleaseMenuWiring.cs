// System
using System;

// Unity
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Systems;
using Shinshi.UI;
using Shinshi.Art;
using Shinshi.Network;
using Shinshi.Contracts;

namespace Shinshi.Editor
{
    /// <summary> 출시 마을의 메뉴를 서버용 창과 설정 프리팹에 연결한다 </summary>
    public static class ReleaseMenuWiring
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SCENE_PATH    = "Assets/00.Scenes/Release/ShinshiTown.unity";
        private const string SETTINGS_PATH = "Assets/02.Prefab/UI/Settings/PF_SettingsWindow.prefab";
        private const string MAIL_ICON_PATH = "Assets/01.Scripts/05.Art/UI/HudMenuButton/Textures/T_HudMenuButton_IconMail.png";
        private static readonly Vector2 G_MailPosition   = new Vector2(276f, -296f);
        private static readonly Vector2 G_TradePosition  = new Vector2(276f, -412f);
        private static readonly Vector2 G_SortiePosition = new Vector2(276f, -580f);
        private static readonly Vector2 G_TraitsPosition = new Vector2(24f, -296f);

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem("Shinshi/UI/Wire Release Town Menu")]
        public static void Wire()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Play Mode에서는 출시 씬을 변경하지 않습니다.");
            }
            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    throw new InvalidOperationException("열린 씬에 미저장 변경이 있습니다.");
                }
            }
            Scene scene = EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Single);
            WireCharacterPreview();
            HotkeyRouter router = InScene<HotkeyRouter>(scene);
            HudMenuDropdown menu = InScene<HudMenuDropdown>(scene);
            UiPopupHub popups = InScene<UiPopupHub>(scene);
            var menuData = new SerializedObject(menu);
            var panel = (RectTransform)menuData.FindProperty("_panel").objectReferenceValue;
            foreach (TMP_Text label in panel.GetComponentsInChildren<TMP_Text>(true))
            {
                if (label.text.StartsWith("Lv.", StringComparison.Ordinal))
                {
                    label.text = "Lv.1";
                    PrefabUtility.RecordPrefabInstancePropertyModifications(label);
                }
            }
            var status = InScene<HudPlayerStatusView>(scene);
            var statusData = new SerializedObject(status);
            var level = (TMP_Text)statusData.FindProperty("_levelText").objectReferenceValue;
            level.text = "1";
            PrefabUtility.RecordPrefabInstancePropertyModifications(level);
            SettingsView settings = InScene<SettingsView>(scene, false);
            if (settings == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SETTINGS_PATH);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.name = "SettingsCanvas";
                settings = instance.GetComponentInChildren<SettingsView>(true);
            }
            settings.gameObject.SetActive(false);
            Reference(settings, "_router", router);
            if (!router.TryGetComponent(out ReleaseMenuActions actions))
            {
                actions = router.gameObject.AddComponent<ReleaseMenuActions>();
            }
            Reference(actions, "_settings", settings);
            Reference(actions, "_popups", popups);
            Reference(popups, "_router", router);

            Button mail = Button(panel, "우편함");
            ((RectTransform)mail.transform).anchoredPosition = G_MailPosition;
            mail.GetComponentInChildren<TMP_Text>(true).text = "우편함";
            PrefabUtility.RecordPrefabInstancePropertyModifications(mail.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(mail.GetComponentInChildren<TMP_Text>(true));
            EnsureButton(panel, mail, "거래", G_TradePosition);
            EnsureButton(panel, mail, "출정 준비", G_SortiePosition);
            EnsureButton(panel, Button(panel, "캐릭터 정보"), "특성", G_TraitsPosition);
            var mailIcon = mail.transform.Find("Icon").GetComponent<Image>();
            mailIcon.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(MAIL_ICON_PATH);
            mailIcon.color = Color.white;
            mailIcon.preserveAspect = true;
            PrefabUtility.RecordPrefabInstancePropertyModifications(mailIcon);

            Connect(Button(panel, "인벤토리"), Toggle(router, EHotkeyAction.Inventory).Toggle);
            Connect(Button(panel, "경매장"), Toggle(router, EHotkeyAction.AuctionHouse).Toggle);
            Connect(Button(panel, "우편함"), Toggle(router, EHotkeyAction.MailBox).Toggle);
            Connect(Button(panel, "캐릭터 정보"), Toggle(router, EHotkeyAction.CharacterInfo).Toggle);
            Connect(Button(panel, "특성"), Toggle(router, EHotkeyAction.CharacterInfo).Toggle);
            Connect(Button(panel, "거래"), actions.OpenTradeHelp);
            Connect(Button(panel, "출정 준비"), InScene<SortiePrepBinder>(scene).Open);
            Connect(Button(panel, "환경설정"), actions.OpenSettings);
            Connect(Button(panel, "캐릭터 선택"), actions.ConfirmCharacterSelect);
            Connect(Button(panel, "게임 종료"), actions.ConfirmQuit);

            PlayerInteractionPanelOpener interaction = InScene<PlayerInteractionPanelOpener>(scene);
            Reference(interaction, "_router", router);
            var interactionData = new SerializedObject(interaction);
            var self = (RectTransform)interactionData.FindProperty("_selfPanel").objectReferenceValue;
            Connect(Button(self, "캐릭터 정보"), Toggle(router, EHotkeyAction.CharacterInfo).Toggle);

            // UI 상자에서 가져온 버튼도 실제 포인터를 받아야 한다.
            foreach (Button button in panel.GetComponentsInChildren<Button>(true))
            {
                if (button.targetGraphic != null)
                {
                    button.targetGraphic.raycastTarget = true;
                    EditorUtility.SetDirty(button.targetGraphic);
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            WireCharacterCreation();
            Debug.Log("[ReleaseMenu] 출시 마을 메뉴 10개와 자기 정보, 설정 창을 연결했습니다.");
        }

        public static void BuildRelease()
        {
            Debug.Log(PlayerP09AssetBuilder.BuildClassAssetsWithReport());
            ReleaseVfxBuilder.Build();
            Wire();
        }

        private static void WireCharacterCreation()
        {
            Scene scene = EditorSceneManager.OpenScene("Assets/00.Scenes/Release/CharacterCreate.unity", OpenSceneMode.Single);
            var screen = InScene<CharacterCreateScreen>(scene);
            var data = new SerializedObject(screen);
            SerializedProperty buttons = data.FindProperty("_classButtons");
            for (int i = 0; i < buttons.arraySize; ++i)
            {
                var button = (Button)buttons.GetArrayElementAtIndex(i).objectReferenceValue;
                foreach (TMP_Text label in button.GetComponentsInChildren<TMP_Text>(true))
                {
                    label.text = LobbyText.ClassName(i + ContractCodes.PlayerClass.SWORDSMAN);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(label);
                }
            }
            Reference(InScene<PlayerFemaleLookPreviewView>(scene), "_catalog",
                AssetDatabase.LoadAssetAtPath<PlayerCharacterLookCatalogSO>(PlayerP09AssetBuilder.CLASS_CATALOG_PATH));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void WireCharacterPreview()
        {
            const string path = "Assets/02.Prefab/UI/Windows/PF_CharDetailPreviewStage.prefab";
            GameObject stage = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Transform legacy = stage.transform.Find("무사 미리보기");
                if (legacy != null)
                {
                    UnityEngine.Object.DestroyImmediate(legacy.gameObject);
                }
                Transform model = stage.transform.Find("CurrentCharacter");
                if (model == null)
                {
                    model = new GameObject("CurrentCharacter").transform;
                    model.SetParent(stage.transform, false);
                    model.localRotation = Quaternion.Euler(0f, 160f, 0f);
                }
                if (!stage.TryGetComponent(out PlayerFemaleLookPreviewView preview))
                {
                    preview = stage.AddComponent<PlayerFemaleLookPreviewView>();
                }
                Reference(preview, "_catalog", AssetDatabase.LoadAssetAtPath<PlayerCharacterLookCatalogSO>(
                    PlayerP09AssetBuilder.CLASS_CATALOG_PATH));
                Reference(preview, "_root", model);
                if (!stage.TryGetComponent(out CharDetailPreviewBinder binder))
                {
                    binder = stage.AddComponent<CharDetailPreviewBinder>();
                }
                Reference(binder, "_preview", preview);
                Reference(binder, "_modelRoot", model.gameObject);
                model.gameObject.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(stage, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(stage);
            }
            Debug.Log(PlayerP09SceneWiring.WireNetPlayerToClassAssets());
        }

        private static T InScene<T>(Scene scene, bool required = true) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component != null)
                {
                    return component;
                }
            }
            if (required)
            {
                throw new InvalidOperationException(typeof(T).Name + "이 씬에 없습니다.");
            }
            return null;
        }

        private static HotkeyWindowToggle Toggle(HotkeyRouter router, EHotkeyAction action)
        {
            foreach (HotkeyWindowToggle toggle in router.GetComponents<HotkeyWindowToggle>())
            {
                var data = new SerializedObject(toggle);
                if (data.FindProperty("_action").intValue == (int)action)
                {
                    return toggle;
                }
            }
            throw new InvalidOperationException(action + " 창 연결이 없습니다.");
        }

        private static Button Button(Transform parent, string name)
        {
            foreach (Button button in parent.GetComponentsInChildren<Button>(true))
            {
                if (button.name == name)
                {
                    return button;
                }
            }
            throw new InvalidOperationException(name + " 버튼이 없습니다.");
        }

        private static void EnsureButton(Transform parent, Button template, string name, Vector2 position)
        {
            foreach (Button existing in parent.GetComponentsInChildren<Button>(true))
            {
                if (existing.name == name)
                {
                    return;
                }
            }
            Button button = UnityEngine.Object.Instantiate(template, template.transform.parent);
            button.name = name;
            ((RectTransform)button.transform).anchoredPosition = position;
            button.GetComponentInChildren<TMP_Text>(true).text = name;
        }

        private static void Connect(Button button, UnityAction action)
        {
            while (button.onClick.GetPersistentEventCount() > 0)
            {
                UnityEventTools.RemovePersistentListener(button.onClick, 0);
            }
            UnityEventTools.AddPersistentListener(button.onClick, action);
            button.gameObject.SetActive(true);
            button.interactable = true;
            EditorUtility.SetDirty(button);
            PrefabUtility.RecordPrefabInstancePropertyModifications(button.gameObject);
            PrefabUtility.RecordPrefabInstancePropertyModifications(button);
        }

        private static void Reference(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var data = new SerializedObject(target);
            data.FindProperty(field).objectReferenceValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
