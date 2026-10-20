// System
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Art;
using Shinshi.Systems;

namespace Shinshi.Editor
{
    // Release 던전 맵(무철/월백/숲)을 만든다. 기준 틀은 Release 숲 씬(UI, 카메라, 입력, 네트워크)이고, 환경은 Dev_HUD 원본 씬에서 복사한다
    // Dev 원본 씬은 미리보기 씬으로만 읽고 저장하지 않는다. 씬, 폴더, 내비게이션 에셋은 지우지 않고 제자리에서 갱신해 GUID를 지킨다
    // 실행: 메뉴 Shinshi/Release/Build Dungeon Maps, 또는 -executeMethod Shinshi.Editor.ReleaseDungeonMapBuilder.BuildFromCommandLine
    // TODO(기웅): 구역 사이 결계(Barrier_*)는 옮기지 않는다. 해제 방식이 확정되면 따로 옮긴다
    public static class ReleaseDungeonMapBuilder
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string MENU_PATH = "Shinshi/Release/Build Dungeon Maps (Mucheol, Wolbaek, Forest)";

        private const string FOREST_PATH  = "Assets/00.Scenes/Release/DungeonForest.unity";
        private const string MUCHEOL_PATH = "Assets/00.Scenes/Release/DungeonMucheol.unity";
        private const string WOLBAEK_PATH = "Assets/00.Scenes/Release/DungeonWolbaek.unity";

        private const string DEV_FOREST_PATH  = "Assets/00.Scenes/Dev_HUD/Dev_ForestPlateauHitFeedback.unity";
        private const string DEV_MUCHEOL_PATH = "Assets/00.Scenes/Dev_HUD/Dev_BossMucheol.unity";
        private const string DEV_WOLBAEK_PATH = "Assets/00.Scenes/Dev_HUD/Dev_BossWolbaek.unity";

        private const string PORT_ROOT_NAME = "10_DevForestPort";
        private const string GAMEPLAY_ROOT  = "90_Gameplay";
        private const string START_ROOT     = "SpawnPoints";
        private const string LAYOUT_ROOT    = "SpawnLayout";

        private const string MUCHEOL_ARENA_ROOT   = "PF_MucheolArena";
        private const string MUCHEOL_PILLAR_ROOT  = "MucheolPillars";
        private const string MUCHEOL_SET_ROOT     = "MucheolChargeStructures";
        private const string MUCHEOL_PILLAR_BLOCK = "Collider_Pillar";
        private const string WOLBAEK_MAP_ROOT     = "BossArenaMap";

        private const int   MUCHEOL_PILLAR_COUNT = 3;
        private const float MUCHEOL_START_RADIUS = 9f;   // m, 기둥(반경 13.5m, 90/210/330도)과 겹치지 않는 45도 어긋난 고리
        private const float WOLBAEK_START_RADIUS = 12f;  // m, 반경 22m 마당 안쪽
        private const float FOREST_START_SPREAD  = 1.5f; // m, Dev 플레이어 위치 주변 간격

        private const float NAV_MARGIN       = 4f;
        private const float GROUND_PROBE_UP  = 3f;  // m, 기준 높이 위에서 아래로 쏘기 시작하는 높이
        private const float GROUND_PROBE_LEN = 10f; // m
        private const float START_LIFT       = 0.1f;
        private const int   NAV_VOLUME_MODE  = 1;   // NavMeshSurface.CollectObjects.Volume
        private const float NAV_START_RANGE  = 2f;  // m

