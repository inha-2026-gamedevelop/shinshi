// System
using System;

// Unity
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Network;
using Shinshi.UI;

namespace Shinshi.Editor
{
    /// <summary> 캐릭터 선택 씬에 환경설정 버튼과 설정 창을 연결한다 </summary>
    public static class CharacterSelectSettingsWiring
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SCENE_PATH    = "Assets/00.Scenes/Release/CharacterSelect.unity";
        private const string SETTINGS_PATH = "Assets/02.Prefab/UI/Settings/PF_SettingsWindow.prefab";
        private const string BUTTON_NAME   = "환경설정 버튼";
        private const string BUTTON_LABEL  = "환경설정";
        private const string CANVAS_NAME   = "SettingsCanvas";
        private static readonly Vector2 G_Margin = new Vector2(-48f, -48f);

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem("Shinshi/UI/Wire Character Select Settings")]
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
            var screen = UnityEngine.Object.FindAnyObjectByType<CharacterSelectScreen>();
            if (screen == null)
            {
                throw new InvalidOperationException("CharacterSelectScreen이 씬에 없습니다.");
            }

            var data = new SerializedObject(screen);
            var back = (Button)data.FindProperty("_backButton").objectReferenceValue;
            Transform parent = back.transform.parent;

            Transform existing = parent.Find(BUTTON_NAME);
            Button button = (existing != null) ? existing.GetComponent<Button>() : CreateButton(back, parent);

            SettingsView settings = UnityEngine.Object.FindAnyObjectByType<SettingsView>(FindObjectsInactive.Include);
            if (settings == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SETTINGS_PATH);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.name = CANVAS_NAME;
                settings = instance.GetComponentInChildren<SettingsView>(true);
            }
            settings.gameObject.SetActive(false);

            data.FindProperty("_settingsButton").objectReferenceValue = button;
            data.FindProperty("_settings").objectReferenceValue       = settings;
            data.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[CharacterSelect] 환경설정 버튼과 설정 창을 연결했습니다.");
        }

        // 뒤로 가기 버튼 모양을 그대로 복제해 오른쪽 위에 둔다
        private static Button CreateButton(Button template, Transform parent)
        {
            GameObject clone = UnityEngine.Object.Instantiate(template.gameObject, parent);
            clone.name = BUTTON_NAME;
            var rect = (RectTransform)clone.transform;
            rect.anchorMin        = Vector2.one;
            rect.anchorMax        = Vector2.one;
            rect.pivot            = Vector2.one;
            rect.anchoredPosition = G_Margin;

            Button button = clone.GetComponent<Button>();
            while (button.onClick.GetPersistentEventCount() > 0)
            {
                UnityEventTools.RemovePersistentListener(button.onClick, 0);
            }
            foreach (TMP_Text label in clone.GetComponentsInChildren<TMP_Text>(true))
            {
                label.text = BUTTON_LABEL;
            }
            return button;
        }
    }
}
