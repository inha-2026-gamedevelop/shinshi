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

        /****************************************
        *                Fields
        ****************************************/

        private const string MENU_PATH          = "Shinshi/Art/Environment/바닥 패치 브러시";
        private const string WINDOW_TITLE       = "바닥 패치 브러시";
        private const string MESH_FOLDER        = "Assets/01.Scripts/05.Art/Environment/VillageGroundPatch/Meshes";
        private const string GRASS_MATERIAL_A   = "Assets/01.Scripts/05.Art/Environment/VillageGroundPatch/Materials/M_VillageGrassPatch_A.mat";
        private const string GRASS_MATERIAL_B   = "Assets/01.Scripts/05.Art/Environment/VillageGroundPatch/Materials/M_VillageGrassPatch_B.mat";
        private const string DIRT_MATERIAL      = "Assets/01.Scripts/05.Art/Environment/DungeonTripo/DVFX_Pack/Materials/M_DVFX_DirtTransition.mat";
        private const string ROOT_NAME          = "16_GroundPatches";
        private const string MESH_PREFIX        = "SM_VillageGroundPatch_";
        private const string PATCH_SUFFIX       = "Patch";
        private const int    GRID_CELLS         = 24;    // 한 변 칸 수, 굴곡을 따라가는 해상도
        private const float  MIN_GROUND_NORMAL  = 0.75f; // 바닥 법선 y가 이보다 작으면(약 41도 이상) 절벽면으로 보고 비운다
        private const int    EDGE_SEARCH_STEPS  = 10;    // 테두리 이분 탐색 횟수, 반경 5m면 오차 5mm
        private const float  HALF               = 0.5f;
        private const float  SURFACE_OFFSET     = 0.04f; // 바닥과 겹쳐 깜빡이지 않게 띄우는 높이(m)
        private const float  RAY_HEIGHT         = 50f;   // 꼭짓점마다 위에서 쏘는 레이 시작 높이(m)
        private const float  RAY_LENGTH         = 100f;  // 시작 높이 아래로 같은 만큼 더 내려가 본다
        private const float  MAX_PICK_DISTANCE  = 2000f;
        private const int    INDICES_PER_CELL   = 6;     // 칸 하나 = 삼각형 두 개
        private const float  SQRT_TWO           = 1.41421356f;
        private const float  FULL_TURN          = 360f;
        private const float  PREVIEW_THICKNESS  = 2f;
        private const float  TOGGLE_LINES       = 2f;    // 칠하기 버튼 높이(줄 수)

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
            if (_grassMaterials.Length == 0)
            {
                _grassMaterials = LoadMaterials(GRASS_MATERIAL_A, GRASS_MATERIAL_B);
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

            EditorGUILayout.HelpBox("칠하기를 켜고 씬 뷰에서 바닥을 클릭하면 패치를 찍는다. Shift + 클릭은 커서 아래 패치를 지운다.", MessageType.Info);

            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = _painting ? Color.green : previous;
            if (GUILayout.Button(_painting ? "칠하기 끄기" : "칠하기 켜기", GUILayout.Height(EditorGUIUtility.singleLineHeight * TOGGLE_LINES)))
            {
                _painting = !_painting;
                SceneView.RepaintAll();
            }
            GUI.backgroundColor = previous;

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(_kind)), new GUIContent("종류"));
            EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(_randomRotation)), new GUIContent("무작위 회전"));
            EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(_maxHeightDelta)), new GUIContent("허용 높이 차(m)"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("풀", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(_grassMinRadius)), new GUIContent("최소 반경(m)"));
            EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(_grassMaxRadius)), new GUIContent("최대 반경(m)"));
            EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(_grassMaterials)), new GUIContent("재질 (무작위 선택)"), true);

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

            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, MAX_PICK_DISTANCE))
            {
                return;
            }

            switch (current.type)
            {
                case EventType.Repaint:
                    DrawPreview(hit, current.shift);
                    break;
                case EventType.MouseMove:
                    view.Repaint();
                    break;
                case EventType.MouseDown:
                    if ((current.button != 0) || (current.alt))
                    {
                        break;
                    }

                    if (current.shift)
                    {
                        ErasePatchAt(hit.point);
                    }
                    else
                    {
                        StampPatch(hit);
                    }
                    current.Use();
                    break;
                default:
                    break;
            }
        }

        private void DrawPreview(RaycastHit hit, bool erasing)
        {
            float maxRadius = (_kind == EGroundPatchKind.Grass) ? _grassMaxRadius : _dirtMaxRadius;
            float minRadius = (_kind == EGroundPatchKind.Grass) ? _grassMinRadius : _dirtMinRadius;
            Handles.color = erasing ? Color.red : ((_kind == EGroundPatchKind.Grass) ? Color.green : Color.yellow);
            Handles.DrawWireDisc(hit.point, Vector3.up, maxRadius, PREVIEW_THICKNESS);
            if (!erasing)
            {
                Handles.DrawWireDisc(hit.point, Vector3.up, minRadius);
            }
        }

        private void StampPatch(RaycastHit hit)
        {
            Material[] materials = (_kind == EGroundPatchKind.Grass) ? _grassMaterials : _dirtMaterials;
            Material material = PickMaterial(materials);
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
            RaycastHit[] hits = Physics.RaycastAll(position + (Vector3.up * RAY_HEIGHT), Vector3.down, RAY_LENGTH);
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
