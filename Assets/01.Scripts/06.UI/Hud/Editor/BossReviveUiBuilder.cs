#if UNITY_EDITOR
// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.UI.Editor
{
    /// <summary> 보스 맵 두 곳(무철, 월백)의 HUD에 부활 창 프리팹을 만들어 놓는다 </summary>
    // 다시 실행하면 프리팹을 새로 쓰고, 씬에 이미 놓인 인스턴스는 지운 뒤 다시 놓아 연결을 고친다
    public static class BossReviveUiBuilder
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string PREFAB_PATH = "Assets/02.Prefab/UI/HUD/PF_BossRevivePanel.prefab";

        private const string ROOT_NAME    = "BossRevive";
        private const string CANVAS_NAME  = "HUDCanvas";
        private const string FONT_BOLD    = "Assets/01.Scripts/06.UI/Settings/Fonts/SettingsKorean-Bold.asset";
        private const string FONT_REGULAR = "Assets/01.Scripts/06.UI/Settings/Fonts/SettingsKorean-Regular.asset";
        private const string SPRITE_PATH  = "UI/Skin/UISprite.psd";

        private const float PANEL_WIDTH   = 340f;
        private const float PANEL_HEIGHT  = 380f;
        private const float CONTENT_WIDTH = PANEL_WIDTH - 40f;

        private static readonly Color G_PanelColor   = new Color(0.06f, 0.07f, 0.10f, 0.94f);
        private static readonly Color G_BorderColor  = new Color(0.86f, 0.68f, 0.30f, 0.95f);
        private static readonly Color G_LineColor    = new Color(0.86f, 0.68f, 0.30f, 0.35f);
        private static readonly Color G_ButtonColor  = new Color(0.20f, 0.36f, 0.55f, 1f);
        private static readonly Color G_TextColor    = new Color(0.93f, 0.92f, 0.88f, 1f);
        private static readonly Color G_SubTextColor = new Color(0.75f, 0.75f, 0.72f, 1f);

        private static readonly string[] G_Scenes =
        {
            Constants.Scene.RELEASE_FOLDER + Constants.Scene.DUNGEON_MUCHEOL + Constants.Scene.EXTENSION,
            Constants.Scene.RELEASE_FOLDER + Constants.Scene.DUNGEON_WOLBAEK + Constants.Scene.EXTENSION
        };

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem("Shinshi/UI/보스 부활 창 배치")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            GameObject prefab = BuildPrefab();
            foreach (string path in G_Scenes)
            {
                PlaceInScene(path, prefab);
            }
        }

        private static GameObject BuildPrefab()
        {
            TMP_FontAsset bold    = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_BOLD);
            TMP_FontAsset regular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_REGULAR);
            Sprite        sprite  = AssetDatabase.GetBuiltinExtraResource<Sprite>(SPRITE_PATH);

            var root = new GameObject(ROOT_NAME, typeof(RectTransform));
            try
            {
                var rootRect = (RectTransform)root.transform;
                Stretch(rootRect);

                // 화면 오른쪽 가운데, 보스 체력바와 스킬 칸을 가리지 않는 자리. 창 위 클릭이 전장으로 새지 않게 배경이 받는다
                Image panel = MakeImage(rootRect, "Panel", sprite, G_BorderColor);
                Place(panel.rectTransform, new Vector2(1f, 0.5f), new Vector2(-40f, 40f), new Vector2(PANEL_WIDTH, PANEL_HEIGHT),
                    new Vector2(1f, 0.5f));
                panel.raycastTarget = true;
                Image inner = MakeImage(panel.rectTransform, "Inner", sprite, G_PanelColor);
                Stretch(inner.rectTransform, 3f);
                RectTransform content = inner.rectTransform;

                TMP_Text title = MakeText(content, "Title", "사망하였습니다.", bold, 24f, G_TextColor);
                PlaceTop(title.rectTransform, -22f, 34f);
                Image topLine = MakeImage(content, "TopLine", sprite, G_LineColor);
                PlaceTop(topLine.rectTransform, -66f, 2f);

                TMP_Text time = MakeText(content, "Time", "10초", bold, 44f, G_BorderColor);
                PlaceTop(time.rectTransform, -84f, 56f);

                TMP_Text body = MakeText(content, "Body", "대기 시간이 끝나면\n부활할 수 있습니다.", regular, 17f, G_SubTextColor);
                body.textWrappingMode = TextWrappingModes.Normal;
                PlaceTop(body.rectTransform, -150f, 52f);

                Image buttonImage = MakeImage(content, "ReviveButton", sprite, G_ButtonColor);
                buttonImage.raycastTarget = true;
                var button = buttonImage.gameObject.AddComponent<Button>();
                button.targetGraphic = buttonImage;
                PlaceTop(buttonImage.rectTransform, -222f, 70f, CONTENT_WIDTH - 40f);
                TMP_Text label = MakeText(buttonImage.rectTransform, "Label", "부활", bold, 24f, G_TextColor);
                Place(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(200f, 32f), new Vector2(0.5f, 1f));
                TMP_Text count = MakeText(buttonImage.rectTransform, "Count", "공용 x 3", regular, 16f, G_SubTextColor);
                Place(count.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(200f, 24f), new Vector2(0.5f, 0f));

                Image bottomLine = MakeImage(content, "BottomLine", sprite, G_LineColor);
                PlaceTop(bottomLine.rectTransform, -312f, 2f);
                TMP_Text footer = MakeText(content, "Footer", "부활 시 파티 공용 부활 횟수가 1회 소모됩니다.", regular, 14f, G_SubTextColor);
                PlaceTop(footer.rectTransform, -324f, 24f);

                var view = root.AddComponent<BossRevivePanelView>();
                var data = new SerializedObject(view);
                data.FindProperty("_panel").objectReferenceValue        = panel.gameObject;
                data.FindProperty("_titleText").objectReferenceValue    = title;
                data.FindProperty("_timeText").objectReferenceValue     = time;
                data.FindProperty("_bodyText").objectReferenceValue     = body;
                data.FindProperty("_reviveButton").objectReferenceValue = button;
                data.FindProperty("_countText").objectReferenceValue    = count;
                data.FindProperty("_footerText").objectReferenceValue   = footer;
                data.ApplyModifiedPropertiesWithoutUndo();

                var binder     = root.AddComponent<BossReviveBinder>();
                var binderData = new SerializedObject(binder);
                binderData.FindProperty("_view").objectReferenceValue = view;
                binderData.ApplyModifiedPropertiesWithoutUndo();

                // 쓰러졌을 때만 보인다
                panel.gameObject.SetActive(false);
                return PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void PlaceInScene(string path, GameObject prefab)
        {
            Scene     scene  = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Transform canvas = FindInScene(scene, CANVAS_NAME);
            if (canvas == null)
            {
                Debug.LogError("[UI] HUD 캔버스를 찾지 못해 부활 창을 놓지 않았다: " + path);
                return;
            }

            Transform existing = canvas.Find(ROOT_NAME);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvas);
            instance.name = ROOT_NAME;
            instance.transform.SetAsLastSibling();

            var popups = Object.FindFirstObjectByType<UiPopupHub>(FindObjectsInactive.Include);
            var data   = new SerializedObject(instance.GetComponent<BossReviveBinder>());
            data.FindProperty("_popups").objectReferenceValue = popups;
            data.ApplyModifiedPropertiesWithoutUndo();
            if (popups == null)
            {
                Debug.LogWarning("[UI] 팝업 허브가 없어 부활 거절 안내 없이 놓는다: " + path);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[UI] 부활 창을 놓았다: " + path);
        }

        private static Transform FindInScene(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                {
                    return root.transform;
                }

                Transform found = FindDeep(root.transform, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; ++i)
            {
                Transform child = parent.GetChild(i);
                if (child.name == name)
                {
                    return child;
                }

                Transform found = FindDeep(child, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Image MakeImage(RectTransform parent, string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite        = sprite;
            image.type          = ((sprite != null) && (sprite.border != Vector4.zero)) ? Image.Type.Sliced : Image.Type.Simple;
            image.color         = color;
            image.raycastTarget = false;
            return image;
        }

        private static TMP_Text MakeText(RectTransform parent, string name, string value, TMP_FontAsset font, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.text             = value;
            text.font             = font;
            text.fontSize         = size;
            text.color            = color;
            text.alignment        = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget    = false;
            return text;
        }

        // 창 위쪽에서 y만큼 내려간 자리에 가운데 정렬로 놓는다
        private static void PlaceTop(RectTransform rect, float y, float height, float width = CONTENT_WIDTH)
        {
            Place(rect, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(width, height), new Vector2(0.5f, 1f));
        }

        private static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot)
        {
            rect.anchorMin        = anchor;
            rect.anchorMax        = anchor;
            rect.pivot            = pivot;
            rect.sizeDelta        = size;
            rect.anchoredPosition = position;
        }
    }
}
#endif
