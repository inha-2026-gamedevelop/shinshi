#if UNITY_EDITOR
// System
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Shinshi.Art.Editor
{
    /// <summary> 씬 뷰에서 클릭한 바닥에 굴곡을 따라 붙는 풀, 흙 패치를 찍는 에디터 브러시 </summary>
    public sealed class GroundPatchBrushWindow : EditorWindow
    {
        public enum EGroundPatchKind
        {
            Grass,
            Dirt
        }

        public enum EBrushMode
        {
            Paint,
            Erase
        }

        /****************************************
        *                Fields
        ****************************************/

        private const string MENU_PATH          = "Shinshi/Art/Environment/바닥 패치 브러시";
        private const string WINDOW_TITLE       = "바닥 패치 브러시";
        private const string MESH_FOLDER        = "Assets/01.Scripts/05.Art/Environment/VillageGroundPatch/Meshes";
        private const string GRASS_MATERIAL_DIR = "Assets/01.Scripts/05.Art/Environment/VillageGroundPatch/Materials/";
        private const string GRASS_NAME_PREFIX  = "M_VillageGrassPatch_";
        private const string RANDOM_COLOR_LABEL = "무작위";
        private const string DIRT_MATERIAL      = "Assets/01.Scripts/05.Art/Environment/DungeonTripo/DVFX_Pack/Materials/M_DVFX_DirtTransition.mat";
        private const string ROOT_NAME          = "16_GroundPatches";
        private const string MESH_PREFIX        = "SM_VillageGroundPatch_";
        private const string MESH_FILTER        = "t:Mesh";
        private const string SCENE_FOLDER       = "Assets/00.Scenes";
        private const string SCENE_FILTER       = "t:Scene";
        private const string PATCH_SUFFIX       = "Patch";
        private const int    GRID_CELLS         = 24;    // 한 변 칸 수, 굴곡을 따라가는 해상도
        private const float  MIN_GROUND_NORMAL  = 0.75f; // 바닥 법선 y가 이보다 작으면(약 41도 이상) 절벽면으로 보고 비운다
        private const int    EDGE_SEARCH_STEPS  = 10;    // 테두리 이분 탐색 횟수, 반경 5m면 오차 5mm
        private const float  HALF               = 0.5f;
        private const float  SURFACE_OFFSET     = 0.04f; // 바닥과 겹쳐 깜빡이지 않게 띄우는 높이(m)
        private const float  RAY_HEIGHT         = 50f;   // 꼭짓점마다 위에서 쏘는 레이 시작 높이(m)
        private const float  RAY_LENGTH         = 100f;  // 시작 높이 아래로 같은 만큼 더 내려가 본다
        private const float  MAX_PICK_DISTANCE  = 2000f;

        // 칠하기 전용 바닥 콜라이더는 게임 레이캐스트와 내비메시에서 빠지도록 Ignore Raycast 레이어에 둔다.
        // 기본 마스크는 그 레이어를 건너뛰므로 브러시만 모든 레이어를 본다
        private const int    PICK_LAYERS        = Physics.AllLayers;
        private const int    INDICES_PER_CELL   = 6;     // 칸 하나 = 삼각형 두 개
        private const float  SQRT_TWO           = 1.41421356f;
        private const float  FULL_TURN          = 360f;
        private const float  PREVIEW_THICKNESS  = 2f;
        private const float  TOGGLE_LINES       = 2f;    // 칠하기 버튼 높이(줄 수)
        private const float  MIN_ERASE_RADIUS   = 0.1f;
        private const int    VERTS_PER_TRIANGLE = 3;

        private static readonly string[] G_GrassMaterialPaths =
        {
            GRASS_MATERIAL_DIR + "M_VillageGrassPatch_A.mat",
            GRASS_MATERIAL_DIR + "M_VillageGrassPatch_B.mat",
            GRASS_MATERIAL_DIR + "M_VillageGrassPatch_Lush.mat",
            GRASS_MATERIAL_DIR + "M_VillageGrassPatch_Fresh.mat",
            GRASS_MATERIAL_DIR + "M_VillageGrassPatch_Dry.mat",
            GRASS_MATERIAL_DIR + "M_VillageGrassPatch_Moss.mat"
        };

        [SerializeField] private EBrushMode       _mode           = EBrushMode.Paint;
        [SerializeField] private int              _grassColor     = 0; // 0이면 무작위, 그 밖은 재질 목록 순번 + 1
        [SerializeField] private float            _eraseRadius    = 1.5f;
        [SerializeField] private EGroundPatchKind _kind           = EGroundPatchKind.Grass;
        [SerializeField] private Material[]       _grassMaterials = new Material[0];
        [SerializeField] private Material[]       _dirtMaterials  = new Material[0];
        [SerializeField] private float            _grassMinRadius = 3.2f;
        [SerializeField] private float            _grassMaxRadius = 5.5f;
        [SerializeField] private float            _dirtMinRadius  = 1.6f;
        [SerializeField] private float            _dirtMaxRadius  = 2.8f;
        [SerializeField] private float            _maxHeightDelta = 0.6f; // 중심보다 이만큼 높거나 낮은 바닥은 비운다(m)
        [SerializeField] private bool             _randomRotation = true;

        private bool             _painting       = false;
        private SerializedObject _serialized     = null;
        private Vector2          _scroll         = Vector2.zero;

        // 한 번 드래그하는 동안 깎은 메쉬, 마우스를 뗄 때 한 번에 저장한다
        private readonly HashSet<Mesh> _strokeMeshes = new HashSet<Mesh>();

        /****************************************
        *              Unity Event
        ****************************************/

        [MenuItem(MENU_PATH)]
        private static void Open()
        {
            GetWindow<GroundPatchBrushWindow>(WINDOW_TITLE);
        }

        private void OnEnable()
        {
            // 창이 이전 목록을 기억하고 있어도 새로 추가된 색상 재질이 빠지지 않게 기본 목록보다 짧으면 다시 채운다
            if (_grassMaterials.Length < G_GrassMaterialPaths.Length)
            {
                _grassMaterials = LoadMaterials(G_GrassMaterialPaths);
            }

            if (_dirtMaterials.Length == 0)
            {
                _dirtMaterials = LoadMaterials(DIRT_MATERIAL);
            }

            _serialized = new SerializedObject(this);
            SceneView.duringSceneGui += OnSceneGui;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGui;
        }

        private void OnGUI()
        {
            _serialized.Update();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.HelpBox("칠하기를 켜고 씬 뷰에서 바닥을 클릭한다.\n칠하기: 클릭한 곳에 패치를 찍는다\n지우기: 클릭하거나 드래그한 원 안의 패치를 깎는다\nShift + 클릭: 커서 아래 패치 하나를 통째로 지운다", MessageType.Info);

            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = _painting ? Color.green : previous;
            if (GUILayout.Button(_painting ? "칠하기 끄기" : "칠하기 켜기", GUILayout.Height(EditorGUIUtility.singleLineHeight * TOGGLE_LINES)))
            {
                _painting = !_painting;
                SceneView.RepaintAll();
            }
            GUI.backgroundColor = previous;

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(_mode)), new GUIContent("모드"));
            EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(_eraseRadius)), new GUIContent("지우개 반경(m)"));

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(_kind)), new GUIContent("종류"));
            EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(_randomRotation)), new GUIContent("무작위 회전"));
            EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(_maxHeightDelta)), new GUIContent("허용 높이 차(m)"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("풀", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(_grassMinRadius)), new GUIContent("최소 반경(m)"));
            EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(_grassMaxRadius)), new GUIContent("최대 반경(m)"));
            _grassColor = EditorGUILayout.Popup("색상", _grassColor, BuildColorLabels());
            EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(_grassMaterials)), new GUIContent("재질 목록"), true);
            if (GUILayout.Button("선택한 풀 패치 색상 섞기"))
            {
                ShuffleSelectedGrassColors();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("흙", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(_dirtMinRadius)), new GUIContent("최소 반경(m)"));
            EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(_dirtMaxRadius)), new GUIContent("최대 반경(m)"));
            EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(_dirtMaterials)), new GUIContent("재질 (무작위 선택)"), true);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("패치를 옮기거나 돌린 뒤 선택하고 누르면 새 자리의 바닥에 맞게 메쉬를 다시 만든다.", MessageType.None);
            if (GUILayout.Button("선택한 패치 바닥에 다시 맞추기"))
            {
                RefitSelection();
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("안 쓰는 패치 메쉬 정리"))
            {
                DeleteUnusedMeshes();
            }

            EditorGUILayout.EndScrollView();
            _serialized.ApplyModifiedProperties();
        }

        /****************************************
        *                Methods
        ****************************************/

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

            // 마우스를 떼는 이벤트는 커서가 바닥 밖이어도 받아야 저장과 hotControl 해제가 빠지지 않는다
            if ((current.type == EventType.MouseUp) && (GUIUtility.hotControl == controlId))
            {
                GUIUtility.hotControl = 0;
                FlushStroke();
                current.Use();
                return;
            }

            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, MAX_PICK_DISTANCE, PICK_LAYERS, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            bool primaryClick = (current.button == 0) && (!current.alt);
            switch (current.type)
            {
                case EventType.Repaint:
                    DrawPreview(hit, current.shift);
                    break;
                case EventType.MouseMove:
                    view.Repaint();
                    break;
                case EventType.MouseDown:
                    if (!primaryClick)
                    {
                        break;
                    }

                    if (current.shift)
                    {
                        ErasePatchAt(hit.point);
                    }
                    else if (_mode == EBrushMode.Erase)
                    {
                        GUIUtility.hotControl = controlId;
                        EraseArea(hit.point);
                    }
                    else
                    {
                        StampPatch(hit);
                    }
                    current.Use();
                    break;
                case EventType.MouseDrag:
                    if ((!primaryClick) || (GUIUtility.hotControl != controlId))
                    {
                        break;
                    }

                    EraseArea(hit.point);
                    view.Repaint();
                    current.Use();
                    break;
                default:
                    break;
            }
        }

        private void DrawPreview(RaycastHit hit, bool removeWhole)
        {
            if (removeWhole)
            {
                Handles.color = Color.red;
                Handles.DrawWireDisc(hit.point, Vector3.up, MIN_ERASE_RADIUS, PREVIEW_THICKNESS);
                return;
            }

            if (_mode == EBrushMode.Erase)
            {
                Handles.color = Color.red;
                Handles.DrawWireDisc(hit.point, Vector3.up, _eraseRadius, PREVIEW_THICKNESS);
                return;
            }

            float maxRadius = (_kind == EGroundPatchKind.Grass) ? _grassMaxRadius : _dirtMaxRadius;
            float minRadius = (_kind == EGroundPatchKind.Grass) ? _grassMinRadius : _dirtMinRadius;
            Handles.color = (_kind == EGroundPatchKind.Grass) ? Color.green : Color.yellow;
            Handles.DrawWireDisc(hit.point, Vector3.up, maxRadius, PREVIEW_THICKNESS);
            Handles.DrawWireDisc(hit.point, Vector3.up, minRadius);
        }

        // 원 안의 꼭짓점을 원 둘레로 밀어내고, 세 꼭짓점이 모두 원 안이던 삼각형은 버린다.
        // 칸째 버리면 잘린 면이 격자 계단으로 남는데, 밀어내면 원호를 따라 매끈하게 잘린다
        private void EraseArea(Vector3 center)
        {
            float radius = Mathf.Max(MIN_ERASE_RADIUS, _eraseRadius);
            float radiusSqr = radius * radius;
            Scene scene = SceneManager.GetActiveScene();
            var patches = new List<MeshRenderer>(EnumeratePatches(scene));
            for (int p = 0; p < patches.Count; ++p)
            {
                MeshRenderer patch = patches[p];
                if ((patch == null) || (!IsNearXZ(patch.bounds, center, radius)))
                {
                    continue;
                }

                if ((!patch.TryGetComponent(out MeshFilter filter)) || (filter.sharedMesh == null))
                {
                    continue;
                }

                Transform patchTransform = patch.transform;
                Vector3[] vertices = filter.sharedMesh.vertices;
                var moved = new bool[vertices.Length];
                bool anyMoved = false;
                for (int i = 0; i < vertices.Length; ++i)
                {
                    Vector3 world = patchTransform.TransformPoint(vertices[i]);
                    var offset = new Vector2(world.x - center.x, world.z - center.z);
                    if (offset.sqrMagnitude >= radiusSqr)
                    {
                        continue;
                    }

                    Vector2 direction = (offset.sqrMagnitude > 0f) ? offset.normalized : Vector2.right;
                    var pushed = new Vector3(center.x + (direction.x * radius), world.y, center.z + (direction.y * radius));
                    vertices[i] = patchTransform.InverseTransformPoint(pushed);
                    moved[i] = true;
                    anyMoved = true;
                }

                if (!anyMoved)
                {
                    continue;
                }

                int[] triangles = filter.sharedMesh.triangles;
                var kept = new List<int>(triangles.Length);
                for (int t = 0; t < triangles.Length; t += VERTS_PER_TRIANGLE)
                {
                    if ((moved[triangles[t]]) && (moved[triangles[t + 1]]) && (moved[triangles[t + 2]]))
                    {
                        continue;
                    }

                    kept.Add(triangles[t]);
                    kept.Add(triangles[t + 1]);
                    kept.Add(triangles[t + 2]);
                }

                if (kept.Count == 0)
                {
                    Undo.DestroyObjectImmediate(patch.gameObject);
                    EditorSceneManager.MarkSceneDirty(scene);
                    continue;
                }

                Mesh mesh = GetStrokeMesh(patch.gameObject, filter);
                mesh.vertices = vertices;
                mesh.SetTriangles(kept, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                EditorSceneManager.MarkSceneDirty(scene);
            }
        }

        // 드래그마다 패치당 한 번 사본을 만들어 바꾸고 MeshFilter 참조만 Undo에 남긴다.
        // 메쉬 에셋을 제자리에서 고치면 Undo가 꼭짓점을 되돌리지 못했고,
        // 다른 씬이나 복제한 패치가 같은 메쉬를 쓰면 그쪽까지 깎인다
        private Mesh GetStrokeMesh(GameObject patch, MeshFilter filter)
        {
            Mesh mesh = filter.sharedMesh;
            if (_strokeMeshes.Contains(mesh))
            {
                return mesh;
            }

            Mesh copy = Instantiate(mesh);
            SaveMeshAsset(copy, patch.name.Replace(PATCH_SUFFIX, string.Empty));
            Undo.RecordObject(filter, WINDOW_TITLE);
            filter.sharedMesh = copy;
            _strokeMeshes.Add(copy);
            return copy;
        }

        // 지우기와 Shift + 클릭으로 남은, 어느 씬도 쓰지 않는 패치 메쉬 파일을 지운다
        private static void DeleteUnusedMeshes()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            string[] scenePaths = AssetDatabase.FindAssets(SCENE_FILTER, new[] { SCENE_FOLDER });
            for (int i = 0; i < scenePaths.Length; ++i)
            {
                scenePaths[i] = AssetDatabase.GUIDToAssetPath(scenePaths[i]);
            }

            var used = new HashSet<string>(AssetDatabase.GetDependencies(scenePaths, false));
            var unused = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets(MESH_FILTER, new[] { MESH_FOLDER }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!used.Contains(path))
                {
                    unused.Add(path);
                }
            }

            if (unused.Count == 0)
            {
                EditorUtility.DisplayDialog(WINDOW_TITLE, "지울 메쉬가 없다", "확인");
                return;
            }

            if (!EditorUtility.DisplayDialog(WINDOW_TITLE, $"어느 씬도 쓰지 않는 패치 메쉬 {unused.Count}개를 지운다. 지운 메쉬를 쓰던 Undo 기록은 되돌릴 수 없게 된다", "지우기", "취소"))
            {
                return;
            }

            var failed = new List<string>();
            AssetDatabase.DeleteAssets(unused.ToArray(), failed);
            Debug.Log($"[GroundPatch] 안 쓰는 패치 메쉬 {unused.Count - failed.Count}개를 지웠다");
        }

        private void FlushStroke()
        {
            foreach (Mesh mesh in _strokeMeshes)
            {
                if (mesh == null)
                {
                    continue;
                }

                EditorUtility.SetDirty(mesh);
                AssetDatabase.SaveAssetIfDirty(mesh);
            }

            _strokeMeshes.Clear();
        }

        private static bool IsNearXZ(Bounds bounds, Vector3 point, float radius)
        {
            float dx = Mathf.Max(0f, Mathf.Abs(point.x - bounds.center.x) - bounds.extents.x);
            float dz = Mathf.Max(0f, Mathf.Abs(point.z - bounds.center.z) - bounds.extents.z);
            return ((dx * dx) + (dz * dz)) < (radius * radius);
        }

        private void StampPatch(RaycastHit hit)
        {
            Material material = (_kind == EGroundPatchKind.Grass) ? PickGrassMaterial() : PickMaterial(_dirtMaterials);
            if (material == null)
            {
                Debug.LogWarning("[GroundPatch] 재질이 비어 있어 찍지 않는다");
                return;
            }

            Scene scene = hit.collider.gameObject.scene;
            Transform parent = GetKindRoot(scene, _kind);
            float minRadius = (_kind == EGroundPatchKind.Grass) ? _grassMinRadius : _dirtMinRadius;
            float maxRadius = (_kind == EGroundPatchKind.Grass) ? _grassMaxRadius : _dirtMaxRadius;
            float radius = Random.Range(minRadius, Mathf.Max(minRadius, maxRadius));
            float yaw = _randomRotation ? Random.Range(0f, FULL_TURN) : 0f;

            var patch = new GameObject(_kind + PATCH_SUFFIX);
            patch.transform.SetParent(parent, false);
            patch.transform.SetPositionAndRotation(hit.point, Quaternion.Euler(0f, yaw, 0f));

            Mesh mesh = BuildMesh(patch.transform, radius, hit.collider, null);
            if (mesh == null)
            {
                DestroyImmediate(patch);
                Debug.LogWarning("[GroundPatch] 클릭한 곳이 경사면이라 찍지 않는다");
                return;
            }

            SaveMeshAsset(mesh, _kind.ToString());
            patch.AddComponent<MeshFilter>().sharedMesh = mesh;
            int topOrder = NextSortingOrder(scene);
            var meshRenderer = patch.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial    = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.sortingOrder      = topOrder;
            GameObjectUtility.SetStaticEditorFlags(patch, StaticEditorFlags.BatchingStatic);

            Undo.RegisterCreatedObjectUndo(patch, WINDOW_TITLE);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private void ErasePatchAt(Vector3 point)
        {
            MeshRenderer target = null;
            float best = float.MaxValue;
            foreach (MeshRenderer candidate in EnumeratePatches(SceneManager.GetActiveScene()))
            {
                Bounds bounds = candidate.bounds;
                var flat = new Vector2(point.x - bounds.center.x, point.z - bounds.center.z);
                if ((Mathf.Abs(flat.x) > bounds.extents.x) || (Mathf.Abs(flat.y) > bounds.extents.z))
                {
                    continue;
                }

                float distance = flat.sqrMagnitude;
                if (distance < best)
                {
                    best   = distance;
                    target = candidate;
                }
            }

            if (target == null)
            {
                return;
            }

            Scene scene = target.gameObject.scene;
            Undo.DestroyObjectImmediate(target.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private void RefitSelection()
        {
            int fitted = 0;
            foreach (GameObject selected in Selection.gameObjects)
            {
                if ((!selected.TryGetComponent(out MeshFilter filter)) || (filter.sharedMesh == null))
                {
                    continue;
                }

                if (RefitPatch(selected.transform, filter))
                {
                    ++fitted;
                }
            }

            Debug.Log($"[GroundPatch] 패치 {fitted}개를 바닥에 다시 맞췄다");
        }

        private bool RefitPatch(Transform patch, MeshFilter filter)
        {
            Mesh oldMesh = filter.sharedMesh;
            ReadPatchShape(oldMesh, out float radius, out float bakedYaw);

            Collider ground = FindGroundBelow(patch.position);
            if (ground == null)
            {
                Debug.LogWarning($"[GroundPatch] {patch.name} 아래에 바닥이 없다");
                return false;
            }

            Undo.RecordObject(patch, WINDOW_TITLE);
            Undo.RecordObject(filter, WINDOW_TITLE);
            Quaternion previousRotation = patch.rotation;
            Vector3    previousScale    = patch.localScale;
            patch.localScale = Vector3.one;
            patch.rotation   = patch.rotation * Quaternion.Euler(0f, bakedYaw, 0f);

            Mesh rebuilt = BuildMesh(patch, radius, ground, IsSharedMesh(patch.gameObject.scene, oldMesh) ? null : oldMesh);
            if (rebuilt == null)
            {
                // 기존 메쉬에 회전이 남아 있으므로 Transform을 되돌려야 두 번 돌지 않는다
                patch.rotation   = previousRotation;
                patch.localScale = previousScale;
                Debug.LogWarning($"[GroundPatch] {patch.name} 중심이 경사면이라 맞추지 못했다");
                return false;
            }

            if (rebuilt != oldMesh)
            {
                SaveMeshAsset(rebuilt, patch.name.Replace(PATCH_SUFFIX, string.Empty));
                filter.sharedMesh = rebuilt;
            }
            else
            {
                EditorUtility.SetDirty(rebuilt);
                AssetDatabase.SaveAssetIfDirty(rebuilt);
            }

            EditorSceneManager.MarkSceneDirty(patch.gameObject.scene);
            return true;
        }

        // 메쉬에서 반경과 메쉬 안에 들어 있는 회전을 읽는다.
        // 처음 일괄 배치본은 회전이 메쉬에 들어 있고 첫 꼭짓점이 항상 격자 모서리라 그 방향으로 읽는다.
        // 브러시로 만든 메쉬는 회전이 Transform에 있고, 모서리가 테두리로 당겨졌을 수 있어 UV와 위치의 비례로 읽는다
        private static void ReadPatchShape(Mesh mesh, out float radius, out float bakedYaw)
        {
            Vector3[] vertices = mesh.vertices;
            Vector2[] uvs      = mesh.uv;
            if (uvs[0] == Vector2.zero)
            {
                Vector3 corner = vertices[0];
                radius   = new Vector2(corner.x, corner.z).magnitude / SQRT_TWO;
                bakedYaw = Vector3.SignedAngle(new Vector3(-1f, 0f, -1f), new Vector3(corner.x, 0f, corner.z), Vector3.up);
                return;
            }

            int farthest = 0;
            for (int i = 1; i < vertices.Length; ++i)
            {
                if (Mathf.Abs(vertices[i].x) > Mathf.Abs(vertices[farthest].x))
                {
                    farthest = i;
                }
            }

            // uv = (x + r) / 2r 이므로 r = x / (2uv - 1)
            radius   = vertices[farthest].x / ((uvs[farthest].x + uvs[farthest].x) - 1f);
            bakedYaw = 0f;
        }

        // target이 있으면 그 메쉬를 덮어쓴다. 중심이 바닥이 아니면 null
        private Mesh BuildMesh(Transform patch, float radius, Collider ground, Mesh target)
        {
            if (!TrySampleGround(patch, Vector3.zero, ground, out Vector3 centerPoint))
            {
                return null;
            }

            int side = GRID_CELLS + 1;
            var vertices = new Vector3[side * side];
            var uvs      = new Vector2[vertices.Length];
            float diameter = radius + radius;

            for (int z = 0; z < side; ++z)
            {
                for (int x = 0; x < side; ++x)
                {
                    int index = (z * side) + x;
                    var local = new Vector3(Mathf.Lerp(-radius, radius, x / (float)GRID_CELLS), 0f, Mathf.Lerp(-radius, radius, z / (float)GRID_CELLS));

                    // 바닥을 벗어난 꼭짓점은 버리지 않고 중심 쪽으로 당겨 테두리에 놓는다.
                    // 칸을 버리면 테두리가 격자 계단 모양으로 잘려 톱니처럼 보인다
                    if (!TrySampleGround(patch, local, ground, out Vector3 point))
                    {
                        point = PullToGroundEdge(patch, local, ground, centerPoint);
                    }

                    vertices[index] = point + (Vector3.up * SURFACE_OFFSET);
                    uvs[index]      = new Vector2((point.x + radius) / diameter, (point.z + radius) / diameter);
                }
            }

            var triangles = new List<int>(GRID_CELLS * GRID_CELLS * INDICES_PER_CELL);
            for (int z = 0; z < GRID_CELLS; ++z)
            {
                for (int x = 0; x < GRID_CELLS; ++x)
                {
                    int a = (z * side) + x;
                    int b = a + 1;
                    int c = a + side;
                    int d = c + 1;
                    triangles.Add(a);
                    triangles.Add(c);
                    triangles.Add(b);
                    triangles.Add(b);
                    triangles.Add(c);
                    triangles.Add(d);
                }
            }

            Mesh mesh = (target != null) ? target : new Mesh();
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.uv       = uvs;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        // 패치 기준 local 위치 아래의 바닥을 찾는다. 높이 차가 크거나 절벽면이면 바닥이 아니다
        private bool TrySampleGround(Transform patch, Vector3 local, Collider ground, out Vector3 localPoint)
        {
            Vector3 world = patch.TransformPoint(local);
            float centerY = patch.position.y;
            var ray = new Ray(new Vector3(world.x, centerY + RAY_HEIGHT, world.z), Vector3.down);
            if ((!ground.Raycast(ray, out RaycastHit hit, RAY_LENGTH))
                || (Mathf.Abs(hit.point.y - centerY) > _maxHeightDelta)
                || (hit.normal.y < MIN_GROUND_NORMAL))
            {
                localPoint = Vector3.zero;
                return false;
            }

            localPoint = patch.InverseTransformPoint(hit.point);
            return true;
        }

        // 중심에서 local 방향으로 나가며 바닥이 끝나는 지점을 이분 탐색으로 찾는다
        private Vector3 PullToGroundEdge(Transform patch, Vector3 local, Collider ground, Vector3 centerPoint)
        {
            Vector3 best = centerPoint;
            float inside = 0f;
            float outside = 1f;
            for (int i = 0; i < EDGE_SEARCH_STEPS; ++i)
            {
                float middle = (inside + outside) * HALF;
                if (TrySampleGround(patch, local * middle, ground, out Vector3 point))
                {
                    inside = middle;
                    best   = point;
                }
                else
                {
                    outside = middle;
                }
            }

            return best;
        }

        // 위에서 내려다봤을 때 패치 높이에 가장 가까운 콜라이더를 바닥으로 본다. 맨 처음 닿는 것은 지붕일 수 있다
        private static Collider FindGroundBelow(Vector3 position)
        {
            RaycastHit[] hits = Physics.RaycastAll(position + (Vector3.up * RAY_HEIGHT), Vector3.down, RAY_LENGTH, PICK_LAYERS, QueryTriggerInteraction.Ignore);
            Collider best = null;
            float bestGap = float.MaxValue;
            for (int i = 0; i < hits.Length; ++i)
            {
                float gap = Mathf.Abs(hits[i].point.y - position.y);
                if (gap < bestGap)
                {
                    bestGap = gap;
                    best    = hits[i].collider;
                }
            }

            return best;
        }

        // 반투명은 카메라 거리순으로 그려서 겹친 패치의 위아래가 각도마다 바뀐다.
        // 순서를 고정해 나중에 찍은 것이 항상 위에 오게 한다
        private static int NextSortingOrder(Scene scene)
        {
            int top = 0;
            foreach (MeshRenderer patch in EnumeratePatches(scene))
            {
                top = Mathf.Max(top, patch.sortingOrder + 1);
            }

            return top;
        }

        // Ctrl + D로 복제한 패치는 메쉬를 같이 쓰므로 덮어쓰면 원본도 바뀐다
        private static bool IsSharedMesh(Scene scene, Mesh mesh)
        {
            int users = 0;
            foreach (MeshRenderer patch in EnumeratePatches(scene))
            {
                if ((patch.TryGetComponent(out MeshFilter filter)) && (filter.sharedMesh == mesh))
                {
                    ++users;
                }
            }

            return users > 1;
        }

        private static void SaveMeshAsset(Mesh mesh, string kind)
        {
            // GenerateUniqueAssetPath는 "_00 1"처럼 공백 번호를 붙여 기존 _NN 이름 규칙과 어긋난다
            int index = 0;
            string path;
            do
            {
                path = $"{MESH_FOLDER}/{MESH_PREFIX}{kind}_{index:00}.asset";
                ++index;
            }
            while (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null);

            mesh.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();
        }

        private static IEnumerable<MeshRenderer> EnumeratePatches(Scene scene)
        {
            Transform root = FindRoot(scene);
            if (root == null)
            {
                yield break;
            }

            foreach (MeshRenderer patch in root.GetComponentsInChildren<MeshRenderer>())
            {
                yield return patch;
            }
        }

        private static Transform GetKindRoot(Scene scene, EGroundPatchKind kind)
        {
            Transform root = FindRoot(scene);
            if (root == null)
            {
                var created = new GameObject(ROOT_NAME);
                SceneManager.MoveGameObjectToScene(created, scene);
                Undo.RegisterCreatedObjectUndo(created, WINDOW_TITLE);
                root = created.transform;
            }

            string kindName = kind.ToString();
            Transform group = root.Find(kindName);
            if (group != null)
            {
                return group;
            }

            var groupObject = new GameObject(kindName);
            groupObject.transform.SetParent(root, false);
            Undo.RegisterCreatedObjectUndo(groupObject, WINDOW_TITLE);
            return groupObject.transform;
        }

        private static Transform FindRoot(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == ROOT_NAME)
                {
                    return root.transform;
                }
            }

            return null;
        }

        private Material PickGrassMaterial()
        {
            int index = _grassColor - 1;
            if ((index >= 0) && (index < _grassMaterials.Length) && (_grassMaterials[index] != null))
            {
                return _grassMaterials[index];
            }

            return PickMaterial(_grassMaterials);
        }

        private string[] BuildColorLabels()
        {
            var labels = new string[_grassMaterials.Length + 1];
            labels[0] = RANDOM_COLOR_LABEL;
            for (int i = 0; i < _grassMaterials.Length; ++i)
            {
                labels[i + 1] = (_grassMaterials[i] != null) ? _grassMaterials[i].name.Replace(GRASS_NAME_PREFIX, string.Empty) : string.Empty;
            }

            return labels;
        }

        // 선택한 오브젝트와 그 자식 중 풀 재질을 쓰는 패치에 목록의 재질을 무작위로 다시 준다
        private void ShuffleSelectedGrassColors()
        {
            var grassSet = new HashSet<Material>(_grassMaterials);
            int changed = 0;
            foreach (GameObject selected in Selection.gameObjects)
            {
                foreach (MeshRenderer patch in selected.GetComponentsInChildren<MeshRenderer>())
                {
                    if (!grassSet.Contains(patch.sharedMaterial))
                    {
                        continue;
                    }

                    Undo.RecordObject(patch, WINDOW_TITLE);
                    patch.sharedMaterial = PickMaterial(_grassMaterials);
                    EditorSceneManager.MarkSceneDirty(patch.gameObject.scene);
                    ++changed;
                }
            }

            Debug.Log($"[GroundPatch] 풀 패치 {changed}개의 색상을 섞었다");
        }

        private static Material PickMaterial(Material[] materials)
        {
            if (materials.Length == 0)
            {
                return null;
            }

            return materials[Random.Range(0, materials.Length)];
        }

        private static Material[] LoadMaterials(params string[] paths)
        {
            var loaded = new List<Material>(paths.Length);
            for (int i = 0; i < paths.Length; ++i)
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(paths[i]);
                if (material != null)
                {
                    loaded.Add(material);
                }
            }

            return loaded.ToArray();
        }
    }
}
#endif
