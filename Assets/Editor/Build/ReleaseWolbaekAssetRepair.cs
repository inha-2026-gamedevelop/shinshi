// System
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

// Unity
using UnityEditor;
using UnityEngine;

namespace Shinshi.Editor
{
    // Release 월백 맵의 복사본에서 끊어진 벽 프리팹과 부모가 끊긴 경계선 재질을 고친다. 원본 씬 · 원본 에셋은 건드리지 않는다
    // 호출 위치: ReleaseDungeonMapBuilder가 월백 Dev 씬 루트를 Instantiate한 직후, NavMesh Bake와 씬 저장보다 앞
    // 복사본은 열린 씬 안에 있어야 한다(프리팹 인스턴스를 부모 아래에 만들기 때문). 이 헬퍼 안에서 씬을 저장하지 않는다
    public static class ReleaseWolbaekAssetRepair
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string WALL_ROOT_NAME      = "Wall_R22";
        private const string OLD_CUBES_ROOT_NAME = "Wall_R22_OldCubes";
        private const string WALL_PREFAB_PATH    = "Assets/02.Prefab/Environment/DungeonReuseOptimized/ModStoneWallStraight2m/PF_ModStoneWallStraight2m.prefab";
        private const string WALL_PREFAB_GUID    = "212241977c9783149844270d7d935bba";

        private const string BOUNDARY_MATERIAL_GUID = "c8aa15d39333cb440b3c4ca45900fe57";
        private const string BOUNDARY_MATERIAL_PATH = "Assets/02.Prefab/Environment/BossArena/Generated/M_CollapseBoundaryLine.mat";
        private const string BOUNDARY_MATERIAL_FILE = "M_CollapseBoundaryLine.mat";
        private const string BOUNDARY_PARENT_GUID   = "a91e5aa018939f746a91917ed7027708";

        private const string SHATTER_VIEW_TYPE_NAME = "BossArenaShatterView";
        private const string SHATTER_FALLBACK_MATERIAL_PATH = "Assets/02.Prefab/Environment/BossArena/Generated/AgedGranite.mat";

        private const string ASSETS_ROOT = "Assets";
        private const string SAVED_NO_PARENT = "m_Parent: {fileID: 0}";

        private const string MISSING_WALL_GUID = "1045083f2495ad54296d4084ab5d3b4c";

        private static readonly Regex G_MissingName = new Regex(@"^(?<base>\S+) \(Missing Prefab with guid: " + MISSING_WALL_GUID + @"\)$", RegexOptions.Compiled);

        private const float HALF             = 0.5f;
        private const float SCALE_EPSILON    = 0.00001f;
        private const float CORNER_TOLERANCE = 0.02f;
        private const float VALUE_TOLERANCE  = 0.0001f;
        private const int   BOX_CORNER_COUNT = 8;

        private static readonly string[] G_ColorProperties = { "_BaseColor", "_Color", "_EmissionColor" };
        private static readonly string[] G_FloatProperties = { "_Metallic", "_Smoothness", "_Glossiness", "_Surface", "_ZWrite", "_BumpScale", "_OcclusionStrength", "_WorkflowMode" };

        private struct CubePose
        {
            public string     Name;
            public int        Layer;
            public Vector3    BottomCenter;
            public Quaternion Rotation;
            public Vector3    Size;
            public Vector3[]  Corners;
        }

        /****************************************
        *               Entry Point
        ****************************************/

