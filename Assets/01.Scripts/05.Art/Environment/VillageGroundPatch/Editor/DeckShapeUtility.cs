#if UNITY_EDITOR
// System
using System.Collections.Generic;

// Unity
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Shinshi.Art.Editor
{
    /// <summary> CA07 단 바닥(덱) 윤곽을 격자 마스크로 읽고 윗면과 가장자리 벽 메쉬를 다시 만드는 공용 기능 </summary>
    internal static class DeckShapeUtility
    {
        /****************************************
        *                Types
        ****************************************/

        /// <summary> 덱 하나의 윗면과 가장자리 벽 구성 요소 </summary>
        internal sealed class Deck
        {
            public Transform    Root         { get; }
            public MeshFilter   TopFilter    { get; }
            public MeshCollider TopCollider  { get; }
            public MeshFilter   WallFilter   { get; }
            public MeshCollider WallCollider { get; }
            public string       Tag          { get { return Root.name.Substring(DECK_PREFIX.Length); } }

            public Deck(Transform root, MeshFilter topFilter, MeshCollider topCollider, MeshFilter wallFilter, MeshCollider wallCollider)
            {
                Root         = root;
                TopFilter    = topFilter;
                TopCollider  = topCollider;
                WallFilter   = wallFilter;
                WallCollider = wallCollider;
            }
        }

        /// <summary> 월드 xz 격자 위에 덱이 덮는 칸을 기록한 마스크, 격자 원점은 칸 크기 배수에 맞춘다 </summary>
        internal sealed class Mask
        {
            private bool[] _cells = new bool[0];

            public float OriginX { get; private set; }
            public float OriginZ { get; private set; }
            public int   SizeX   { get; private set; }
            public int   SizeZ   { get; private set; }
            public float Height  { get; }

            public Mask(float minX, float minZ, float maxX, float maxZ, float height)
            {
                Height = height;
                Allocate(minX, minZ, maxX, maxZ);
            }

            public bool Get(int i, int j) { return _cells[(i * SizeZ) + j]; }
            public void Set(int i, int j, bool value) { _cells[(i * SizeZ) + j] = value; }
            public float WorldX(int i) { return OriginX + (i * CELL_SIZE); }
            public float WorldZ(int j) { return OriginZ + (j * CELL_SIZE); }

            // 브러시가 마스크 바깥에 닿으면 여유를 붙여 다시 잡고 기존 칸을 옮겨 담는다
            public void EnsureContains(float x, float z, float radius)
            {
                float minX = OriginX + CELL_SIZE;
                float minZ = OriginZ + CELL_SIZE;
                float maxX = WorldX(SizeX - BORDER_CELLS - 1);
                float maxZ = WorldZ(SizeZ - BORDER_CELLS - 1);
                if (((x - radius) >= minX) && ((x + radius) <= maxX) && ((z - radius) >= minZ) && ((z + radius) <= maxZ))
                {
                    return;
                }

                bool[] oldCells   = _cells;
                float  oldOriginX = OriginX;
                float  oldOriginZ = OriginZ;
                int    oldSizeX   = SizeX;
                int    oldSizeZ   = SizeZ;

                Allocate(Mathf.Min(minX, x - radius - GROW_MARGIN), Mathf.Min(minZ, z - radius - GROW_MARGIN),
                         Mathf.Max(maxX, x + radius + GROW_MARGIN), Mathf.Max(maxZ, z + radius + GROW_MARGIN));

                int offsetX = Mathf.RoundToInt((oldOriginX - OriginX) / CELL_SIZE);
                int offsetZ = Mathf.RoundToInt((oldOriginZ - OriginZ) / CELL_SIZE);
                for (int i = 0; i < oldSizeX; ++i)
                {
                    for (int j = 0; j < oldSizeZ; ++j)
                    {
                        _cells[((i + offsetX) * SizeZ) + j + offsetZ] = oldCells[(i * oldSizeZ) + j];
                    }
                }
            }

            private void Allocate(float minX, float minZ, float maxX, float maxZ)
            {
                OriginX = (Mathf.Floor(minX / CELL_SIZE) - BORDER_CELLS) * CELL_SIZE;
                OriginZ = (Mathf.Floor(minZ / CELL_SIZE) - BORDER_CELLS) * CELL_SIZE;
                SizeX   = Mathf.CeilToInt((maxX - OriginX) / CELL_SIZE) + BORDER_CELLS + 1;
                SizeZ   = Mathf.CeilToInt((maxZ - OriginZ) / CELL_SIZE) + BORDER_CELLS + 1;
                _cells  = new bool[SizeX * SizeZ];
            }
        }

        /// <summary> 돌 지형에 맞추기 기준값 </summary>
        internal sealed class FitSettings
        {
            public float    ExtendDistance;   // 원래 윤곽에서 돌 위로 넓힐 최대 거리(m)
            public float    FloatDepth;       // 덱 아래 이 깊이 안에 돌이 없으면 허공으로 보고 깎는다(m)
            public float    LedgeBelow;       // 덱보다 이만큼 낮은 돌까지 넓힐 대상으로 본다(m)
            public float    LedgeAbove;       // 덱보다 이만큼 높은 돌까지 넓힐 대상으로 본다(m)
            public float    ProtectRadius;    // 소품 기준점 주변은 허공이어도 남긴다(m)
            public string[] RockRootPrefixes; // 돌 지형 루트 이름 앞부분
            public string[] WaterNames;       // 넓히지 않을 물 오브젝트 이름
            public string[] PropRootNames;    // 소품 루트 이름, 루트/묶음/소품 구조
        }

        /****************************************
        *                Fields
        ****************************************/

        public  const string DECK_PREFIX        = "SM_CA07_Deck_";
        public  const string FITTED_FOLDER      = "Assets/01.Scripts/05.Art/Environment/VillageGroundPatch/FittedDecks";
        public  const float  CELL_SIZE          = 0.5f;
        private const string FITTED_PARENT      = "Assets/01.Scripts/05.Art/Environment/VillageGroundPatch";
        private const string FITTED_NAME        = "FittedDecks";
        private const string MODEL_FOLDER       = "Assets/01.Scripts/05.Art/Environment/VillageCA07/Models/";
        private const string WALL_MODEL_PREFIX  = "SM_CA07_CliffWall_";
        private const string MODEL_EXTENSION    = ".obj";
        private const string ASSET_EXTENSION    = ".asset";
        private const string TOP_CHILD          = "top";
        private const string WALL_CHILD         = "rock";
        private const string WALL_SUBMESH       = "wall";
        private const string TOP_MESH_PREFIX    = "SM_GP_Deck_";
        private const string WALL_MESH_PREFIX   = "SM_GP_DeckWall_";
        private const string LOW_LOD_MARK       = "Low";
        private const string MEDIUM_LOD_MARK    = "Medium";
        private const string TEMP_OBJECT_NAME   = "DeckShapeTemp";
        private const string NAVMESH_PREFIX     = "NavMesh-";
        private const string SCENE_EXTENSION    = ".unity";
        private const string UNDO_NAME          = "덱 모양 변경";
        private const int    BORDER_CELLS       = 2;     // 마스크 바깥 테두리 빈칸, 등고선이 배열 끝에서 끊기지 않게 한다
        private const float  GROW_MARGIN        = 10f;   // 마스크를 넓힐 때 한 번에 더 잡는 여유(m)
        private const float  ISO_LEVEL          = 0.5f;  // 흐린 마스크에서 윤곽으로 삼는 값
        private const float  SET_MARGIN         = 0.1f;  // 칠한 칸의 최소 값을 윤곽보다 이만큼 올린다
        private const float  BLUR_CENTER        = 4f;    // 윤곽을 둥글게 펴는 3x3 가중치, 자신 4 : 상하좌우 2 : 대각 1
        private const float  BLUR_SIDE          = 2f;
        private const float  BLUR_CORNER        = 1f;
        private const int    BLUR_RADIUS        = 1;
        private const int    CORNERS            = 4;
        private const int    VERTS_PER_TRIANGLE = 3;
        private const int    BOX_CORNERS        = 8;
        private const float  RAY_HEIGHT         = 60f;   // 칸마다 위에서 쏘는 레이 시작 높이(m)
        private const float  RAY_LENGTH         = 300f;
        private const float  WALL_MARGIN        = 2f;    // 벽 콜라이더가 덱 위아래로 최소 이만큼 덮는다(m)
        private const int    MIN_PIECE_CELLS    = 16;    // 이보다 작은 조각은 버린다(칸 수)
        private const float  PROP_RAY_LIFT      = 0.5f;  // 소품 기준점에서 바닥을 찾을 때 띄우는 높이(m)
        private const float  PROP_RAY_LENGTH    = 1.2f;
        private const float  VERTEX_KEY_SCALE   = 1000f; // 꼭짓점 합치기 정밀도, 1mm
        private const int    KEY_SHIFT          = 32;

        private static readonly int[] G_StepX = { 1, -1, 0, 0 };
        private static readonly int[] G_StepZ = { 0, 0, 1, -1 };

        /****************************************
        *                Methods
        ****************************************/

        public static bool TryGetDeck(Transform from, out Deck deck)
        {
            deck = null;
            Transform current = from;
            while ((current != null) && (!current.name.StartsWith(DECK_PREFIX)))
            {
                current = current.parent;
            }

            if (current == null)
            {
                return false;
            }

            Transform top  = current.Find(TOP_CHILD);
            Transform wall = current.Find(WALL_CHILD);
            if ((top == null) || (wall == null))
            {
                return false;
            }

            MeshFilter   topFilter    = top.GetComponent<MeshFilter>();
            MeshCollider topCollider  = top.GetComponent<MeshCollider>();
            MeshFilter   wallFilter   = wall.GetComponent<MeshFilter>();
            MeshCollider wallCollider = wall.GetComponent<MeshCollider>();
            if ((topFilter == null) || (topCollider == null) || (wallFilter == null) || (wallCollider == null))
            {
                return false;
            }

            deck = new Deck(current, topFilter, topCollider, wallFilter, wallCollider);
            return true;
        }

        public static List<Deck> FindDecks(Scene scene)
        {
            var decks = new List<Deck>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    if ((child.name.StartsWith(DECK_PREFIX)) && (TryGetDeck(child, out Deck deck)) && (deck.Root == child))
                    {
                        decks.Add(deck);
                    }
                }
            }
            return decks;
        }

        public static float GetHeight(Deck deck)
        {
            return GetWorldBounds(deck.TopFilter).max.y;
        }

        // 지금 윗면 메쉬가 덮는 칸을 위에서 레이로 찍어 마스크로 만든다
        public static Mask ReadMask(Deck deck)
        {
            Mesh mesh = deck.TopFilter.sharedMesh;
            Bounds bounds = GetWorldBounds(deck.TopFilter);
            var mask = new Mask(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z, bounds.max.y);
            if (mesh == null)
            {
                return mask;
            }

            MeshCollider probe = CreateProbe(mesh, deck.TopFilter.transform);
            for (int i = 0; i < mask.SizeX; ++i)
            {
                for (int j = 0; j < mask.SizeZ; ++j)
                {
                    var ray = new Ray(new Vector3(mask.WorldX(i), mask.Height + RAY_HEIGHT, mask.WorldZ(j)), Vector3.down);
                    mask.Set(i, j, probe.Raycast(ray, out _, RAY_LENGTH));
                }
            }
            Object.DestroyImmediate(probe.gameObject);
            return mask;
        }

        // 원본이 그대로면 이 씬 전용 사본 에셋을 만들어 연결한다. 원본 obj는 다른 씬도 쓰므로 고치지 않는다
        public static void EnsureEditable(Deck deck, out Mesh top, out Mesh wall)
        {
            top  = deck.TopFilter.sharedMesh;
            wall = deck.WallFilter.sharedMesh;
            bool topEditable  = IsFittedAsset(top);
            bool wallEditable = IsFittedAsset(wall);
            if ((topEditable) && (wallEditable))
            {
                return;
            }

            if (!AssetDatabase.IsValidFolder(FITTED_FOLDER))
            {
                AssetDatabase.CreateFolder(FITTED_PARENT, FITTED_NAME);
            }

            Undo.RecordObjects(new Object[] { deck.TopFilter, deck.TopCollider, deck.WallFilter, deck.WallCollider }, UNDO_NAME);
            if (!topEditable)
            {
                top = CreateMeshAsset(top, TOP_MESH_PREFIX + deck.Tag);
                deck.TopFilter.sharedMesh   = top;
                deck.TopCollider.sharedMesh = top;
            }

            if (!wallEditable)
            {
                wall = CreateMeshAsset(wall, WALL_MESH_PREFIX + deck.Tag);
                deck.WallFilter.sharedMesh   = wall;
                deck.WallCollider.sharedMesh = wall;
            }
        }

        // 벽 콜라이더 높이 범위, 원본 벽이 있으면 그 범위를 따른다
        public static void GetWallRange(Deck deck, float height, out float bottom, out float top)
        {
            Mesh source = LoadModelMesh(WALL_MODEL_PREFIX + deck.Tag, WALL_SUBMESH);
            if ((source == null) && (deck.WallFilter.sharedMesh != null) && (deck.WallFilter.sharedMesh.vertexCount > 0))
            {
                source = deck.WallFilter.sharedMesh;
            }

            bottom = height - WALL_MARGIN;
            top    = height + WALL_MARGIN;
            if (source == null)
            {
                return;
            }

            Matrix4x4 toWorld = deck.WallFilter.transform.localToWorldMatrix;
            Vector3 min = toWorld.MultiplyPoint3x4(source.bounds.min);
            Vector3 max = toWorld.MultiplyPoint3x4(source.bounds.max);
            bottom = Mathf.Min(bottom, Mathf.Min(min.y, max.y));
            top    = Mathf.Max(top, Mathf.Max(min.y, max.y));
        }

        // 마스크를 흐린 뒤 등고선으로 둥근 윤곽을 만들고, 그 테두리를 따라 벽을 세운다
        public static void WriteMeshes(Deck deck, Mask mask, Mesh top, Mesh wall, float wallBottom, float wallTop, bool updateColliders)
        {
            float[] field = Blur(mask);
            var vertices  = new List<Vector3>();
            var triangles = new List<int>();
            var lookup    = new Dictionary<long, int>();
            var polygon   = new List<int>(CORNERS * 2);
            int[] cornerX = new int[CORNERS];
            int[] cornerZ = new int[CORNERS];

            for (int i = 0; i < (mask.SizeX - 1); ++i)
            {
                for (int j = 0; j < (mask.SizeZ - 1); ++j)
                {
                    cornerX[0] = i;     cornerZ[0] = j;
                    cornerX[1] = i + 1; cornerZ[1] = j;
                    cornerX[2] = i + 1; cornerZ[2] = j + 1;
                    cornerX[3] = i;     cornerZ[3] = j + 1;

                    polygon.Clear();
                    for (int c = 0; c < CORNERS; ++c)
                    {
                        int   n  = (c + 1) % CORNERS;
                        float va = field[(cornerX[c] * mask.SizeZ) + cornerZ[c]];
                        float vb = field[(cornerX[n] * mask.SizeZ) + cornerZ[n]];
                        float ax = mask.WorldX(cornerX[c]);
                        float az = mask.WorldZ(cornerZ[c]);
                        if (va >= ISO_LEVEL)
                        {
                            polygon.Add(GetVertex(ax, az, mask.Height, vertices, lookup));
                        }

                        if ((va >= ISO_LEVEL) != (vb >= ISO_LEVEL))
                        {
                            float t = (ISO_LEVEL - va) / (vb - va);
                            float bx = mask.WorldX(cornerX[n]);
                            float bz = mask.WorldZ(cornerZ[n]);
                            polygon.Add(GetVertex(ax + ((bx - ax) * t), az + ((bz - az) * t), mask.Height, vertices, lookup));
                        }
                    }

                    // 위에서 볼 때 시계 방향이어야 앞면이 위를 본다
                    for (int p = 1; p < (polygon.Count - 1); ++p)
                    {
                        if ((polygon[0] == polygon[p]) || (polygon[p] == polygon[p + 1]) || (polygon[0] == polygon[p + 1]))
                        {
                            continue;
                        }
                        triangles.Add(polygon[0]);
                        triangles.Add(polygon[p + 1]);
                        triangles.Add(polygon[p]);
                    }
                }
            }

            FillTop(top, vertices, triangles, deck.TopFilter.transform.worldToLocalMatrix);
            FillWall(wall, vertices, triangles, wallBottom, wallTop, deck.WallFilter.transform.worldToLocalMatrix);
            EditorUtility.SetDirty(top);
            EditorUtility.SetDirty(wall);

            if (updateColliders)
            {
                RefreshColliders(deck, top, wall);
            }
        }

        public static void RefreshColliders(Deck deck, Mesh top, Mesh wall)
        {
            // 같은 메쉬를 다시 넣어야 바뀐 꼭짓점으로 충돌 데이터를 새로 굽는다
            deck.TopCollider.sharedMesh  = null;
            deck.TopCollider.sharedMesh  = top;
            deck.WallCollider.sharedMesh = null;
            deck.WallCollider.sharedMesh = wall;
        }

        public static bool RestoreOriginal(Deck deck)
        {
            Mesh top  = LoadModelMesh(deck.Root.name, TOP_CHILD);
            Mesh wall = LoadModelMesh(WALL_MODEL_PREFIX + deck.Tag, WALL_SUBMESH);
            if ((top == null) || (wall == null))
            {
                return false;
            }

            Undo.RecordObjects(new Object[] { deck.TopFilter, deck.TopCollider, deck.WallFilter, deck.WallCollider }, UNDO_NAME);
            deck.TopFilter.sharedMesh    = top;
            deck.TopCollider.sharedMesh  = top;
            deck.WallFilter.sharedMesh   = wall;
            deck.WallCollider.sharedMesh = wall;
            EditorSceneManager.MarkSceneDirty(deck.Root.gameObject.scene);
            return true;
        }

        // 원본 윤곽에서 시작해 돌이 받치지 않는 가장자리는 깎고, 덱 높이 근처 돌 위로는 넓힌다.
        // 겹치는 넓힘은 원래 윤곽에서 가장 가까운 덱 하나만 가져간다
        public static int FitToRock(Scene scene, List<Deck> decks, ICollection<Deck> targets, FitSettings settings)
        {
            if ((decks.Count == 0) || (targets.Count == 0))
            {
                return 0;
            }

            var temps = new List<GameObject>();
            HashSet<Collider> rock  = CreateRockColliders(scene, settings.RockRootPrefixes, temps);
            HashSet<Collider> water = CreateNamedColliders(scene, settings.WaterNames, temps);

            int count = decks.Count;
            var sources = new MeshCollider[count];
            var heights = new float[count];
            for (int k = 0; k < count; ++k)
            {
                Mesh original = LoadModelMesh(decks[k].Root.name, TOP_CHILD);
                if (original == null)
                {
                    original = decks[k].TopFilter.sharedMesh;
                }
                sources[k] = CreateProbe(original, decks[k].TopFilter.transform);
                temps.Add(sources[k].gameObject);
                heights[k] = sources[k].bounds.max.y;
            }
            Physics.SyncTransforms();

            List<Vector3>[] props = CollectProps(scene, settings.PropRootNames, sources);

            Bounds all = sources[0].bounds;
            foreach (MeshCollider source in sources)
            {
                all.Encapsulate(source.bounds);
            }
            float pad = settings.ExtendDistance + (CELL_SIZE * BORDER_CELLS);
            var grid = new Mask(all.min.x - pad, all.min.z - pad, all.max.x + pad, all.max.z + pad, all.max.y);
            int sizeX = grid.SizeX;
            int sizeZ = grid.SizeZ;
            int cells = sizeX * sizeZ;

            var rockTop  = new float[cells];
            var blocked  = new bool[cells];
            var firstOwn = new int[cells];
            var inside   = new bool[count][];
            for (int k = 0; k < count; ++k)
            {
                inside[k] = new bool[cells];
            }

            for (int i = 0; i < sizeX; ++i)
            {
                for (int j = 0; j < sizeZ; ++j)
                {
                    int index = (i * sizeZ) + j;
                    var ray = new Ray(new Vector3(grid.WorldX(i), all.max.y + RAY_HEIGHT, grid.WorldZ(j)), Vector3.down);
                    firstOwn[index] = -1;
                    for (int k = 0; k < count; ++k)
                    {
                        if (sources[k].Raycast(ray, out _, RAY_LENGTH))
                        {
                            inside[k][index] = true;
                            if (firstOwn[index] < 0)
                            {
                                firstOwn[index] = k;
                            }
                        }
                    }

                    rockTop[index] = float.NegativeInfinity;
                    foreach (RaycastHit hit in Physics.RaycastAll(ray, RAY_LENGTH, Physics.AllLayers, QueryTriggerInteraction.Ignore))
                    {
                        if (rock.Contains(hit.collider))
                        {
                            rockTop[index] = Mathf.Max(rockTop[index], hit.point.y);
                        }
                        else if (water.Contains(hit.collider))
                        {
                            blocked[index] = true;
                        }
                    }
                }
            }

            // 원래 윤곽에서 가장 가까운 덱을 칸마다 정한다
            var owner    = new int[cells];
            var distance = new int[cells];
            var queue    = new Queue<int>();
            for (int index = 0; index < cells; ++index)
            {
                owner[index]    = firstOwn[index];
                distance[index] = (firstOwn[index] >= 0) ? 0 : int.MaxValue;
                if (firstOwn[index] >= 0)
                {
                    queue.Enqueue(index);
                }
            }
            SpreadOwners(queue, owner, distance, sizeX, sizeZ);

            int maxSteps     = Mathf.RoundToInt(settings.ExtendDistance / CELL_SIZE);
            int protectSteps = Mathf.CeilToInt(settings.ProtectRadius / CELL_SIZE);
            int written      = 0;
            for (int k = 0; k < count; ++k)
            {
                if (!targets.Contains(decks[k]))
                {
                    continue;
                }

                float height  = heights[k];
                bool[] mine   = inside[k];
                bool[] protect = BuildProtection(grid, props[k], mine, protectSteps, settings.ProtectRadius);
                var result = new Mask(grid.WorldX(BORDER_CELLS), grid.WorldZ(BORDER_CELLS),
                                      grid.WorldX(sizeX - BORDER_CELLS - 1), grid.WorldZ(sizeZ - BORDER_CELLS - 1), height);

                var include = new bool[cells];
                for (int index = 0; index < cells; ++index)
                {
                    float top = rockTop[index];
                    if (mine[index])
                    {
                        include[index] = (top >= (height - settings.FloatDepth)) || (protect[index]);
                    }
                    else if ((firstOwn[index] < 0) && (owner[index] == k) && (distance[index] <= maxSteps) && (!blocked[index])
                             && (top >= (height - settings.LedgeBelow)) && (top <= (height + settings.LedgeAbove)))
                    {
                        include[index] = true;
                    }
                }
                DropLoosePieces(include, mine, protect, sizeX, sizeZ);

                for (int i = 0; i < sizeX; ++i)
                {
                    for (int j = 0; j < sizeZ; ++j)
                    {
                        if (!include[(i * sizeZ) + j])
                        {
                            continue;
                        }
                        int ri = Mathf.RoundToInt((grid.WorldX(i) - result.OriginX) / CELL_SIZE);
                        int rj = Mathf.RoundToInt((grid.WorldZ(j) - result.OriginZ) / CELL_SIZE);
                        if ((ri >= 0) && (rj >= 0) && (ri < result.SizeX) && (rj < result.SizeZ))
                        {
                            result.Set(ri, rj, true);
                        }
                    }
                }

                EnsureEditable(decks[k], out Mesh topMesh, out Mesh wallMesh);
                GetWallRange(decks[k], height, out float wallBottom, out float wallTop);
                WriteMeshes(decks[k], result, topMesh, wallMesh, wallBottom, wallTop, true);
                ++written;
            }

            foreach (GameObject temp in temps)
            {
                Object.DestroyImmediate(temp);
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            return written;
        }

        // 씬의 NavMeshSurface를 모두 다시 굽는다. 기존 데이터 파일이 있으면 같은 파일(GUID)에 덮어쓴다
        public static int RebakeNavMesh(Scene scene)
        {
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (NavMeshSurface surface in root.GetComponentsInChildren<NavMeshSurface>(true))
                {
                    NavMeshData existing = surface.navMeshData;
                    string path = (existing != null) ? AssetDatabase.GetAssetPath(existing) : string.Empty;
                    surface.RemoveData();
                    surface.BuildNavMesh();
                    NavMeshData fresh = surface.navMeshData;

                    if (!string.IsNullOrEmpty(path))
                    {
                        string assetName = existing.name;
                        EditorUtility.CopySerialized(fresh, existing);
                        existing.name = assetName;
                        surface.RemoveData();
                        surface.navMeshData = existing;
                        surface.AddData();
                        Object.DestroyImmediate(fresh);
                        EditorUtility.SetDirty(existing);
                    }
                    else if (fresh != null)
                    {
                        string folder = scene.path.Substring(0, scene.path.Length - SCENE_EXTENSION.Length);
                        if (!AssetDatabase.IsValidFolder(folder))
                        {
                            AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(folder).Replace('\\', '/'), System.IO.Path.GetFileName(folder));
                        }
                        AssetDatabase.CreateAsset(fresh, $"{folder}/{NAVMESH_PREFIX}{surface.name}{ASSET_EXTENSION}");
                    }

                    EditorUtility.SetDirty(surface);
                    ++count;
                }
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            return count;
        }

        // 돌 지형 루트 아래 가장 정밀한 LOD 메쉬에 임시 콜라이더를 붙인다. 쓰고 나면 temps를 지운다
        public static HashSet<Collider> CreateRockColliders(Scene scene, string[] rootPrefixes, List<GameObject> temps)
        {
            var colliders = new HashSet<Collider>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if ((!root.activeInHierarchy) || (!StartsWithAny(root.name, rootPrefixes)))
                {
                    continue;
                }

                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(false))
                {
                    if ((filter.sharedMesh == null) || (filter.name.Contains(LOW_LOD_MARK)) || (filter.name.Contains(MEDIUM_LOD_MARK)))
                    {
                        continue;
                    }
                    MeshCollider probe = CreateProbe(filter.sharedMesh, filter.transform);
                    temps.Add(probe.gameObject);
                    colliders.Add(probe);
                }
            }
            Physics.SyncTransforms();
            return colliders;
        }

        public static float SampleTop(float x, float z, float fromY, HashSet<Collider> colliders)
        {
            float top = float.NegativeInfinity;
            var ray = new Ray(new Vector3(x, fromY + RAY_HEIGHT, z), Vector3.down);
            foreach (RaycastHit hit in Physics.RaycastAll(ray, RAY_LENGTH, Physics.AllLayers, QueryTriggerInteraction.Ignore))
            {
                if (colliders.Contains(hit.collider))
                {
                    top = Mathf.Max(top, hit.point.y);
                }
            }
            return top;
        }

        public static void CopyMesh(Mesh source, Mesh target)
        {
            target.Clear();
            target.indexFormat = source.indexFormat;
            target.vertices    = source.vertices;
            target.normals     = source.normals;
            target.tangents    = source.tangents;
            target.uv          = source.uv;
            target.triangles   = source.triangles;
            target.RecalculateBounds();
            EditorUtility.SetDirty(target);
        }

        // 메쉬를 제자리에서 바꾼 직후에는 렌더러 경계가 갱신되지 않았을 수 있어 메쉬 경계를 직접 옮긴다
        private static Bounds GetWorldBounds(MeshFilter filter)
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null)
            {
                return new Bounds(filter.transform.position, Vector3.zero);
            }

            Matrix4x4 toWorld = filter.transform.localToWorldMatrix;
            Vector3 min = mesh.bounds.min;
            Vector3 max = mesh.bounds.max;
            var bounds = new Bounds(toWorld.MultiplyPoint3x4(min), Vector3.zero);
            for (int corner = 0; corner < BOX_CORNERS; ++corner)
            {
                var local = new Vector3(((corner & 1) == 0) ? min.x : max.x, ((corner & 2) == 0) ? min.y : max.y, ((corner & 4) == 0) ? min.z : max.z);
                bounds.Encapsulate(toWorld.MultiplyPoint3x4(local));
            }
            return bounds;
        }

        private static bool IsFittedAsset(Mesh mesh)
        {
            return (mesh != null) && (AssetDatabase.GetAssetPath(mesh).StartsWith(FITTED_FOLDER));
        }

        private static Mesh CreateMeshAsset(Mesh source, string baseName)
        {
            Mesh copy = (source != null) ? Object.Instantiate(source) : new Mesh();
            string path = AssetDatabase.GenerateUniqueAssetPath($"{FITTED_FOLDER}/{baseName}{ASSET_EXTENSION}");
            copy.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(copy, path);
            return copy;
        }

        private static Mesh LoadModelMesh(string modelName, string subMeshName)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(MODEL_FOLDER + modelName + MODEL_EXTENSION))
            {
                if ((asset is Mesh mesh) && (mesh.name == subMeshName))
                {
                    return mesh;
                }
            }
            return null;
        }

        private static MeshCollider CreateProbe(Mesh mesh, Transform pose)
        {
            var probe = new GameObject(TEMP_OBJECT_NAME);
            probe.hideFlags = HideFlags.HideAndDontSave;
            probe.transform.SetPositionAndRotation(pose.position, pose.rotation);
            probe.transform.localScale = pose.lossyScale;
            var collider = probe.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            Physics.SyncTransforms();
            return collider;
        }

        private static HashSet<Collider> CreateNamedColliders(Scene scene, string[] names, List<GameObject> temps)
        {
            var colliders = new HashSet<Collider>();
            var nameSet = new HashSet<string>(names);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(false))
                {
                    if (!nameSet.Contains(child.name))
                    {
                        continue;
                    }

                    foreach (MeshFilter filter in child.GetComponentsInChildren<MeshFilter>(false))
                    {
                        if (filter.sharedMesh == null)
                        {
                            continue;
                        }
                        MeshCollider probe = CreateProbe(filter.sharedMesh, filter.transform);
                        temps.Add(probe.gameObject);
                        colliders.Add(probe);
                    }
                }
            }
            Physics.SyncTransforms();
            return colliders;
        }

        // 소품 루트/묶음/소품 구조에서 소품 기준점이 어느 덱 위에 서 있는지 모은다
        private static List<Vector3>[] CollectProps(Scene scene, string[] rootNames, MeshCollider[] sources)
        {
            var props = new List<Vector3>[sources.Length];
            for (int k = 0; k < sources.Length; ++k)
            {
                props[k] = new List<Vector3>();
            }

            var nameSet = new HashSet<string>(rootNames);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (!nameSet.Contains(root.name))
                {
                    continue;
                }

                foreach (Transform group in root.transform)
                {
                    foreach (Transform item in group)
                    {
                        var ray = new Ray(item.position + (Vector3.up * PROP_RAY_LIFT), Vector3.down);
                        for (int k = 0; k < sources.Length; ++k)
                        {
                            if (sources[k].Raycast(ray, out _, PROP_RAY_LENGTH))
                            {
                                props[k].Add(item.position);
                            }
                        }
                    }
                }
            }
            return props;
        }

        private static bool[] BuildProtection(Mask grid, List<Vector3> props, bool[] mine, int steps, float radius)
        {
            var protect = new bool[grid.SizeX * grid.SizeZ];
            foreach (Vector3 point in props)
            {
                int ci = Mathf.RoundToInt((point.x - grid.OriginX) / CELL_SIZE);
                int cj = Mathf.RoundToInt((point.z - grid.OriginZ) / CELL_SIZE);
                for (int a = -steps; a <= steps; ++a)
                {
                    for (int b = -steps; b <= steps; ++b)
                    {
                        int i = ci + a;
                        int j = cj + b;
                        if ((i < 0) || (j < 0) || (i >= grid.SizeX) || (j >= grid.SizeZ))
                        {
                            continue;
                        }

                        int index = (i * grid.SizeZ) + j;
                        if ((mine[index]) && (new Vector2(a * CELL_SIZE, b * CELL_SIZE).magnitude <= radius))
                        {
                            protect[index] = true;
                        }
                    }
                }
            }
            return protect;
        }

        private static void SpreadOwners(Queue<int> queue, int[] owner, int[] distance, int sizeX, int sizeZ)
        {
            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                int i = index / sizeZ;
                int j = index % sizeZ;
                for (int n = 0; n < G_StepX.Length; ++n)
                {
                    int ni = i + G_StepX[n];
                    int nj = j + G_StepZ[n];
                    if ((ni < 0) || (nj < 0) || (ni >= sizeX) || (nj >= sizeZ))
                    {
                        continue;
                    }

                    int next = (ni * sizeZ) + nj;
                    if (distance[next] != int.MaxValue)
                    {
                        continue;
                    }
                    distance[next] = distance[index] + 1;
                    owner[next]    = owner[index];
                    queue.Enqueue(next);
                }
            }
        }

        // 원래 덱과 이어지지 않거나 너무 작은 조각은 버린다. 소품을 받치는 칸은 남긴다
        private static void DropLoosePieces(bool[] include, bool[] mine, bool[] protect, int sizeX, int sizeZ)
        {
            var piece = new int[include.Length];
            var keep  = new List<bool> { false };
            var queue = new Queue<int>();
            var members = new List<int>();
            for (int start = 0; start < include.Length; ++start)
            {
                if ((!include[start]) || (piece[start] != 0))
                {
                    continue;
                }

                int id = keep.Count;
                bool touchesOriginal = false;
                members.Clear();
                piece[start] = id;
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    int index = queue.Dequeue();
                    members.Add(index);
                    touchesOriginal |= mine[index];
                    int i = index / sizeZ;
                    int j = index % sizeZ;
                    for (int n = 0; n < G_StepX.Length; ++n)
                    {
                        int ni = i + G_StepX[n];
                        int nj = j + G_StepZ[n];
                        if ((ni < 0) || (nj < 0) || (ni >= sizeX) || (nj >= sizeZ))
                        {
                            continue;
                        }

                        int next = (ni * sizeZ) + nj;
                        if ((include[next]) && (piece[next] == 0))
                        {
                            piece[next] = id;
                            queue.Enqueue(next);
                        }
                    }
                }
                keep.Add((touchesOriginal) && (members.Count >= MIN_PIECE_CELLS));
            }

            for (int index = 0; index < include.Length; ++index)
            {
                if ((include[index]) && (!keep[piece[index]]) && (!protect[index]))
                {
                    include[index] = false;
                }
            }
        }

        private static float[] Blur(Mask mask)
        {
            var field = new float[mask.SizeX * mask.SizeZ];
            for (int i = 0; i < mask.SizeX; ++i)
            {
                for (int j = 0; j < mask.SizeZ; ++j)
                {
                    float sum    = 0f;
                    float weight = 0f;
                    for (int a = -BLUR_RADIUS; a <= BLUR_RADIUS; ++a)
                    {
                        for (int b = -BLUR_RADIUS; b <= BLUR_RADIUS; ++b)
                        {
                            int ni = i + a;
                            int nj = j + b;
                            if ((ni < 0) || (nj < 0) || (ni >= mask.SizeX) || (nj >= mask.SizeZ))
                            {
                                continue;
                            }

                            float w = ((a == 0) && (b == 0)) ? BLUR_CENTER : (((a == 0) || (b == 0)) ? BLUR_SIDE : BLUR_CORNER);
                            sum    += mask.Get(ni, nj) ? w : 0f;
                            weight += w;
                        }
                    }

                    // 칠한 칸은 흐려져도 윤곽 안에 남게 해 소품 발밑이 깎이지 않게 한다.
                    // 윤곽이 칸 위를 정확히 지나면 다시 읽을 때 레이가 빗나가므로 조금 더 안쪽으로 둔다
                    float value = sum / weight;
                    if ((mask.Get(i, j)) && (value < (ISO_LEVEL + SET_MARGIN)))
                    {
                        value = ISO_LEVEL + SET_MARGIN;
                    }
                    field[(i * mask.SizeZ) + j] = value;
                }
            }
            return field;
        }

        private static int GetVertex(float x, float z, float y, List<Vector3> vertices, Dictionary<long, int> lookup)
        {
            long key = ((long)Mathf.RoundToInt(x * VERTEX_KEY_SCALE) << KEY_SHIFT) ^ (uint)Mathf.RoundToInt(z * VERTEX_KEY_SCALE);
            if (!lookup.TryGetValue(key, out int index))
            {
                index = vertices.Count;
                vertices.Add(new Vector3(x, y, z));
                lookup[key] = index;
            }
            return index;
        }

        private static void FillTop(Mesh mesh, List<Vector3> worldVertices, List<int> triangles, Matrix4x4 toLocal)
        {
            var local   = new List<Vector3>(worldVertices.Count);
            var normals = new List<Vector3>(worldVertices.Count);
            var uvs     = new List<Vector2>(worldVertices.Count);
            Vector3 up  = toLocal.MultiplyVector(Vector3.up).normalized;
            foreach (Vector3 point in worldVertices)
            {
                local.Add(toLocal.MultiplyPoint3x4(point));
                normals.Add(up);
                uvs.Add(new Vector2(point.x, point.z)); // 원본 덱과 같은 월드 xz 1m 타일
            }

            mesh.Clear();
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(local);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
        }

        // 한 삼각형만 쓰는 모서리가 윤곽이다. 그 모서리마다 양면 벽 사각형을 세운다
        private static void FillWall(Mesh mesh, List<Vector3> worldVertices, List<int> triangles, float bottom, float top, Matrix4x4 toLocal)
        {
            var edgeUse = new Dictionary<long, int>();
            for (int t = 0; t < triangles.Count; t += VERTS_PER_TRIANGLE)
            {
                for (int e = 0; e < VERTS_PER_TRIANGLE; ++e)
                {
                    int a = triangles[t + e];
                    int b = triangles[t + ((e + 1) % VERTS_PER_TRIANGLE)];
                    long key = (a < b) ? (((long)a << KEY_SHIFT) | (uint)b) : (((long)b << KEY_SHIFT) | (uint)a);
                    edgeUse.TryGetValue(key, out int used);
                    edgeUse[key] = used + 1;
                }
            }

            var vertices = new List<Vector3>();
            var indices  = new List<int>();
            foreach (KeyValuePair<long, int> edge in edgeUse)
            {
                if (edge.Value != 1)
                {
                    continue;
                }

                Vector3 a = worldVertices[(int)(edge.Key >> KEY_SHIFT)];
                Vector3 b = worldVertices[(int)(edge.Key & uint.MaxValue)];
                int s = vertices.Count;
                vertices.Add(toLocal.MultiplyPoint3x4(new Vector3(a.x, bottom, a.z)));
                vertices.Add(toLocal.MultiplyPoint3x4(new Vector3(b.x, bottom, b.z)));
                vertices.Add(toLocal.MultiplyPoint3x4(new Vector3(b.x, top, b.z)));
                vertices.Add(toLocal.MultiplyPoint3x4(new Vector3(a.x, top, a.z)));
                indices.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3, s, s + 2, s + 1, s, s + 3, s + 2 });
            }

            mesh.Clear();
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private static bool StartsWithAny(string value, string[] prefixes)
        {
            foreach (string prefix in prefixes)
            {
                if ((!string.IsNullOrEmpty(prefix)) && (value.StartsWith(prefix)))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
#endif
