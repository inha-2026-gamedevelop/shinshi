#if UNITY_EDITOR
// System
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shinshi.Art.Editor
{
    /// <summary> 씬 뷰에서 CA07 단 바닥(덱) 윤곽을 넓히거나 깎고, 아래 돌 지형에 자동으로 맞추는 에디터 브러시 </summary>
    public sealed class DeckShapeBrushWindow : EditorWindow
    {
        public enum EDeckBrushMode
        {
            Grow,
            Cut
        }

        /// <summary> 한 번의 드래그나 자동 맞추기 전 덱 모양 </summary>
        private sealed class Snapshot
        {
            public Transform Root;
            public Mesh      Top;
            public Mesh      Wall;
            public Mesh      TopCopy;
            public Mesh      WallCopy;
        }

        /****************************************
        *                Fields
        ****************************************/

        private const string MENU_PATH         = "Shinshi/Art/Environment/덱 모양 브러시";
        private const string WINDOW_TITLE      = "덱 모양 브러시";
        private const string EDITABLE_FOLDER   = "Assets/00.Scenes/Dev_HUD/"; // 원본 덱은 팀장 소유 씬도 쓰므로 개인 개발 씬에서만 고친다
        private const float  MAX_PICK_DISTANCE = 2000f;
        private const float  STAMP_SPACING     = 0.25f; // 드래그 중 반경의 이 비율만큼 움직이면 다시 찍는다
        private const float  TOGGLE_LINES      = 2f;
        private const float  RING_THICKNESS    = 2f;
        private const int    MAX_HISTORY       = 20;
        private const float  LABEL_LIFT        = 0.5f;
        private const float  VERTEX_KEY_SCALE  = 1000f;
        private const int    KEY_SHIFT         = 32;
        private const float  PAVING_DECK_GAP   = 0.3f; // 포장 높이에서 이 거리 안에 덱 윗면이 있어야 포장을 넓힌다(m)

        private static readonly Color G_GrowColor = new Color(0.3f, 1f, 0.4f, 1f);
        private static readonly Color G_CutColor  = new Color(1f, 0.35f, 0.3f, 1f);

        [SerializeField] private EDeckBrushMode _mode             = EDeckBrushMode.Grow;
        [SerializeField] private float          _radius           = 2f;
        [SerializeField] private bool           _rockOnly         = true;  // 넓힐 때 덱 높이 근처 돌 위에만 넓힌다
        [SerializeField] private float          _ledgeBelow       = 0.6f;  // 덱보다 이만큼 낮은 돌까지 받침으로 본다(m)
        [SerializeField] private float          _ledgeAbove       = 1.5f;  // 덱보다 이만큼 높은 돌까지 받침으로 본다(m)
        [SerializeField] private float          _extendDistance   = 4f;
        [SerializeField] private float          _floatDepth       = 3f;
        [SerializeField] private float          _protectRadius    = 1.5f;
        [SerializeField] private string[]       _rockRootPrefixes = { "01_CA07_IslandBody", "06_", "07_", "PF_CliffRock" };
        [SerializeField] private string[]       _waterNames       = { "SM_CA07_Ponds", "SM_CA07_Streams", "SM_CA07_Waterfalls" };
        [SerializeField] private string[]       _propRootNames    = { "03_CA07_Placements", "02_CentralSanctuary" };

        private bool                     _painting        = false;
        private int                      _activeControlId = 0;
        private SerializedObject         _serialized      = null;
        private Vector2                  _scroll          = Vector2.zero;
        private bool                     _showSources     = false;
        private DeckShapeUtility.Deck    _target          = null;
        private DeckShapeUtility.Mask    _mask            = null;
        private Mesh                     _strokeTop       = null;
        private Mesh                     _strokeWall      = null;
        private float                    _wallBottom      = 0f;
        private float                    _wallTop         = 0f;
        private Vector3                  _lastStamp       = Vector3.zero;
        private Scene                    _rockScene       = default;
        private HashSet<Collider>        _rockColliders   = null;

        private readonly List<GameObject>       _rockTemps = new List<GameObject>();
        private readonly Dictionary<long, bool> _rockCache = new Dictionary<long, bool>();
        private readonly List<Snapshot>         _history   = new List<Snapshot>();

        // 메쉬에서 마스크를 다시 읽으면 윤곽 위 칸이 빠져 조금씩 줄어든다. 브러시로 만든 마스크를 메쉬별로 들고 있다가 다시 쓴다
        private readonly Dictionary<Mesh, DeckShapeUtility.Mask> _maskCache = new Dictionary<Mesh, DeckShapeUtility.Mask>();
        private readonly Dictionary<Mesh, int>                   _maskStamp = new Dictionary<Mesh, int>();

        /****************************************
        *              Unity Event
        ****************************************/

        [MenuItem(MENU_PATH)]
        private static void Open()
        {
            GetWindow<DeckShapeBrushWindow>(WINDOW_TITLE);
        }

        private void OnEnable()
        {
            _serialized = new SerializedObject(this);
            SceneView.duringSceneGui += OnSceneGui;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            EndStroke();
            ReleaseRock();
            ClearHistory();
            ForgetMasks();
        }

        private void OnGUI()
        {
            _serialized.Update();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            Scene scene = SceneManager.GetActiveScene();
            bool editable = CanEdit(scene);
            if (!editable)
            {
                EditorGUILayout.HelpBox($"원본 덱은 팀장 소유 씬(ShinshiTown 등)도 함께 쓴다. 덱 모양은 {EDITABLE_FOLDER} 아래 개발 씬에서만 고칠 수 있다.", MessageType.Warning);
                _painting = false;
            }

            EditorGUILayout.HelpBox("브러시를 켜고 씬 뷰에서 덱이나 포장(돌판)을 누르거나 드래그한다.\n넓히기: 원 안을 채운다(바깥 허공에서도 대상 높이 기준)\n깎기: 원 안을 지운다\n처음 고칠 때 원본 obj 대신 이 씬 전용 사본(FittedDecks)을 만들어 연결한다.\n고친 뒤 NavMesh 다시 굽기를 누른다.", MessageType.Info);

            using (new EditorGUI.DisabledScope(!editable))
            {
                Color previous = GUI.backgroundColor;
                GUI.backgroundColor = _painting ? Color.green : previous;
                if (GUILayout.Button(_painting ? "브러시 끄기" : "브러시 켜기", GUILayout.Height(EditorGUIUtility.singleLineHeight * TOGGLE_LINES)))
                {
                    _painting = !_painting;
                    if (!_painting)
                    {
                        EndStroke();
                        ReleaseRock();
                    }
                    SceneView.RepaintAll();
                }
                GUI.backgroundColor = previous;

                EditorGUILayout.LabelField("대상", (_target != null) ? _target.Root.name : "씬 뷰에서 덱이나 포장을 누르면 정해진다");

                EditorGUILayout.Space();
                EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(_mode)), new GUIContent("모드"));
                EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(_radius)), new GUIContent("반경(m)"));
                EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(_rockOnly)), new GUIContent("받침 위로만 넓히기", "덱은 덱 높이 근처 돌 위, 포장은 덱 윗면 위로만 넓힌다"));
                EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(_ledgeBelow)), new GUIContent("받침 돌 아래 허용(m)"));
                EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(_ledgeAbove)), new GUIContent("받침 돌 위 허용(m)"));

                using (new EditorGUI.DisabledScope(_history.Count == 0))
                {
                    if (GUILayout.Button($"한 단계 되돌리기 ({_history.Count})"))
                    {
                        UndoLast();
                    }
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("돌 지형에 자동 맞추기", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("원본 윤곽에서 다시 시작해, 아래에 돌이 없는 가장자리는 깎고 덱 높이 근처 돌 위로는 넓힌다. 소품이 선 자리는 남긴다. 손으로 고친 모양은 덮어쓴다.", MessageType.None);
                EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(_extendDistance)), new GUIContent("최대 넓힘 거리(m)"));
                EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(_floatDepth)), new GUIContent("허공 판정 깊이(m)"));
                EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(_protectRadius)), new GUIContent("소품 보호 반경(m)"));
                _showSources = EditorGUILayout.Foldout(_showSources, "돌·물·소품 루트 이름");
                if (_showSources)
                {
                    EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(_rockRootPrefixes)), new GUIContent("돌 지형 루트 (앞부분)"), true);
                    EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(_waterNames)), new GUIContent("넓히지 않을 물"), true);
                    EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(_propRootNames)), new GUIContent("소품 루트"), true);
                }

                if (GUILayout.Button("선택한 덱 돌 지형에 맞추기"))
                {
                    FitDecks(scene, false);
                }

                if (GUILayout.Button("모든 덱 돌 지형에 맞추기"))
                {
                    FitDecks(scene, true);
                }

                EditorGUILayout.Space();
                if (GUILayout.Button("선택한 덱 원본으로 되돌리기"))
                {
                    RestoreSelection();
                }

                if (GUILayout.Button("NavMesh 다시 굽기"))
                {
                    int count = DeckShapeUtility.RebakeNavMesh(scene);
                    Debug.Log($"[DeckShape] NavMeshSurface {count}개를 다시 구웠다");
                }
            }

            EditorGUILayout.EndScrollView();
            _serialized.ApplyModifiedProperties();
        }

        /****************************************
        *                Methods
        ****************************************/

        private static bool CanEdit(Scene scene)
        {
            return (scene.IsValid()) && (scene.path.StartsWith(EDITABLE_FOLDER));
        }

        private void OnSceneGui(SceneView view)
        {
            if (!_painting)
            {
                return;
            }

            Event current = Event.current;
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            if (current.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(controlId);
                return;
            }

            // 마우스를 떼는 이벤트는 커서가 어디 있든 받아야 저장과 hotControl 해제가 빠지지 않는다
            if ((current.type == EventType.MouseUp) && (GUIUtility.hotControl == controlId))
            {
                GUIUtility.hotControl = 0;
                _activeControlId = 0;
                EndStroke();
                current.Use();
                return;
            }

            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            bool stroking = (_mask != null);
            // 화면 고르기는 그리기 중에 부르면 렌더링이 꼬이므로 누를 때만 하고, 커서 표시는 물리 레이로 찾는다
            DeckShapeUtility.Deck deck = _target;
            if (!stroking)
            {
                DeckShapeUtility.Deck picked = (current.type == EventType.MouseDown) ? PickDeck(current.mousePosition, ray) : PickDeckByRay(ray);
                deck = picked ?? _target;
            }
            if (deck == null)
            {
                return;
            }

            float height = stroking ? _mask.Height : DeckShapeUtility.GetHeight(deck);
            if (!new Plane(Vector3.up, new Vector3(0f, height, 0f)).Raycast(ray, out float enter))
            {
                return;
            }
            Vector3 center = ray.GetPoint(enter);

            bool primaryClick = (current.button == 0) && (!current.alt);
            switch (current.type)
            {
                case EventType.Repaint:
                    DrawBrush(center, deck);
                    break;
                case EventType.MouseMove:
                    view.Repaint();
                    break;
                case EventType.MouseDown:
                    if (!primaryClick)
                    {
                        break;
                    }

                    GUIUtility.hotControl = controlId;
                    _activeControlId = controlId;
                    BeginStroke(deck);
                    Stamp(center);
                    current.Use();
                    break;
                case EventType.MouseDrag:
                    if ((GUIUtility.hotControl != controlId) || (_mask == null))
                    {
                        break;
                    }

                    if (Vector3.Distance(center, _lastStamp) >= (_radius * STAMP_SPACING))
                    {
                        Stamp(center);
                    }
                    current.Use();
                    view.Repaint();
                    break;
            }
        }

        // 커서 아래 보이는 덱 윗면이나 포장을 고른다. 포장은 콜라이더가 없어 화면 고르기로 찾고,
        // 건물 등이 가리면 물리 레이로 덱 윗면을 찾는다. 투명 벽 콜라이더는 건너뛴다
        private static DeckShapeUtility.Deck PickDeck(Vector2 mousePosition, Ray ray)
        {
            GameObject picked = HandleUtility.PickGameObject(mousePosition, false);
            if ((picked != null) && (DeckShapeUtility.TryGetDeck(picked.transform, out DeckShapeUtility.Deck pickedDeck))
                && (picked.transform == pickedDeck.TopFilter.transform))
            {
                return pickedDeck;
            }
            return PickDeckByRay(ray);
        }

        private static DeckShapeUtility.Deck PickDeckByRay(Ray ray)
        {
            RaycastHit[] hits = Physics.RaycastAll(ray, MAX_PICK_DISTANCE, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if ((DeckShapeUtility.TryGetDeck(hit.collider.transform, out DeckShapeUtility.Deck deck)) && (hit.collider == deck.TopCollider))
                {
                    return deck;
                }
            }
            return null;
        }

        private void DrawBrush(Vector3 center, DeckShapeUtility.Deck deck)
        {
            Handles.color = (_mode == EDeckBrushMode.Grow) ? G_GrowColor : G_CutColor;
            Handles.DrawWireDisc(center, Vector3.up, _radius, RING_THICKNESS);
            Handles.Label(center + (Vector3.up * LABEL_LIFT), deck.Root.name);
        }

        private void BeginStroke(DeckShapeUtility.Deck deck)
        {
            _target = deck;
            DeckShapeUtility.EnsureEditable(deck, out _strokeTop, out _strokeWall);
            PushSnapshot(deck);
            // 다른 도구가 메쉬를 바꿨으면(꼭짓점 수가 다르면) 메쉬에서 다시 읽는다
            if ((_maskCache.TryGetValue(_strokeTop, out DeckShapeUtility.Mask cached)) && (_maskStamp[_strokeTop] == _strokeTop.vertexCount))
            {
                _mask = cached;
            }
            else
            {
                _mask = DeckShapeUtility.ReadMask(deck);
            }
            DeckShapeUtility.GetWallRange(deck, _mask.Height, out _wallBottom, out _wallTop);
            _rockCache.Clear();
            if ((_mode == EDeckBrushMode.Grow) && (_rockOnly) && (!deck.IsPaving))
            {
                PrepareRock(deck.Root.gameObject.scene);
            }
            Repaint();
        }

        private void Stamp(Vector3 center)
        {
            _lastStamp = center;
            _mask.EnsureContains(center.x, center.z, _radius);

            bool grow = (_mode == EDeckBrushMode.Grow);
            float radiusSqr = _radius * _radius;
            int minI = Mathf.FloorToInt((center.x - _radius - _mask.OriginX) / DeckShapeUtility.CELL_SIZE);
            int maxI = Mathf.CeilToInt((center.x + _radius - _mask.OriginX) / DeckShapeUtility.CELL_SIZE);
            int minJ = Mathf.FloorToInt((center.z - _radius - _mask.OriginZ) / DeckShapeUtility.CELL_SIZE);
            int maxJ = Mathf.CeilToInt((center.z + _radius - _mask.OriginZ) / DeckShapeUtility.CELL_SIZE);
            for (int i = Mathf.Max(minI, 0); i <= Mathf.Min(maxI, _mask.SizeX - 1); ++i)
            {
                for (int j = Mathf.Max(minJ, 0); j <= Mathf.Min(maxJ, _mask.SizeZ - 1); ++j)
                {
                    float dx = _mask.WorldX(i) - center.x;
                    float dz = _mask.WorldZ(j) - center.z;
                    if (((dx * dx) + (dz * dz)) > radiusSqr)
                    {
                        continue;
                    }

                    if (!grow)
                    {
                        _mask.Set(i, j, false);
                    }
                    else if ((!_rockOnly) || (HasRockSupport(_mask.WorldX(i), _mask.WorldZ(j))))
                    {
                        _mask.Set(i, j, true);
                    }
                }
            }

            DeckShapeUtility.WriteMeshes(_target, _mask, _strokeTop, _strokeWall, _wallBottom, _wallTop, false);
        }

        // 덱은 덱 높이 근처 돌, 포장은 바로 아래 덱 윗면이 받칠 때만 넓힌다
        private bool HasRockSupport(float x, float z)
        {
            long key = ((long)Mathf.RoundToInt(x * VERTEX_KEY_SCALE) << KEY_SHIFT) ^ (uint)Mathf.RoundToInt(z * VERTEX_KEY_SCALE);
            if (_rockCache.TryGetValue(key, out bool supported))
            {
                return supported;
            }

            if (_target.IsPaving)
            {
                supported = HasDeckBelow(x, z);
            }
            else if (_rockColliders != null)
            {
                float top = DeckShapeUtility.SampleTop(x, z, _mask.Height, _rockColliders);
                supported = (top >= (_mask.Height - _ledgeBelow)) && (top <= (_mask.Height + _ledgeAbove));
            }
            _rockCache[key] = supported;
            return supported;
        }

        private bool HasDeckBelow(float x, float z)
        {
            var ray = new Ray(new Vector3(x, _mask.Height + PAVING_DECK_GAP, z), Vector3.down);
            foreach (RaycastHit hit in Physics.RaycastAll(ray, PAVING_DECK_GAP * 2f, Physics.AllLayers, QueryTriggerInteraction.Ignore))
            {
                if ((DeckShapeUtility.TryGetDeck(hit.collider.transform, out DeckShapeUtility.Deck deck)) && (!deck.IsPaving) && (hit.collider == deck.TopCollider))
                {
                    return true;
                }
            }
            return false;
        }

        private void EndStroke()
        {
            if (_mask == null)
            {
                return;
            }

            DeckShapeUtility.RefreshColliders(_target, _strokeTop, _strokeWall);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(_target.Root.gameObject.scene);
            _maskCache[_strokeTop] = _mask;
            _maskStamp[_strokeTop] = _strokeTop.vertexCount;
            _mask       = null;
            _strokeTop  = null;
            _strokeWall = null;
            Repaint();
        }

        // 돌 콜라이더 굽기는 느려서 브러시를 켜 둔 동안 한 번만 만든다
        private void PrepareRock(Scene scene)
        {
            if ((_rockColliders != null) && (_rockScene == scene))
            {
                return;
            }

            ReleaseRock();
            _rockScene = scene;
            _rockColliders = DeckShapeUtility.CreateRockColliders(scene, _rockRootPrefixes, _rockTemps);
        }

        private void ReleaseRock()
        {
            foreach (GameObject temp in _rockTemps)
            {
                if (temp != null)
                {
                    DestroyImmediate(temp);
                }
            }
            _rockTemps.Clear();
            _rockColliders = null;
            _rockCache.Clear();
        }

        private void FitDecks(Scene scene, bool all)
        {
            List<DeckShapeUtility.Deck> decks = DeckShapeUtility.FindDecks(scene);
            var targets = new HashSet<DeckShapeUtility.Deck>();
            if (all)
            {
                if (!EditorUtility.DisplayDialog(WINDOW_TITLE, $"덱 {decks.Count}개를 원본 윤곽에서 다시 맞춘다. 손으로 고친 모양은 덮어쓴다.", "맞추기", "취소"))
                {
                    return;
                }
                targets.UnionWith(decks);
            }
            else
            {
                foreach (DeckShapeUtility.Deck deck in decks)
                {
                    if (IsSelected(deck))
                    {
                        targets.Add(deck);
                    }
                }
            }

            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog(WINDOW_TITLE, "고칠 덱을 하이어라키에서 선택한다.", "확인");
                return;
            }

            EndStroke();
            ForgetMasks();
            foreach (DeckShapeUtility.Deck deck in targets)
            {
                DeckShapeUtility.EnsureEditable(deck, out _, out _);
                PushSnapshot(deck);
            }

            var settings = new DeckShapeUtility.FitSettings
            {
                ExtendDistance   = _extendDistance,
                FloatDepth       = _floatDepth,
                LedgeBelow       = _ledgeBelow,
                LedgeAbove       = _ledgeAbove,
                ProtectRadius    = _protectRadius,
                RockRootPrefixes = _rockRootPrefixes,
                WaterNames       = _waterNames,
                PropRootNames    = _propRootNames
            };
            int written = DeckShapeUtility.FitToRock(scene, decks, targets, settings);
            Debug.Log($"[DeckShape] 덱 {written}개를 돌 지형에 맞췄다. NavMesh를 다시 구워야 한다");
        }

        private void RestoreSelection()
        {
            EndStroke();
            ForgetMasks();
            int restored = 0;
            foreach (DeckShapeUtility.Deck deck in DeckShapeUtility.FindShapes(SceneManager.GetActiveScene(), true))
            {
                if ((IsSelected(deck)) && (DeckShapeUtility.RestoreOriginal(deck)))
                {
                    ++restored;
                }
            }
            Debug.Log($"[DeckShape] 덱 {restored}개를 원본 obj로 되돌렸다. 사본 에셋은 지우지 않는다");
        }

        private static bool IsSelected(DeckShapeUtility.Deck deck)
        {
            foreach (Transform selected in Selection.transforms)
            {
                if ((selected == deck.Root) || (selected.IsChildOf(deck.Root)))
                {
                    return true;
                }
            }
            return false;
        }

        private void PushSnapshot(DeckShapeUtility.Deck deck)
        {
            // 포장은 벽이 없어 윗면만 기록한다
            Mesh wall = (deck.WallFilter != null) ? deck.WallFilter.sharedMesh : null;
            var snapshot = new Snapshot
            {
                Root     = deck.Root,
                Top      = deck.TopFilter.sharedMesh,
                Wall     = wall,
                TopCopy  = Instantiate(deck.TopFilter.sharedMesh),
                WallCopy = (wall != null) ? Instantiate(wall) : null
            };
            snapshot.TopCopy.hideFlags = HideFlags.HideAndDontSave;
            if (snapshot.WallCopy != null)
            {
                snapshot.WallCopy.hideFlags = HideFlags.HideAndDontSave;
            }
            _history.Add(snapshot);

            if (_history.Count > MAX_HISTORY)
            {
                DestroySnapshot(_history[0]);
                _history.RemoveAt(0);
            }
        }

        private void ForgetMasks()
        {
            _maskCache.Clear();
            _maskStamp.Clear();
        }

        private void UndoLast()
        {
            EndStroke();
            ForgetMasks();
            Snapshot snapshot = _history[_history.Count - 1];
            _history.RemoveAt(_history.Count - 1);

            if ((snapshot.Root != null) && (snapshot.Top != null)
                && (DeckShapeUtility.TryGetDeck(snapshot.Root, out DeckShapeUtility.Deck deck)))
            {
                DeckShapeUtility.CopyMesh(snapshot.TopCopy, snapshot.Top);
                if ((snapshot.Wall != null) && (snapshot.WallCopy != null))
                {
                    DeckShapeUtility.CopyMesh(snapshot.WallCopy, snapshot.Wall);
                }
                DeckShapeUtility.RefreshColliders(deck, snapshot.Top, snapshot.Wall);
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(snapshot.Root.gameObject.scene);
            }
            DestroySnapshot(snapshot);
            SceneView.RepaintAll();
        }

        private void ClearHistory()
        {
            foreach (Snapshot snapshot in _history)
            {
                DestroySnapshot(snapshot);
            }
            _history.Clear();
        }

        private static void DestroySnapshot(Snapshot snapshot)
        {
            DestroyImmediate(snapshot.TopCopy);
            if (snapshot.WallCopy != null)
            {
                DestroyImmediate(snapshot.WallCopy);
            }
        }
    }
}
#endif
