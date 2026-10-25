#if UNITY_EDITOR
// System
using System;

// Unity
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 프로젝트 내부
using Shinshi.Systems;

namespace Shinshi.UI.Editor
{
    /// <summary> 출시 마을, 던전 씬의 팝업, 거래창, 결과 화면, 장비 강화 창이 Esc로 닫히게 잇고 장비 메뉴 두 버튼이 옵션 창 토글을 거치게 한다 </summary>
    public static class WindowEscapeWiring
    {
        // 이미 붙은 것은 다시 만들지 않고 참조만 맞춘다. 장비 강화, 옵션 재설정 창은 마을에만 있다

        /****************************************
        *                Fields
        ****************************************/

        private const string MENU_ENHANCE = "장비 강화";
        private const string MENU_OPTION  = "옵션 재설정";

        private static readonly string[] G_ScenePaths =
        {
            "Assets/00.Scenes/Release/ShinshiTown.unity",
            "Assets/00.Scenes/Release/DungeonForest.unity",
            "Assets/00.Scenes/Release/DungeonMucheol.unity",
            "Assets/00.Scenes/Release/DungeonWolbaek.unity"
        };

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem("Shinshi/UI/Wire Window Escape")]
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

            for (int i = 0; i < G_ScenePaths.Length; ++i)
            {
                Scene scene = EditorSceneManager.OpenScene(G_ScenePaths[i], OpenSceneMode.Single);
                WireScene(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            Debug.Log("[UI] 출시 씬 팝업, 거래창, 결과 화면, 장비 강화 창의 Esc 닫기를 연결했습니다.");
        }

        private static void WireScene(Scene scene)
        {
            HotkeyRouter router = InScene<HotkeyRouter>(scene, true);
            Reference(InScene<UiPopupHub>(scene, true), "_router", router);

            TradeView trade = InScene<TradeView>(scene, true);
            Attach(trade.gameObject, router, Field<Button>(trade, "_closeButton"));

            var result = new SerializedObject(InScene<SortieResultBinder>(scene, true));
            var window = (GameObject)result.FindProperty("_window").objectReferenceValue;
            var view   = (ResultScreenView)result.FindProperty("_view").objectReferenceValue;
            Attach(window, router, Field<Button>(view, "_returnButton"));

            EquipmentEnhanceView enhance = InScene<EquipmentEnhanceView>(scene, false);
            if (enhance == null)
            {
                return;
            }

            Attach(enhance.gameObject, router, Field<Button>(enhance, "_closeButton"));
            HotkeyWindowToggle option = OptionToggle(router);
            HudMenuDropdown    menu   = InScene<HudMenuDropdown>(scene, true);
            var panel = Field<RectTransform>(menu, "_panel");

            // 옵션 창을 토글로 열어야 토글이 Esc 처리기를 등록한다. 한쪽을 열 때 다른 쪽은 각 창의 닫기 규칙으로 닫는다
            Button enhanceButton = MenuButton(panel, MENU_ENHANCE);
            ClearPersistent(enhanceButton);
            UnityEventTools.AddBoolPersistentListener(enhanceButton.onClick, option.SetOpen, false);
            UnityEventTools.AddBoolPersistentListener(enhanceButton.onClick, enhance.gameObject.SetActive, true);

            Button optionButton = MenuButton(panel, MENU_OPTION);
            ClearPersistent(optionButton);
            UnityEventTools.AddPersistentListener(optionButton.onClick, enhance.Close);
            UnityEventTools.AddBoolPersistentListener(optionButton.onClick, option.SetOpen, true);
        }

        private static void Attach(GameObject window, HotkeyRouter router, Button closeButton)
        {
            if ((window == null) || (closeButton == null))
            {
                throw new InvalidOperationException("Esc로 닫을 창이나 닫기 버튼이 없습니다.");
            }

            if (!window.TryGetComponent(out WindowEscapeButton escape))
            {
                escape = window.AddComponent<WindowEscapeButton>();
            }

            Reference(escape, "_router", router);
            Reference(escape, "_closeButton", closeButton);
        }

        private static HotkeyWindowToggle OptionToggle(HotkeyRouter router)
        {
            foreach (HotkeyWindowToggle toggle in router.GetComponents<HotkeyWindowToggle>())
            {
                if (new SerializedObject(toggle).FindProperty("_action").intValue == (int)EHotkeyAction.EquipmentOption)
                {
                    return toggle;
                }
            }

            throw new InvalidOperationException("옵션 재설정 창 토글이 없습니다.");
        }

        private static Button MenuButton(Transform panel, string name)
        {
            foreach (Button button in panel.GetComponentsInChildren<Button>(true))
            {
                if (button.name == name)
                {
                    return button;
                }
            }

            throw new InvalidOperationException(name + " 버튼이 없습니다.");
        }

        private static void ClearPersistent(Button button)
        {
            while (button.onClick.GetPersistentEventCount() > 0)
            {
                UnityEventTools.RemovePersistentListener(button.onClick, 0);
            }

            EditorUtility.SetDirty(button);
        }

        private static T InScene<T>(Scene scene, bool required) where T : Component
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
                throw new InvalidOperationException(typeof(T).Name + "이 " + scene.name + " 씬에 없습니다.");
            }

            return null;
        }

        private static T Field<T>(UnityEngine.Object target, string field) where T : UnityEngine.Object
        {
            return new SerializedObject(target).FindProperty(field).objectReferenceValue as T;
        }

        private static void Reference(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var data = new SerializedObject(target);
            data.FindProperty(field).objectReferenceValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
