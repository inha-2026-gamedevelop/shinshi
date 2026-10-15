#if UNITY_EDITOR
// System
using System;

// Unity
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Data;

namespace Shinshi.UI.Editor
{
    /// <summary> 기존 개발 씬과 분리된 성장 UI 검수 씬을 만든다 </summary>
    public static class EquipmentGrowthPreviewBuilder
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string SCENE_PATH = "Assets/00.Scenes/Dev_HUD/Dev_EquipmentGrowthStyle.unity";
        private const string ENHANCE_PATH = "Assets/02.Prefab/UI/EquipmentEnhance/PF_EquipmentEnhanceWindow.prefab";
        private const string OPTION_PATH = "Assets/02.Prefab/UI/Windows/PF_EquipmentOptionWindow.prefab";
        private const string CATALOG_PATH = "Assets/09.Data/Growth/EquipmentCatalog.asset";
        private const string FONT_PATH = "Assets/01.Scripts/06.UI/Hud/Fonts/HudQuestKorean-Bold.asset";
        private const float WIDTH = 1440f;
        private const float HEIGHT = 900f;
        private const float TAB_WIDTH = 180f;
        private const float TAB_HEIGHT = 40f;

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem("Shinshi/UI/성장 UI 스타일 검수 씬 만들기")]
        public static void Build()
        {
            if (SceneManager.GetActiveScene().isDirty)
            {
                throw new InvalidOperationException("현재 씬에 저장하지 않은 변경이 있습니다");
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Camera camera = Camera.main;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = EquipmentGrowthStyle.Canvas;
            GameObject canvasObject = new GameObject("GrowthStyleCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(WIDTH, HEIGHT);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            GameObject enhance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ENHANCE_PATH), canvas.transform);
            GameObject option = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(OPTION_PATH), canvas.transform);
            enhance.name = "EnhancePreview";
            option.name = "OptionPreview";
            GameObject helper = new GameObject("PreviewData");
            helper.SetActive(false);
            EquipmentCatalogSO catalog = AssetDatabase.LoadAssetAtPath<EquipmentCatalogSO>(CATALOG_PATH);
            EquipmentEnhancePreviewBinder enhanceBinder = helper.AddComponent<EquipmentEnhancePreviewBinder>();
            Assign(enhanceBinder, "_view", enhance.GetComponent<EquipmentEnhanceView>());
            Assign(enhanceBinder, "_catalog", catalog);
            EquipmentOptionPreviewBinder optionBinder = helper.AddComponent<EquipmentOptionPreviewBinder>();
            Assign(optionBinder, "_view", option.GetComponent<EquipmentOptionView>());
            Assign(optionBinder, "_catalog", catalog);
            helper.SetActive(true);
            Tab(canvasObject.transform, "강화", -TAB_WIDTH, enhance, option);
            Tab(canvasObject.transform, "옵션 재설정", 0f, option, enhance);
            enhance.SetActive(true);
            option.SetActive(false);
            EditorSceneManager.SaveScene(scene, SCENE_PATH);
            Selection.activeGameObject = enhance;
        }

        private static void Tab(Transform parent, string title, float x, GameObject show, GameObject hide)
        {
            GameObject obj = new GameObject(title + " 미리보기", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rect = (RectTransform)obj.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = rect.anchorMin;
            rect.pivot = Vector2.up;
            rect.anchoredPosition = new Vector2(x, -20f);
            rect.sizeDelta = new Vector2(TAB_WIDTH, TAB_HEIGHT);
            Button button = obj.GetComponent<Button>();
            button.targetGraphic = obj.GetComponent<Image>();
            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform labelRect = (RectTransform)labelObject.transform;
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            TMP_Text label = labelObject.GetComponent<TMP_Text>();
            label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_PATH);
            label.fontSize = 18f;
            label.text = title;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            EquipmentGrowthStyle.Primary(button);
            UnityEventTools.AddBoolPersistentListener(button.onClick, hide.SetActive, false);
            UnityEventTools.AddBoolPersistentListener(button.onClick, show.SetActive, true);
        }

        private static void Assign(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
