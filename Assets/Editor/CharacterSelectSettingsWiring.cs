// System
using System;

// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
        private const string CANVAS_NAME   = "SettingsCanvas";

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

            Transform bottom = parent.Find("하단 버튼 그룹/" + BUTTON_NAME);
            Button button = (bottom != null) ? bottom.GetComponent<Button>() : null;
            if (button == null)
            {
                throw new InvalidOperationException("하단 환경설정 버튼이 없습니다.");
            }
            button.interactable = true;

            Transform existing = parent.Find(BUTTON_NAME);
            if (existing != null)
            {
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            }

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

    }
}
