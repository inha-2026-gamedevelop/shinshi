#if UNITY_EDITOR
// System
using System;
using System.IO;

// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Shinshi.Art.Editor
{
    /// <summary> 선택한 지형 표면의 사본을 올리거나 낮추는 씬 뷰 브러시 </summary>
    public sealed class GroundSculptBrushWindow : EditorWindow
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string MENU_PATH    = "Shinshi/Art/Environment/지형 높이 브러시";
        private const string WINDOW_TITLE = "지형 높이 브러시";
        private const string MESH_FOLDER  = "Assets/01.Scripts/05.Art/Environment/VillageGroundPatch/SculptedMeshes";
        private const string UNDO_LABEL   = "지형 높이 브러시";
        private const float MIN_RADIUS    = 0.2f;     // 브러시 최소 반경(m)
        private const float MAX_RADIUS    = 30f;      // 브러시 최대 반경(m)
        private const float MIN_SPACING   = 0.25f;    // 준비 메시의 최소 변 길이(m)
        private const float MAX_SPACING   = 3f;       // 준비 메시의 최대 변 길이(m)
        private const float STAMP_INTERVAL = 0.05f;  // 누르는 동안 초당 20회 편집
        private const float PREVIEW_WIDTH  = 2f;
        private const float MAX_PICK_DISTANCE = 2000f;
        private const float BUTTON_LINES      = 2f;
        private const float MAX_HEIGHT_RATE   = 10f; // 올리기/내리기 최대 속도(m/s)
        private const int CONTROL_HINT        = 193471;

        private static readonly string[] G_ModeLabels = { "올리기", "내리기", "부드럽게", "평탄화" };

        [SerializeField] private MeshFilter _target         = null;
        [SerializeField] private MeshFilter _preparedTarget = null;
        [SerializeField] private Mesh       _editableMesh   = null;
        [SerializeField] private GroundSculptMeshUtility.ESculptMode _mode = GroundSculptMeshUtility.ESculptMode.Raise;
        [SerializeField] private float _radius           = 3f;
        [SerializeField] private float _heightRate       = 1.5f;
        [SerializeField] private float _blendRate        = 3f;
        [SerializeField] private float _edgeLength       = 0.75f;
        [SerializeField] private float _flattenHeight    = 0f;
        [SerializeField] private bool  _preserveBoundary = true;

        private bool         _painting      = false;
        private bool         _strokeActive  = false;
        private bool         _hasHit        = false;
        private bool         _strokeChanged = false;
        private bool         _shift         = false;
        private bool         _smooth        = false;
        private int          _controlId     = 0;
        private int          _undoGroup     = -1;
        private double       _lastStampTime = 0;
        private Vector3      _hitPoint      = Vector3.zero;
        private MeshCollider _collider      = null;
        private SceneView    _strokeView    = null;
        private string       _status        = "Hierarchy에서 지형의 top 표면을 선택하세요.";
        private Vector2      _scroll        = Vector2.zero;

        /****************************************
        *              Unity Event
        ****************************************/

        [MenuItem(MENU_PATH)]
        public static void Open()
        {
            GetWindow<GroundSculptBrushWindow>(WINDOW_TITLE);
        }

        private void OnEnable()
        {
            minSize = new Vector2(380f, 380f);
            SceneView.duringSceneGui += OnSceneGui;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            if (_preparedTarget != null)
            {
                _preparedTarget.TryGetComponent(out _collider);
            }
        }

        private void OnDisable()
        {
            FinishStroke();
            SceneView.duringSceneGui -= OnSceneGui;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.HelpBox("선택한 표면을 준비한 뒤 브러시를 켜고 Scene 뷰에서 누르거나 드래그하세요.\nShift: 올리기/내리기 반전 · Ctrl: 부드럽게 · Alt: 카메라 조작\nCtrl+Z: 한 번의 드래그 되돌리기 · Esc: 브러시 끄기", MessageType.Info);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                EditorGUI.BeginChangeCheck();
                MeshFilter target = (MeshFilter)EditorGUILayout.ObjectField("편집할 표면", _target, typeof(MeshFilter), true);
                if (EditorGUI.EndChangeCheck())
                {
                    FinishStroke();
                    _painting = false;
                    _target = target;
                }

                if (GUILayout.Button("선택한 표면 사용"))
                {
                    UseSelection();
                }

                _edgeLength = EditorGUILayout.Slider("메시 간격(m)", _edgeLength, MIN_SPACING, MAX_SPACING);
                EditorGUILayout.HelpBox("준비하면 원본을 유지한 촘촘한 사본을 만들고 표면의 MeshCollider도 연결합니다. 건물·물·장식이 아닌 지형 표면을 선택하세요.", MessageType.None);
                using (new EditorGUI.DisabledScope(_target == null))
                {
                    if (GUILayout.Button("선택 표면의 편집 사본 준비"))
                    {
                        PrepareTarget();
                    }
                }

                EditorGUILayout.Space();
                _mode = (GroundSculptMeshUtility.ESculptMode)GUILayout.Toolbar((int)_mode, G_ModeLabels);
                _radius = EditorGUILayout.Slider("반경(m)", _radius, MIN_RADIUS, MAX_RADIUS);
                _heightRate = EditorGUILayout.Slider("높이 변화(m/s)", _heightRate, MIN_RADIUS, MAX_HEIGHT_RATE);
                _blendRate = EditorGUILayout.Slider("부드럽게/평탄화 강도", _blendRate, MIN_RADIUS, MAX_HEIGHT_RATE);
                _preserveBoundary = EditorGUILayout.Toggle("표면 가장자리 고정", _preserveBoundary);
                _flattenHeight = EditorGUILayout.FloatField("평탄화 높이(월드 Y)", _flattenHeight);
                if (GUILayout.Button("평탄화 선택 후 Shift+클릭으로 높이 잡기"))
                {
                    _status = "평탄화 모드에서 Shift+클릭하면 클릭한 지면 높이를 가져옵니다.";
                    _mode = GroundSculptMeshUtility.ESculptMode.Flatten;
                    GroundPatchBrushWindow.StopAllPainting();
                    _painting = IsPrepared();
                }

                using (new EditorGUI.DisabledScope(!IsPrepared()))
                {
                    if (GUILayout.Button(_painting ? "브러시 끄기" : "브러시 켜기", GUILayout.Height(EditorGUIUtility.singleLineHeight * BUTTON_LINES)))
                    {
                        FinishStroke();
                        if (!_painting)
                        {
                            GroundPatchBrushWindow.StopAllPainting();
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
                EditorGUILayout.LabelField("편집 정점", _editableMesh.vertexCount.ToString("N0"));
                EditorGUILayout.ObjectField("저장된 사본", _editableMesh, typeof(Mesh), false);
            }
            EditorGUILayout.HelpBox("편집 후 씬을 저장하고 NavMesh를 다시 구우세요. 포장·패치·소품은 자동으로 움직이지 않습니다. 바닥 패치는 기존 브러시의 '선택한 패치 바닥에 다시 맞추기'를 사용하세요.", MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        /****************************************
        *                Methods
        ****************************************/

        internal static void StopAllPainting()
        {
            foreach (GroundSculptBrushWindow window in Resources.FindObjectsOfTypeAll<GroundSculptBrushWindow>())
            {
                window.FinishStroke();
                window._painting = false;
                window.Repaint();
            }
            SceneView.RepaintAll();
        }

        private void UseSelection()
        {
            FinishStroke();
            _painting = false;
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                _status = "Hierarchy에서 지형 표면을 먼저 선택하세요.";
                return;
            }

            if (selected.TryGetComponent(out MeshFilter filter))
            {
                _target = filter;
                return;
            }

            // CA07 지형 부모를 선택한 경우 보행 표면인 top 자식을 우선한다.
            MeshFilter[] filters = selected.GetComponentsInChildren<MeshFilter>();
            foreach (MeshFilter candidate in filters)
            {
                if (candidate.name == "top")
                {
                    _target = candidate;
                    return;
                }
            }
            _status = "MeshFilter가 있는 지형 표면을 선택하세요. 여러 표면은 하나씩 편집합니다.";
        }

        private bool CanEditTarget()
        {
            return (_target != null) && (_target.sharedMesh != null)
                && (!EditorUtility.IsPersistent(_target))
                && (_target.gameObject.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene())
                && (!EditorApplication.isPlayingOrWillChangePlaymode)
                && (PrefabStageUtility.GetPrefabStage(_target.gameObject) == null)
                && (!PrefabUtility.IsPartOfPrefabAsset(_target));
        }

        private bool IsPrepared()
        {
            return CanEditTarget() && (_target == _preparedTarget) && (_editableMesh != null)
                && (_target.sharedMesh == _editableMesh) && (_collider != null)
                && (_collider.sharedMesh == _editableMesh)
                && AssetDatabase.GetAssetPath(_editableMesh).StartsWith(MESH_FOLDER + "/", StringComparison.Ordinal);
        }

        private void PrepareTarget()
        {
            FinishStroke();
            _painting = false;
            if (!CanEditTarget())
            {
                _status = "활성 씬에 있는 지형 표면만 편집할 수 있습니다.";
                return;
            }
            if (_target.GetComponentInParent<LODGroup>() != null)
            {
                _status = "LOD 조립물 대신 보행 표면(top)을 선택하세요. LOD가 있는 절벽은 별도 모델 편집이 필요합니다.";
                return;
            }
            if ((_target.TryGetComponent(out MeshCollider existing)) && (existing.convex || existing.isTrigger))
            {
                _status = "Convex/Trigger가 아닌 지형 MeshCollider 표면을 선택하세요.";
                return;
            }
            Collider[] colliders = _target.GetComponents<Collider>();
            if ((_target.GetComponentInParent<Rigidbody>() != null) || (colliders.Length > 1)
                || ((colliders.Length == 1) && !(colliders[0] is MeshCollider)))
            {
                _status = "Rigidbody나 여러 Collider가 없는 정적 지형 표면을 선택하세요.";
                return;
            }

            Mesh copy;
            try
            {
                copy = GroundSculptMeshUtility.CreateEditableMesh(_target.sharedMesh, _target.transform, _edgeLength);
            }
            catch (Exception exception)
            {
                _status = exception.Message;
                Debug.LogError("[GroundSculpt] " + exception.Message, _target);
                return;
            }

            string sceneName = Path.GetFileNameWithoutExtension(_target.gameObject.scene.path);
            if (string.IsNullOrEmpty(sceneName))
            {
                sceneName = "UnsavedScene";
            }
            string folder = MESH_FOLDER + "/" + sceneName;
            string meshName = "SM_Sculpt_" + _target.transform.parent?.name + "_" + _target.name;
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                meshName = meshName.Replace(invalid, '_');
            }
            copy.name = meshName;
            try
            {
                SaveNewMesh(copy, folder);
            }
            catch (Exception exception)
            {
                _status = exception.Message;
                Debug.LogError("[GroundSculpt] " + exception.Message, _target);
                return;
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("지형 편집 사본 준비");
            Undo.RecordObject(_target, UNDO_LABEL);
            _collider = existing != null ? existing : Undo.AddComponent<MeshCollider>(_target.gameObject);
            Undo.RecordObject(_collider, UNDO_LABEL);
            _target.sharedMesh = copy;
            _collider.sharedMesh = copy;
            _editableMesh = copy;
            _preparedTarget = _target;
            RecordPrefabOverrides();
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(_target.gameObject.scene);
            AssetDatabase.SaveAssetIfDirty(copy);
            _status = "준비 완료. 브러시를 켜고 표면을 누르세요.";
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
                throw new IOException("지형 편집 사본 폴더를 만들지 못했습니다: " + path);
            }
        }

        private static void SaveNewMesh(Mesh copy, string folder)
        {
            try
            {
                EnsureFolder(folder);
                string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + copy.name + ".asset");
                AssetDatabase.CreateAsset(copy, path);
                if (!AssetDatabase.Contains(copy))
                {
                    throw new IOException("지형 편집 사본을 저장하지 못했습니다: " + path);
                }
            }
            catch
            {
                if ((copy != null) && !AssetDatabase.Contains(copy))
                {
                    DestroyImmediate(copy);
                }
                throw;
            }
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
                FinishStroke();
                _painting = false;
                return;
            }

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
                FinishStroke();
                _painting = false;
                current.Use();
                Repaint();
                return;
            }
            if ((current.type == EventType.KeyDown) && (current.keyCode == KeyCode.Z)
                && (current.control || current.command))
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
            _hasHit = _collider.Raycast(ray, out RaycastHit hit, MAX_PICK_DISTANCE);
            _shift = current.shift;
            _smooth = current.control || current.command;
            if (_hasHit)
            {
                _hitPoint = hit.point;
            }

            if ((current.type == EventType.Repaint) && _hasHit)
            {
                Color previous = Handles.color;
                Handles.color = _smooth ? Color.cyan : GetEffectiveMode() == GroundSculptMeshUtility.ESculptMode.Lower ? Color.red : Color.green;
                Handles.DrawWireDisc(_hitPoint, Vector3.up, _radius, PREVIEW_WIDTH);
                Handles.color = previous;
            }
            if (current.type == EventType.MouseMove)
            {
                view.Repaint();
            }
            if ((current.type == EventType.MouseDown) && (current.button == 0) && _hasHit)
            {
                if ((_mode == GroundSculptMeshUtility.ESculptMode.Flatten) && current.shift)
                {
                    _flattenHeight = _hitPoint.y;
                    Repaint();
                }
                else
                {
                    BeginStroke(view, control);
                }
                current.Use();
            }
            if ((current.type == EventType.MouseDrag) && _strokeActive)
            {
                current.Use();
                view.Repaint();
            }
        }

        private GroundSculptMeshUtility.ESculptMode GetEffectiveMode()
        {
            if (_smooth)
            {
                return GroundSculptMeshUtility.ESculptMode.Smooth;
            }
            if (_shift && (_mode == GroundSculptMeshUtility.ESculptMode.Raise))
            {
                return GroundSculptMeshUtility.ESculptMode.Lower;
            }
            if (_shift && (_mode == GroundSculptMeshUtility.ESculptMode.Lower))
            {
                return GroundSculptMeshUtility.ESculptMode.Raise;
            }
            return _mode;
        }

        private void BeginStroke(SceneView view, int control)
        {
            FinishStroke();
            // Mesh 에셋 자체의 Undo 대신 드래그별 사본 참조를 기록해 정점과 충돌을 함께 복구한다.
            // 씬이나 오브젝트를 복제해 같은 사본을 공유해도 다른 인스턴스는 바뀌지 않는다.
            Mesh copy = Instantiate(_editableMesh);
            copy.name = _editableMesh.name;
            string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(_editableMesh)).Replace('\\', '/');
            try
            {
                SaveNewMesh(copy, folder);
            }
            catch (Exception exception)
            {
                _painting = false;
                _status = exception.Message;
                Debug.LogError("[GroundSculpt] " + exception.Message, _target);
                return;
            }
            Undo.IncrementCurrentGroup();
            _undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UNDO_LABEL);
            Undo.RecordObject(_target, UNDO_LABEL);
            Undo.RecordObject(_collider, UNDO_LABEL);
            _editableMesh = copy;
            _target.sharedMesh = copy;
            _collider.sharedMesh = copy;
            RecordPrefabOverrides();
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
            GroundSculptMeshUtility.ESculptMode mode = GetEffectiveMode();
            float amount = (mode == GroundSculptMeshUtility.ESculptMode.Raise) || (mode == GroundSculptMeshUtility.ESculptMode.Lower)
                ? _heightRate * STAMP_INTERVAL : _blendRate * STAMP_INTERVAL;
            int changed;
            try
            {
                changed = GroundSculptMeshUtility.ApplyBrush(_editableMesh, _target.transform, _hitPoint,
                    _radius, amount, mode, _flattenHeight, _preserveBoundary);
                if (changed != 0)
                {
                    _strokeChanged = true;
                    RefreshCollider();
                }
            }
            catch (Exception exception)
            {
                FinishStroke();
                _painting = false;
                _status = exception.Message;
                Debug.LogError("[GroundSculpt] " + exception.Message, _target);
                return;
            }
            if (changed == 0)
            {
                return;
            }
            _strokeView.Repaint();
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
            if (_strokeChanged && (_editableMesh != null))
            {
                EditorUtility.SetDirty(_editableMesh);
                AssetDatabase.SaveAssetIfDirty(_editableMesh);
                if (_preparedTarget != null)
                {
                    EditorSceneManager.MarkSceneDirty(_preparedTarget.gameObject.scene);
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

        private void RefreshCollider()
        {
            if ((_collider == null) || (_editableMesh == null))
            {
                return;
            }
            _collider.sharedMesh = null;
            _collider.sharedMesh = _editableMesh;
        }

        private void RecordPrefabOverrides()
        {
            if (PrefabUtility.IsPartOfPrefabInstance(_target))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(_target);
            }
            if (PrefabUtility.IsPartOfPrefabInstance(_collider))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(_collider);
            }
        }

        private void OnUndoRedo()
        {
            // Undo 적용 중에는 새 저장이나 Undo 그룹 병합으로 방금 복구된 참조를 덮지 않는다.
            EditorApplication.update -= ContinueStroke;
            if (GUIUtility.hotControl == _controlId)
            {
                GUIUtility.hotControl = 0;
            }
            _strokeActive = false;
            _undoGroup = -1;
            _strokeView = null;
            if (_preparedTarget != null)
            {
                _editableMesh = _preparedTarget.sharedMesh;
                _preparedTarget.TryGetComponent(out _collider);
            }
            if (!IsPrepared())
            {
                _painting = false;
                return;
            }
            RefreshCollider();
            EditorUtility.SetDirty(_editableMesh);
            AssetDatabase.SaveAssetIfDirty(_editableMesh);
            SceneView.RepaintAll();
            Repaint();
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            FinishStroke();
            _painting = false;
        }
    }
}
#endif
