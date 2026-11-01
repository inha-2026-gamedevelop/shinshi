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
    /// <summary> 보스 맵 두 곳(무철, 월백)의 HUD에 토벌 중단 버튼과 투표 창 프리팹을 만들어 놓는다 </summary>
    // 다시 실행하면 프리팹을 새로 쓰고, 씬에 이미 놓인 인스턴스는 지운 뒤 다시 놓아 연결을 고친다
    public static class BossStopVoteUiBuilder
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string PREFAB_PATH = "Assets/02.Prefab/UI/HUD/PF_BossStopVotePanel.prefab";

        private const string ROOT_NAME    = "BossStopVote";
        private const string CANVAS_NAME  = "HUDCanvas";
        private const string FONT_BOLD    = "Assets/01.Scripts/06.UI/Settings/Fonts/SettingsKorean-Bold.asset";
        private const string FONT_REGULAR = "Assets/01.Scripts/06.UI/Settings/Fonts/SettingsKorean-Regular.asset";
        private const string SPRITE_PATH  = "UI/Skin/UISprite.psd";
        private const string KNOB_PATH    = "UI/Skin/Knob.psd";
        private const int    MARK_COUNT   = Constants.Session.MAX_PARTY_SIZE;

        private static readonly Color G_PanelColor    = new Color(0.07f, 0.08f, 0.10f, 0.92f);
        private static readonly Color G_BorderColor   = new Color(0.78f, 0.62f, 0.36f, 0.9f);
        private static readonly Color G_OpenColor     = new Color(0.45f, 0.14f, 0.12f, 0.95f);
        private static readonly Color G_AgreeColor    = new Color(0.20f, 0.46f, 0.26f, 1f);
        private static readonly Color G_DisagreeColor = new Color(0.50f, 0.17f, 0.15f, 1f);
        private static readonly Color G_TextColor     = new Color(0.93f, 0.92f, 0.88f, 1f);
        private static readonly Color G_SubTextColor  = new Color(0.72f, 0.72f, 0.70f, 1f);

        private static readonly string[] G_Scenes =
        {
            Constants.Scene.RELEASE_FOLDER + Constants.Scene.DUNGEON_MUCHEOL + Constants.Scene.EXTENSION,
            Constants.Scene.RELEASE_FOLDER + Constants.Scene.DUNGEON_WOLBAEK + Constants.Scene.EXTENSION
        };

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem("Shinshi/UI/보스 토벌 중단 투표 UI 배치")]
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
            Sprite        knob    = AssetDatabase.GetBuiltinExtraResource<Sprite>(KNOB_PATH);

            var root = new GameObject(ROOT_NAME, typeof(RectTransform));
            try
            {
                var rootRect = (RectTransform)root.transform;
                Stretch(rootRect);

                // 오른쪽 위, 메뉴 버튼 아래에 둔다
                Button open = MakeButton(rootRect, "OpenButton", "토벌 중단", bold, sprite, G_OpenColor, 18f);
                Place((RectTransform)open.transform, new Vector2(1f, 1f), new Vector2(-24f, -96f), new Vector2(132f, 40f));

                // 보스 체력바 아래 가운데
                Image panel = MakeImage(rootRect, "VotePanel", sprite, G_BorderColor);
                Place(panel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(460f, 176f));
                Image inner = MakeImage(panel.rectTransform, "Inner", sprite, G_PanelColor);
                Stretch(inner.rectTransform, 2f);

                TMP_Text title = MakeText(inner.rectTransform, "Title", "토벌 중단 투표", bold, 22f, G_TextColor, TextAlignmentOptions.Left);
                Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -14f), new Vector2(260f, 30f));
                TMP_Text time = MakeText(inner.rectTransform, "Time", "30초", bold, 20f, G_BorderColor, TextAlignmentOptions.Right);
                Place(time.rectTransform, new Vector2(1f, 1f), new Vector2(-20f, -14f), new Vector2(120f, 30f));

                TMP_Text count = MakeText(inner.rectTransform, "Count", "찬성 1 / 4", bold, 18f, G_TextColor, TextAlignmentOptions.Left);
                Place(count.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -52f), new Vector2(160f, 28f));

                var marks = new Image[MARK_COUNT];
                for (int i = 0; i < MARK_COUNT; ++i)
                {
                    marks[i] = MakeImage(inner.rectTransform, "Mark" + i, knob, G_SubTextColor);
                    Place(marks[i].rectTransform, new Vector2(0f, 1f), new Vector2(184f + (i * 30f), -55f), new Vector2(22f, 22f));
                }

                TMP_Text status = MakeText(inner.rectTransform, "Status", "파티원 전원이 찬성하면 보상 없이 토벌을 마칩니다", regular, 15f,
                    G_SubTextColor, TextAlignmentOptions.Left);
                Place(status.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -86f), new Vector2(420f, 24f));

                Button agree    = MakeButton(inner.rectTransform, "AgreeButton", "찬성", bold, sprite, G_AgreeColor, 18f);
                Place((RectTransform)agree.transform, new Vector2(0.5f, 0f), new Vector2(-110f, 14f), new Vector2(200f, 40f), new Vector2(0.5f, 0f));
                Button disagree = MakeButton(inner.rectTransform, "DisagreeButton", "반대", bold, sprite, G_DisagreeColor, 18f);
                Place((RectTransform)disagree.transform, new Vector2(0.5f, 0f), new Vector2(110f, 14f), new Vector2(200f, 40f), new Vector2(0.5f, 0f));

                var view = root.AddComponent<BossStopVoteView>();
                var data = new SerializedObject(view);
                data.FindProperty("_openButton").objectReferenceValue     = open;
                data.FindProperty("_panel").objectReferenceValue          = panel.gameObject;
                data.FindProperty("_countText").objectReferenceValue      = count;
                data.FindProperty("_timeText").objectReferenceValue       = time;
                data.FindProperty("_statusText").objectReferenceValue     = status;
                data.FindProperty("_agreeButton").objectReferenceValue    = agree;
                data.FindProperty("_disagreeButton").objectReferenceValue = disagree;
                SerializedProperty markList = data.FindProperty("_marks");
                markList.arraySize = MARK_COUNT;
                for (int i = 0; i < MARK_COUNT; ++i)
                {
                    markList.GetArrayElementAtIndex(i).objectReferenceValue = marks[i];
                }

                data.ApplyModifiedPropertiesWithoutUndo();

                var binder     = root.AddComponent<BossStopVoteBinder>();
                var binderData = new SerializedObject(binder);
                binderData.FindProperty("_view").objectReferenceValue = view;
                binderData.ApplyModifiedPropertiesWithoutUndo();

                // 투표가 열릴 때만 보인다
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
                Debug.LogError("[UI] HUD 캔버스를 찾지 못해 토벌 중단 투표 UI를 놓지 않았다: " + path);
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
            var data   = new SerializedObject(instance.GetComponent<BossStopVoteBinder>());
            data.FindProperty("_popups").objectReferenceValue = popups;
            data.ApplyModifiedPropertiesWithoutUndo();
            if (popups == null)
            {
                Debug.LogWarning("[UI] 팝업 허브가 없어 토벌 중단 확인 창 없이 바로 제안한다: " + path);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[UI] 토벌 중단 투표 UI를 놓았다: " + path);
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

        private static Button MakeButton(RectTransform parent, string name, string label, TMP_FontAsset font, Sprite sprite,
            Color color, float size)
        {
            Image image  = MakeImage(parent, name, sprite, color);
            var   button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            TMP_Text text = MakeText(image.rectTransform, "Label", label, font, size, G_TextColor, TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            return button;
        }

        private static Image MakeImage(RectTransform parent, string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.type   = ((sprite != null) && (sprite.border != Vector4.zero)) ? Image.Type.Sliced : Image.Type.Simple;
            image.color  = color;
            return image;
        }

        private static TMP_Text MakeText(RectTransform parent, string name, string value, TMP_FontAsset font, float size, Color color,
            TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.text             = value;
            text.font             = font;
            text.fontSize         = size;
            text.color            = color;
            text.alignment        = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget    = false;
            return text;
        }

        private static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            Place(rect, anchor, position, size, anchor);
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
