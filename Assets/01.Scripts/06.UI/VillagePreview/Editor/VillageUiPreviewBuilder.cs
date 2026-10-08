#if UNITY_EDITOR
// System
using System;
using System.Collections.Generic;

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

namespace Shinshi.UI.Editor
{
    /// <summary>마을 HUD 개발 씬에 기존 창의 사본과 로컬 조작 연결을 배치한다</summary>
    public static class VillageUiPreviewBuilder
    {
        private const string TARGET_SCENE   = "Assets/00.Scenes/Dev_HUD/Dev_ShinshiVillageQuarterView.unity";
        private const string SOURCE_SCENE   = "Assets/00.Scenes/Release/ShinshiTown.unity";
        private const string ROOT_NAME      = "VillageUiPreview";
        private const string MAIL_ICON_PATH = "Assets/01.Scripts/05.Art/UI/HudMenuButton/Textures/T_HudMenuButton_IconMail.png";
        private static readonly string[] G_Windows = { "InventoryCanvas", "TradeCanvas", "AuctionCanvas", "SortiePrepCanvas", "PopupCanvas", "MailBoxCanvas", "CharDetailCanvas", "CharDetailPreviewStage" };

        [MenuItem("Shinshi/UI/마을 개발 씬 조작 연결")]
        public static void Build()
        {
            Scene target = SceneManager.GetActiveScene();
            if ((EditorApplication.isPlaying) || (target.path != TARGET_SCENE))
            {
                throw new InvalidOperationException("Dev_HUD/Dev_ShinshiVillageQuarterView 편집 모드에서 실행합니다");
            }
            GameObject previous = FindRoot(target, ROOT_NAME);
            if (previous != null)
            {
                Undo.DestroyObjectImmediate(previous);
            }
            var root = new GameObject(ROOT_NAME);
            Undo.RegisterCreatedObjectUndo(root, "마을 UI 조작 연결");
            Scene source = SceneManager.GetSceneByPath(SOURCE_SCENE);
            bool openedSource = !source.isLoaded;
            if (openedSource)
            {
                source = EditorSceneManager.OpenScene(SOURCE_SCENE, OpenSceneMode.Additive);
            }
            SceneManager.SetActiveScene(target);
            try
            {
                foreach (string name in G_Windows)
                {
                    GameObject original = FindRoot(source, name);
                    if (original == null)
                    {
                        throw new InvalidOperationException("기존 창을 찾지 못했습니다: " + name);
                    }
                    GameObject clone = UnityEngine.Object.Instantiate(original);
                    clone.name = name;
                    clone.SetActive(name != "CharDetailPreviewStage");
                    SceneManager.MoveGameObjectToScene(clone, target);
                    clone.transform.SetParent(root.transform, false);
                    RemoveExternalListeners(clone);
                    foreach (Canvas canvas in clone.GetComponentsInChildren<Canvas>(true))
                    {
                        if (canvas.renderMode != RenderMode.WorldSpace)
                        {
                            canvas.sortingOrder = name == "PopupCanvas" ? 500 : 100;
                        }
                    }
                }
            }
            finally
            {
                if (openedSource)
                {
                    EditorSceneManager.CloseScene(source, true);
                }
            }

            GameObject settingsAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/02.Prefab/UI/Settings/PF_SettingsWindow.prefab");
            var settingsCanvas = (GameObject)PrefabUtility.InstantiatePrefab(settingsAsset, target);
            settingsCanvas.transform.SetParent(root.transform, false);
            settingsCanvas.name = "SettingsCanvas";
            var controller = root.AddComponent<VillageUiPreview>();
            var router = root.AddComponent<HotkeyRouter>();
            Reference(router, "_config", AssetDatabase.LoadAssetAtPath<HotkeyConfigSO>("Assets/09.Data/UI/HotkeyConfig.asset"));
            var interaction = FindRoot(target, "HUDCanvas").GetComponentInChildren<PlayerInteractionPanelOpener>(true);
            Reference(controller, "_router", router);
            Reference(controller, "_interaction", interaction);
            Reference(controller, "_inventory", In<InventoryView>(root, "InventoryCanvas"));
            var auction = In<AuctionView>(root, "AuctionCanvas");
            Reference(controller, "_auction", auction);
            Reference(controller, "_mail", In<MailBoxView>(root, "MailBoxCanvas"));
            var trade = In<TradeView>(root, "TradeCanvas");
            Reference(controller, "_trade", trade);
            var tradeStatus = (TMP_Text)new SerializedObject(trade).FindProperty("_statusLabel").objectReferenceValue;
            tradeStatus.rectTransform.anchoredPosition = new Vector2(16f, -64f);
            Reference(controller, "_tradeInventory", root.transform.Find("TradeCanvas/Inventory").gameObject);
            var character = In<CharDetailView>(root, "CharDetailCanvas");
            Reference(controller, "_character", character);
            Reference(character, "_previewStage", root.transform.Find("CharDetailPreviewStage").gameObject);
            Reference(controller, "_settings", settingsCanvas.GetComponentInChildren<SettingsView>(true));
            var sortie = In<SortiePrepView>(root, "SortiePrepCanvas");
            Reference(controller, "_sortie", sortie);
            var sortieReady = (Button)new SerializedObject(sortie).FindProperty("_readyButton").objectReferenceValue;
            ((RectTransform)sortieReady.transform).anchoredPosition = new Vector2(270f, -12f);
            Reference(controller, "_sortieReadyControl", sortieReady.gameObject);
            var popup = In<UiPopupHub>(root, "PopupCanvas");
            foreach (ConfirmPopupView view in root.GetComponentsInChildren<ConfirmPopupView>(true))
            {
                CenterPopup((RectTransform)view.transform, root.transform.Find("PopupCanvas"));
            }
            foreach (AlertPopupView view in root.GetComponentsInChildren<AlertPopupView>(true))
            {
                CenterPopup((RectTransform)view.transform, root.transform.Find("PopupCanvas"));
            }
            Reference(controller, "_popup", popup);
            var auctionPreview = root.AddComponent<VillageAuctionPreview>();
            Reference(auctionPreview, "_view", auction);
            Reference(auctionPreview, "_popup", popup);
            Reference(controller, "_auctionPreview", auctionPreview);
            Reference(interaction, "_router", router);
            Flag(interaction, "_previewActions", true);
            var interactionData = new SerializedObject(interaction);
            var other = (RectTransform)interactionData.FindProperty("_otherPanel").objectReferenceValue;
            Reference(interaction, "_tradeButton", Button(other, "거래하기"));
            Reference(interaction, "_partyInviteButton", Button(other, "파티초대"));
            Reference(interaction, "_whisperButton", Button(other, "귓속말"));
            Reference(interaction, "_infoButton", Button(other, "캐릭터 정보"));
            var self = (RectTransform)interactionData.FindProperty("_selfPanel").objectReferenceValue;
            Connect(Button(self, "캐릭터 정보"), controller.OpenCharacter);

            GameObject hud = FindRoot(target, "HUDCanvas");
            var menu = hud.GetComponentInChildren<HudMenuDropdown>(true);
            Reference(menu, "_router", router);
            var menuData = new SerializedObject(menu);
            var panel = (RectTransform)menuData.FindProperty("_panel").objectReferenceValue;
            Button template = Button(panel, "거래");
            AddMenu(panel, template, "우편함", "우편함", new Vector2(276f, -296f));
            AddMenu(panel, template, "출정 준비", "출정 준비", new Vector2(276f, -580f));
            MenuIcon(Button(panel, "우편함"), true);
            MenuIcon(Button(panel, "거래"), false);
            MenuIcon(Button(panel, "출정 준비"), false);
            Connect(Button(panel, "캐릭터 정보"), controller.OpenCharacter);
            Connect(Button(panel, "인벤토리"), controller.OpenInventory);
            Connect(Button(panel, "특성"), controller.OpenCharacter);
            Connect(Button(panel, "경매장"), controller.OpenAuction);
            Connect(Button(panel, "거래"), controller.OpenTradeMenu);
            Connect(Button(panel, "우편함"), controller.OpenMail);
            Connect(Button(panel, "출정 준비"), controller.OpenSortie);
            Connect(Button(panel, "환경설정"), controller.OpenSettings);
            Connect(Button(panel, "캐릭터 선택"), controller.OpenCharacterSelect);
            Connect(Button(panel, "게임 종료"), controller.OpenExitConfirm);
            var chat = hud.GetComponentInChildren<HudChatView>(true);
            if (chat != null)
            {
                Reference(chat, "_router", router);
            }
            Transform legacyInventory = hud.transform.Find("InventoryPanel");
            if (legacyInventory != null)
            {
                legacyInventory.gameObject.SetActive(false);
            }
            var inventory = In<InventoryView>(root, "InventoryCanvas");
            Flag(inventory, "_enableSlotInteraction", true);
            foreach (MonoBehaviour view in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if ((view is InventoryView) || (view is AuctionView) || (view is MailBoxView) || (view is TradeView)
                    || (view is CharDetailView) || (view is SortiePrepView))
                {
                    OpaqueBackground((RectTransform)view.transform);
                }
            }
            foreach (Selectable control in root.GetComponentsInChildren<Selectable>(true))
            {
                if (control.targetGraphic != null)
                {
                    control.targetGraphic.raycastTarget = true;
                }
            }
            foreach (Button button in inventory.GetComponentsInChildren<Button>(true))
            {
                if ((button.name == "DismantleButton") || (button.name == "StorageButton"))
                {
                    button.interactable = false;
                }
            }
            foreach (var view in root.GetComponentsInChildren<InventoryView>(true))
            {
                view.SetOpen(false);
            }
            foreach (var view in root.GetComponentsInChildren<AuctionView>(true))
            {
                view.SetOpen(false);
            }
            foreach (var view in root.GetComponentsInChildren<MailBoxView>(true))
            {
                view.SetOpen(false);
            }
            foreach (var view in root.GetComponentsInChildren<TradeView>(true))
            {
                view.SetOpen(false);
            }
            character.SetOpen(false);
            In<SortiePrepView>(root, "SortiePrepCanvas").gameObject.SetActive(false);
            settingsCanvas.GetComponentInChildren<SettingsView>(true).gameObject.SetActive(false);
            panel.gameObject.SetActive(false);
            EditorSceneManager.MarkSceneDirty(target);
            EditorSceneManager.SaveScene(target);
            Selection.activeGameObject = root;
            Debug.Log("[VillageUI] 마을 UI 미리보기 연결 완료");
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                {
                    return root;
                }
            }
            return null;
        }

