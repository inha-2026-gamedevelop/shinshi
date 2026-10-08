using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Shinshi.UI;

namespace Shinshi.Editor
{
    /// <summary>
    /// 씬에 없는 View를 시험용으로 조립한다. 비활성 루트에 View를 붙이고 직렬화 필드를 만든 UI로 채운 뒤 활성화하므로
    /// Awake와 OnEnable은 참조가 모두 연결된 뒤에 불린다. 생성물은 실제 배치가 아니라 시험 배치이며 씬에 저장되지 않는다
    /// </summary>
    public static class PresentationFixtureFactory
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string CANVAS_NAME       = "UiSandboxFixtureCanvas";
        public const string EVENT_SYSTEM_NAME = "UiSandboxFixtureEventSystem";
        public const string FIXTURE_PREFIX    = "Fixture_";
        public const string HEADER_FORMAT     = "[테스트 배치] {0} (실제 배치 아님)";

        private const string FONT_ASSET_NAME = "HudQuestKorean-Regular";
        private const int    SORTING_ORDER   = 500;
        private const int    MAX_DEPTH       = 4;
        private const int    PADDING         = 6;
        private const int    ROW_PADDING     = 2;
        private const float  SPACING         = 2.0f;
        private const float  TEXT_WIDTH      = 240.0f;
        private const float  TEXT_MIN_HEIGHT = 20.0f;
        private const float  FONT_SIZE       = 14.0f;
        private const float  HEADER_SIZE     = 12.0f;
        private const float  BAR_WIDTH       = 240.0f;
        private const float  BAR_HEIGHT      = 14.0f;
        private const float  IMAGE_WIDTH     = 64.0f;
        private const float  IMAGE_HEIGHT    = 32.0f;
        private const float  MARKER_WIDTH    = 160.0f;
        private const float  MARKER_HEIGHT   = 20.0f;
        private const float  BUTTON_WIDTH    = 140.0f;
        private const float  BUTTON_HEIGHT   = 26.0f;
        private const float  GRID_CELL       = 64.0f;
        private const float  GRID_SPACING    = 4.0f;
        private const int    GRID_COLUMNS    = 4;
        private const float  UNSET           = -1.0f;
        private const float  MATCH_BALANCED  = 0.5f;
        private const string ROOT_SUFFIX     = "root";
        private const string FILL_TOKEN      = "fill";

        private const float REFERENCE_WIDTH  = 1920.0f;
        private const float REFERENCE_HEIGHT = 1080.0f;

        private static readonly Color G_BackgroundColor = new Color(0.08f, 0.1f, 0.14f, 0.82f);
        private static readonly Color G_RowColor        = new Color(1.0f, 1.0f, 1.0f, 0.06f);
        private static readonly Color G_BarBackColor    = new Color(0.0f, 0.0f, 0.0f, 0.6f);
        private static readonly Color G_BarFillColor    = new Color(0.8f, 0.3f, 0.3f, 0.95f);
        private static readonly Color G_ImageColor      = new Color(0.45f, 0.5f, 0.6f, 0.9f);
        private static readonly Color G_MarkerColor     = new Color(0.3f, 0.5f, 0.35f, 0.85f);
        private static readonly Color G_ButtonColor     = new Color(0.25f, 0.35f, 0.55f, 0.95f);
        private static readonly Color G_HeaderColor     = new Color(1.0f, 0.82f, 0.4f, 1.0f);

        // 안에 View가 또 들어 있는 필드만 중첩 조립을 허용한다. 그 밖의 컴포넌트 필드는 null로 남긴다
        private static readonly HashSet<Type> G_NestedViewTypes = new HashSet<Type>
        {
            typeof(SaveStatusView),
            typeof(AlertPopupView),
            typeof(ConfirmPopupView),
            typeof(ToastMessageView),
            typeof(HudSkillSlotView),
            typeof(ItemSlotGridView)
        };

        public const string REFUSE_REASON = "시험 배치는 도구가 만든 샌드박스 씬(UiSandboxGenerated 라벨)에서만 만든다. 원본 씬과 임의의 미저장 씬에는 만들지 않는다";

        private const HideFlags FIXTURE_FLAGS = HideFlags.DontSaveInEditor;

        private static TMP_FontAsset            _font;
        private static bool                     _fontSearched;
        private static PresentationFixtureScope _testScope;

