#if UNITY_EDITOR
// System
using System;
using System.Collections.Generic;
using System.IO;

// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shinshi.Art.Editor
{
    /// <summary> LOD가 있는 바위, 섬 청크를 씬 전용 사본으로 바꿔 모든 LOD와 충돌 메시를 함께 깎는 씬 뷰 브러시 </summary>
    public sealed class RockCarveBrushWindow : EditorWindow
    {
        public enum ECarveMode
        {
            Carve,
            Smooth,
            PlaneCut
        }

        /****************************************
        *                Fields
        ****************************************/

        private const string MENU_PATH             = "Shinshi/Art/Environment/바위 깎기 브러시";
        private const string WINDOW_TITLE          = "바위 깎기 브러시";
        private const string MESH_FOLDER           = "Assets/01.Scripts/05.Art/Environment/VillageGroundPatch/CarvedMeshes";
        private const string ALLOWED_SCENE_ROOT    = "Assets/00.Scenes/Dev_HUD/";
        private const string UNDO_LABEL            = "바위 깎기 브러시";
        private const string PICK_OBJECT_NAME      = "__RockCarvePick";
        private const string LOG_PREFIX            = "[RockCarve] ";
        private const float  MIN_RADIUS            = 0.2f;      // 브러시 최소 반경(m)
        private const float  MAX_RADIUS            = 15f;       // 브러시 최대 반경(m)
        private const float  MAX_CARVE_RATE        = 5f;        // 깎기 최대 속도(m/s)
        private const float  MAX_BLEND_RATE        = 10f;       // 부드럽게, 평면 자르기 최대 혼합 속도(1/s)
        private const float  PLANE_CLEARANCE       = 0.03f;     // 잘린 면이 계단 면과 겹쳐 깜빡이지 않게 평면 뒤로 두는 거리(m)
        private const float  NO_SUBDIVISION        = 1000f;     // 사본을 만들 때 원본 삼각형을 나누지 않는다
        private const float  STAMP_INTERVAL        = 0.05f;     // 누르는 동안 초당 20회 편집
        private const float  PREVIEW_WIDTH         = 2f;
        private const float  PLANE_PREVIEW_SIZE    = 1.5f;
        private const float  MAX_PICK_DISTANCE     = 2000f;
        private const float  BUTTON_LINES          = 2f;
        private const float  MIN_CHANGE            = 0.000001f;
        private const float  WELD_DISTANCE         = 0.0001f;
        private const float  WINDOW_MIN_WIDTH      = 380f;
        private const float  WINDOW_MIN_HEIGHT     = 420f;
        private const int    VERTICES_PER_TRIANGLE = 3;
        private const int    CONTROL_HINT          = 193472;

        private static readonly string[] G_ModeLabels = { "깎기", "부드럽게", "평면 자르기" };

        [SerializeField] private GameObject         _target      = null;
        [SerializeField] private GameObject         _prepared    = null;
        [SerializeField] private List<MeshFilter>   _filters     = new List<MeshFilter>();
        [SerializeField] private List<MeshCollider> _colliders   = new List<MeshCollider>();
        [SerializeField] private List<Mesh>         _meshes      = new List<Mesh>();
        [SerializeField] private ECarveMode         _mode        = ECarveMode.Carve;
        [SerializeField] private float              _radius      = 1.5f;
        [SerializeField] private float              _carveRate   = 0.8f;
        [SerializeField] private float              _blendRate   = 3f;
        [SerializeField] private bool               _planeSet    = false;
        [SerializeField] private Vector3            _planePoint  = Vector3.zero;
        [SerializeField] private Vector3            _planeNormal = Vector3.up;

        private sealed class CarveTopology
        {
            public int[]    _groups;     // 같은 위치 정점을 하나로 묶은 번호
            public int[][]  _neighbors;  // 묶음 사이 이웃
            public bool[]   _touched;    // 이번 편집 세션에서 움직인 정점
        }

        private readonly Dictionary<Mesh, CarveTopology> _topology = new Dictionary<Mesh, CarveTopology>();
        private RockCarveUndoStateSO _undoState     = null;
        private MeshCollider         _pickCollider  = null;
        private bool                 _painting      = false;
        private bool                 _strokeActive  = false;
        private bool                 _strokeChanged = false;
        private bool                 _hasHit        = false;
        private bool                 _shift         = false;
        private bool                 _smooth        = false;
        private int                  _controlId     = 0;
        private int                  _undoGroup     = -1;
        private double               _lastStampTime = 0;
        private Vector3              _hitPoint      = Vector3.zero;
        private Vector3              _hitNormal     = Vector3.up;
        private SceneView            _strokeView    = null;
        private string               _status        = "Hierarchy에서 깎을 바위나 섬 청크(LOD 묶음)를 선택하세요.";
        private Vector2              _scroll        = Vector2.zero;

        /****************************************
        *              Unity Event
        ****************************************/

        [MenuItem(MENU_PATH)]
        public static void Open()
        {
            GetWindow<RockCarveBrushWindow>(WINDOW_TITLE);
        }

        private void OnEnable()
        {
            minSize = new Vector2(WINDOW_MIN_WIDTH, WINDOW_MIN_HEIGHT);
            SceneView.duringSceneGui += OnSceneGui;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            _undoState = CreateInstance<RockCarveUndoStateSO>();
            _undoState.hideFlags = HideFlags.DontSave;
        }

        private void OnDisable()
        {
            FinishStroke();
            SceneView.duringSceneGui -= OnSceneGui;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            DestroyPickCollider();
            if (_undoState != null)
            {
                DestroyImmediate(_undoState);
            }
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.HelpBox("바위를 준비한 뒤 브러시를 켜고 Scene 뷰에서 누르거나 드래그하세요.\n깎기: 누른 면 안쪽으로 밀어 넣기, Shift: 바깥으로 부풀리기\nCtrl: 부드럽게, Alt: 카메라 조작\n평면 자르기: Shift+클릭으로 자를 평면(계단 면 등)을 잡고 칠하면 그 평면 바깥으로 나온 부분을 평면 뒤로 민다\nCtrl+Z: 한 번의 드래그 되돌리기, Esc: 브러시 끄기", MessageType.Info);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                EditorGUI.BeginChangeCheck();
                GameObject target = (GameObject)EditorGUILayout.ObjectField("깎을 바위", _target, typeof(GameObject), true);
                if (EditorGUI.EndChangeCheck())
                {
                    StopPainting();
                    _target = target;
                }
                if (GUILayout.Button("선택한 바위 사용"))
                {
                    UseSelection();
                }
                EditorGUILayout.HelpBox("준비하면 모든 LOD 메시와 충돌 메시를 이 씬 전용 사본으로 바꿉니다. 원본 FBX와 다른 씬은 바뀌지 않습니다.", MessageType.None);
                using (new EditorGUI.DisabledScope(_target == null))
                {
                    if (GUILayout.Button("선택 바위의 편집 사본 준비"))
                    {
                        PrepareTarget();
                    }
                }

                EditorGUILayout.Space();
                _mode = (ECarveMode)GUILayout.Toolbar((int)_mode, G_ModeLabels);
                _radius = EditorGUILayout.Slider("반경(m)", _radius, MIN_RADIUS, MAX_RADIUS);
                _carveRate = EditorGUILayout.Slider("깎기 속도(m/s)", _carveRate, MIN_RADIUS, MAX_CARVE_RATE);
                _blendRate = EditorGUILayout.Slider("부드럽게/자르기 강도", _blendRate, MIN_RADIUS, MAX_BLEND_RATE);
                EditorGUILayout.LabelField("자를 평면", _planeSet ? _planePoint.ToString("F2") + " / 법선 " + _planeNormal.ToString("F2") : "없음 (평면 자르기 모드에서 Shift+클릭)");
                using (new EditorGUI.DisabledScope(!_planeSet))
                {
                    if (GUILayout.Button("평면 지우기"))
                    {
                        _planeSet = false;
                        SceneView.RepaintAll();
                    }
                }

                using (new EditorGUI.DisabledScope(!IsPrepared()))
                {
                    if (GUILayout.Button(_painting ? "브러시 끄기" : "브러시 켜기", GUILayout.Height(EditorGUIUtility.singleLineHeight * BUTTON_LINES)))
                    {
                        FinishStroke();
                        if (!_painting)
                        {
                            GroundPatchBrushWindow.StopAllPainting();
                            GroundSculptBrushWindow.StopAllPainting();
                            EnsurePickCollider();
                        }
                        _painting = !_painting;
                        SceneView.RepaintAll();
                    }
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(_status, MessageType.None);
            if (IsPrepared())
            {
                foreach (Mesh mesh in _meshes)
                {
                    EditorGUILayout.ObjectField(mesh.name + " (" + mesh.vertexCount.ToString("N0") + ")", mesh, typeof(Mesh), false);
                }
            }
            EditorGUILayout.HelpBox("편집 후 씬을 저장하고 NavMesh를 다시 구우세요. 깎은 부분의 AO, 텍스처는 원본 그대로라 크게 깎으면 무늬가 늘어날 수 있습니다.", MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void UseSelection()
        {
            StopPainting();
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                _status = "Hierarchy에서 바위를 먼저 선택하세요.";
                return;
            }
            LODGroup group = selected.GetComponentInParent<LODGroup>();
            _target = (group != null) ? group.gameObject : selected;
        }

        private static bool IsAllowedScene(GameObject gameObject)
        {
            string path = gameObject.scene.path;
            return (!string.IsNullOrEmpty(path)) && path.StartsWith(ALLOWED_SCENE_ROOT, StringComparison.Ordinal);
        }

        private bool CanEditTarget()
        {
            return (_target != null) && (!EditorUtility.IsPersistent(_target))
                && (_target.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene())
                && IsAllowedScene(_target)
                && (!EditorApplication.isPlayingOrWillChangePlaymode)
                && (PrefabStageUtility.GetPrefabStage(_target) == null)
                && (!PrefabUtility.IsPartOfPrefabAsset(_target));
        }

        private bool IsPrepared()
        {
            if ((!CanEditTarget()) || (_target != _prepared) || (_meshes.Count == 0) || (_filters.Count == 0))
            {
                return false;
            }
            foreach (MeshFilter filter in _filters)
            {
                if ((filter == null) || (filter.sharedMesh == null) || (!IsCarvedAsset(filter.sharedMesh)))
                {
                    return false;
                }
            }
            foreach (MeshCollider collider in _colliders)
            {
                if ((collider == null) || (collider.sharedMesh == null) || (!IsCarvedAsset(collider.sharedMesh)))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsCarvedAsset(Mesh mesh)
        {
            return AssetDatabase.GetAssetPath(mesh).StartsWith(MESH_FOLDER + "/", StringComparison.Ordinal);
        }

        private void PrepareTarget()
        {
            StopPainting();
            if (_target == null)
            {
                return;
            }
            if (!IsAllowedScene(_target))
            {
                _status = "릴리즈 씬 보호를 위해 " + ALLOWED_SCENE_ROOT + " 아래 개발 씬에서만 깎을 수 있습니다.";
                return;
            }
            if (!CanEditTarget())
            {
                _status = "활성 씬에 놓인 바위 인스턴스만 편집할 수 있습니다.";
                return;
            }
            if (_target.GetComponentInParent<Rigidbody>() != null)
            {
                _status = "Rigidbody가 없는 정적 바위만 편집할 수 있습니다.";
                return;
            }

            var filters = new List<MeshFilter>();
            if (_target.TryGetComponent(out LODGroup lodGroup))
            {
                foreach (LOD lod in lodGroup.GetLODs())
                {
                    foreach (Renderer renderer in lod.renderers)
                    {
                        if ((renderer != null) && renderer.TryGetComponent(out MeshFilter filter) && (!filters.Contains(filter)))
                        {
                            filters.Add(filter);
                        }
                    }
                }
            }
            else
            {
                filters.AddRange(_target.GetComponentsInChildren<MeshFilter>());
            }
            var colliders = new List<MeshCollider>();
            foreach (MeshCollider collider in _target.GetComponentsInChildren<MeshCollider>())
            {
                if ((collider.sharedMesh != null) && (!collider.convex) && (!collider.isTrigger))
                {
                    colliders.Add(collider);
                }
            }
            if (filters.Count == 0)
            {
                _status = "MeshFilter가 있는 바위를 선택하세요.";
                return;
            }

            // LOD끼리 메시를 공유하면 사본도 하나를 공유해 같은 모양을 유지한다
            var copies = new Dictionary<Mesh, Mesh>();
            var created = new List<Mesh>();
            string folder = MESH_FOLDER + "/" + SafeName(Path.GetFileNameWithoutExtension(_target.scene.path));
            try
            {
                string owner = SafeName(_target.name);
                foreach (MeshFilter filter in filters)
                {
                    GetOrCreateCopy(filter.sharedMesh, filter.transform, owner, folder, copies, created);
                }
                foreach (MeshCollider collider in colliders)
                {
                    GetOrCreateCopy(collider.sharedMesh, collider.transform, owner, folder, copies, created);
                }
            }
            catch (Exception exception)
            {
                foreach (Mesh mesh in created)
                {
                    if (!AssetDatabase.Contains(mesh))
                    {
                        DestroyImmediate(mesh);
                    }
                }
                _status = exception.Message;
                Debug.LogError(LOG_PREFIX + exception.Message, _target);
                return;
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("바위 편집 사본 준비");
            foreach (MeshFilter filter in filters)
            {
                Undo.RecordObject(filter, UNDO_LABEL);
                filter.sharedMesh = copies[filter.sharedMesh];
                RecordPrefabOverride(filter);
            }
            foreach (MeshCollider collider in colliders)
            {
                Undo.RecordObject(collider, UNDO_LABEL);
                collider.sharedMesh = copies[collider.sharedMesh];
                RecordPrefabOverride(collider);
            }
            Undo.CollapseUndoOperations(group);

            _filters = filters;
            _colliders = colliders;
            _meshes = new List<Mesh>(new HashSet<Mesh>(copies.Values));
            _prepared = _target;
            _topology.Clear();
            EditorSceneManager.MarkSceneDirty(_target.scene);
            AssetDatabase.SaveAssets();
            _status = "준비 완료. 메시 " + _meshes.Count + "개 (LOD " + filters.Count + ", 충돌 " + colliders.Count + "). 브러시를 켜세요.";
        }

        // 이름은 깎는 바위(LOD 묶음) 기준으로 짓는다. 최상위 부모로 지으면 같은 원본을 쓰는
        // 청크 둘이 같은 이름을 받아 번호만 다른 사본이 생겨 어느 것인지 알 수 없다
        private static void GetOrCreateCopy(Mesh source, Transform transform, string owner, string folder,
            Dictionary<Mesh, Mesh> copies, List<Mesh> created)
        {
            if ((source == null) || copies.ContainsKey(source))
            {
                return;
            }
            if (IsCarvedAsset(source))
            {
                copies.Add(source, source);   // 이미 이 도구로 만든 사본이면 그대로 이어서 편집
                return;
            }
            Mesh copy = GroundSculptMeshUtility.CreateEditableMesh(source, transform, NO_SUBDIVISION);
            copy.name = "SM_Carve_" + owner + "_" + SafeName(source.name);
            created.Add(copy);
            SaveNewMesh(copy, folder);
            copies.Add(source, copy);
        }

        private static string SafeName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "Unsaved";
            }
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }
            return name.Replace(' ', '_');
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path))))
            {
                throw new IOException("바위 편집 사본 폴더를 만들지 못했습니다: " + path);
            }
        }

        private static void SaveNewMesh(Mesh copy, string folder)
        {
            EnsureFolder(folder);
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + copy.name + ".asset");
            AssetDatabase.CreateAsset(copy, path);
            if (!AssetDatabase.Contains(copy))
            {
                throw new IOException("바위 편집 사본을 저장하지 못했습니다: " + path);
            }
        }

        private static void RecordPrefabOverride(UnityEngine.Object component)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(component))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }
        }

        // 바위 옆면을 집으려면 실제 보이는 LOD0 모양이 필요하다. 섬 청크의 충돌 메시는
        // 윗면만 덮는 경우가 많아 그것으로는 계단을 뚫은 옆면을 집을 수 없다
        private void EnsurePickCollider()
        {
            if ((_filters.Count == 0) || (_filters[0] == null))
            {
                return;
            }
            if (_pickCollider == null)
            {
                var pick = new GameObject(PICK_OBJECT_NAME) { hideFlags = HideFlags.HideAndDontSave };
                _pickCollider = pick.AddComponent<MeshCollider>();
            }
            Transform source = _filters[0].transform;
            _pickCollider.transform.SetPositionAndRotation(source.position, source.rotation);
            _pickCollider.transform.localScale = source.lossyScale;
            RefreshCollider(_pickCollider, _filters[0].sharedMesh);
        }

        private void DestroyPickCollider()
        {
            if (_pickCollider != null)
            {
                DestroyImmediate(_pickCollider.gameObject);
            }
            _pickCollider = null;
        }

        private static void RefreshCollider(MeshCollider collider, Mesh mesh)
        {
            if (collider == null)
            {
                return;
            }
            collider.sharedMesh = null;
            collider.sharedMesh = mesh;
        }

        private void StopPainting()
        {
            FinishStroke();
            _painting = false;
        }

        private void OnSceneGui(SceneView view)
        {
            if (!_painting)
            {
                return;
            }
            if (_strokeActive && (view != _strokeView))
            {
                return;
            }
            if (!IsPrepared())
            {
                StopPainting();
                return;
            }
            EnsurePickCollider();

            Event current = Event.current;
            int control = GUIUtility.GetControlID(CONTROL_HINT, FocusType.Passive);
            if ((current.rawType == EventType.MouseUp) && (current.button == 0) && _strokeActive)
            {
                FinishStroke();
                current.Use();
                return;
            }
            if ((current.type == EventType.KeyDown) && (current.keyCode == KeyCode.Escape))
            {
                StopPainting();
                current.Use();
                Repaint();
                return;
            }
            if ((current.type == EventType.KeyDown) && (current.keyCode == KeyCode.Z) && (current.control || current.command))
            {
                FinishStroke();
                return;
            }
            if (current.alt)
            {
                FinishStroke();
                _hasHit = false;
                return;
            }
            if (current.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(control);
            }

            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            _hasHit = _pickCollider.Raycast(ray, out RaycastHit hit, MAX_PICK_DISTANCE);
            _shift = current.shift;
            _smooth = current.control || current.command;
            if (_hasHit)
            {
                _hitPoint = hit.point;
                _hitNormal = hit.normal;
            }

            if (current.type == EventType.Repaint)
            {
                DrawPreview();
            }
            if (current.type == EventType.MouseMove)
            {
                view.Repaint();
            }
            if ((current.type == EventType.MouseDown) && (current.button == 0))
            {
                if ((_mode == ECarveMode.PlaneCut) && current.shift)
                {
                    PickPlane(ray);
                    current.Use();
                    return;
                }
                if (_hasHit)
                {
                    BeginStroke(view, control);
                    current.Use();
                }
            }
            if ((current.type == EventType.MouseDrag) && _strokeActive)
            {
                current.Use();
                view.Repaint();
            }
        }

        private void DrawPreview()
        {
            Color previous = Handles.color;
            if (_hasHit)
            {
                ECarveMode mode = GetEffectiveMode();
                Handles.color = mode == ECarveMode.Smooth ? Color.cyan
                    : mode == ECarveMode.PlaneCut ? Color.yellow
                    : _shift ? Color.green : Color.red;
                Handles.DrawWireDisc(_hitPoint, _hitNormal, _radius, PREVIEW_WIDTH);
            }
            if (_planeSet && (_mode == ECarveMode.PlaneCut))
            {
                Quaternion rotation = Quaternion.LookRotation(_planeNormal);
                Vector3 right = rotation * Vector3.right * PLANE_PREVIEW_SIZE;
                Vector3 up = rotation * Vector3.up * PLANE_PREVIEW_SIZE;
                Handles.color = Color.yellow;
                Handles.DrawLine(_planePoint - right - up, _planePoint + right - up, PREVIEW_WIDTH);
                Handles.DrawLine(_planePoint + right - up, _planePoint + right + up, PREVIEW_WIDTH);
                Handles.DrawLine(_planePoint + right + up, _planePoint - right + up, PREVIEW_WIDTH);
                Handles.DrawLine(_planePoint - right + up, _planePoint - right - up, PREVIEW_WIDTH);
                Handles.DrawLine(_planePoint, _planePoint + (_planeNormal * PLANE_PREVIEW_SIZE), PREVIEW_WIDTH);
            }
            Handles.color = previous;
        }

        // 자를 기준은 보통 바위가 뚫고 나온 계단 같은 다른 오브젝트의 면이다.
        // 바위 자신보다 먼저 맞은 다른 충돌체를 쓰고, 없으면 바위 면을 그대로 쓴다
        private void PickPlane(Ray ray)
        {
            RaycastHit[] hits = Physics.RaycastAll(ray, MAX_PICK_DISTANCE);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if ((hit.collider == _pickCollider) || (_colliders.Contains(hit.collider as MeshCollider)))
                {
                    continue;
                }
                SetPlane(hit.point, hit.normal, hit.collider.name);
                return;
            }
            if (_hasHit)
            {
                SetPlane(_hitPoint, _hitNormal, "바위 면");
                return;
            }
            _status = "평면을 잡을 면을 찾지 못했습니다. 계단이나 바위 면을 Shift+클릭하세요.";
        }

        private void SetPlane(Vector3 point, Vector3 normal, string source)
        {
            _planePoint = point;
            _planeNormal = normal.normalized;
            _planeSet = true;
            _status = "자를 평면: " + source + ". 칠하면 이 평면 바깥으로 나온 바위를 평면 뒤로 밉니다.";
            Repaint();
            SceneView.RepaintAll();
        }

        private ECarveMode GetEffectiveMode()
        {
            return _smooth ? ECarveMode.Smooth : _mode;
        }

        private void BeginStroke(SceneView view, int control)
        {
            FinishStroke();
            if ((_mode == ECarveMode.PlaneCut) && (!_planeSet) && (!_smooth))
            {
                _status = "평면 자르기는 먼저 Shift+클릭으로 자를 평면을 잡아야 합니다.";
                Repaint();
                return;
            }
            _undoState.Capture(_meshes);
            Undo.IncrementCurrentGroup();
            _undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UNDO_LABEL);
            Undo.RegisterCompleteObjectUndo(_undoState, UNDO_LABEL);
            _strokeActive = true;
            _strokeChanged = false;
            _strokeView = view;
            _controlId = control;
            GUIUtility.hotControl = control;
            _lastStampTime = EditorApplication.timeSinceStartup - STAMP_INTERVAL;
            EditorApplication.update += ContinueStroke;
            ContinueStroke();
        }

        private void ContinueStroke()
        {
            if (!_strokeActive)
            {
                return;
            }
            if ((!IsPrepared()) || (_strokeView == null) || (focusedWindow != _strokeView))
            {
                FinishStroke();
                return;
            }
            double now = EditorApplication.timeSinceStartup;
            if ((!_hasHit) || ((now - _lastStampTime) < STAMP_INTERVAL))
            {
                return;
            }
            _lastStampTime = now;

            int changed = 0;
            try
            {
                foreach (Mesh mesh in _meshes)
                {
                    changed += ApplyStamp(mesh, FindTransform(mesh));
                }
            }
            catch (Exception exception)
            {
                StopPainting();
                _status = exception.Message;
                Debug.LogError(LOG_PREFIX + exception.Message, _target);
                return;
            }
            if (changed == 0)
            {
                return;
            }
            _strokeChanged = true;
            foreach (MeshCollider collider in _colliders)
            {
                RefreshCollider(collider, collider.sharedMesh);
            }
            RefreshCollider(_pickCollider, _filters[0].sharedMesh);
            _strokeView.Repaint();
        }

        private Transform FindTransform(Mesh mesh)
        {
            foreach (MeshFilter filter in _filters)
            {
                if ((filter != null) && (filter.sharedMesh == mesh))
                {
                    return filter.transform;
                }
            }
            foreach (MeshCollider collider in _colliders)
            {
                if ((collider != null) && (collider.sharedMesh == mesh))
                {
                    return collider.transform;
                }
            }
            throw new InvalidOperationException("편집 사본을 쓰는 오브젝트를 찾지 못했습니다: " + mesh.name);
        }

        // 모든 LOD와 충돌 메시에 같은 월드 공간 변위를 적용한다. 메시마다 토폴로지는
        // 달라도 같은 위치는 같은 만큼 움직이므로 LOD를 바꿔도 깎인 모양이 유지된다
        private int ApplyStamp(Mesh mesh, Transform transform)
        {
            CarveTopology topology = GetTopology(mesh);
            Vector3[] vertices = mesh.vertices;
            int groupCount = topology._neighbors.Length;
            var positions = new Vector3[groupCount];
            var offsets = new Vector3[groupCount];
            var inside = new bool[groupCount];
            for (int vertex = 0; vertex < vertices.Length; ++vertex)
            {
                positions[topology._groups[vertex]] = transform.TransformPoint(vertices[vertex]);
            }

            ECarveMode mode = GetEffectiveMode();
            float carve = _carveRate * STAMP_INTERVAL;
            float blend = Mathf.Clamp01(_blendRate * STAMP_INTERVAL);
            Vector3 push = (_shift ? _hitNormal : -_hitNormal).normalized;
            for (int group = 0; group < groupCount; ++group)
            {
                float distance = Vector3.Distance(positions[group], _hitPoint);
                if (distance >= _radius)
                {
                    continue;
                }
                float falloff = Mathf.SmoothStep(1f, 0f, distance / _radius);
                switch (mode)
                {
                    case ECarveMode.Carve:
                        offsets[group] = push * (carve * falloff);
                        break;
                    case ECarveMode.Smooth:
                        int[] neighbors = topology._neighbors[group];
                        if (neighbors.Length == 0)
                        {
                            continue;
                        }
                        Vector3 sum = Vector3.zero;
                        foreach (int neighbor in neighbors)
                        {
                            sum += positions[neighbor];
                        }
                        offsets[group] = ((sum / neighbors.Length) - positions[group]) * (blend * falloff);
                        break;
                    case ECarveMode.PlaneCut:
                        float depth = Vector3.Dot(positions[group] - _planePoint, _planeNormal) + PLANE_CLEARANCE;
                        if (depth <= 0f)
                        {
                            continue;
                        }
                        offsets[group] = -_planeNormal * (depth * Mathf.Clamp01(blend + falloff));
                        break;
                }
                inside[group] = offsets[group].sqrMagnitude > (MIN_CHANGE * MIN_CHANGE);
            }

            int changed = 0;
            for (int vertex = 0; vertex < vertices.Length; ++vertex)
            {
                int group = topology._groups[vertex];
                if (!inside[group])
                {
                    continue;
                }
                vertices[vertex] = transform.InverseTransformPoint(positions[group] + offsets[group]);
                topology._touched[vertex] = true;
                ++changed;
            }
            if (changed != 0)
            {
                UpdateShading(mesh, vertices, topology._touched);
            }
            return changed;
        }

        // 메시 전체 법선을 다시 계산하면 원본에서 다듬은 법선과 UV 경계가 바뀌어
        // 깎지 않은 곳까지 음영이 달라진다. 움직인 정점만 새 법선, 접선을 받는다
        private static void UpdateShading(Mesh mesh, Vector3[] vertices, bool[] touched)
        {
            Vector3[] oldNormals = mesh.normals;
            Vector4[] oldTangents = mesh.tangents;
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            bool hasUv = mesh.HasVertexAttribute(VertexAttribute.TexCoord0);
            if (hasUv)
            {
                mesh.RecalculateTangents();
            }
            if (oldNormals.Length == vertices.Length)
            {
                Vector3[] normals = mesh.normals;
                for (int vertex = 0; vertex < normals.Length; ++vertex)
                {
                    if (!touched[vertex])
                    {
                        normals[vertex] = oldNormals[vertex];
                    }
                }
                mesh.normals = normals;
            }
            if (hasUv && (oldTangents.Length == vertices.Length))
            {
                Vector4[] tangents = mesh.tangents;
                for (int vertex = 0; vertex < tangents.Length; ++vertex)
                {
                    if (!touched[vertex])
                    {
                        tangents[vertex] = oldTangents[vertex];
                    }
                }
                mesh.tangents = tangents;
            }
            mesh.RecalculateBounds();
        }

        private CarveTopology GetTopology(Mesh mesh)
        {
            if (_topology.TryGetValue(mesh, out CarveTopology cached) && (cached._groups.Length == mesh.vertexCount))
            {
                return cached;
            }
            Vector3[] vertices = mesh.vertices;
            var groupByPosition = new Dictionary<Vector3Int, int>();
            var groups = new int[vertices.Length];
            for (int vertex = 0; vertex < vertices.Length; ++vertex)
            {
                Vector3 position = vertices[vertex];
                var key = new Vector3Int(Mathf.RoundToInt(position.x / WELD_DISTANCE),
                    Mathf.RoundToInt(position.y / WELD_DISTANCE), Mathf.RoundToInt(position.z / WELD_DISTANCE));
                if (!groupByPosition.TryGetValue(key, out int group))
                {
                    group = groupByPosition.Count;
                    groupByPosition.Add(key, group);
                }
                groups[vertex] = group;
            }
            var neighborSets = new HashSet<int>[groupByPosition.Count];
            for (int group = 0; group < neighborSets.Length; ++group)
            {
                neighborSets[group] = new HashSet<int>();
            }
            for (int submesh = 0; submesh < mesh.subMeshCount; ++submesh)
            {
                if (mesh.GetTopology(submesh) != MeshTopology.Triangles)
                {
                    throw new ArgumentException("삼각형 메시만 깎을 수 있습니다: " + mesh.name);
                }
                int[] indices = mesh.GetIndices(submesh);
                for (int triangle = 0; triangle < indices.Length; triangle += VERTICES_PER_TRIANGLE)
                {
                    int a = groups[indices[triangle]];
                    int b = groups[indices[triangle + 1]];
                    int c = groups[indices[triangle + 2]];
                    AddNeighbor(neighborSets, a, b);
                    AddNeighbor(neighborSets, b, c);
                    AddNeighbor(neighborSets, c, a);
                }
            }
            var topology = new CarveTopology
            {
                _groups = groups,
                _neighbors = new int[neighborSets.Length][],
                _touched = new bool[vertices.Length]
            };
            for (int group = 0; group < neighborSets.Length; ++group)
            {
                topology._neighbors[group] = new int[neighborSets[group].Count];
                neighborSets[group].CopyTo(topology._neighbors[group]);
            }
            _topology[mesh] = topology;
            return topology;
        }

        private static void AddNeighbor(HashSet<int>[] neighbors, int a, int b)
        {
            if (a == b)
            {
                return;
            }
            neighbors[a].Add(b);
            neighbors[b].Add(a);
        }

        private void FinishStroke()
        {
            EditorApplication.update -= ContinueStroke;
            if (GUIUtility.hotControl == _controlId)
            {
                GUIUtility.hotControl = 0;
            }
            if (!_strokeActive)
            {
                return;
            }
            _strokeActive = false;
            if (_strokeChanged)
            {
                _undoState.Capture(_meshes);
                SaveMeshes();
                if (_prepared != null)
                {
                    EditorSceneManager.MarkSceneDirty(_prepared.scene);
                }
                _status = "편집 사본 저장 완료. 씬 저장과 NavMesh 재베이크가 필요합니다.";
            }
            if (_undoGroup >= 0)
            {
                Undo.CollapseUndoOperations(_undoGroup);
            }
            _undoGroup = -1;
            _strokeView = null;
            Repaint();
        }

        private void SaveMeshes()
        {
            foreach (Mesh mesh in _meshes)
            {
                if (mesh == null)
                {
                    continue;
                }
                EditorUtility.SetDirty(mesh);
                AssetDatabase.SaveAssetIfDirty(mesh);
            }
        }

        private void OnUndoRedo()
        {
            // Undo 적용 중에는 새 Undo 그룹을 만들거나 병합하지 않는다
            EditorApplication.update -= ContinueStroke;
            if (GUIUtility.hotControl == _controlId)
            {
                GUIUtility.hotControl = 0;
            }
            _strokeActive = false;
            _undoGroup = -1;
            _strokeView = null;
            if ((_undoState == null) || (_undoState.Restore() == 0))
            {
                return;
            }
            foreach (Mesh mesh in _meshes)
            {
                if ((mesh != null) && mesh.HasVertexAttribute(VertexAttribute.TexCoord0))
                {
                    mesh.RecalculateTangents();
                }
            }
            foreach (MeshCollider collider in _colliders)
            {
                RefreshCollider(collider, (collider != null) ? collider.sharedMesh : null);
            }
            if (_filters.Count > 0)
            {
                RefreshCollider(_pickCollider, _filters[0].sharedMesh);
            }
            SaveMeshes();
            SceneView.RepaintAll();
            Repaint();
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            StopPainting();
            DestroyPickCollider();
        }
    }
}
#endif
