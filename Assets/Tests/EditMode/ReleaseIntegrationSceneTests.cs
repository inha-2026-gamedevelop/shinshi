// System
using System;
using System.Collections.Generic;

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
        private const string BOSS_PATH = "Assets/02.Prefab/Network/NetBoss/PF_NetWolbaekBoss.prefab";

        /****************************************
        *                Tests
        ****************************************/

        [TestCase(TOWN_PATH)]
        [TestCase(DUNGEON_PATH)]
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