        private static T In<T>(GameObject root, string canvas) where T : Component
        {
            T component = root.transform.Find(canvas).GetComponentInChildren<T>(true);
            if (component == null)
            {
                throw new InvalidOperationException(canvas + "에 " + typeof(T).Name + "이 없습니다");
            }
            return component;
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
            throw new InvalidOperationException("버튼을 찾지 못했습니다: " + name);
        }

        private static void Connect(Button button, UnityAction action)
        {
            Clear(button.onClick);
            UnityEventTools.AddPersistentListener(button.onClick, action);
            button.interactable = true;
        }

        private static void AddMenu(Transform panel, Button template, string name, string text, Vector2 position)
        {
            Button item = null;
            foreach (Button existing in panel.GetComponentsInChildren<Button>(true))
            {
                if (existing.name == name)
                {
                    item = existing;
                    break;
                }
            }
            if (item == null)
            {
                item = UnityEngine.Object.Instantiate(template, template.transform.parent);
                item.name = name;
            }
            ((RectTransform)item.transform).anchoredPosition = position;
            item.GetComponentInChildren<TMP_Text>(true).text = text;
            UiLayoutNote note = item.GetComponent<UiLayoutNote>();
            if (note != null)
            {
                note.Apply("village-preview-" + name, "마을 개발 씬 메뉴", UiLayoutNote.ECondition.Always);
            }
        }

