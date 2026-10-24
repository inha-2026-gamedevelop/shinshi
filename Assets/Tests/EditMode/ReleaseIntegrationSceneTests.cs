// System
using System;
using System.Collections.Generic;
using System.Linq;

// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Art;
using Shinshi.Systems;
using Shinshi.UI;

namespace Shinshi.Tests
{
    public sealed class ReleaseIntegrationSceneTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string TOWN_PATH = "Assets/00.Scenes/Release/ShinshiTown.unity";
        private const string DUNGEON_PATH = "Assets/00.Scenes/Release/DungeonForest.unity";
        private const string MUCHEOL_PATH = "Assets/00.Scenes/Release/DungeonMucheol.unity";
        private const string WOLBAEK_PATH = "Assets/00.Scenes/Release/DungeonWolbaek.unity";
        private const string BOSS_PATH = "Assets/02.Prefab/Network/NetBoss/PF_NetWolbaekBoss.prefab";

        /****************************************
        *                Tests
        ****************************************/

        [TestCase(TOWN_PATH)]
        [TestCase(DUNGEON_PATH)]
        [TestCase(MUCHEOL_PATH)]
        [TestCase(WOLBAEK_PATH)]
        public void SavedScene_Integration_HasOneInputAndCameraOwnerWithoutMissingScripts(string path)
        {
            Inspect(path, scene =>
            {
                Assert.That(All<EventSystem>(scene).Count, Is.EqualTo(1), "EventSystem 중복은 클릭 대상을 바꾼다");
                Assert.That(All<HotkeyRouter>(scene).Count, Is.EqualTo(1), "단축키를 중복 소비하면 안 된다");
                Assert.That(All<LocalPlayerCameraBinder>(scene).Count, Is.EqualTo(1));
                int mainCameras = 0;
                foreach (Camera camera in All<Camera>(scene))
                {
                    if (camera.CompareTag("MainCamera") && (camera.targetTexture == null))
                    {
                        ++mainCameras;
                        Assert.That(camera.enabled, Is.True);
                    }
                }
                Assert.That(mainCameras, Is.EqualTo(1), "미리보기 카메라가 지면 조준 카메라로 선택되면 안 된다");
                foreach (Transform transform in All<Transform>(scene))
                {
                    Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject),
                        Is.Zero, path + "/" + transform.name);
                    foreach (MonoBehaviour behaviour in transform.GetComponents<MonoBehaviour>())
                    {
                        if (behaviour != null)
                        {
                            Assert.That(behaviour.GetType().Assembly.GetName().Name, Is.Not.EqualTo("Shinshi.Dev"),
                                transform.name + ": 개발용 데모 컴포넌트가 남아 있다");
                        }
                    }
                }
            });
        }

        [TestCase(TOWN_PATH)]
        [TestCase(DUNGEON_PATH)]
        [TestCase(MUCHEOL_PATH)]
        [TestCase(WOLBAEK_PATH)]
        public void SavedScene_CommonUiReferences_StayInsideCopiedScene(string path)
        {
            Inspect(path, scene =>
            {
                RequireReferences(One<PartyHudBinder>(scene), "_view");
                RequireReferences(One<HudChatView>(scene), "_router", "_messages", "_input", "_channel");
                RequireReferences(One<SortieResultBinder>(scene), "_window", "_view", "_popups");
                RequireReferences(One<ReleaseMenuActions>(scene), "_router", "_settings", "_popups");
                RequireReferences(One<SettingsView>(scene), "_router");
                RequireReferences(One<HudPlayerStatusView>(scene), "_hpFill", "_hpText", "_experience");
                RequireReferences(One<NetworkFeedbackBinder>(scene), "_popups", "_skillReject");
            });
        }

        [TestCase(DUNGEON_PATH)]
        [TestCase(MUCHEOL_PATH)]
        [TestCase(WOLBAEK_PATH)]
        public void DungeonScene_CommonUi_HasNoDuplicatedBindersOrCopiedUiRoots(string path)
        {
            Inspect(path, scene =>
            {
                Assert.That(All<PartyHudBinder>(scene).Count, Is.EqualTo(1), "PartyHudBinder 중복");
                Assert.That(All<SortieResultBinder>(scene).Count, Is.EqualTo(1), "SortieResultBinder 중복");
                Assert.That(All<HudChatView>(scene).Count, Is.EqualTo(1), "HudChatView 중복");
                Assert.That(All<SettingsView>(scene).Count, Is.EqualTo(1), "SettingsView 중복");
                Assert.That(All<ReleaseMenuActions>(scene).Count, Is.EqualTo(1), "ReleaseMenuActions 중복");
                Assert.That(All<HotkeyRouter>(scene).Count, Is.EqualTo(1), "HotkeyRouter 중복");
                Assert.That(All<EventSystem>(scene).Count, Is.EqualTo(1), "EventSystem 중복");

                var uiRoots = new List<string>();
                foreach (Transform transform in All<Transform>(scene))
                {
                    if ((transform.name == "UiBinding") || (transform.name == "UiPopupHub") || (transform.name == "UiCursor"))
                    {
                        uiRoots.Add(transform.name);
                    }
                }
                Assert.That(uiRoots.Count, Is.LessThanOrEqualTo(3), "공용 UI 루트가 복사돼 겹쳤다: " + string.Join(", ", uiRoots));
                Assert.That(uiRoots.Distinct().Count(), Is.EqualTo(uiRoots.Count), "같은 이름의 공용 UI 루트가 둘 이상이다: " + string.Join(", ", uiRoots));
            });
        }

        [Test]
        public void Forest_Scene_HasNoDevUiContainerLeftovers()
        {
            Inspect(DUNGEON_PATH, scene =>
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.name != "10_DevForestPort")
                    {
                        continue;
                    }

                    foreach (Transform child in root.transform)
                    {
                        Assert.That(IsSkippedSourceRoot(child.gameObject), Is.False,
                            "숲 이식 컨테이너에 건너뛰어야 할 Dev 루트가 있다: " + child.name);
                    }
                    Assert.That(root.GetComponentsInChildren<Canvas>(true).Length, Is.Zero, "이식 컨테이너에 캔버스가 있다");
                }
            });
        }

        [TestCase(DUNGEON_PATH)]
        [TestCase(MUCHEOL_PATH)]
        [TestCase(WOLBAEK_PATH)]
        public void DungeonScene_Navigation_CoversEnvironmentAndStarts(string path)
        {
            Inspect(path, scene =>
            {
                MonoBehaviour surface = FindSurface(scene);
                Assert.That(surface, Is.Not.Null, "내비게이션 표면이 필요하다");
                string navPath = NavDataPath(surface);
                Assert.That(navPath, Does.StartWith(path.Replace(".unity", "/")), "Release 씬 전용 NavMesh 에셋이어야 한다: " + navPath);

                SerializedObject serialized = new SerializedObject(surface);
                var volume = new Bounds(
                    surface.transform.TransformPoint(serialized.FindProperty("m_Center").vector3Value),
                    serialized.FindProperty("m_Size").vector3Value);

                bool found = false;
                var ground = new Bounds();
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (IsSkippedSourceRoot(root) || (root.name == "90_Gameplay"))
                    {
                        continue;
                    }
                    foreach (Collider collider in root.GetComponentsInChildren<Collider>(false))
                    {
                        if ((!collider.enabled) || collider.isTrigger)
                        {
                            continue;
                        }
                        if (!found)
                        {
                            ground = collider.bounds;
                            found = true;
                        }
                        else
                        {
                            ground.Encapsulate(collider.bounds);
                        }
                    }
                }
                Assert.That(found, Is.True, "환경 콜라이더가 없다");
                Assert.That(volume.Contains(ground.min) && volume.Contains(ground.max), Is.True,
                    $"NavMesh 표면 {volume}이 환경 콜라이더 {ground}를 덮지 못한다");

                var starts = All<Mirror.NetworkStartPosition>(scene);
                Assert.That(starts.Count, Is.EqualTo(4));
                var data = AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>(navPath);
                Assert.That(data, Is.Not.Null, "NavMesh 데이터를 읽지 못했다: " + navPath);
                UnityEngine.AI.NavMeshDataInstance instance = UnityEngine.AI.NavMesh.AddNavMeshData(data);
                try
                {
                    foreach (Mirror.NetworkStartPosition start in starts)
                    {
                        Assert.That(UnityEngine.AI.NavMesh.SamplePosition(start.transform.position, out UnityEngine.AI.NavMeshHit _, 2f,
                            UnityEngine.AI.NavMesh.AllAreas), Is.True, start.name + " 시작점이 NavMesh 2m 안에 없다");
                    }
                }
                finally
                {
                    UnityEngine.AI.NavMesh.RemoveNavMeshData(instance);
                }
            });
        }

        [Test]
        public void Town_CopiedMap_HasGroundPatchesAndReleaseOwnedNavigation()
        {
            Inspect(TOWN_PATH, scene =>
            {
                bool hasGroundPatches = false;
                bool hasNavigation = false;
                foreach (Transform transform in All<Transform>(scene))
                {
                    hasGroundPatches |= transform.name == "16_GroundPatches";
                    foreach (MonoBehaviour component in transform.GetComponents<MonoBehaviour>())
                    {
                        if ((component == null) || (component.GetType().Name != "NavMeshSurface"))
                        {
                            continue;
                        }
                        SerializedProperty data = new SerializedObject(component).FindProperty("m_NavMeshData");
                        Assert.That(data, Is.Not.Null);
                        Assert.That(data.objectReferenceValue, Is.Not.Null);
                        Assert.That(AssetDatabase.GetAssetPath(data.objectReferenceValue),
                            Does.StartWith("Assets/00.Scenes/Release/ShinshiTown/"));
                        hasNavigation = true;
                    }
                }
                Assert.That(hasGroundPatches, Is.True, "최신 마을 지면 패치가 누락됐다");
                Assert.That(hasNavigation, Is.True, "개발씬 데이터와 독립된 Release 내비게이션이 필요하다");
            });
        }

        [Test]
        public void Dungeon_CombatAndReturnUi_HaveLiveReferences()
        {
            Inspect(DUNGEON_PATH, scene =>
            {
                ReleaseCombatHudBinder binder = One<ReleaseCombatHudBinder>(scene);
                Assert.That(binder.gameObject.activeInHierarchy, Is.True,
                    "숨기는 전투 뷰 아래에 바인더를 두면 다시 표시할 수 없다");
                RequireReferences(binder, "_bossView", "_bossPhaseLabel", "_bossRevivesLabel", "_bossPotionLabel",
                    "_resonanceView", "_potionView", "_waveView", "_reviveView");
                RequireReferences(One<CombatMeterHudBinder>(scene), "_router", "_hudView", "_resultView");
                RequireReferences(One<SortieResultBinder>(scene), "_window", "_view", "_popups");
                Assert.That(All<HudSkillSlotView>(scene).Count, Is.GreaterThanOrEqualTo(7),
                    "기술 슬롯과 물약을 모두 가져와야 한다");
            });
        }

        [Test]
        public void Mucheol_Scene_HasRealPillarsWiredToChargeStructureSet()
        {
            Inspect(MUCHEOL_PATH, scene =>
            {
                Assert.That(RootNames(scene), Does.Contain("PF_MucheolArena"));
                Assert.That(RootNames(scene), Does.Contain("MucheolPillars"));

                BossChargeStructureSet set = One<BossChargeStructureSet>(scene);
                var infos = new List<BossChargeStructureInfo>();
                Assert.That(set.TryCollect(infos, out string reason), Is.True, reason);
                Assert.That(infos.Count, Is.EqualTo(3), "기믹 대상 구조물은 3개여야 한다");

                var markers = All<BossChargeStructureMarker>(scene);
                Assert.That(markers.Count, Is.EqualTo(3));
                foreach (BossChargeStructureMarker marker in markers)
                {
                    Assert.That(marker.IsGimmickTarget, Is.True, marker.name);
                    Assert.That(marker.Radius, Is.GreaterThan(0.5f), marker.name);
                    Assert.That(set.TryGetMarker(marker.StructureId, out BossChargeStructureMarker found), Is.True);
                    Assert.That(found, Is.SameAs(marker));
                    RequireReferences(marker, "_playerBlocker", "_shatter");
                    Assert.That(marker.GetComponentInChildren<MucheolPillarShatter>(true), Is.Not.Null, marker.name);
                }

                AssertBossStarts(scene);
                AssertNoPlaceholderBoss(scene);
            });
        }

        [Test]
        public void Wolbaek_Scene_HasArenaShatterViewAndRepairedReferences()
        {
            Inspect(WOLBAEK_PATH, scene =>
            {
                foreach (string name in new[] { "BossArenaMap", "00_SkyIslandClouds", "GlobalVolume_ForestNight", "Ravine_WarmBounce", "Moon_CoolKey" })
                {
                    Assert.That(RootNames(scene), Does.Contain(name), name);
                }
                foreach (string name in new[] { "MinimapCanvas", "MinimapDevTarget", "MinimapDevBoss", "Dev_BossArenaShatterTrigger", "95_ObjectPalette" })
                {
                    Assert.That(RootNames(scene), Does.Not.Contain(name), name + ": Dev 전용 루트가 남아 있다");
                }

                BossArenaShatterView view = One<BossArenaShatterView>(scene);
                RequireReferences(view, "_intactRoot", "_fracturedRoot");

                AssertNoMissingReferences(scene);
                AssertBossStarts(scene);
                AssertNoPlaceholderBoss(scene);
            });
        }

        [Test]
        public void Forest_Scene_HasValidSpawnLayoutAndNavigationVolume()
        {
            Inspect(DUNGEON_PATH, scene =>
            {
                DungeonSpawnLayout layout = One<DungeonSpawnLayout>(scene);
                Assert.That(layout.TryValidate(out string reason), Is.True, reason);

                SerializedProperty zones = new SerializedObject(layout).FindProperty("_zones");
                Assert.That(zones.arraySize, Is.GreaterThan(0));
                for (int i = 0; i < zones.arraySize; ++i)
                {
                    SerializedProperty zone = zones.GetArrayElementAtIndex(i);
                    Assert.That(zone.FindPropertyRelative("door").objectReferenceValue, Is.Not.Null, "구역 " + i + " 문 지점");
                    Assert.That(zone.FindPropertyRelative("innerNodes").arraySize
                        + zone.FindPropertyRelative("outerNodes").arraySize, Is.GreaterThan(0), "구역 " + i + " 출현 지점");
                    if (i < (zones.arraySize - 1))
                    {
                        var next = zone.FindPropertyRelative("nextWave").objectReferenceValue as Transform;
                        Assert.That(next, Is.Not.Null, "구역 " + i + " 다음 웨이브 지점");
                        Assert.That(next.name, Is.EqualTo($"Zone{i + 2:00}_NextWave"));
                        Assert.That(layout.TryGetNextWave(i, out Vector3 point), Is.True);
                        Assert.That(point, Is.EqualTo(next.position));
                    }
                }

                MonoBehaviour surface = FindSurface(scene);
                Assert.That(surface, Is.Not.Null, "NavMeshSurface");
                var surfaceData = new SerializedObject(surface);
                Assert.That(surfaceData.FindProperty("m_NavMeshData").objectReferenceValue, Is.Not.Null);
                Assert.That(surfaceData.FindProperty("m_CollectObjects").intValue, Is.EqualTo(1), "부피 모드여야 한다");
                Vector3 size = surfaceData.FindProperty("m_Size").vector3Value;
                Assert.That(size.x, Is.GreaterThan(10f));
                Assert.That(size.z, Is.GreaterThan(10f));

                AssertNoMissingReferences(scene);
                AssertBossStarts(scene);
                foreach (MonoBehaviour behaviour in All<MonoBehaviour>(scene))
                {
                    if (behaviour != null)
                    {
                        Assert.That(behaviour.GetType().Name, Does.Not.Contain("MockMonster"), "Dev 모의 몬스터가 남아 있다");
                    }
                }
            });
        }

        [Test]
        public void TryValidate_MissingIntermediateWavePoint_RejectsLayout()
        {
            Inspect(DUNGEON_PATH, scene =>
            {
                DungeonSpawnLayout layout = One<DungeonSpawnLayout>(scene);
                var data = new SerializedObject(layout);
                data.FindProperty("_zones").GetArrayElementAtIndex(0).FindPropertyRelative("nextWave").objectReferenceValue = null;
                data.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(layout.TryValidate(out string reason), Is.False);
                Assert.That(reason, Does.Contain("다음 웨이브"));
            });
        }

        [Test]
        public void BossScenes_NavigationAssets_AreOwnedByEachScene()
        {
            foreach (string path in new[] { MUCHEOL_PATH, WOLBAEK_PATH, DUNGEON_PATH })
            {
                Inspect(path, scene =>
                {
                    MonoBehaviour surface = FindSurface(scene);
                    Assert.That(surface, Is.Not.Null, path);
                    UnityEngine.Object nav = new SerializedObject(surface).FindProperty("m_NavMeshData").objectReferenceValue;
                    Assert.That(nav, Is.Not.Null, path);
                    string navPath = AssetDatabase.GetAssetPath(nav);
                    Assert.That(navPath, Does.StartWith("Assets/00.Scenes/Release/"), path);
                    Assert.That(navPath, Does.Not.Contain("Dev_"), path + ": Dev 내비게이션을 공유한다");
                });
            }
        }

        [Test]
        public void NetworkBoss_Prefab_HasBoundClientEffects()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BOSS_PATH);
            Assert.That(prefab, Is.Not.Null);
            BossNetworkViewBinder[] binders = prefab.GetComponentsInChildren<BossNetworkViewBinder>(true);
            Assert.That(binders.Length, Is.EqualTo(1));
            var data = new SerializedObject(binders[0]);
            foreach (string field in new[] { "_state", "_view", "_presenter", "_barrierView", "_effectsRoot" })
            {
                Assert.That(data.FindProperty(field).objectReferenceValue, Is.Not.Null, field);
            }
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static void Inspect(string path, Action<Scene> inspect)
        {
            Scene scene = EditorSceneManager.OpenPreviewScene(path);
            try
            {
                inspect(scene);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static List<T> All<T>(Scene scene) where T : Component
        {
            var result = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                result.AddRange(root.GetComponentsInChildren<T>(true));
            }
            return result;
        }

        private static bool IsSkippedSourceRoot(GameObject root)
        {
            // 빌더 ShouldSkipDevRoot와 같은 기준을 유지한다
            foreach (string prefix in new[]
            {
                "Dev", "MinimapDev", "MinimapCanvas", "95_", "Player", "Barrier_", "ZoneBarriers",
                "Ui", "CharDetail", "EventSystem", "Overview", "QuarterView", "HUD", "Popup",
            })
            {
                if (root.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if ((root.name == "Main Camera")
                || (root.GetComponentInChildren<Camera>(true) != null)
                || (root.GetComponentInChildren<Canvas>(true) != null)
                || (root.GetComponentInChildren<EventSystem>(true) != null)
                || (root.GetComponentInChildren<AudioListener>(true) != null)
                || (root.GetComponentInChildren<Mirror.NetworkIdentity>(true) != null))
            {
                return true;
            }

            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null)
                {
                    continue;
                }
                Type type = behaviour.GetType();
                if (((type.Namespace != null) && type.Namespace.StartsWith("Shinshi.UI", StringComparison.Ordinal))
                    || (Array.IndexOf(new[] { "HotkeyRouter", "LocalPlayerCameraBinder", "SortieResultBinder", "PartyHudBinder", "ReleaseMenuActions" }, type.Name) >= 0))
                {
                    return true;
                }
            }
            return false;
        }

        private static string NavDataPath(MonoBehaviour surface)
        {
            UnityEngine.Object data = new SerializedObject(surface).FindProperty("m_NavMeshData").objectReferenceValue;
            return (data == null) ? string.Empty : AssetDatabase.GetAssetPath(data);
        }

        private static List<string> RootNames(Scene scene)
        {
            var names = new List<string>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                names.Add(root.name);
            }
            return names;
        }

        private static MonoBehaviour FindSurface(Scene scene)
        {
            foreach (MonoBehaviour behaviour in All<MonoBehaviour>(scene))
            {
                if ((behaviour != null) && (behaviour.GetType().Name == "NavMeshSurface"))
                {
                    return behaviour;
                }
            }
            return null;
        }

        private static void AssertBossStarts(Scene scene)
        {
            var starts = All<Mirror.NetworkStartPosition>(scene);
            Assert.That(starts.Count, Is.EqualTo(4), "안전한 플레이어 시작점 4개");
            foreach (Mirror.NetworkStartPosition start in starts)
            {
                Assert.That(float.IsNaN(start.transform.position.y), Is.False, start.name);
            }
        }

        private static void AssertNoPlaceholderBoss(Scene scene)
        {
            foreach (Transform transform in All<Transform>(scene))
            {
                if (transform.TryGetComponent(out MeshFilter filter) && (filter.sharedMesh != null)
                    && transform.name.IndexOf("Boss", StringComparison.OrdinalIgnoreCase) >= 0
                    && filter.sharedMesh.name == "Cylinder")
                {
                    Assert.Fail(transform.name + ": 원기둥 임시 보스가 남아 있다");
                }
                Assert.That(transform.name, Does.Not.StartWith("MinimapDev"), transform.name);
            }
        }

        private static void AssertNoMissingReferences(Scene scene)
        {
            foreach (Transform transform in All<Transform>(scene))
            {
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject), Is.Zero, transform.name);
                if (PrefabUtility.IsPrefabAssetMissing(transform.gameObject))
                {
                    Assert.Fail(transform.name + ": 끊어진 프리팹 인스턴스");
                }
                foreach (Renderer renderer in transform.GetComponents<Renderer>())
                {
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        Assert.That(material, Is.Not.Null, transform.name + ": 빈 재질 칸");
                    }
                }
            }
        }

        private static T One<T>(Scene scene) where T : Component
        {
            List<T> values = All<T>(scene);
            Assert.That(values.Count, Is.EqualTo(1), typeof(T).Name);
            return values[0];
        }

        private static void RequireReferences(Component owner, params string[] fields)
        {
            var data = new SerializedObject(owner);
            foreach (string field in fields)
            {
                SerializedProperty property = data.FindProperty(field);
                Assert.That(property, Is.Not.Null, owner.name + "/" + field);
                UnityEngine.Object reference = property.objectReferenceValue;
                Assert.That(reference, Is.Not.Null, owner.name + "/" + field);
                GameObject referencedObject = reference as GameObject;
                if (reference is Component component)
                {
                    referencedObject = component.gameObject;
                }
                if (referencedObject != null)
                {
                    Assert.That(referencedObject.scene, Is.EqualTo(owner.gameObject.scene),
                        owner.name + "/" + field + ": 다른 씬을 가리킨다");
                }
            }
        }
    }
}