        // copiedBossArenaMap: 월백 Dev 씬 루트를 Instantiate한 열린 씬 안의 복사본. 요약 문자열을 돌려주고, 필수 기준이 없으면 예외를 던진다
        // 같은 복사본에 다시 불러도 벽이 중복되지 않는다. 실패하면 복사본이 반쯤 고쳐졌을 수 있으니 호출자는 그 복사본을 버린다
        public static string Repair(GameObject copiedBossArenaMap, string destinationAssetFolder)
        {
            if (copiedBossArenaMap == null)
            {
                throw new ArgumentNullException(nameof(copiedBossArenaMap));
            }

            if (!copiedBossArenaMap.scene.IsValid())
            {
                throw new InvalidOperationException("복사본이 열린 씬 안에 있어야 한다: " + copiedBossArenaMap.name);
            }

            if (PrefabUtility.IsPartOfPrefabAsset(copiedBossArenaMap) || EditorUtility.IsPersistent(copiedBossArenaMap))
            {
                throw new InvalidOperationException("에셋 원본은 고치지 않는다. 씬 안의 복사본을 넘긴다: " + copiedBossArenaMap.name);
            }

            if (string.IsNullOrEmpty(destinationAssetFolder))
            {
                throw new ArgumentException("재질을 둘 폴더가 비었다", nameof(destinationAssetFolder));
            }

            string folder = destinationAssetFolder.Replace('\\', '/').TrimEnd('/');
            if ((folder != ASSETS_ROOT) && (!folder.StartsWith(ASSETS_ROOT + "/", StringComparison.Ordinal)))
            {
                throw new ArgumentException("Assets 아래 폴더여야 한다: " + destinationAssetFolder, nameof(destinationAssetFolder));
            }

            var report = new StringBuilder("[ReleaseWolbaekAssetRepair]");
            report.Append(RepairWalls(copiedBossArenaMap));
            report.Append(RepairBoundaryMaterial(copiedBossArenaMap, folder));
            report.Append(RepairShatterNullMaterials(copiedBossArenaMap));
            return report.ToString();
        }

        /****************************************
        *        Shatter null material slots
        ****************************************/

        // Ring FBX 메타가 없는 외부 재질 4개(MI_KoreanBrick_2/4, MI_KoreanStone_1, MI_R_Roof)를 가리켜 슬롯이 null이다
        // 원래 재질은 복구할 수 없으므로 대체(fallback)로 AgedGranite를 채운다. 슬롯 순서는 추정하지 않고 null 슬롯만 채운다
        private static string RepairShatterNullMaterials(GameObject root)
        {
            Material fallback = null;
            var views = new List<MonoBehaviour>();
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if ((behaviour != null) && (behaviour.GetType().Name == SHATTER_VIEW_TYPE_NAME))
                {
                    views.Add(behaviour);
                }
            }

            if (views.Count == 0)
            {
                throw new InvalidOperationException("복사본에서 " + SHATTER_VIEW_TYPE_NAME + "을 찾지 못했다");
            }

