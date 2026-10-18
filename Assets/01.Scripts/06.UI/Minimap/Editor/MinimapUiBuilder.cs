#if UNITY_EDITOR
// System
using System.IO;

// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI.Editor
{
    /// <summary> 미니맵 창을 씬에 붙인다. HUD에 이미 있는 원형 틀(Minimap, Frame, MapName)은 그대로 두고 지도 층과 표시, 뷰와 바인더만 더한다 </summary>
    public static class MinimapUiBuilder
    {
        // 틀이 없는 씬은 틀을 가진 미니맵 프리팹을 새 캔버스에 놓는다. 프리팹은 마을 HUD의 완성된 틀에서 만들어 모양이 같다.
        // 다시 실행해도 같은 이름의 자식이 있으면 새로 만들지 않고 참조만 다시 잇는다

        /****************************************
        *                Fields
        ****************************************/

        public const string PREFAB_PATH = "Assets/02.Prefab/UI/HUD/PF_HudMinimap.prefab";

        private const string TEXTURE_FOLDER = "Assets/01.Scripts/05.Art/UI/HudMinimap/Textures";
        private const string ARROW_PATH     = TEXTURE_FOLDER + "/T_HudMinimap_SelfArrow.png";
        private const string FONT_BOLD      = "Assets/01.Scripts/06.UI/Hud/Fonts/HudQuestKorean-Bold.asset";
        private const string KNOB_RESOURCE  = "UI/Skin/Knob.psd";
        private const string UI_LAYER       = "UI";

        private const string VIEWPORT_NAME   = "MapViewport";
        private const string MAP_NAME        = "Map";
        private const string MAP_IMAGE_NAME  = "MapImage";
        private const string SELF_NAME       = "SelfMarker";
        private const string FRAME_NAME      = "Frame";
        private const string LABEL_NAME      = "NameLabel";
        private const string MARKER_PREFIX   = "Marker_";
        private const string CANVAS_NAME     = "MinimapCanvas";
        private const string GROUP_NAME      = "MapInfoGroup";
        private const string MINIMAP_NAME    = "Minimap";
        private const string NAME_PLATE_ROOT = "MapName";

        private const int   MARKER_COUNT     = 8;
        private const float MARKER_SIZE      = 12f;
        private const float SELF_SIZE        = 26f;
        private const float LABEL_FONT_SIZE  = 17f;
        private const int   ARROW_TEXTURE    = 64;
        private const int   ARROW_SAMPLES    = 4;
        private const float ARROW_TIP        = 0.08f;
        private const float ARROW_BASE_Y     = 0.88f;
        private const float ARROW_HALF_WIDTH = 0.34f;
        private const float ARROW_NOTCH      = 0.64f;
        private const float NOTCH_WIDTH_RATIO = 0.5f;
        private const float REFERENCE_WIDTH  = 1920f;
        private const float REFERENCE_HEIGHT = 1080f;
        private const float MATCH_WIDTH_HEIGHT = 0.5f;
        private const int   CANVAS_SORTING   = 2;
        private const float GROUP_MARGIN     = 40f;

        private static readonly Color G_SelfColor  = new Color(1f, 0.93f, 0.55f, 1f);
        private static readonly Color G_LabelColor = new Color(0.92f, 0.94f, 0.97f, 1f);

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem("Shinshi/UI/Minimap/Apply To Active Scene")]
        public static void ApplyToActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            MinimapCaptureProfile profile = MinimapCaptureProfiles.Find(scene.name);
            if (profile == null)
            {
                Debug.LogError("[Minimap] 이 씬의 캡처 규칙이 없어 미니맵을 붙일 수 없다: " + scene.name);
                return;
            }

            string mapPath = MinimapCaptureTool.DATA_FOLDER + "/MinimapMap_" + profile.MapId + ".asset";
            var map = AssetDatabase.LoadAssetAtPath<MinimapMapSO>(mapPath);
            if (map == null)
            {
                Debug.LogError("[Minimap] 지도 설정이 없다. 먼저 캡처한다: " + mapPath);
                return;
            }

            Transform minimap = FindMinimapRoot(scene);
            if (minimap == null)
            {
                minimap = InstantiatePrefabInNewCanvas(scene);
                if (minimap == null)
                {
                    return;
                }
            }
            else
            {
                EnsureLayers(minimap);
            }

            Transform devTarget = EnsureDevTarget(scene, profile.MapId);
            Transform devBoss   = EnsureDevBoss(scene, profile.MapId);
            Wire(minimap, map, devTarget, devBoss);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[Minimap] 미니맵을 붙였다: " + scene.name + " (" + map.MapId + ")");
        }

        /// <summary> 완성된 미니맵 묶음의 사본으로 프리팹을 새로 쓴다. 사본은 지도와 개발 대상 연결을 비우고 저장 뒤 지운다 </summary>
        public static void SavePrefabFrom(Transform minimap)
        {
            GameObject copy = Object.Instantiate(minimap.parent.gameObject, minimap.parent.parent);
            copy.name = GROUP_NAME;
            Wire(copy.transform.Find(MINIMAP_NAME), null, null, null);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Directory.GetParent(Application.dataPath).FullName, PREFAB_PATH)));
            PrefabUtility.SaveAsPrefabAsset(copy, PREFAB_PATH);
            Object.DestroyImmediate(copy);
            AssetDatabase.SaveAssets();
        }

        public static Transform FindMinimapRoot(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; ++i)
            {
                Transform found = roots[i].transform.Find("Layout_AlwaysVisible/" + GROUP_NAME + "/" + MINIMAP_NAME);
                if (found != null)
                {
                    return found;
                }

                found = roots[i].transform.Find(GROUP_NAME + "/" + MINIMAP_NAME);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary> 틀 위에 지도 층, 내 표시, 표시 칸, 이름 글자를 더한다 </summary>
        public static void EnsureLayers(Transform minimap)
        {
            Sprite knob = AssetDatabase.GetBuiltinExtraResource<Sprite>(KNOB_RESOURCE);

            RectTransform viewport = EnsureChild(minimap, VIEWPORT_NAME);
            Stretch(viewport);
            Image viewportImage = EnsureComponent<Image>(viewport.gameObject);
            viewportImage.sprite        = knob;
            viewportImage.color         = Color.white;
            viewportImage.raycastTarget = false;
            Mask mask = EnsureComponent<Mask>(viewport.gameObject);
            mask.showMaskGraphic = false;

            RectTransform map = EnsureChild(viewport, MAP_NAME);
            map.anchorMin = map.anchorMax = new Vector2(0.5f, 0.5f);
            map.pivot     = new Vector2(0.5f, 0.5f);
            Image mapImage = EnsureComponent<Image>(map.gameObject);
            mapImage.raycastTarget = false;
            mapImage.enabled       = false;

            RectTransform self = EnsureChild(minimap, SELF_NAME);
            SetCentered(self, SELF_SIZE);
            Image selfImage = EnsureComponent<Image>(self.gameObject);
            selfImage.sprite        = EnsureArrowSprite();
            selfImage.color         = G_SelfColor;
            selfImage.raycastTarget = false;

            for (int i = 0; i < MARKER_COUNT; ++i)
            {
                RectTransform marker = EnsureChild(minimap, MARKER_PREFIX + i.ToString("00"));
                SetCentered(marker, MARKER_SIZE);
                Image markerImage = EnsureComponent<Image>(marker.gameObject);
                markerImage.sprite        = knob;
                markerImage.raycastTarget = false;
                marker.gameObject.SetActive(false);
            }

            Transform namePlate = minimap.parent.Find(NAME_PLATE_ROOT);
            if (namePlate != null)
            {
                EnsureNameLabel((RectTransform)namePlate);
            }

            // 지도 층은 배경 바로 위, 틀은 맨 위에 둔다
            viewport.SetAsFirstSibling();
            Transform frame = minimap.Find(FRAME_NAME);
            if (frame != null)
            {
                frame.SetAsLastSibling();
            }
        }

        public static void Wire(Transform minimap, MinimapMapSO map, Transform devTarget, Transform devBoss)
        {
            MinimapView view = EnsureComponent<MinimapView>(minimap.gameObject);
            var viewSerialized = new SerializedObject(view);
            Transform viewport = minimap.Find(VIEWPORT_NAME);
            Transform mapRect  = viewport.Find(MAP_NAME);
            viewSerialized.FindProperty("_viewport").objectReferenceValue = viewport;
            viewSerialized.FindProperty("_map").objectReferenceValue      = mapRect;
            viewSerialized.FindProperty("_mapImage").objectReferenceValue = mapRect.GetComponent<Image>();
            viewSerialized.FindProperty("_selfMarker").objectReferenceValue = minimap.Find(SELF_NAME);
            SerializedProperty markers = viewSerialized.FindProperty("_markers");
            markers.arraySize = MARKER_COUNT;
            for (int i = 0; i < MARKER_COUNT; ++i)
            {
                markers.GetArrayElementAtIndex(i).objectReferenceValue = minimap.Find(MARKER_PREFIX + i.ToString("00")).GetComponent<Image>();
            }

            Transform label = minimap.parent.Find(NAME_PLATE_ROOT + "/" + LABEL_NAME);
            viewSerialized.FindProperty("_nameLabel").objectReferenceValue = (label != null) ? label.GetComponent<TMP_Text>() : null;
            viewSerialized.ApplyModifiedPropertiesWithoutUndo();

            MinimapBinder binder = EnsureComponent<MinimapBinder>(minimap.gameObject);
            var binderSerialized = new SerializedObject(binder);
            binderSerialized.FindProperty("_view").objectReferenceValue      = view;
            binderSerialized.FindProperty("_map").objectReferenceValue       = map;
            binderSerialized.FindProperty("_devTarget").objectReferenceValue = devTarget;
            binderSerialized.FindProperty("_devBoss").objectReferenceValue   = devBoss;
            binderSerialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Transform InstantiatePrefabInNewCanvas(Scene scene)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            if (prefab == null)
            {
                Debug.LogError("[Minimap] 미니맵 프리팹이 없다: " + PREFAB_PATH);
                return null;
            }

            var canvasObject = new GameObject(CANVAS_NAME, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.layer = LayerMask.NameToLayer(UI_LAYER);
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CANVAS_SORTING;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(REFERENCE_WIDTH, REFERENCE_HEIGHT);
            scaler.matchWidthOrHeight  = MATCH_WIDTH_HEIGHT;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvasObject.transform);

            // 프리팹 루트는 파일 이름으로 바뀌어 있어 다시 찾을 수 있게 묶음 이름으로 되돌린다
            instance.name = GROUP_NAME;
            var group = (RectTransform)instance.transform;
            group.anchorMin        = group.anchorMax = new Vector2(0f, 1f);
            group.anchoredPosition = new Vector2(GROUP_MARGIN, -GROUP_MARGIN);
            return group.Find(MINIMAP_NAME);
        }

        private static Transform EnsureDevTarget(Scene scene, string mapId)
        {
            switch (mapId)
            {
                case "mucheol":
                    return FindRoot(scene, "DevRammerCapsule");
                case "forest":
                    Transform hitFeedbackPlayer = FindRoot(scene, "DevHitFeedbackPlayer");
                    if (hitFeedbackPlayer != null)
                    {
                        return hitFeedbackPlayer;
                    }

                    return EnsureDevObject(scene, "MinimapDevTarget", FirstChildPosition(scene, "90_Markers/player_start", new Vector3(0f, 0f, 0f)));
                case "town":
                    return EnsureDevObject(scene, "MinimapDevTarget", FirstChildPosition(scene, "90_Gameplay/SpawnPoints", new Vector3(2.2f, 0f, -7f)));
                default:
                    return EnsureDevObject(scene, "MinimapDevTarget", new Vector3(0f, 0f, -5f));
            }
        }

        private static Transform EnsureDevBoss(Scene scene, string mapId)
        {
            if ((mapId == "wolbaek") || (mapId == "mucheol"))
            {
                return EnsureDevObject(scene, "MinimapDevBoss", new Vector3(0f, 0f, 8f));
            }

            return null;
        }

        private static Transform EnsureDevObject(Scene scene, string objectName, Vector3 position)
        {
            Transform existing = FindRoot(scene, objectName);
            if (existing != null)
            {
                return existing;
            }

            var created = new GameObject(objectName);
            SceneManager.MoveGameObjectToScene(created, scene);
            created.transform.position = position;
            return created.transform;
        }

        private static Vector3 FirstChildPosition(Scene scene, string path, Vector3 fallback)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; ++i)
            {
                string rootName = roots[i].name + "/";
                if (!path.StartsWith(rootName, System.StringComparison.Ordinal))
                {
                    continue;
                }

                Transform parent = roots[i].transform.Find(path.Substring(rootName.Length));
                if ((parent != null) && (parent.childCount > 0))
                {
                    return parent.GetChild(0).position;
                }
            }

            return fallback;
        }

        private static Transform FindRoot(Scene scene, string objectName)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; ++i)
            {
                if (roots[i].name == objectName)
                {
                    return roots[i].transform;
                }
            }

            return null;
        }

        private static void EnsureNameLabel(RectTransform plate)
        {
            RectTransform label = EnsureChild(plate, LABEL_NAME);
            Stretch(label);
            var text = EnsureComponent<TextMeshProUGUI>(label.gameObject);
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_BOLD);
            if (font != null)
            {
                text.font = font;
            }

            text.fontSize      = LABEL_FONT_SIZE;
            text.alignment     = TextAlignmentOptions.Center;
            text.color         = G_LabelColor;
            text.raycastTarget = false;
        }

        private static RectTransform EnsureChild(Transform parent, string childName)
        {
            Transform existing = parent.Find(childName);
            if (existing != null)
            {
                return (RectTransform)existing;
            }

            var go = new GameObject(childName, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer(UI_LAYER);
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static T EnsureComponent<T>(GameObject go) where T : Component
        {
            if (go.TryGetComponent(out T existing))
            {
                return existing;
            }

            return go.AddComponent<T>();
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin        = Vector2.zero;
            rect.anchorMax        = Vector2.one;
            rect.offsetMin        = Vector2.zero;
            rect.offsetMax        = Vector2.zero;
        }

        private static void SetCentered(RectTransform rect, float size)
        {
            rect.anchorMin        = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot            = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta        = new Vector2(size, size);
        }

        // 위쪽을 가리키는 삼각 화살표, 월드 +Z가 그림 위쪽이므로 회전 0이 북쪽이다
        private static Sprite EnsureArrowSprite()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(ARROW_PATH);
            if (existing != null)
            {
                return existing;
            }

            var pixels = new Color32[ARROW_TEXTURE * ARROW_TEXTURE];
            for (int y = 0; y < ARROW_TEXTURE; ++y)
            {
                for (int x = 0; x < ARROW_TEXTURE; ++x)
                {
                    int inside = 0;
                    for (int sy = 0; sy < ARROW_SAMPLES; ++sy)
                    {
                        for (int sx = 0; sx < ARROW_SAMPLES; ++sx)
                        {
                            float u = (x + ((sx + 0.5f) / ARROW_SAMPLES)) / ARROW_TEXTURE;
                            float v = (y + ((sy + 0.5f) / ARROW_SAMPLES)) / ARROW_TEXTURE;
                            if (IsInsideArrow(u, v))
                            {
                                ++inside;
                            }
                        }
                    }

                    byte alpha = (byte)(255f * inside / (ARROW_SAMPLES * ARROW_SAMPLES));
                    pixels[(y * ARROW_TEXTURE) + x] = new Color32(255, 255, 255, alpha);
                }
            }

            var texture = new Texture2D(ARROW_TEXTURE, ARROW_TEXTURE, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            string fullPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ARROW_PATH);
            File.WriteAllBytes(fullPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(ARROW_PATH, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(ARROW_PATH) as TextureImporter;
            importer.textureType         = TextureImporterType.Sprite;
            importer.spriteImportMode    = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled       = false;
            importer.filterMode          = FilterMode.Bilinear;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(ARROW_PATH);
        }

        // 텍스처의 v는 아래에서 위로 늘어난다. 위에서 아래로 센 값으로 바꿔 끝이 위를 향하게 하고 밑변 가운데를 삼각형으로 판다
        private static bool IsInsideArrow(float u, float v)
        {
            float top = 1f - v;
            if ((top < ARROW_TIP) || (top > ARROW_BASE_Y))
            {
                return false;
            }

            float spread    = (top - ARROW_TIP) / (ARROW_BASE_Y - ARROW_TIP);
            float offset    = Mathf.Abs(u - 0.5f);
            if (offset > (ARROW_HALF_WIDTH * spread))
            {
                return false;
            }

            if (top <= ARROW_NOTCH)
            {
                return true;
            }

            float notch = (top - ARROW_NOTCH) / (ARROW_BASE_Y - ARROW_NOTCH);
            return offset >= (ARROW_HALF_WIDTH * NOTCH_WIDTH_RATIO * notch);
        }
    }
}
#endif