        /****************************************
        *              Properties
        ****************************************/

        public static int ActiveFixtureCount
        {
            get
            {
                Canvas canvas = FindCanvas();
                return (canvas == null) ? 0 : canvas.transform.childCount;
            }
        }

        /// <summary> 명시적 시험 범위가 열려 있는지. 열려 있으면 샌드박스 씬이 없어도 프리뷰 씬에 만든다 </summary>
        public static bool HasTestScope
        {
            get { return (_testScope != null) && (_testScope.IsOpen); }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 프리뷰 씬을 호스트로 쓰는 시험 범위를 연다. 한 번에 하나만 열 수 있고 Dispose로 닫는다 </summary>
        public static PresentationFixtureScope OpenTestScope()
        {
            if (HasTestScope)
            {
                throw new InvalidOperationException("시험 범위가 이미 열려 있다");
            }

            _testScope = new PresentationFixtureScope();
            return _testScope;
        }

        internal static void OnScopeClosed(PresentationFixtureScope scope)
        {
            if (ReferenceEquals(_testScope, scope))
            {
                _testScope = null;
            }
        }

        /// <summary> 지금 시험 배치를 만들 수 있는지. 시험 범위가 열려 있거나 활성 씬이 도구가 만든 샌드박스 씬일 때만 참이다 </summary>
        public static bool CanCreate(out string reason)
        {
            return TryGetHostScene(out _, out reason);
        }

        public static bool TryGetHostScene(out Scene scene, out string reason)
        {
            if (HasTestScope)
            {
                scene  = _testScope.Scene;
                reason = string.Empty;
                return true;
            }

            if (UiSandboxSceneBuilder.TryGetActiveOwnedSandboxScene(out scene))
            {
                reason = string.Empty;
                return true;
            }

            scene  = default;
            reason = REFUSE_REASON;
            return false;
        }

        /// <summary> 주어진 씬이 시험 범위의 프리뷰 씬이거나 도구가 만든 활성 샌드박스 씬인지 </summary>
        public static bool IsHostScene(Scene scene)
        {
            return (scene.IsValid()) && (TryGetHostScene(out Scene host, out _)) && (scene == host);
        }

        public static PresentationFixture Create<TView>(string label, PresentationFixtureOptions options = null)
            where TView : Component
        {
            return Create(typeof(TView), label, options);
        }

        public static PresentationFixture Create(Type viewType, string label, PresentationFixtureOptions options = null)
        {
            if (viewType == null)
            {
                throw new ArgumentNullException(nameof(viewType));
            }

            PresentationFixtureOptions resolved = options ?? new PresentationFixtureOptions();
            GameObject root = BeginFixture(label, resolved, out GameObject host);

            Component view = host.AddComponent(viewType);
            List<string> unwired = new List<string>();
            Wire(view, host.transform, resolved, 0, unwired);
            AddChildButtons(view, resolved);

            root.SetActive(true);
            return new PresentationFixture(label, root, view, unwired);
        }

        /// <summary>
        /// 직렬화 필드를 자동으로 채울 수 없는 구조(레이아웃 그룹이 위치를 덮어쓰면 안 되는 말풍선, 끌 수 있는 창 등)를 직접 짜서 붙인다.
        /// build는 비활성 상태의 View 호스트 아래에서 불리고, 돌려준 컴포넌트가 PresentationFixture.View가 된다. 예외가 나면 만든 것을 모두 지운다
        /// </summary>
        public static PresentationFixture CreateScaffold(string label, PresentationFixtureOptions options, Func<Transform, Component> build)
        {
            if (build == null)
            {
                throw new ArgumentNullException(nameof(build));
            }

            PresentationFixtureOptions resolved = options ?? new PresentationFixtureOptions();
            GameObject root = BeginFixture(label, resolved, out GameObject host);

            Component primary;
            try
            {
                primary = build(host.transform);
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(root);
                throw;
            }

            root.SetActive(true);
            return new PresentationFixture(label, root, primary, new List<string>());
        }

        // 활성화 전에 뼈대(배경 · 레이아웃 · 머리글 · View 호스트)를 만든다. 호출자가 연결을 끝낸 뒤 root를 활성화한다
        private static GameObject BeginFixture(string label, PresentationFixtureOptions resolved, out GameObject host)
        {
            if (!TryGetHostScene(out Scene hostScene, out string reason))
            {
                throw new InvalidOperationException(reason);
            }

            Canvas canvas = EnsureCanvas(hostScene);

            GameObject root = NewUi(FIXTURE_PREFIX + label, canvas.transform);
            root.SetActive(false);

            RectTransform rootRect = root.GetComponent<RectTransform>();
            ApplyAnchor(rootRect, resolved.Anchor, resolved.Offset);

            Image background = root.AddComponent<Image>();
            background.color         = G_BackgroundColor;
            background.raycastTarget = false;
            AddVerticalLayout(root, PADDING);

            ContentSizeFitter fitter = root.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            TMP_Text header = CreateText(root.transform, "header", string.Format(HEADER_FORMAT, label), HEADER_SIZE);
            header.color = G_HeaderColor;

            // View는 별도 자식에 둔다. View가 gameObject를 끄고 켜도 머리글 표시가 사라지지 않는다
            host = NewUi("View", root.transform);
            AddVerticalLayout(host, 0);
            return root;
        }

        /// <summary> 시험 배치가 하나도 남지 않았으면 빈 시험 캔버스와 임시 EventSystem을 없앤다 </summary>
        public static void ReleaseCanvasIfUnused()
        {
            List<GameObject> canvases = FindFlaggedRoots<Canvas>(CANVAS_NAME);
            bool anyKept = false;
            for (int i = 0; i < canvases.Count; ++i)
            {
                if (canvases[i].transform.childCount > 0)
                {
                    anyKept = true;
                    continue;
                }

                UnityEngine.Object.DestroyImmediate(canvases[i]);
            }

            if (!anyKept)
            {
                DestroyEventSystem();
            }
        }

        /// <summary> 도메인 리로드 등으로 주인을 잃은 시험 캔버스를 모두 정리한다. 이름이 같아도 시험용 숨김 플래그가 없는 사용자 객체는 건드리지 않는다 </summary>
        public static void DestroyOrphans()
        {
            List<GameObject> canvases = FindFlaggedRoots<Canvas>(CANVAS_NAME);
            for (int i = 0; i < canvases.Count; ++i)
            {
                UnityEngine.Object.DestroyImmediate(canvases[i]);
            }

            DestroyEventSystem();
        }

        private static void DestroyEventSystem()
        {
            List<GameObject> systems = FindFlaggedRoots<EventSystem>(EVENT_SYSTEM_NAME);
            for (int i = 0; i < systems.Count; ++i)
            {
                UnityEngine.Object.DestroyImmediate(systems[i]);
            }
        }

        private static Canvas FindCanvas()
        {
            List<GameObject> canvases = FindFlaggedRoots<Canvas>(CANVAS_NAME);
            if (canvases.Count == 0)
            {
                return null;
            }

            if (TryGetHostScene(out Scene host, out _))
            {
                for (int i = 0; i < canvases.Count; ++i)
                {
                    if (canvases[i].scene == host)
                    {
                        return canvases[i].GetComponent<Canvas>();
                    }
                }

                return null;
            }

            return canvases[0].GetComponent<Canvas>();
        }

        // 이름만으로 찾으면 원본 씬의 같은 이름 객체를 지울 수 있어 시험 전용 숨김 플래그와 최상위 여부까지 요구한다
        private static List<GameObject> FindFlaggedRoots<T>(string objectName) where T : Component
        {
            List<GameObject> result = new List<GameObject>();
            T[] all = Resources.FindObjectsOfTypeAll<T>();
            for (int i = 0; i < all.Length; ++i)
            {
                GameObject go = all[i].gameObject;
                if ((go.scene.IsValid())
                    && (go.name == objectName)
                    && ((go.hideFlags & FIXTURE_FLAGS) == FIXTURE_FLAGS)
                    && (go.transform.parent == null))
                {
                    result.Add(go);
                }
            }

            return result;
        }

        private static Canvas EnsureCanvas(Scene hostScene)
        {
            Canvas existing = FindCanvas();
            if (existing != null)
            {
                EnsureEventSystem(hostScene);
                return existing;
            }

            GameObject go = EditorUtility.CreateGameObjectWithHideFlags(
                CANVAS_NAME, FIXTURE_FLAGS, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            if (go.scene != hostScene)
            {
                SceneManager.MoveGameObjectToScene(go, hostScene);
            }

            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SORTING_ORDER;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(REFERENCE_WIDTH, REFERENCE_HEIGHT);
            scaler.matchWidthOrHeight  = MATCH_BALANCED;

            EnsureEventSystem(hostScene);
            return canvas;
        }

        // 프리뷰 씬 범위에서는 전역 입력 시스템을 건드리지 않는다. 실제 클릭은 샌드박스 씬 Play에서만 필요하다
        private static void EnsureEventSystem(Scene hostScene)
        {
            if ((HasTestScope) || (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null))
            {
                return;
            }

            GameObject go = EditorUtility.CreateGameObjectWithHideFlags(
                EVENT_SYSTEM_NAME, FIXTURE_FLAGS, typeof(EventSystem), typeof(StandaloneInputModule));
            if (go.scene != hostScene)
            {
                SceneManager.MoveGameObjectToScene(go, hostScene);
            }
        }

        private static void ApplyAnchor(RectTransform rect, EFixtureAnchor anchor, Vector2 offset)
        {
            float x = 0.5f;
            float y = 0.5f;

            switch (anchor)
            {
                case EFixtureAnchor.TopLeft:      x = 0.0f; y = 1.0f; break;
                case EFixtureAnchor.TopCenter:    x = 0.5f; y = 1.0f; break;
                case EFixtureAnchor.TopRight:     x = 1.0f; y = 1.0f; break;
                case EFixtureAnchor.MiddleLeft:   x = 0.0f; y = 0.5f; break;
                case EFixtureAnchor.Center:       x = 0.5f; y = 0.5f; break;
                case EFixtureAnchor.MiddleRight:  x = 1.0f; y = 0.5f; break;
                case EFixtureAnchor.BottomLeft:   x = 0.0f; y = 0.0f; break;
                case EFixtureAnchor.BottomCenter: x = 0.5f; y = 0.0f; break;
                case EFixtureAnchor.BottomRight:  x = 1.0f; y = 0.0f; break;
            }

            Vector2 point = new Vector2(x, y);
            rect.anchorMin        = point;
            rect.anchorMax        = point;
            rect.pivot            = point;
            rect.anchoredPosition = offset;
        }

        /****************************************
        *               Wiring
        ****************************************/

        private static void Wire(object target, Transform parent, PresentationFixtureOptions options, int depth, List<string> unwired)
        {
            if ((target == null) || (depth > MAX_DEPTH))
            {
                return;
            }

            Dictionary<string, Transform> groups = new Dictionary<string, Transform>();
            List<FieldInfo> fields = CollectFields(target.GetType());

            for (int i = 0; i < fields.Count; ++i)
            {
                FieldInfo field = fields[i];
                if (options.IsSkipped(field.Name))
                {
                    continue;
                }

                string key = NormalizeKey(field.Name);
                Transform fieldParent = ResolveParent(groups, key, parent);
                Type fieldType = field.FieldType;

                if (fieldType.IsArray)
                {
                    WireArray(target, field, key, fieldParent, options, depth, unwired);
                    continue;
                }

                if (!IsNull(field.GetValue(target)))
                {
                    continue;
                }

                if (options.TryGetAsset(field.Name, fieldType, out UnityEngine.Object asset))
                {
                    field.SetValue(target, asset);
                    continue;
                }

                object value = CreateValue(fieldType, key, fieldParent, groups, options, depth, unwired);
                if (value == null)
                {
                    if (typeof(Component).IsAssignableFrom(fieldType))
                    {
                        unwired.Add(field.Name + " (" + fieldType.Name + ")");
                    }

                    continue;
                }

                field.SetValue(target, value);
            }
        }

        // View가 활성화될 때 패널의 자식 버튼을 훑어 구독하는 경우를 위해 버튼을 활성화 전에 만들어 둔다
        private static void AddChildButtons(Component view, PresentationFixtureOptions options)
        {
            List<FieldInfo> fields = CollectFields(view.GetType());
            for (int i = 0; i < fields.Count; ++i)
            {
                if (!options.ChildButtons.TryGetValue(fields[i].Name, out int count))
                {
                    continue;
                }

                RectTransform parent = fields[i].GetValue(view) as RectTransform;
                if (parent == null)
                {
                    continue;
                }

                for (int j = 0; j < count; ++j)
                {
                    CreateButton(parent, "item" + j);
                }
            }
        }

        private static void WireArray(object target, FieldInfo field, string key, Transform parent,
            PresentationFixtureOptions options, int depth, List<string> unwired)
        {
            Type elementType = field.FieldType.GetElementType();
            if (!IsWirableElement(elementType))
            {
                return;
            }

            Array existing = field.GetValue(target) as Array;
            bool reuse = (existing != null) && (existing.Length > 0);
            Array array = reuse ? existing : Array.CreateInstance(elementType, ResolveLength(options, field.Name));

            for (int i = 0; i < array.Length; ++i)
            {
                if (!IsNull(array.GetValue(i)))
                {
                    continue;
                }

                object element = CreateElement(elementType, key + i, parent, options, depth, unwired);
                if (element != null)
                {
                    array.SetValue(element, i);
                }
            }

            field.SetValue(target, array);
        }

        private static object CreateElement(Type elementType, string key, Transform parent,
            PresentationFixtureOptions options, int depth, List<string> unwired)
        {
            if (elementType == typeof(CanvasGroup))
            {
                GameObject groupRow = CreateContainer(parent, key);
                return groupRow.AddComponent<CanvasGroup>();
            }

            if (IsSerializableClass(elementType))
            {
                GameObject row = CreateContainer(parent, key);
                object instance = Activator.CreateInstance(elementType, true);
                Wire(instance, row.transform, options, depth + 1, unwired);
                return instance;
            }

            Dictionary<string, Transform> noGroups = new Dictionary<string, Transform>();
            return CreateValue(elementType, key, parent, noGroups, options, depth, unwired);
        }

        private static object CreateValue(Type fieldType, string key, Transform fieldParent,
            Dictionary<string, Transform> groups, PresentationFixtureOptions options, int depth, List<string> unwired)
        {
            if (fieldType == typeof(GameObject))
            {
                if (key.EndsWith(ROOT_SUFFIX, StringComparison.Ordinal))
                {
                    GameObject container = CreateContainer(fieldParent, key);
                    groups[key.Substring(0, key.Length - ROOT_SUFFIX.Length)] = container.transform;
                    return container;
                }

                return CreateMarker(fieldParent, key);
            }

            if (typeof(TMP_Text).IsAssignableFrom(fieldType))
            {
                if (!fieldType.IsAssignableFrom(typeof(TextMeshProUGUI)))
                {
                    return null;
                }

                return CreateText(fieldParent, key, key, FONT_SIZE);
            }

            if (fieldType == typeof(RectTransform))
            {
                if (key.Contains(FILL_TOKEN))
                {
                    return CreateBar(fieldParent, key);
                }

                return CreateContainer(fieldParent, key).GetComponent<RectTransform>();
            }

            if ((typeof(Graphic).IsAssignableFrom(fieldType)) && (fieldType.IsAssignableFrom(typeof(Image))))
            {
                return CreateImage(fieldParent, key);
            }

            if (fieldType == typeof(Button))
            {
                return CreateButton(fieldParent, key);
            }

            if (fieldType == typeof(CanvasGroup))
            {
                CanvasGroup existing = fieldParent.GetComponent<CanvasGroup>();
                return (existing != null) ? existing : fieldParent.gameObject.AddComponent<CanvasGroup>();
            }

            if ((G_NestedViewTypes.Contains(fieldType)) && (depth < MAX_DEPTH))
            {
                GameObject nestedRoot = CreateContainer(fieldParent, key);
                if (fieldType == typeof(ItemSlotGridView))
                {
                    ConvertToGrid(nestedRoot);
                }

                Component nested = nestedRoot.AddComponent(fieldType);
                Wire(nested, nestedRoot.transform, options, depth + 1, unwired);
                return nested;
            }

            return null;
        }

        // 세로 레이아웃 아래에서는 복제된 칸의 크기가 0으로 눌리므로 격자 컨테이너만 칸 크기를 직접 정하는 격자로 바꾼다
        private static void ConvertToGrid(GameObject container)
        {
            VerticalLayoutGroup vertical = container.GetComponent<VerticalLayoutGroup>();
            if (vertical != null)
            {
                UnityEngine.Object.DestroyImmediate(vertical);
            }

            GridLayoutGroup grid = container.AddComponent<GridLayoutGroup>();
            grid.cellSize        = new Vector2(GRID_CELL, GRID_CELL);
            grid.spacing         = new Vector2(GRID_SPACING, GRID_SPACING);
            grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = GRID_COLUMNS;
            grid.padding         = new RectOffset(ROW_PADDING, ROW_PADDING, ROW_PADDING, ROW_PADDING);
        }

        private static bool IsWirableElement(Type elementType)
        {
            if (elementType == null)
            {
                return false;
            }

            if ((elementType == typeof(GameObject)) || (elementType == typeof(CanvasGroup)) || (elementType == typeof(Button)))
            {
                return true;
            }

            if ((typeof(TMP_Text).IsAssignableFrom(elementType)) && (elementType.IsAssignableFrom(typeof(TextMeshProUGUI))))
            {
                return true;
            }

            if ((typeof(Graphic).IsAssignableFrom(elementType)) && (elementType.IsAssignableFrom(typeof(Image))))
            {
                return true;
            }

            return (G_NestedViewTypes.Contains(elementType)) || (IsSerializableClass(elementType));
        }

        private static bool IsSerializableClass(Type type)
        {
            return (type.IsClass) && (!typeof(UnityEngine.Object).IsAssignableFrom(type))
                && (type != typeof(string)) && (type.IsDefined(typeof(SerializableAttribute), false));
        }

        private static int ResolveLength(PresentationFixtureOptions options, string fieldName)
        {
            return options.GetLength(fieldName);
        }

        private static bool IsNull(object value)
        {
            if (value == null)
            {
                return true;
            }

            UnityEngine.Object unityObject = value as UnityEngine.Object;
            if (ReferenceEquals(unityObject, null))
            {
                return false;
            }

            return unityObject == null;
        }

        private static string NormalizeKey(string fieldName)
        {
            return fieldName.TrimStart('_').ToLowerInvariant();
        }

        // 앞서 만든 '...Root' 묶음 이름으로 시작하는 필드는 그 묶음의 자식으로 놓는다. 가장 긴 접두어가 이긴다
        private static Transform ResolveParent(Dictionary<string, Transform> groups, string key, Transform fallback)
        {
            Transform best = fallback;
            int bestLength = -1;

            foreach (KeyValuePair<string, Transform> pair in groups)
            {
                if ((key.StartsWith(pair.Key, StringComparison.Ordinal)) && (pair.Key.Length > bestLength))
                {
                    best       = pair.Value;
                    bestLength = pair.Key.Length;
                }
            }

            return best;
        }

        private static List<FieldInfo> CollectFields(Type type)
        {
            List<Type> chain = new List<Type>();
            Type current = type;
            while ((current != null) && (current != typeof(MonoBehaviour)) && (current != typeof(Behaviour))
                && (current != typeof(Component)) && (current != typeof(UnityEngine.Object)) && (current != typeof(object)))
            {
                chain.Add(current);
                current = current.BaseType;
            }

            chain.Reverse();

            List<FieldInfo> result = new List<FieldInfo>();
            const BindingFlags FLAGS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (int i = 0; i < chain.Count; ++i)
            {
                FieldInfo[] fields = chain[i].GetFields(FLAGS);
                for (int j = 0; j < fields.Length; ++j)
                {
                    FieldInfo field = fields[j];
                    if ((field.IsStatic) || (field.IsInitOnly) || (field.IsDefined(typeof(NonSerializedAttribute), false)))
                    {
                        continue;
                    }

                    if ((field.IsPublic) || (field.IsDefined(typeof(SerializeField), false)))
                    {
                        result.Add(field);
                    }
                }
            }

            return result;
        }

        /****************************************
        *            UI Construction
        ****************************************/

        internal static GameObject NewUi(string objectName, Transform parent)
        {
            GameObject go = new GameObject(objectName, typeof(RectTransform));
            go.hideFlags = HideFlags.DontSaveInEditor;
            go.transform.SetParent(parent, false);
            return go;
        }

        internal static void AddVerticalLayout(GameObject go, int padding)
        {
            VerticalLayoutGroup layout = go.AddComponent<VerticalLayoutGroup>();
            layout.padding                = new RectOffset(padding, padding, padding, padding);
            layout.spacing                = SPACING;
            layout.childControlWidth      = true;
            layout.childControlHeight     = true;
            layout.childForceExpandWidth  = false;
            layout.childForceExpandHeight = false;
        }

        internal static void SetPreferred(GameObject go, float width, float height, float minHeight)
        {
            LayoutElement element = go.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = go.AddComponent<LayoutElement>();
            }

            if (width >= 0.0f)
            {
                element.preferredWidth = width;
            }

            if (height >= 0.0f)
            {
                element.preferredHeight = height;
            }

            if (minHeight >= 0.0f)
            {
                element.minHeight = minHeight;
            }
        }

        internal static GameObject CreateContainer(Transform parent, string objectName)
        {
            GameObject go = NewUi(objectName, parent);
            Image image = go.AddComponent<Image>();
            image.color         = G_RowColor;
            image.raycastTarget = false;
            AddVerticalLayout(go, ROW_PADDING);
            return go;
        }

        internal static TMP_Text CreateText(Transform parent, string objectName, string text, float size)
        {
            GameObject go = NewUi(objectName, parent);
            TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
            label.text          = text;
            label.fontSize      = size;
            label.color         = Color.white;
            label.raycastTarget = false;

            TMP_FontAsset font = GetFont();
            if (font != null)
            {
                label.font = font;
            }

            SetPreferred(go, TEXT_WIDTH, UNSET, TEXT_MIN_HEIGHT);
            return label;
        }

        internal static Image CreateImage(Transform parent, string objectName)
        {
            GameObject go = NewUi(objectName, parent);
            Image image = go.AddComponent<Image>();
            image.color         = G_ImageColor;
            image.raycastTarget = false;
            SetPreferred(go, IMAGE_WIDTH, IMAGE_HEIGHT, UNSET);
            return image;
        }

        private static GameObject CreateMarker(Transform parent, string objectName)
        {
            GameObject go = NewUi(objectName, parent);
            Image image = go.AddComponent<Image>();
            image.color         = G_MarkerColor;
            image.raycastTarget = false;
            SetPreferred(go, MARKER_WIDTH, MARKER_HEIGHT, UNSET);
            AddVerticalLayout(go, 0);

            TMP_Text label = CreateText(go.transform, "label", objectName, HEADER_SIZE);
            label.alignment = TextAlignmentOptions.Center;
            return go;
        }

        // 막대 뒷판에는 LayoutGroup을 붙이지 않아 채움 칸의 앵커를 View가 직접 움직일 수 있다
        private static RectTransform CreateBar(Transform parent, string objectName)
        {
            GameObject back = NewUi(objectName + "_bar", parent);
            Image backImage = back.AddComponent<Image>();
            backImage.color         = G_BarBackColor;
            backImage.raycastTarget = false;
            SetPreferred(back, BAR_WIDTH, BAR_HEIGHT, UNSET);

            GameObject fill = NewUi(objectName, back.transform);
            RectTransform fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            Image fillImage = fill.AddComponent<Image>();
            fillImage.color         = G_BarFillColor;
            fillImage.raycastTarget = false;
            return fillRect;
        }

        internal static Button CreateButton(Transform parent, string objectName)
        {
            GameObject go = NewUi(objectName, parent);
            Image image = go.AddComponent<Image>();
            image.color = G_ButtonColor;
            SetPreferred(go, BUTTON_WIDTH, BUTTON_HEIGHT, UNSET);

            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;

            GameObject labelObject = NewUi("label", go.transform);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
            label.text          = objectName;
            label.fontSize      = HEADER_SIZE;
            label.alignment     = TextAlignmentOptions.Center;
            label.color         = Color.white;
            label.raycastTarget = false;

            TMP_FontAsset font = GetFont();
            if (font != null)
            {
                label.font = font;
            }

            return button;
        }

        private static TMP_FontAsset GetFont()
        {
            if (_fontSearched)
            {
                return _font;
            }

            _fontSearched = true;
            string[] guids = AssetDatabase.FindAssets(FONT_ASSET_NAME + " t:TMP_FontAsset");
            for (int i = 0; i < guids.Length; ++i)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                TMP_FontAsset asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if ((asset != null) && (asset.name == FONT_ASSET_NAME))
                {
                    _font = asset;
                    break;
                }
            }

            return _font;
        }
    }
}