            int filledSlots     = 0;
            int touchedRenderers = 0;
            var visited         = new HashSet<Renderer>();
            for (int v = 0; v < views.Count; ++v)
            {
                Renderer[] renderers = views[v].GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; ++r)
                {
                    Renderer renderer = renderers[r];
                    if ((renderer == null) || (!visited.Add(renderer)))
                    {
                        continue;
                    }

                    Material[] slots = renderer.sharedMaterials;
                    bool changed = false;
                    for (int s = 0; s < slots.Length; ++s)
                    {
                        if (slots[s] == null)
                        {
                            if (fallback == null)
                            {
                                fallback = AssetDatabase.LoadAssetAtPath<Material>(SHATTER_FALLBACK_MATERIAL_PATH);
                                if (fallback == null)
                                {
                                    throw new InvalidOperationException("파쇄 대체 재질이 없다: " + SHATTER_FALLBACK_MATERIAL_PATH);
                                }
                            }

                            slots[s] = fallback;
                            ++filledSlots;
                            changed = true;
                        }
                    }

                    if (changed)
                    {
                        renderer.sharedMaterials = slots;
                        EditorUtility.SetDirty(renderer);
                        ++touchedRenderers;
                    }
                }
            }

            foreach (Renderer renderer in visited)
            {
                Material[] slots = renderer.sharedMaterials;
                for (int s = 0; s < slots.Length; ++s)
                {
                    if (slots[s] == null)
                    {
                        throw new InvalidOperationException($"파쇄 렌더러 재질 슬롯이 아직 null이다: {renderer.name} 슬롯 {s}");
                    }
                }
            }

            if (filledSlots == 0)
            {
                return "\n- shatter: 원본 재질 보존, 채운 슬롯 0개, null 0.";
            }

            return $"\n- shatter 대체재질(AgedGranite, 원본 재질 없음): 렌더러 {touchedRenderers}개, 슬롯 {filledSlots}개 채움, null 0.";
        }

        /****************************************
        *                Walls
        ****************************************/

        private static string RepairWalls(GameObject root)
        {
            GameObject wallPrefab = LoadWallPrefab();
            BoxCollider prefabBox = wallPrefab.GetComponent<BoxCollider>();
            if (prefabBox == null)
            {
                throw new InvalidOperationException("벽 프리팹 루트에 BoxCollider가 없다: " + WALL_PREFAB_PATH);
            }

            Transform wallRoot = FindUnique(root.transform, WALL_ROOT_NAME);
            Transform oldCubes = FindUnique(root.transform, OLD_CUBES_ROOT_NAME);

            Vector3 parentScale = wallRoot.lossyScale;
            if ((Mathf.Abs(parentScale.x) < SCALE_EPSILON) || (Mathf.Abs(parentScale.y) < SCALE_EPSILON) || (Mathf.Abs(parentScale.z) < SCALE_EPSILON))
            {
                throw new InvalidOperationException("Wall_R22의 월드 크기가 0이라 벽을 맞출 수 없다");
            }

            List<CubePose> poses = ReadCubePoses(oldCubes);
            if (poses.Count == 0)
            {
                throw new InvalidOperationException("Wall_R22_OldCubes에 원래 벽이 없다");
            }

            var pending = new List<CubePose>(poses.Count);
            int originalCount = 0;
            Dictionary<string, Mesh> originalSignature = LoadOriginalWallSignature();
            for (int i = 0; i < poses.Count; ++i)
            {
                if (HasOriginalWall(wallRoot, poses[i].Name, originalSignature))
                {
                    ++originalCount;
                }
                else if (!HasRepairedWall(wallRoot, poses[i].Name, wallPrefab))
                {
                    RejectUnrecognizedWall(wallRoot, poses[i].Name);
                    pending.Add(poses[i]);
                }
            }

            List<Transform> missing = CollectMissingInstances(wallRoot, out List<string> missingNames);
            if (missing.Count != pending.Count)
            {
                throw new InvalidOperationException($"끊어진 벽 {missing.Count}개와 고쳐야 할 옛 벽 {pending.Count}개가 맞지 않는다. 구조가 달라져 자동 교체를 멈춘다");
            }

            var siblingIndexByName = new Dictionary<string, int>(missing.Count);
            for (int i = 0; i < missing.Count; ++i)
            {
                if (!siblingIndexByName.TryAdd(missingNames[i], missing[i].GetSiblingIndex()))
                {
                    throw new InvalidOperationException("끊어진 벽 이름이 겹친다: " + missingNames[i]);
                }
            }

            for (int i = 0; i < pending.Count; ++i)
            {
                if (!siblingIndexByName.ContainsKey(pending[i].Name))
                {
                    throw new InvalidOperationException("옛 벽에 짝이 되는 끊어진 벽이 없다: " + pending[i].Name);
                }
            }

            for (int i = 0; i < missing.Count; ++i)
            {
                UnityEngine.Object.DestroyImmediate(missing[i].gameObject);
            }

            for (int i = 0; i < pending.Count; ++i)
            {
                Transform wrapper = CreateWall(wallRoot, wallPrefab, prefabBox, pending[i], parentScale);
                wrapper.SetSiblingIndex(Mathf.Min(siblingIndexByName[pending[i].Name], wallRoot.childCount - 1));
            }

            if (oldCubes.gameObject.activeSelf)
            {
                oldCubes.gameObject.SetActive(false);
            }

            int verified = 0;
            for (int i = 0; i < poses.Count; ++i)
            {
                if (HasOriginalWall(wallRoot, poses[i].Name, originalSignature))
                {
                    continue;
                }

                VerifyWall(wallRoot, wallPrefab, poses[i]);
                ++verified;
            }

            if (missing.Count == 0)
            {
                return $"\n- 벽: 원본 보존 {originalCount}개, 교체 0개, 대체 벽 {verified}개 유지(모서리 대조 통과), OldCubes 비활성 유지";
            }

            return $"\n- 벽: 원본 보존 {originalCount}개, 끊어진 인스턴스 {missing.Count}개만 대체 벽으로 교체, 이미 고친 대체 벽 {verified - missing.Count}개 유지, 모서리 대조 {verified}개 통과, OldCubes 비활성 유지";
        }

        // Instantiate 복사본은 프리팹 연결을 잃으므로 연결 대신 원본 프리팹 에셋(GUID)의 메시 참조 + 계층(형제 인덱스 경로)이 정확히 같은지로 가린다
        private static Dictionary<string, Mesh> LoadOriginalWallSignature()
        {
            string path = AssetDatabase.GUIDToAssetPath(MISSING_WALL_GUID);
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            GameObject original = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (original == null)
            {
                return null;
            }

            Dictionary<string, Mesh> signature = BuildMeshSignature(original.transform);
            return ((signature.Count > 0) && (!signature.ContainsValue(null))) ? signature : null;
        }

        private static Dictionary<string, Mesh> BuildMeshSignature(Transform root)
        {
            var signature = new Dictionary<string, Mesh>();
            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; ++i)
            {
                Mesh mesh = filters[i].sharedMesh;
                if ((mesh == null) || (filters[i].GetComponent<Renderer>() == null))
                {
                    signature.Clear();
                    signature[string.Empty] = null;
                    return signature;
                }

                signature[RelativeIndexPath(root, filters[i].transform)] = mesh;
            }

            return signature;
        }

        private static string RelativeIndexPath(Transform root, Transform target)
        {
            var path = new StringBuilder();
            for (Transform t = target; t != root; t = t.parent)
            {
                path.Insert(0, "/" + t.GetSiblingIndex());
            }

            return path.ToString();
        }

        private static bool MatchesSignature(Transform candidate, Dictionary<string, Mesh> expected)
        {
            Dictionary<string, Mesh> actual = BuildMeshSignature(candidate);
            if ((actual.Count != expected.Count) || actual.ContainsValue(null))
            {
                return false;
            }

            foreach (KeyValuePair<string, Mesh> pair in expected)
            {
                if ((!actual.TryGetValue(pair.Key, out Mesh mesh)) || (mesh != pair.Value))
                {
                    return false;
                }
            }

            return true;
        }

        // 이름이 정확히 같고(자리표시자는 접미사가 붙어 다르다) 원본 에셋과 메시·계층이 일치하는 자식이 있는가
        private static bool HasOriginalWall(Transform wallRoot, string wallName, Dictionary<string, Mesh> signature)
        {
            if (signature == null)
            {
                return false;
            }

            for (int i = 0; i < wallRoot.childCount; ++i)
            {
                Transform candidate = wallRoot.GetChild(i);
                if ((candidate.name == wallName) && MatchesSignature(candidate, signature))
                {
                    return true;
                }
            }

            return false;
        }

        private static void RejectUnrecognizedWall(Transform wallRoot, string wallName)
        {
            for (int i = 0; i < wallRoot.childCount; ++i)
            {
                if (wallRoot.GetChild(i).name == wallName)
                {
                    throw new InvalidOperationException("같은 이름이지만 원본 벽(메시·계층)도 대체 벽도 아닌 형태다: " + wallName);
                }
            }
        }

        private static GameObject LoadWallPrefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WALL_PREFAB_PATH);
            if (prefab == null)
            {
                throw new InvalidOperationException("벽 프리팹이 없다: " + WALL_PREFAB_PATH);
            }

            string guid = AssetDatabase.AssetPathToGUID(WALL_PREFAB_PATH);
            if (guid != WALL_PREFAB_GUID)
            {
                throw new InvalidOperationException($"벽 프리팹 GUID가 다르다: {guid} (기대 {WALL_PREFAB_GUID})");
            }

            return prefab;
        }

        private static Transform FindUnique(Transform root, string objectName)
        {
            Transform found = null;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; ++i)
            {
                if (all[i].name != objectName)
                {
                    continue;
                }

                if (found != null)
                {
                    throw new InvalidOperationException($"'{objectName}'이 둘 이상이다");
                }

                found = all[i];
            }

            if (found == null)
            {
                throw new InvalidOperationException($"'{objectName}'을 복사본에서 찾지 못했다");
            }

            return found;
        }

        private static List<CubePose> ReadCubePoses(Transform oldCubes)
        {
            var poses = new List<CubePose>(oldCubes.childCount);
            for (int i = 0; i < oldCubes.childCount; ++i)
            {
                Transform cube = oldCubes.GetChild(i);
                Vector3 localCenter = Vector3.zero;
                Vector3 localSize   = Vector3.one;
                BoxCollider box = cube.GetComponent<BoxCollider>();
                if (box != null)
                {
                    localCenter = box.center;
                    localSize   = box.size;
                }

                Vector3 lossy = cube.lossyScale;
                var pose = new CubePose();
                pose.Name     = cube.name;
                pose.Layer    = cube.gameObject.layer;
                pose.Rotation = cube.rotation;
                pose.Size     = new Vector3(Mathf.Abs(localSize.x * lossy.x), Mathf.Abs(localSize.y * lossy.y), Mathf.Abs(localSize.z * lossy.z));

                Vector3 center = cube.TransformPoint(localCenter);
                pose.BottomCenter = center + (cube.rotation * new Vector3(0f, -pose.Size.y * HALF, 0f));
                pose.Corners      = new Vector3[BOX_CORNER_COUNT];
                FillCorners(cube, localCenter, localSize, pose.Corners);
                poses.Add(pose);
            }

            return poses;
        }

        private static void FillCorners(Transform owner, Vector3 center, Vector3 size, Vector3[] corners)
        {
            int index = 0;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 offset = new Vector3(size.x * HALF * x, size.y * HALF * y, size.z * HALF * z);
                        corners[index] = owner.TransformPoint(center + offset);
                        ++index;
                    }
                }
            }
        }

        // 원본 씬은 MissingAsset 상태로, Instantiate 복사본은 NotAPrefab 상태로 같은 이름의 자리표시자를 갖는다. 상태 대신 정확한 GUID 이름과 구조로 가린다
        private static bool TryParsePlaceholder(Transform candidate, out string baseName)
        {
            baseName = null;
            Match match = G_MissingName.Match(candidate.name);
            if (!match.Success)
            {
                return false;
            }

            if (candidate.GetComponentInChildren<Renderer>(true) != null)
            {
                return false;
            }

            if (candidate.GetComponentInChildren<MeshFilter>(true) != null)
            {
                return false;
            }

            baseName = match.Groups["base"].Value;
            return true;
        }

        private static List<Transform> CollectMissingInstances(Transform wallRoot, out List<string> baseNames)
        {
            var result = new List<Transform>();
            baseNames = new List<string>();
            for (int i = 0; i < wallRoot.childCount; ++i)
            {
                Transform candidate = wallRoot.GetChild(i);
                if (!TryParsePlaceholder(candidate, out string baseName))
                {
                    continue;
                }

                result.Add(candidate);
                baseNames.Add(baseName);
            }

            return result;
        }

        private static bool HasRepairedWall(Transform wallRoot, string wallName, GameObject wallPrefab)
        {
            for (int i = 0; i < wallRoot.childCount; ++i)
            {
                Transform wrapper = wallRoot.GetChild(i);
                if (wrapper.name != wallName)
                {
                    continue;
                }

                if (FindPrefabChild(wrapper, wallPrefab) != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static Transform FindPrefabChild(Transform wrapper, GameObject wallPrefab)
        {
            for (int i = 0; i < wrapper.childCount; ++i)
            {
                Transform child = wrapper.GetChild(i);
                if (PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject) == wallPrefab)
                {
                    return child;
                }
            }

            return null;
        }

        private static Transform CreateWall(Transform wallRoot, GameObject wallPrefab, BoxCollider prefabBox, CubePose pose, Vector3 parentScale)
        {
            var wrapper = new GameObject(pose.Name);
            Transform wrapperTransform = wrapper.transform;
            wrapperTransform.SetParent(wallRoot, false);
            wrapperTransform.SetPositionAndRotation(pose.BottomCenter, pose.Rotation);

            Vector3 worldScale = new Vector3(pose.Size.x / prefabBox.size.x, pose.Size.y / prefabBox.size.y, pose.Size.z / prefabBox.size.z);
            wrapperTransform.localScale = new Vector3(worldScale.x / parentScale.x, worldScale.y / parentScale.y, worldScale.z / parentScale.z);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(wallPrefab, wrapperTransform);
            Transform instanceTransform = instance.transform;
            instanceTransform.localRotation = Quaternion.identity;
            instanceTransform.localScale    = Vector3.one;
            instanceTransform.localPosition = new Vector3(-prefabBox.center.x, -(prefabBox.center.y - (prefabBox.size.y * HALF)), -prefabBox.center.z);

            SetLayerRecursively(wrapperTransform, pose.Layer);
            return wrapperTransform;
        }

        private static void SetLayerRecursively(Transform target, int layer)
        {
            target.gameObject.layer = layer;
            for (int i = 0; i < target.childCount; ++i)
            {
                SetLayerRecursively(target.GetChild(i), layer);
            }
        }

        private static void VerifyWall(Transform wallRoot, GameObject wallPrefab, CubePose pose)
        {
            Transform wrapper = null;
            for (int i = 0; i < wallRoot.childCount; ++i)
            {
                Transform candidate = wallRoot.GetChild(i);
                if ((candidate.name == pose.Name) && (FindPrefabChild(candidate, wallPrefab) != null))
                {
                    if (wrapper != null)
                    {
                        throw new InvalidOperationException("같은 이름의 고친 벽이 둘 이상이다: " + pose.Name);
                    }

                    wrapper = candidate;
                }
            }

            if (wrapper == null)
            {
                throw new InvalidOperationException("고친 벽을 찾지 못했다: " + pose.Name);
            }

            Collider[] colliders = wrapper.GetComponentsInChildren<Collider>(true);
            if ((colliders.Length != 1) || (!(colliders[0] is BoxCollider)))
            {
                throw new InvalidOperationException($"{pose.Name}의 콜라이더가 하나(BoxCollider)가 아니다: {colliders.Length}개");
            }

            if (wrapper.gameObject.layer != pose.Layer)
            {
                throw new InvalidOperationException($"{pose.Name}의 레이어가 옛 벽과 다르다");
            }

            var box = (BoxCollider)colliders[0];
            var corners = new Vector3[BOX_CORNER_COUNT];
            FillCorners(box.transform, box.center, box.size, corners);
            for (int i = 0; i < pose.Corners.Length; ++i)
            {
                float nearest = float.MaxValue;
                for (int j = 0; j < corners.Length; ++j)
                {
                    nearest = Mathf.Min(nearest, Vector3.Distance(pose.Corners[i], corners[j]));
                }

                if (nearest > CORNER_TOLERANCE)
                {
                    throw new InvalidOperationException($"{pose.Name}의 충돌 모서리가 옛 벽과 {nearest:F3}m 어긋난다");
                }
            }
        }

        /****************************************
        *               Material
        ****************************************/

        private static string RepairBoundaryMaterial(GameObject root, string folder)
        {
            string sourcePath = AssetDatabase.GUIDToAssetPath(BOUNDARY_MATERIAL_GUID);
            if (string.IsNullOrEmpty(sourcePath))
            {
                sourcePath = BOUNDARY_MATERIAL_PATH;
            }

            Material source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
            if ((source == null) || (source.shader == null))
            {
                throw new InvalidOperationException("경계선 원본 재질이나 셰이더를 읽지 못했다: " + sourcePath);
            }

            Material parent = source.parent;
            if ((parent != null) && (parent.shader != null) && (AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(parent)) == BOUNDARY_PARENT_GUID))
            {
                return $"\n- 재질: 원본 보존 {sourcePath} (부모 {AssetDatabase.GetAssetPath(parent)} 유효), 독립 재질 생성·슬롯 교체 0개";
            }

            string releasePath = folder + "/" + BOUNDARY_MATERIAL_FILE;
            if (string.Equals(releasePath, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Release 재질 경로가 원본과 같다: " + releasePath);
            }

            EnsureFolder(folder);

            Material clean = BuildCleanMaterial(source);
            Material release = AssetDatabase.LoadAssetAtPath<Material>(releasePath);
            bool created = (release == null);
            if (created)
            {
                AssetDatabase.CreateAsset(clean, releasePath);
                release = clean;
            }
            else
            {
                string releaseName = release.name;
                EditorUtility.CopySerialized(clean, release);
                release.name = releaseName;
                release.parent = null;
                EditorUtility.SetDirty(release);
                UnityEngine.Object.DestroyImmediate(clean);
            }

            AssetDatabase.SaveAssetIfDirty(release);
            AssetDatabase.ImportAsset(releasePath, ImportAssetOptions.ForceUpdate);
            release = AssetDatabase.LoadAssetAtPath<Material>(releasePath);
            if (release == null)
            {
                throw new InvalidOperationException("저장한 Release 재질을 다시 읽지 못했다: " + releasePath);
            }

            VerifyStandalone(source, release);
            VerifySavedFile(releasePath);

            int replaced = ReplaceRendererMaterials(root, source, sourcePath, release);
            return $"\n- 재질: {(created ? "새로 만듦" : "기존 GUID로 갱신")} {releasePath}, 부모 없음 확인, 렌더러 슬롯 {replaced}개 교체";
        }

        // 변종 정보가 섞이지 않도록 CopyPropertiesFromMaterial 없이 새 재질에 원본의 유효값만 하나씩 옮긴다
        private static Material BuildCleanMaterial(Material source)
        {
            var clean = new Material(source.shader);
            clean.name = System.IO.Path.GetFileNameWithoutExtension(BOUNDARY_MATERIAL_FILE);
            ApplyEffectiveProperties(source, clean);
            clean.renderQueue = source.renderQueue;
            clean.globalIlluminationFlags = source.globalIlluminationFlags;
            clean.enableInstancing = source.enableInstancing;
            clean.doubleSidedGI = source.doubleSidedGI;

            string[] keywords = source.shaderKeywords;
            for (int i = 0; i < keywords.Length; ++i)
            {
                clean.EnableKeyword(keywords[i]);
            }

            return clean;
        }

        private static void VerifySavedFile(string releasePath)
        {
            string text = System.IO.File.ReadAllText(releasePath);
            if (!text.Contains(SAVED_NO_PARENT))
            {
                throw new InvalidOperationException("저장된 Release 재질 파일에 부모 연결이 남았다: " + releasePath);
            }
        }

        // 부모를 끊으면 변종 덮어쓰기 값이 초기화될 수 있어, 끊은 뒤에 원본의 현재 유효값을 셰이더 속성 단위로 다시 써 넣는다
        private static void ApplyEffectiveProperties(Material source, Material release)
        {
            Shader shader = source.shader;
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; ++i)
            {
                string property = shader.GetPropertyName(i);
                UnityEngine.Rendering.ShaderPropertyType type = shader.GetPropertyType(i);
                if (type == UnityEngine.Rendering.ShaderPropertyType.Color)
                {
                    release.SetColor(property, source.GetColor(property));
                }
                else if (type == UnityEngine.Rendering.ShaderPropertyType.Vector)
                {
                    release.SetVector(property, source.GetVector(property));
                }
                else if ((type == UnityEngine.Rendering.ShaderPropertyType.Float) || (type == UnityEngine.Rendering.ShaderPropertyType.Range))
                {
                    release.SetFloat(property, source.GetFloat(property));
                }
                else if (type == UnityEngine.Rendering.ShaderPropertyType.Int)
                {
                    release.SetInteger(property, source.GetInteger(property));
                }
                else if (type == UnityEngine.Rendering.ShaderPropertyType.Texture)
                {
                    release.SetTexture(property, source.GetTexture(property));
                    release.SetTextureScale(property, source.GetTextureScale(property));
                    release.SetTextureOffset(property, source.GetTextureOffset(property));
                }
            }
        }

        private static void VerifyStandalone(Material source, Material release)
        {
            if ((release.parent != null) || release.isVariant)
            {
                throw new InvalidOperationException("Release 재질에 부모 연결이 남았다: " + AssetDatabase.GetAssetPath(release));
            }

            if (release.shader != source.shader)
            {
                throw new InvalidOperationException("Release 재질의 셰이더가 원본과 다르다");
            }

            for (int i = 0; i < G_ColorProperties.Length; ++i)
            {
                string property = G_ColorProperties[i];
                if (!source.HasProperty(property))
                {
                    continue;
                }

                Color a = source.GetColor(property);
                Color b = release.GetColor(property);
                if ((Mathf.Abs(a.r - b.r) > VALUE_TOLERANCE) || (Mathf.Abs(a.g - b.g) > VALUE_TOLERANCE) || (Mathf.Abs(a.b - b.b) > VALUE_TOLERANCE) || (Mathf.Abs(a.a - b.a) > VALUE_TOLERANCE))
                {
                    throw new InvalidOperationException($"재질 색 {property}이 원본과 다르다: 원본 {a}, Release {b}");
                }
            }

            for (int i = 0; i < G_FloatProperties.Length; ++i)
            {
                string property = G_FloatProperties[i];
                if (!source.HasProperty(property))
                {
                    continue;
                }

                if (Mathf.Abs(source.GetFloat(property) - release.GetFloat(property)) > VALUE_TOLERANCE)
                {
                    throw new InvalidOperationException($"재질 값 {property}가 원본과 다르다: 원본 {source.GetFloat(property)}, Release {release.GetFloat(property)}");
                }
            }
        }

        private static int ReplaceRendererMaterials(GameObject root, Material source, string sourcePath, Material release)
        {
            int replaced = 0;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; ++i)
            {
                Material[] materials = renderers[i].sharedMaterials;
                bool changed = false;
                for (int j = 0; j < materials.Length; ++j)
                {
                    Material current = materials[j];
                    if (current == null)
                    {
                        continue;
                    }

                    if ((current != source) && (AssetDatabase.GetAssetPath(current) != sourcePath))
                    {
                        continue;
                    }

                    materials[j] = release;
                    changed = true;
                    ++replaced;
                }

                if (changed)
                {
                    renderers[i].sharedMaterials = materials;
                }
            }

            return replaced;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            int split = folder.LastIndexOf('/');
            if (split <= 0)
            {
                throw new InvalidOperationException("폴더를 만들 수 없다: " + folder);
            }

            string parent = folder.Substring(0, split);
            EnsureFolder(parent);

            string created = AssetDatabase.CreateFolder(parent, folder.Substring(split + 1));
            if (string.IsNullOrEmpty(created))
            {
                throw new InvalidOperationException("폴더 생성에 실패했다: " + folder);
            }
        }
    }
}