        private static void Reference(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var data = new SerializedObject(target);
            data.FindProperty(field).objectReferenceValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void MenuIcon(Button button, bool envelope)
        {
            var icon = (RectTransform)button.transform.Find("Icon");
            var image = icon.GetComponent<Image>();
            Sprite mailSprite = null;
            if (envelope)
            {
                mailSprite = AssetDatabase.LoadAssetAtPath<Sprite>(MAIL_ICON_PATH);
                if (mailSprite == null)
                {
                    throw new InvalidOperationException("우편함 메뉴 Sprite를 찾지 못했습니다: " + MAIL_ICON_PATH);
                }
            }
            Transform previous = icon.Find("PreviewIcon");
            if (previous != null)
            {
                UnityEngine.Object.DestroyImmediate(previous.gameObject);
            }
            if (envelope)
            {
                image.sprite = mailSprite;
                image.color = Color.white;
                image.preserveAspect = true;
                image.raycastTarget = false;
                image.enabled = true;
                return;
            }
            image.enabled = false;
            var drawing = new GameObject("PreviewIcon", typeof(RectTransform));
            drawing.transform.SetParent(icon, false);
            IconLine(drawing.transform, new Vector2(-10f, 5f), new Vector2(10f, 5f));
            IconLine(drawing.transform, new Vector2(5f, 10f), new Vector2(10f, 5f));
            IconLine(drawing.transform, new Vector2(5f, 0f), new Vector2(10f, 5f));
            IconLine(drawing.transform, new Vector2(10f, -5f), new Vector2(-10f, -5f));
            IconLine(drawing.transform, new Vector2(-5f, 0f), new Vector2(-10f, -5f));
            IconLine(drawing.transform, new Vector2(-5f, -10f), new Vector2(-10f, -5f));
        }

        private static void IconLine(Transform parent, Vector2 start, Vector2 end)
        {
            var line = new GameObject("Line", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)line.transform;
            rect.SetParent(parent, false);
            Vector2 delta = end - start;
            rect.anchoredPosition = (start + end) * 0.5f;
            rect.sizeDelta = new Vector2(delta.magnitude, 2f);
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            var image = line.GetComponent<Image>();
            image.color = new Color(0.8f, 0.82f, 0.82f);
            image.raycastTarget = false;
        }

        private static void Flag(UnityEngine.Object target, string field, bool value)
        {
            var data = new SerializedObject(target);
            data.FindProperty(field).boolValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Clear(UnityEventBase action)
        {
            for (int i = action.GetPersistentEventCount() - 1; i >= 0; --i)
            {
                UnityEventTools.RemovePersistentListener(action, i);
            }
        }

        private static void RemoveExternalListeners(GameObject root)
        {
            foreach (Button view in root.GetComponentsInChildren<Button>(true))
            {
                Clear(view.onClick);
            }
            foreach (Toggle view in root.GetComponentsInChildren<Toggle>(true))
            {
                Clear(view.onValueChanged);
            }
            foreach (TMP_Dropdown view in root.GetComponentsInChildren<TMP_Dropdown>(true))
            {
                Clear(view.onValueChanged);
            }
            foreach (TMP_InputField view in root.GetComponentsInChildren<TMP_InputField>(true))
            {
                Clear(view.onValueChanged);
                Clear(view.onEndEdit);
                Clear(view.onSubmit);
            }
        }

        private static void OpaqueBackground(RectTransform window)
        {
            var go = new GameObject("PreviewWindowBackground", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(window, false);
            rect.SetAsFirstSibling();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var image = go.GetComponent<Image>();
            image.color = new Color(0.035f, 0.047f, 0.06f, 0.97f);
            image.raycastTarget = true;
        }

        private static void CenterPopup(RectTransform popup, Transform canvas)
        {
            Vector2 size = popup.rect.size;
            popup.SetParent(canvas, false);
            popup.anchorMin = new Vector2(0.5f, 0.5f);
            popup.anchorMax = popup.anchorMin;
            popup.pivot = popup.anchorMin;
            popup.sizeDelta = size;
            popup.anchoredPosition = Vector2.zero;
            OpaqueBackground(popup);
            var backdrop = new GameObject("PreviewModalBackdrop", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)backdrop.transform;
            rect.SetParent(popup, false);
            rect.SetAsFirstSibling();
            rect.sizeDelta = new Vector2(10000f, 10000f);
            backdrop.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
        }
    }
}
#endif