        private static readonly Regex G_TerrainRoot = new Regex(@"^0[1-7]_", RegexOptions.Compiled);
        private static readonly Regex G_Door  = new Regex(@"^Zone(\d+)_DoorSpawn$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex G_Next  = new Regex(@"^Zone(\d+)_NextWave$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex G_Node  = new Regex(@"^Zone(\d+)_Spawn_(\d+)_(inner|outer)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // 원본 전장에서 가져오지 않는 루트 이름 앞부분(개발 입력, 가짜 보스, 미니맵 대상, 객체 팔레트)
        // 공용 UI/입력/커서/미리보기 무대/구역 결계/카메라는 틀 씬이 이미 가지고 있어 복사하면 바인더와 라우터가 중복된다
        private static readonly string[] G_SkipPrefixes =
        {
            "Dev", "MinimapDev", "MinimapCanvas", "95_", "Player", "Barrier_", "ZoneBarriers",
            "Ui", "CharDetail", "EventSystem", "Overview", "QuarterView", "HUD", "Popup",
        };

        private static readonly string[] G_UiTypeNames = { "HotkeyRouter", "LocalPlayerCameraBinder", "SortieResultBinder", "PartyHudBinder", "ReleaseMenuActions" };

        private const string DEV_PLAYER_ROOT = "DevHitFeedbackPlayer";

        /****************************************
        *               Entry Points
        ****************************************/

        [MenuItem(MENU_PATH)]
        public static void BuildAll()
        {
            Run();
        }

        public static void BuildFromCommandLine()
        {
            try
            {
                Run();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void RepairForestTransitionsFromCommandLine()
        {
            try
            {
                Scene scene = EditorSceneManager.OpenScene(FOREST_PATH, OpenSceneMode.Single);
                DungeonSpawnLayout layout = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    DungeonSpawnLayout found = root.GetComponentInChildren<DungeonSpawnLayout>(true);
                    if (found == null)
                    {
                        continue;
                    }
                    if (layout != null)
                    {
                        throw new InvalidOperationException("숲 출현 배치가 둘 이상이다");
                    }
                    layout = found;
                }
                if (layout == null)
                {
                    throw new InvalidOperationException("숲 출현 배치가 없다");
                }

                var markers = new Dictionary<int, Transform>();
                foreach (Transform marker in layout.GetComponentsInChildren<Transform>(true))
                {
                    Match match = G_Next.Match(marker.name);
                    if (match.Success)
                    {
                        markers.Add(ParseInt(match.Groups[1].Value), marker);
                    }
                }
                var data = new SerializedObject(layout);
                SerializedProperty zones = data.FindProperty("_zones");
                for (int i = 0; i < zones.arraySize; ++i)
                {
                    Transform next = null;
                    if ((i < (zones.arraySize - 1)) && (!markers.TryGetValue(i + 2, out next)))
                    {
                        throw new InvalidOperationException($"Zone{i + 2:00}_NextWave 표식이 없다");
                    }
                    zones.GetArrayElementAtIndex(i).FindPropertyRelative("nextWave").objectReferenceValue = next;
                }
                data.ApplyModifiedPropertiesWithoutUndo();
                if (!layout.TryValidate(out string reason))
                {
                    throw new InvalidOperationException(reason);
                }
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                {
                    throw new InvalidOperationException("숲 씬을 저장하지 못했다");
                }
                Debug.Log($"[ReleaseMapBuilder] 숲 {zones.arraySize - 1}개 구역 이동 연결 수정");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("플레이 모드에서는 맵을 만들 수 없다");
            }

            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    throw new InvalidOperationException("저장하지 않은 열린 씬이 있다. 저장하거나 닫은 뒤 다시 실행한다: " + SceneManager.GetSceneAt(i).path);
                }
            }

            foreach (string path in new[] { FOREST_PATH, DEV_FOREST_PATH, DEV_MUCHEOL_PATH, DEV_WOLBAEK_PATH })
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                {
                    throw new InvalidOperationException("필요한 씬이 없다: " + path);
                }
            }

            SceneSetup[] initialSetup = EditorSceneManager.GetSceneManagerSetup();
            var report = new StringBuilder("[ReleaseMapBuilder] 결과\n");

            try
            {
                BuildMucheol(report);
                BuildWolbaek(report);
                PortForest(report);
                RegisterBuildScenes(report);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log(report.ToString());
            }
            finally
            {
                if (initialSetup.Length > 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(initialSetup);
                }
                else
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                }
            }
        }

        /************* 무철 *************/

        private static void BuildMucheol(StringBuilder report)
        {
            Scene scene = OpenBossScene(MUCHEOL_PATH);
            Scene dev = EditorSceneManager.OpenPreviewScene(DEV_MUCHEOL_PATH);
            try
            {
                List<GameObject> clones = CloneEnvironment(dev, scene, out string skipped);

                ConfigureMucheolPillars(scene, report);
                CopyRenderSettings(DEV_MUCHEOL_PATH, scene);

                Vector3 origin = RequireGround(Vector3.zero, "무철 전장 원점");
                FinishBossScene(scene, MUCHEOL_PATH, RingPositions(MUCHEOL_START_RADIUS), Vector3.zero, clones, report);

                report.AppendLine($"- {MUCHEOL_PATH}: 환경 루트 {clones.Count}개 복사, 건너뜀 [{skipped}], 원점 바닥 y={origin.y:F2}");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(dev);
            }
        }

        private static void ConfigureMucheolPillars(Scene scene, StringBuilder report)
        {
            Transform pillars = FindRoot(scene, MUCHEOL_PILLAR_ROOT);
            if (pillars == null)
            {
                throw new InvalidOperationException("무철 기둥 루트를 복사하지 못했다: " + MUCHEOL_PILLAR_ROOT);
            }

            if (pillars.childCount != MUCHEOL_PILLAR_COUNT)
            {
                throw new InvalidOperationException($"무철 기둥이 {MUCHEOL_PILLAR_COUNT}개여야 한다: {pillars.childCount}개");
            }

            Physics.SyncTransforms();
            var markers = new List<BossChargeStructureMarker>(MUCHEOL_PILLAR_COUNT);
            for (int i = 0; i < MUCHEOL_PILLAR_COUNT; ++i)
            {
                Transform pillar = pillars.Find("Pillar_" + i);
                if (pillar == null)
                {
                    throw new InvalidOperationException("무철 기둥을 찾지 못했다: Pillar_" + i);
                }

                MucheolPillarShatter shatter = pillar.GetComponentInChildren<MucheolPillarShatter>(true);
                Collider blocker = FindCollider(pillar, MUCHEOL_PILLAR_BLOCK);
                if ((shatter == null) || (blocker == null))
                {
                    throw new InvalidOperationException($"{pillar.name}에 파편 연출이나 {MUCHEOL_PILLAR_BLOCK}이 없다");
                }

                BossChargeStructureMarker marker = pillar.GetComponent<BossChargeStructureMarker>();
                if (marker == null)
                {
                    marker = pillar.gameObject.AddComponent<BossChargeStructureMarker>();
                }

                var markerData = new SerializedObject(marker);
                markerData.FindProperty("_structureId").intValue = i + 1;
                markerData.FindProperty("_radius").floatValue = ColliderRadius(blocker);
                markerData.FindProperty("_isGimmickTarget").boolValue = true;
                markerData.FindProperty("_playerBlocker").objectReferenceValue = blocker;
                markerData.FindProperty("_shatter").objectReferenceValue = shatter;
                markerData.ApplyModifiedPropertiesWithoutUndo();

                // 판정이 있는 실제 기둥은 지우면 복원과 늦은 접속을 되살릴 수 없어 끄기만 한다
                var shatterData = new SerializedObject(shatter);
                shatterData.FindProperty("_removeWhenVanished").boolValue = false;
                shatterData.ApplyModifiedPropertiesWithoutUndo();

                markers.Add(marker);
            }

            RemoveRootsNamed(scene, MUCHEOL_SET_ROOT);
            var setObject = new GameObject(MUCHEOL_SET_ROOT);
            SceneManager.MoveGameObjectToScene(setObject, scene);
            BossChargeStructureSet set = setObject.AddComponent<BossChargeStructureSet>();
            var setData = new SerializedObject(set);
            SerializedProperty array = setData.FindProperty("_markers");
            array.arraySize = markers.Count;
            for (int i = 0; i < markers.Count; ++i)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = markers[i];
            }
            setData.ApplyModifiedPropertiesWithoutUndo();

            var infos = new List<BossChargeStructureInfo>(markers.Count);
            if (!set.TryCollect(infos, out string reason))
            {
                throw new InvalidOperationException("무철 구조물 표식 검증에 실패했다: " + reason);
            }

            report.AppendLine($"- 무철 구조물 표식 {infos.Count}개 연결(기둥 파편 연출, 플레이어 차단 콜라이더, 지정 대상)");
        }

        private static Collider FindCollider(Transform root, string name)
        {
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (collider.name == name)
                {
                    return collider;
                }
            }

            return null;
        }

        // 실제 콜라이더 반경(축척 반영). 캡슐과 구가 아니면 바운드의 수평 반폭을 쓴다
        private static float ColliderRadius(Collider collider)
        {
            Vector3 scale = collider.transform.lossyScale;
            float horizontal = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            if (collider is CapsuleCollider capsule)
            {
                return capsule.radius * horizontal;
            }

            if (collider is SphereCollider sphere)
            {
                return sphere.radius * Mathf.Max(horizontal, Mathf.Abs(scale.y));
            }

            Bounds bounds = collider.bounds;
            return Mathf.Max(bounds.extents.x, bounds.extents.z);
        }

        /************* 월백 *************/

        private static void BuildWolbaek(StringBuilder report)
        {
            Scene scene = OpenBossScene(WOLBAEK_PATH);
            Scene dev = EditorSceneManager.OpenPreviewScene(DEV_WOLBAEK_PATH);
            try
            {
                List<GameObject> clones = CloneEnvironment(dev, scene, out string skipped);

                GameObject map = FindClones(clones, WOLBAEK_MAP_ROOT);
                if (map == null)
                {
                    throw new InvalidOperationException("월백 환경 루트를 복사하지 못했다: " + WOLBAEK_MAP_ROOT);
                }

                // 끊어진 벽 프리팹과 부모가 끊긴 경계선 재질을 현재 에셋으로 고친다. 내비게이션을 굽고 저장하기 전에 한다
                string folder = Path.ChangeExtension(WOLBAEK_PATH, null);
                EnsureFolder(folder);
                string repair = ReleaseWolbaekAssetRepair.Repair(map, folder);

                CopyRenderSettings(DEV_WOLBAEK_PATH, scene);

                Vector3 origin = RequireGround(Vector3.zero, "월백 전장 원점");
                FinishBossScene(scene, WOLBAEK_PATH, RingPositions(WOLBAEK_START_RADIUS), Vector3.zero, clones, report);

                report.AppendLine($"- {WOLBAEK_PATH}: 환경 루트 {clones.Count}개 복사, 건너뜀 [{skipped}], 원점 바닥 y={origin.y:F2}");
                report.AppendLine("  자산 복구: " + repair);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(dev);
            }
        }

        /************* 보스 맵 공통 *************/

        // 있는 씬은 그대로 열어 GUID를 지키고, 없을 때만 숲 틀을 열어 새 경로에 처음 저장한다
        private static Scene OpenBossScene(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null)
            {
                return EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            }

            Scene scene = EditorSceneManager.OpenScene(FOREST_PATH, OpenSceneMode.Single);
            RemoveTerrainRoots(scene);
            Component surface = FindSurface(scene);
            if (surface != null)
            {
                SetSurfaceData(surface, null);
            }

            if (!EditorSceneManager.SaveScene(scene, path))
            {
                throw new InvalidOperationException("새 보스 씬 저장에 실패했다: " + path);
            }

            return scene;
        }

        private static List<GameObject> CloneEnvironment(Scene dev, Scene target, out string skipped)
        {
            RemoveTerrainRoots(target);
            Transform gameplay = FindRoot(target, GAMEPLAY_ROOT);
            if (gameplay != null)
            {
                RemoveGameplayLeftovers(gameplay);
            }

            var sources = new List<GameObject>();
            var skippedNames = new List<string>();
            foreach (GameObject root in dev.GetRootGameObjects())
            {
                if (ShouldSkipDevRoot(root))
                {
                    skippedNames.Add(root.name);
                }
                else
                {
                    sources.Add(root);
                }
            }

            skipped = string.Join(", ", skippedNames);

            if (sources.Count == 0)
            {
                throw new InvalidOperationException("복사할 환경 루트가 없다: " + dev.path);
            }

            RemoveTemplateEnvironment(target, sources);

            var clones = new List<GameObject>(sources.Count);
            foreach (GameObject source in sources)
            {
                RemoveRootsNamed(target, source.name);
                GameObject clone = UnityEngine.Object.Instantiate(source);
                clone.name = source.name;
                SceneManager.MoveGameObjectToScene(clone, target);
                StripClone(clone);
                clones.Add(clone);
            }

            return clones;
        }

        private static void FinishBossScene(Scene scene, string scenePath, List<Vector3> startPositions, Vector3 lookAt,
            List<GameObject> navGroups, StringBuilder report)
        {
            Transform gameplay = FindRoot(scene, GAMEPLAY_ROOT);
            if (gameplay == null)
            {
                gameplay = new GameObject(GAMEPLAY_ROOT).transform;
                SceneManager.MoveGameObjectToScene(gameplay.gameObject, scene);
            }

            Physics.SyncTransforms();
            var grounded = new List<Vector3>(startPositions.Count);
            for (int i = 0; i < startPositions.Count; ++i)
            {
                grounded.Add(RequireGround(startPositions[i], $"시작점 {i + 1}"));
            }

            ResetStartPositions(EnsureStartRoot(scene, gameplay), grounded, lookAt);

            Component surface = FindSurface(scene);
            if (surface == null)
            {
                throw new InvalidOperationException("내비게이션 표면이 없다: " + scenePath);
            }

            FitSurfaceToColliders(surface, navGroups);
            string navPath = ResolveNavPath(surface, Path.ChangeExtension(scenePath, null));
            BakeInPlace(surface, navPath);
            VerifyNavStarts(surface, grounded, scenePath);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                throw new InvalidOperationException("씬 저장에 실패했다: " + scenePath);
            }

            report.AppendLine($"  시작점 {grounded.Count}개, NavMesh {navPath}");
        }

        private static List<Vector3> RingPositions(float radius)
        {
            var positions = new List<Vector3>(4);
            for (int i = 0; i < 4; ++i)
            {
                float angle = (45f + (90f * i)) * Mathf.Deg2Rad;
                positions.Add(new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }

            return positions;
        }

        private static GameObject FindClones(List<GameObject> clones, string name)
        {
            foreach (GameObject clone in clones)
            {
                if (clone.name == name)
                {
                    return clone;
                }
            }

            return null;
        }

        // 기준 높이 근처에서 아래로 쏘아 실제 바닥을 찾는다. 찾지 못하면 허공에 세운 것이므로 멈춘다
        private static Vector3 RequireGround(Vector3 position, string label)
        {
            Physics.SyncTransforms();
            var origin = new Vector3(position.x, position.y + GROUND_PROBE_UP, position.z);
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, GROUND_PROBE_LEN, ~0, QueryTriggerInteraction.Ignore))
            {
                throw new InvalidOperationException($"{label} 아래에 바닥 콜라이더가 없다: {position}");
            }

            return hit.point + (Vector3.up * START_LIFT);
        }

        // 구운 데이터를 직접 등록해 시작점마다 2m 안에 걷는 면이 있는지 확인한다. 하나라도 없으면 운영 맵으로 쓸 수 없어 멈춘다
        private static void VerifyNavStarts(Component surface, List<Vector3> points, string label)
        {
            var data = new SerializedObject(surface).FindProperty("m_NavMeshData").objectReferenceValue as NavMeshData;
            if (data == null)
            {
                throw new InvalidOperationException("NavMesh 데이터가 없다: " + label);
            }

            NavMeshDataInstance instance = NavMesh.AddNavMeshData(data);
            try
            {
                var missed = new List<string>();
                for (int i = 0; i < points.Count; ++i)
                {
                    if (!NavMesh.SamplePosition(points[i], out NavMeshHit _, NAV_START_RANGE, NavMesh.AllAreas))
                    {
                        missed.Add($"{i + 1}번 {points[i]}");
                    }
                }

                if (missed.Count > 0)
                {
                    throw new InvalidOperationException($"시작점 {missed.Count}개가 NavMesh {NAV_START_RANGE}m 안에 없다({label}): {string.Join(", ", missed)}");
                }
            }
            finally
            {
                NavMesh.RemoveNavMeshData(instance);
            }
        }

        /************* 숲 포팅 *************/

        private static void PortForest(StringBuilder report)
        {
            Scene scene = EditorSceneManager.OpenScene(FOREST_PATH, OpenSceneMode.Single);
            int removed = RemoveTerrainRoots(scene);
            Transform gameplay = FindRoot(scene, GAMEPLAY_ROOT);
            int cleaned = (gameplay == null) ? 0 : RemoveGameplayLeftovers(gameplay);
            if (gameplay == null)
            {
                gameplay = new GameObject(GAMEPLAY_ROOT).transform;
                SceneManager.MoveGameObjectToScene(gameplay.gameObject, scene);
            }

            Scene dev = EditorSceneManager.OpenPreviewScene(DEV_FOREST_PATH);
            try
            {
                var container = new GameObject(PORT_ROOT_NAME);
                SceneManager.MoveGameObjectToScene(container, scene);

                var sources = new List<GameObject>();
                var skipped = new List<string>();
                foreach (GameObject root in dev.GetRootGameObjects())
                {
                    if (ShouldSkipDevRoot(root))
                    {
                        skipped.Add(root.name);
                    }
                    else
                    {
                        sources.Add(root);
                    }
                }

                RemoveTemplateEnvironment(scene, sources);

                foreach (GameObject root in sources)
                {
                    GameObject clone = UnityEngine.Object.Instantiate(root, container.transform);
                    clone.name = root.name;
                    StripClone(clone);
                }

                Vector3 playerStart = FindPlayerStart(dev);
                BuildLayout(dev, gameplay, report);
                Physics.SyncTransforms();
                List<Vector3> forestStarts = ResetForestStarts(scene, gameplay, playerStart, report);
                DumpSpawnerSettings(dev, report);

                CopyRenderSettings(DEV_FOREST_PATH, scene);

                Component surface = FindSurface(scene);
                if (surface == null)
                {
                    throw new InvalidOperationException("숲 내비게이션 표면이 없다");
                }

                FitSurfaceToColliders(surface, new List<GameObject> { container });
                string navPath = ResolveNavPath(surface, Path.ChangeExtension(FOREST_PATH, null));
                BakeInPlace(surface, navPath);
                VerifyNavStarts(surface, forestStarts, FOREST_PATH);

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                {
                    throw new InvalidOperationException("숲 씬 저장에 실패했다");
                }

                report.AppendLine($"- {FOREST_PATH}: 옛 지형 루트 {removed}개, 옛 배치 {cleaned}개 제거, Dev 루트 {sources.Count}개 이식, 건너뜀 [{string.Join(", ", skipped)}], NavMesh {navPath}");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(dev);
            }
        }

        private static Vector3 FindPlayerStart(Scene dev)
        {
            foreach (GameObject root in dev.GetRootGameObjects())
            {
                if (root.name == DEV_PLAYER_ROOT)
                {
                    return root.transform.position;
                }
            }

            foreach (GameObject root in dev.GetRootGameObjects())
            {
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (transform.name == DEV_PLAYER_ROOT)
                    {
                        return transform.position;
                    }
                }
            }

            throw new InvalidOperationException("Dev 숲에서 플레이어 루트를 찾지 못했다: " + DEV_PLAYER_ROOT);
        }

        private static void BuildLayout(Scene dev, Transform gameplay, StringBuilder report)
        {
            var doors = new SortedDictionary<int, Transform>();
            var nexts = new SortedDictionary<int, Transform>();
            var inner = new SortedDictionary<int, SortedDictionary<int, Transform>>();
            var outer = new SortedDictionary<int, SortedDictionary<int, Transform>>();

            foreach (GameObject root in dev.GetRootGameObjects())
            {
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                {
                    Match match = G_Door.Match(transform.name);
                    if (match.Success)
                    {
                        doors[ParseInt(match.Groups[1].Value)] = transform;
                        continue;
                    }

                    match = G_Next.Match(transform.name);
                    if (match.Success)
                    {
                        nexts[ParseInt(match.Groups[1].Value)] = transform;
                        continue;
                    }

                    match = G_Node.Match(transform.name);
                    if (match.Success)
                    {
                        int zone = ParseInt(match.Groups[1].Value);
                        var target = (match.Groups[3].Value.ToLowerInvariant() == "inner") ? inner : outer;
                        if (!target.TryGetValue(zone, out SortedDictionary<int, Transform> nodes))
                        {
                            nodes = new SortedDictionary<int, Transform>();
                            target[zone] = nodes;
                        }
                        nodes[ParseInt(match.Groups[2].Value)] = transform;
                    }
                }
            }

            if (doors.Count == 0)
            {
                throw new InvalidOperationException("Dev 숲에 ZoneNN_DoorSpawn 표식이 없다");
            }

            RemoveChildrenNamed(gameplay, LAYOUT_ROOT);
            var layoutObject = new GameObject(LAYOUT_ROOT);
            layoutObject.transform.SetParent(gameplay, false);
            DungeonSpawnLayout layout = layoutObject.AddComponent<DungeonSpawnLayout>();

            var serialized = new SerializedObject(layout);
            SerializedProperty zones = serialized.FindProperty("_zones");
            zones.arraySize = doors.Count;
            int index = 0;
            foreach (KeyValuePair<int, Transform> door in doors)
            {
                int zone = door.Key;
                string prefix = $"Zone{zone:00}";
                var zoneRoot = new GameObject(prefix).transform;
                zoneRoot.SetParent(layoutObject.transform, false);

                SerializedProperty element = zones.GetArrayElementAtIndex(index++);
                element.FindPropertyRelative("door").objectReferenceValue = Mark(zoneRoot, prefix + "_DoorSpawn", door.Value);
                // Dev 표식 번호는 출발 구역이 아니라 도착 구역이다. Zone02는 첫 웨이브를 마친 뒤 향할 지점이다
                element.FindPropertyRelative("nextWave").objectReferenceValue = nexts.TryGetValue(zone + 1, out Transform next)
                    ? Mark(zoneRoot, $"Zone{zone + 1:00}_NextWave", next)
                    : null;
                WriteNodes(element.FindPropertyRelative("innerNodes"), zoneRoot, prefix, "inner", inner, zone);
                WriteNodes(element.FindPropertyRelative("outerNodes"), zoneRoot, prefix, "outer", outer, zone);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            if (!layout.TryValidate(out string reason))
            {
                throw new InvalidOperationException("출현 배치 검증에 실패했다: " + reason);
            }

            report.AppendLine($"- 출현 배치: 구역 {doors.Count}개, 다음 지점 {nexts.Count}개");
        }

        private static void WriteNodes(SerializedProperty array, Transform zoneRoot, string prefix, string kind,
            SortedDictionary<int, SortedDictionary<int, Transform>> source, int zone)
        {
            if (!source.TryGetValue(zone, out SortedDictionary<int, Transform> nodes))
            {
                array.arraySize = 0;
                return;
            }

            array.arraySize = nodes.Count;
            int i = 0;
            foreach (KeyValuePair<int, Transform> node in nodes)
            {
                array.GetArrayElementAtIndex(i++).objectReferenceValue =
                    Mark(zoneRoot, $"{prefix}_Spawn_{node.Key}_{kind}", node.Value);
            }
        }

        private static Transform Mark(Transform parent, string name, Transform source)
        {
            var marker = new GameObject(name).transform;
            marker.SetParent(parent, false);
            marker.SetPositionAndRotation(source.position, source.rotation);
            return marker;
        }

        private static List<Vector3> ResetForestStarts(Scene scene, Transform gameplay, Vector3 playerStart, StringBuilder report)
        {
            Transform starts = EnsureStartRoot(scene, gameplay);
            var offsets = new[]
            {
                new Vector3(-FOREST_START_SPREAD, 0f, -FOREST_START_SPREAD),
                new Vector3(FOREST_START_SPREAD, 0f, -FOREST_START_SPREAD),
                new Vector3(-FOREST_START_SPREAD, 0f, FOREST_START_SPREAD),
                new Vector3(FOREST_START_SPREAD, 0f, FOREST_START_SPREAD),
            };

            var positions = new List<Vector3>(offsets.Length);
            for (int i = 0; i < offsets.Length; ++i)
            {
                positions.Add(RequireGround(playerStart + offsets[i], $"숲 시작점 {i + 1}"));
            }

            ResetStartPositions(starts, positions, playerStart + (Vector3.forward * 10f));
            report.AppendLine($"- 숲 시작점 4개: Dev 플레이어 위치 {playerStart} 주변, 실제 바닥에 맞춤");
            return positions;
        }

        private static void DumpSpawnerSettings(Scene dev, StringBuilder report)
        {
            foreach (GameObject root in dev.GetRootGameObjects())
            {
                foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if ((behaviour == null) || (!behaviour.GetType().Name.Contains("Spawner")))
                    {
                        continue;
                    }

                    report.AppendLine($"- Dev 스포너 {behaviour.GetType().Name} 설정(DungeonSpawnLayout 기본값과 대조):");
                    SerializedProperty property = new SerializedObject(behaviour).GetIterator();
                    bool enter = true;
                    while (property.NextVisible(enter))
                    {
                        enter = false;
                        if ((property.name == "m_Script") || (property.propertyType == SerializedPropertyType.ObjectReference))
                        {
                            continue;
                        }

                        report.AppendLine($"    {property.propertyPath} = {ValueText(property)}");
                    }
                }
            }
        }

        private static string ValueText(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer: return property.intValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Boolean: return property.boolValue.ToString();
                case SerializedPropertyType.Float: return property.floatValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.String: return property.stringValue;
                case SerializedPropertyType.Enum: return property.enumDisplayNames[property.enumValueIndex];
                case SerializedPropertyType.Vector3: return property.vector3Value.ToString();
                default: return property.propertyType.ToString();
            }
        }

        /************* 환경 복사 도우미 *************/

        private static bool ShouldSkipDevRoot(GameObject root)
        {
            string name = root.name;
            if (name == "Main Camera")
            {
                return true;
            }

            foreach (string prefix in G_SkipPrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if ((root.GetComponentInChildren<Camera>(true) != null)
                || (root.GetComponentInChildren<Canvas>(true) != null)
                || (root.GetComponentInChildren<UnityEngine.EventSystems.EventSystem>(true) != null)
                || (root.GetComponentInChildren<NetworkIdentity>(true) != null)
                || (root.GetComponentInChildren<AudioListener>(true) != null))
            {
                return true;
            }

            // 캔버스가 아닌 루트에 붙은 UI 바인더, 단축키, 카메라 바인더도 공용 틀과 중복된다
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null)
                {
                    continue;
                }

                Type type = behaviour.GetType();
                if (((type.Namespace != null) && type.Namespace.StartsWith("Shinshi.UI", StringComparison.Ordinal))
                    || (Array.IndexOf(G_UiTypeNames, type.Name) >= 0))
                {
                    return true;
                }
            }

            return false;
        }

        // 원본 환경이 조명이나 볼륨을 가지면 틀 씬의 같은 종류를 깊이에 상관없이 걷어 낸다. 틀 환경 조명이 원본 위에 겹쳐 밝아지는 것을 막는다
        private static void RemoveTemplateEnvironment(Scene scene, List<GameObject> sources)
        {
            bool sourceHasLight = false;
            bool sourceHasVolume = false;
            foreach (GameObject source in sources)
            {
                sourceHasLight |= source.GetComponentInChildren<Light>(true) != null;
                foreach (MonoBehaviour behaviour in source.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    sourceHasVolume |= (behaviour != null) && (behaviour.GetType().Name == "Volume");
                }
            }

            var targets = new List<GameObject>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (IsProtectedTemplateObject(transform))
                    {
                        continue;
                    }

                    if ((sourceHasLight && (transform.GetComponent<Light>() != null))
                        || (sourceHasVolume && HasComponentNamed(transform.gameObject, "Volume")))
                    {
                        targets.Add(transform.gameObject);
                    }
                }
            }

            foreach (GameObject target in targets)
            {
                if (target == null)
                {
                    continue;
                }

                if (IsLightingOnly(target))
                {
                    UnityEngine.Object.DestroyImmediate(target);
                    continue;
                }

                // 카메라나 자식이 있는 오브젝트는 지키고 조명 컴포넌트만 걷어 낸다
                foreach (MonoBehaviour behaviour in target.GetComponents<MonoBehaviour>())
                {
                    if ((behaviour != null) && IsLightingBehaviour(behaviour.GetType().Name)
                        && (behaviour.GetType().Name != "UniversalAdditionalLightData"))
                    {
                        UnityEngine.Object.DestroyImmediate(behaviour);
                    }
                }

                foreach (MonoBehaviour behaviour in target.GetComponents<MonoBehaviour>())
                {
                    if ((behaviour != null) && (behaviour.GetType().Name == "UniversalAdditionalLightData"))
                    {
                        UnityEngine.Object.DestroyImmediate(behaviour);
                    }
                }

                Light light = target.GetComponent<Light>();
                if (light != null)
                {
                    UnityEngine.Object.DestroyImmediate(light);
                }
            }
        }

        // 캐릭터 상세 미리보기 무대와 UI 캔버스 아래 조명(PreviewRim 등)은 환경 조명이 아니다
        private static bool IsProtectedTemplateObject(Transform transform)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                if ((current.name == "CharDetailPreviewStage") || (current.GetComponent<Canvas>() != null))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasComponentNamed(GameObject target, string typeName)
        {
            foreach (MonoBehaviour behaviour in target.GetComponents<MonoBehaviour>())
            {
                if ((behaviour != null) && (behaviour.GetType().Name == typeName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsLightingBehaviour(string typeName)
        {
            return (typeName == "Volume") || (typeName == "UniversalAdditionalLightData");
        }

        // 변환, 조명, 볼륨, 조명 추가 데이터만 가진 잎 오브젝트다
        private static bool IsLightingOnly(GameObject target)
        {
            if (target.transform.childCount != 0)
            {
                return false;
            }

            foreach (Component component in target.GetComponents<Component>())
            {
                if ((component == null) || (component is Transform) || (component is Light))
                {
                    continue;
                }

                if (!IsLightingBehaviour(component.GetType().Name))
                {
                    return false;
                }
            }

            return true;
        }

        private static void RemoveRootsNamed(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        private static void RemoveChildrenNamed(Transform parent, string name)
        {
            for (int i = parent.childCount - 1; i >= 0; --i)
            {
                if (parent.GetChild(i).name == name)
                {
                    UnityEngine.Object.DestroyImmediate(parent.GetChild(i).gameObject);
                }
            }
        }

        private static void StripClone(GameObject clone)
        {
            var barriers = new List<GameObject>();
            foreach (Transform child in clone.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.StartsWith("Barrier_", StringComparison.OrdinalIgnoreCase))
                {
                    barriers.Add(child.gameObject);
                }
            }

            foreach (GameObject barrier in barriers)
            {
                if (barrier != null)
                {
                    UnityEngine.Object.DestroyImmediate(barrier);
                }
            }

            foreach (MonoBehaviour behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null)
                {
                    continue;
                }

                string assemblyName = behaviour.GetType().Assembly.GetName().Name;
                if ((assemblyName == "Shinshi.Dev") || (behaviour.GetType().Name == "NavMeshSurface"))
                {
                    UnityEngine.Object.DestroyImmediate(behaviour);
                }
            }
        }

        // 원본 조명 설정(환경광, 안개, 하늘, 반사)을 대상 씬 것으로 옮긴다. 원본 씬은 잠깐 겹쳐 열고 저장 없이 닫는다
        private static void CopyRenderSettings(string devPath, Scene target)
        {
            MethodInfo getter = typeof(RenderSettings).GetMethod("GetRenderSettings", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (getter == null)
            {
                throw new InvalidOperationException("RenderSettings 개체를 얻는 API를 찾지 못했다");
            }

            Scene dev = EditorSceneManager.OpenScene(devPath, OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(dev);
                var source = (UnityEngine.Object)getter.Invoke(null, null);
                SceneManager.SetActiveScene(target);
                var destination = (UnityEngine.Object)getter.Invoke(null, null);
                if ((source == null) || (destination == null) || (source == destination))
                {
                    throw new InvalidOperationException("RenderSettings를 옮길 수 없다: " + devPath);
                }

                EditorUtility.CopySerialized(source, destination);

                var data = new SerializedObject(destination);
                data.FindProperty("m_Sun").objectReferenceValue = FindDirectionalLight(target);
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            finally
            {
                SceneManager.SetActiveScene(target);
                EditorSceneManager.CloseScene(dev, true);
            }
        }

        private static Light FindDirectionalLight(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Light light in root.GetComponentsInChildren<Light>(true))
                {
                    if (light.type == LightType.Directional)
                    {
                        return light;
                    }
                }
            }

            return null;
        }

        /************* 씬 도우미 *************/

        private static int RemoveTerrainRoots(Scene scene)
        {
            int removed = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (G_TerrainRoot.IsMatch(root.name) || (root.name == PORT_ROOT_NAME))
                {
                    UnityEngine.Object.DestroyImmediate(root);
                    ++removed;
                }
            }

            return removed;
        }

        private static int RemoveGameplayLeftovers(Transform gameplay)
        {
            var targets = new List<GameObject>();
            foreach (Transform child in gameplay)
            {
                string name = child.name;
                bool isSpawn = name.IndexOf("Spawn", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isBarrier = name.IndexOf("Barrier", StringComparison.OrdinalIgnoreCase) >= 0;
                if ((isSpawn && (name != START_ROOT)) || isBarrier)
                {
                    targets.Add(child.gameObject);
                }
            }

            foreach (GameObject target in targets)
            {
                UnityEngine.Object.DestroyImmediate(target);
            }

            return targets.Count;
        }

        private static Transform FindRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                {
                    return root.transform;
                }
            }

            return null;
        }

        private static Transform EnsureStartRoot(Scene scene, Transform gameplay)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (transform.name == START_ROOT)
                    {
                        return transform;
                    }
                }
            }

            var created = new GameObject(START_ROOT).transform;
            if (gameplay != null)
            {
                created.SetParent(gameplay, false);
            }
            else
            {
                SceneManager.MoveGameObjectToScene(created.gameObject, scene);
            }

            return created;
        }

        private static void ResetStartPositions(Transform root, List<Vector3> positions, Vector3 lookAt)
        {
            for (int i = root.childCount - 1; i >= 0; --i)
            {
                UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject);
            }

            for (int i = 0; i < positions.Count; ++i)
            {
                var point = new GameObject($"Start_{i + 1:00}");
                point.transform.SetParent(root, false);
                point.transform.position = positions[i];
                Vector3 facing = lookAt - positions[i];
                facing.y = 0f;
                if (facing.sqrMagnitude > 0.0001f)
                {
                    point.transform.rotation = Quaternion.LookRotation(facing.normalized);
                }

                point.AddComponent<NetworkStartPosition>();
            }
        }

        /************* NavMesh (Unity.AI.Navigation은 리플렉션으로 다룬다) *************/

        private static Component FindSurface(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if ((behaviour != null) && (behaviour.GetType().Name == "NavMeshSurface"))
                    {
                        return behaviour;
                    }
                }
            }

            return null;
        }

        private static void SetSurfaceData(Component surface, UnityEngine.Object data)
        {
            PropertyInfo property = surface.GetType().GetProperty("navMeshData");
            if (property == null)
            {
                throw new InvalidOperationException("NavMeshSurface.navMeshData를 찾지 못했다");
            }

            property.SetValue(surface, data);
            EditorUtility.SetDirty(surface);
        }

        // 부피 모드로 바꾸고 이식한 콜라이더를 감싸는 실제 부피를 준다
        private static void FitSurfaceToColliders(Component surface, List<GameObject> groups)
        {
            bool found = false;
            var bounds = new Bounds();
            Physics.SyncTransforms();
            foreach (GameObject group in groups)
            {
                foreach (Collider collider in group.GetComponentsInChildren<Collider>(false))
                {
                    if ((!collider.enabled) || collider.isTrigger)
                    {
                        continue;
                    }

                    if (!found)
                    {
                        bounds = collider.bounds;
                        found = true;
                    }
                    else
                    {
                        bounds.Encapsulate(collider.bounds);
                    }
                }
            }

            if (!found)
            {
                throw new InvalidOperationException("이식한 지형에 콜라이더가 없어 NavMesh를 구울 수 없다");
            }

            var serialized = new SerializedObject(surface);
            serialized.FindProperty("m_CollectObjects").intValue = NAV_VOLUME_MODE;
            serialized.FindProperty("m_Center").vector3Value = surface.transform.InverseTransformPoint(bounds.center);
            serialized.FindProperty("m_Size").vector3Value = bounds.size + new Vector3(NAV_MARGIN * 2f, 10f, NAV_MARGIN * 2f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static string ResolveNavPath(Component surface, string folder)
        {
            UnityEngine.Object data = new SerializedObject(surface).FindProperty("m_NavMeshData").objectReferenceValue;
            string existing = (data == null) ? string.Empty : AssetDatabase.GetAssetPath(data);
            return string.IsNullOrEmpty(existing) ? (folder + "/NavMesh-Navigation.asset") : existing;
        }

        // 에셋 파일은 지우지 않는다. 새로 구운 데이터를 기존 에셋에 덮어써 GUID를 지키고, 없을 때만 새 에셋을 만든다
        private static void BakeInPlace(Component surface, string assetPath)
        {
            Type type = surface.GetType();
            PropertyInfo dataProperty = type.GetProperty("navMeshData");
            MethodInfo build = type.GetMethod("BuildNavMesh", Type.EmptyTypes);
            if ((dataProperty == null) || (build == null))
            {
                throw new InvalidOperationException("NavMeshSurface API를 찾지 못했다");
            }

            var existing = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            dataProperty.SetValue(surface, null);
            Physics.SyncTransforms();
            build.Invoke(surface, null);

            var baked = dataProperty.GetValue(surface) as UnityEngine.Object;
            if (baked == null)
            {
                throw new InvalidOperationException("NavMesh 굽기가 데이터를 만들지 못했다: " + assetPath);
            }

            if (existing == null)
            {
                EnsureFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/'));
                AssetDatabase.CreateAsset(baked, assetPath);
            }
            else if (existing != baked)
            {
                EditorUtility.CopySerialized(baked, existing);
                EditorUtility.SetDirty(existing);
                dataProperty.SetValue(surface, existing);
                UnityEngine.Object.DestroyImmediate(baked);
            }

            EditorUtility.SetDirty(surface);
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        /************* 빌드 설정 *************/

        private static void RegisterBuildScenes(StringBuilder report)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (string path in new[] { FOREST_PATH, MUCHEOL_PATH, WOLBAEK_PATH })
            {
                int existing = scenes.FindIndex(s => s.path == path);
                if (existing < 0)
                {
                    scenes.Add(new EditorBuildSettingsScene(path, true));
                }
                else if (!scenes[existing].enabled)
                {
                    scenes[existing] = new EditorBuildSettingsScene(path, true);
                }
            }

            EditorBuildSettings.scenes = scenes.ToArray();
            report.AppendLine("- EditorBuildSettings에 숲/무철/월백 씬 등록 확인");
        }

        private static int ParseInt(string text)
        {
            return int.Parse(text, CultureInfo.InvariantCulture);
        }
    }
}
