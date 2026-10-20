// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Art;

namespace Shinshi.Editor
{
    // 소매는 Damped Transform 대신 WolbaekSleeveSpring(중력을 받는 스프링)으로 움직인다
    // 이전 시험에서 SkirtRig 아래에 만든 Damp_sleeve_* 제약이 남아 있으면 지운다
    // 이미 컴포넌트가 있으면 값을 그대로 두므로 여러 번 실행해도 같은 결과가 된다
    /// <summary> 월백 모델에 소매 스프링을 붙이는 에디터 도구(열린 씬 또는 정식 프리팹) </summary>
    public static class WolbaekSleeveRigBuilder
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string MENU_PATH   = "Shinshi/Boss/Wolbaek Sleeve/Apply To Open Scene Model";
        private const string RIG_NAME    = "SkirtRig";
        private const string OLD_PREFIX  = "Damp_sleeve_";

        private const string PREFAB_MENU_PATH = "Shinshi/Boss/Wolbaek Sleeve/Port To PF_WolbaekBoss";
        private const string PREFAB_PATH      = "Assets/02.Prefab/Boss/PF_WolbaekBoss.prefab";
        private const string MODEL_NAME       = "SK_KitsuneYuna Blue";

        // BossView._onTeleported에 연결하는 UnityEvent 호출 설정이다
        private const string TELEPORT_METHOD = "OnTeleported";
        private const int    VOID_MODE       = 1;
        private const int    RUNTIME_ONLY    = 2;

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem(MENU_PATH)]
        public static void ApplyToOpenScene()
        {
            WolbaekSkirtWarpGuard[] guards = Object.FindObjectsByType<WolbaekSkirtWarpGuard>(FindObjectsInactive.Include);
            if (guards.Length == 0)
            {
                Debug.LogError("[WolbaekSleeveRigBuilder] 열린 씬에서 치마 리그가 붙은 월백 모델을 찾지 못했다");
                return;
            }

            foreach (WolbaekSkirtWarpGuard guard in guards)
            {
                int removed = RemoveOldDamped(guard.transform);

                if (guard.GetComponent<WolbaekSleeveSpring>() == null)
                {
                    guard.gameObject.AddComponent<WolbaekSleeveSpring>();
                }

                EditorSceneManager.MarkSceneDirty(guard.gameObject.scene);
                Debug.Log($"[WolbaekSleeveRigBuilder] 소매 스프링 적용: {guard.name} (이전 Damped Transform {removed}개 제거)");
            }
        }

        [MenuItem(PREFAB_MENU_PATH)]
        public static void PortToBossPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PREFAB_PATH);

            try
            {
                Animator animator = null;
                foreach (Animator candidate in root.GetComponentsInChildren<Animator>(true))
                {
                    if (candidate.gameObject.name == MODEL_NAME)
                    {
                        animator = candidate;
                        break;
                    }
                }

                BossView view = root.GetComponentInChildren<BossView>(true);
                if ((animator == null) || (view == null))
                {
                    Debug.LogError($"[WolbaekSleeveRigBuilder] 모델 또는 BossView를 찾지 못했다: {PREFAB_PATH}");
                    return;
                }

                int removed = RemoveOldDamped(animator.transform);

                WolbaekSleeveSpring spring = animator.GetComponent<WolbaekSleeveSpring>();
                if (spring == null)
                {
                    spring = animator.gameObject.AddComponent<WolbaekSleeveSpring>();
                }

                ConnectTeleportEvent(view, spring);

                PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH, out bool isSaved);
                if (!isSaved)
                {
                    Debug.LogError($"[WolbaekSleeveRigBuilder] 프리팹 저장에 실패했다: {PREFAB_PATH}");
                    return;
                }

                Debug.Log($"[WolbaekSleeveRigBuilder] 소매 스프링 이식 완료 (이전 Damped Transform {removed}개 제거): {PREFAB_PATH}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // 소매 스프링의 OnTeleported 호출만 지우고 다시 연결한다, 치마 가드와 머리카락 가드의 호출은 건드리지 않는다
        private static void ConnectTeleportEvent(BossView view, WolbaekSleeveSpring spring)
        {
            SerializedObject viewObject = new SerializedObject(view);
            SerializedProperty calls = viewObject.FindProperty("_onTeleported.m_PersistentCalls.m_Calls");

            for (int i = calls.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty existing = calls.GetArrayElementAtIndex(i);
                if (existing.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue.StartsWith(typeof(WolbaekSleeveSpring).FullName))
                {
                    calls.DeleteArrayElementAtIndex(i);
                }
            }

            calls.InsertArrayElementAtIndex(calls.arraySize);
            SerializedProperty call = calls.GetArrayElementAtIndex(calls.arraySize - 1);
            call.FindPropertyRelative("m_Target").objectReferenceValue = spring;
            call.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue = typeof(WolbaekSleeveSpring).AssemblyQualifiedName;
            call.FindPropertyRelative("m_MethodName").stringValue = TELEPORT_METHOD;
            call.FindPropertyRelative("m_Mode").enumValueIndex = VOID_MODE;
            call.FindPropertyRelative("m_CallState").enumValueIndex = RUNTIME_ONLY;
            viewObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static int RemoveOldDamped(Transform model)
        {
            Transform rig = model.Find(RIG_NAME);
            if (rig == null)
            {
                return 0;
            }

            int removed = 0;

            for (int i = rig.childCount - 1; i >= 0; i--)
            {
                if (rig.GetChild(i).name.StartsWith(OLD_PREFIX))
                {
                    Object.DestroyImmediate(rig.GetChild(i).gameObject);
                    removed++;
                }
            }

            return removed;
        }
    }
}
