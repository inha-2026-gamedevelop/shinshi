// System
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations.Rigging;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Art;

namespace Shinshi.Editor
{
    // 시험 씬(Dev_WolbaekSkirtPreview)의 치마 리그 구성을 정식 월백 프리팹에 옮긴다
    // 치마 줄 6개(앞·뒤·옆 x 좌·우)마다 02~06번 본을 부모 본에 매달아 흔든다
    // 이미 SkirtRig가 있으면 지우고 다시 만들므로 여러 번 실행해도 같은 결과가 된다
    /// <summary> 정식 월백 프리팹의 모델에 치마 Damped Transform 리그와 워프 가드를 붙이는 에디터 도구 </summary>
    public static class WolbaekSkirtRigBuilder
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string MENU_PATH   = "Shinshi/Boss/Wolbaek Skirt/Port Rig To PF_WolbaekBoss";
        private const string PREFAB_PATH = "Assets/02.Prefab/Boss/PF_WolbaekBoss.prefab";
        private const string MODEL_NAME  = "SK_KitsuneYuna Blue";
        private const string RIG_NAME    = "SkirtRig";

        private const string TELEPORT_METHOD = "OnTeleported";

        // UnityEvent 직렬화 열거값, m_Mode 1은 인자 없는 호출이고 m_CallState 2는 런타임에만 부른다
        private const int VOID_MODE    = 1;
        private const int RUNTIME_ONLY = 2;

        private const int   FIRST_BONE   = 2;
        private const int   LAST_BONE    = 6;
        // 위치 감쇠는 본이 부모를 늦게 따라와 돌진 때 치마가 늘어나므로 쓰지 않고 회전 감쇠만 쓴다
        private const float DAMP_POSITION = 0f;
        private const float DAMP_ROTATION = 0.5f;

        // 치마 뿌리의 회전은 세움 기준, 골반, 같은 쪽 허벅지 셋을 섞어 정한다
        // 고관절을 숙이는 모션은 골반이 척추보다 더 기울고 허벅지는 거의 서 있어서, 몸 쪽을 따르면 치마가 수평으로 눕는다
        // 그래서 숙이지 않는 세움 기준을 크게 섞어 치마가 아래로 늘어지게 하고, 허벅지는 다리를 들 때 치마가 같이 밀려 오르게 하는 용도다
        // 뒤쪽은 허벅지가 앞으로 들려도 닿지 않으므로 허벅지를 거의 섞지 않는다
        private const string PELVIS_NAME  = "pelvis";
        private const string THIGH_PREFIX = "thigh_";
        private const string UPRIGHT_NAME = "SkirtUprightRef";

        // 골반을 너무 줄이면 치마가 허리 높이에서 수직으로만 늘어져 뒤로 튀어나온 엉덩이를 덮지 못한다
        // 뒤와 옆은 골반을 더 섞어 치마 윗부분이 엉덩이 곡면을 따라 덮이게 한다
        private const float UPRIGHT_FRONT = 0.5f;
        private const float UPRIGHT_SIDE  = 0.2f;
        private const float UPRIGHT_BACK  = 0.15f;
        private const float PELVIS_FRONT  = 0.1f;
        private const float PELVIS_SIDE   = 0.7f;
        private const float PELVIS_BACK   = 0.8f;

        private static readonly string[] G_Lines =
        {
            "front_l", "front_r", "back_l", "back_r", "side_l", "side_r",
        };

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem(MENU_PATH)]
        public static void PortToBossPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PREFAB_PATH);

            try
            {
                Animator animator = FindModelAnimator(root);
                if (animator == null)
                {
                    Debug.LogError($"[WolbaekSkirtRigBuilder] '{MODEL_NAME}' 모델의 Animator를 찾지 못했다: {PREFAB_PATH}");
                    return;
                }

                BossView view = root.GetComponentInChildren<BossView>(true);
                if (view == null)
                {
                    Debug.LogError($"[WolbaekSkirtRigBuilder] BossView를 찾지 못했다: {PREFAB_PATH}");
                    return;
                }

                int count = Build(animator.gameObject, CreateUprightRef(view.transform), out WolbaekSkirtWarpGuard guard);
                if (count < 0)
                {
                    return;
                }

                if (!ConnectTeleportEvent(root, guard))
                {
                    return;
                }

                PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH);
                Debug.Log($"[WolbaekSkirtRigBuilder] 치마 리그 이식 완료: Damped Transform {count}개 -> {PREFAB_PATH}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Animator FindModelAnimator(GameObject root)
        {
            foreach (Animator animator in root.GetComponentsInChildren<Animator>(true))
            {
                if (animator.gameObject.name == MODEL_NAME)
                {
                    return animator;
                }
            }

            return null;
        }

        /// <summary> 모델 루트에 리그를 만들고 만든 Damped Transform 수를 돌려준다, 본이 모자라면 아무것도 바꾸지 않고 -1을 돌려준다 </summary>
        private static int Build(GameObject model, Transform upright, out WolbaekSkirtWarpGuard guard)
        {
            guard = null;

            Dictionary<string, Transform> bones = CollectBones(model.transform);
            List<(Transform constrained, Transform source)> pairs = new List<(Transform, Transform)>();

            foreach (string line in G_Lines)
            {
                for (int i = FIRST_BONE; i <= LAST_BONE; i++)
                {
                    string boneName   = BoneName(line, i);
                    string sourceName = BoneName(line, i - 1);

                    if (!bones.TryGetValue(boneName, out Transform constrained) ||
                        !bones.TryGetValue(sourceName, out Transform source))
                    {
                        Debug.LogError($"[WolbaekSkirtRigBuilder] 치마 본을 찾지 못했다: {boneName} 또는 {sourceName}");
                        return -1;
                    }

                    pairs.Add((constrained, source));
                }
            }

            Transform oldRig = model.transform.Find(RIG_NAME);
            if (oldRig != null)
            {
                Object.DestroyImmediate(oldRig.gameObject);
            }

            GameObject rigObject = new GameObject(RIG_NAME);
            rigObject.transform.SetParent(model.transform, false);
            Rig rig = rigObject.AddComponent<Rig>();

            // 뿌리 제약이 Damped Transform보다 먼저 계산되어야 하므로 자식 순서상 앞에 만든다
            foreach (string line in G_Lines)
            {
                if (!CreateRoot(rigObject.transform, bones, upright, line))
                {
                    Object.DestroyImmediate(rigObject);
                    return -1;
                }
            }

            foreach ((Transform constrained, Transform source) in pairs)
            {
                CreateDamped(rigObject.transform, constrained, source);
            }

            RigBuilder builder = GetOrAdd<RigBuilder>(model);
            builder.layers.Clear();
            builder.layers.Add(new RigLayer(rig, true));

            guard = GetOrAdd<WolbaekSkirtWarpGuard>(model);
            SerializedObject guardObject = new SerializedObject(guard);
            guardObject.FindProperty("_rig").objectReferenceValue = rig;
            guardObject.ApplyModifiedPropertiesWithoutUndo();

            return pairs.Count;
        }

        // BossView가 모델 위치를 한 번에 옮길 때 가드의 OnTeleported가 불리도록 연결한다, 이미 있으면 지우고 다시 연결한다
        private static bool ConnectTeleportEvent(GameObject root, WolbaekSkirtWarpGuard guard)
        {
            BossView view = root.GetComponentInChildren<BossView>(true);
            if (view == null)
            {
                Debug.LogError($"[WolbaekSkirtRigBuilder] BossView를 찾지 못했다: {PREFAB_PATH}");
                return false;
            }

            SerializedObject viewObject = new SerializedObject(view);
            SerializedProperty calls = viewObject.FindProperty("_onTeleported.m_PersistentCalls.m_Calls");

            for (int i = calls.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty existing = calls.GetArrayElementAtIndex(i);
                if (existing.FindPropertyRelative("m_MethodName").stringValue == TELEPORT_METHOD)
                {
                    calls.DeleteArrayElementAtIndex(i);
                }
            }

            calls.InsertArrayElementAtIndex(calls.arraySize);
            SerializedProperty call = calls.GetArrayElementAtIndex(calls.arraySize - 1);
            call.FindPropertyRelative("m_Target").objectReferenceValue = guard;
            call.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue = typeof(WolbaekSkirtWarpGuard).AssemblyQualifiedName;
            call.FindPropertyRelative("m_MethodName").stringValue = TELEPORT_METHOD;
            call.FindPropertyRelative("m_Mode").enumValueIndex = VOID_MODE;
            call.FindPropertyRelative("m_CallState").enumValueIndex = RUNTIME_ONLY;
            viewObject.ApplyModifiedPropertiesWithoutUndo();

            return true;
        }

        // 보스 루트 아래에 숙이지 않는 세움 기준을 만든다, 보스 루트는 바라보는 방향으로만 돌기 때문이다
        private static Transform CreateUprightRef(Transform bossRoot)
        {
            Transform old = bossRoot.Find(UPRIGHT_NAME);
            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }

            GameObject upright = new GameObject(UPRIGHT_NAME);
            upright.transform.SetParent(bossRoot, false);
            return upright.transform;
        }

        // 치마 줄의 01번 본이 골반과 같은 쪽 허벅지의 회전을 섞어 따라가게 한다
        private static bool CreateRoot(Transform parent, Dictionary<string, Transform> bones, Transform upright, string line)
        {
            string side      = line.Substring(line.LastIndexOf('_') + 1);
            string rootName  = BoneName(line, 1);
            string thighName = THIGH_PREFIX + side;

            if (!bones.TryGetValue(rootName, out Transform root) ||
                !bones.TryGetValue(PELVIS_NAME, out Transform pelvis) ||
                !bones.TryGetValue(thighName, out Transform thigh))
            {
                Debug.LogError($"[WolbaekSkirtRigBuilder] 뿌리 제약에 쓸 본을 찾지 못했다: {rootName}, {PELVIS_NAME}, {thighName}");
                return false;
            }

            float uprightWeight = line.StartsWith("front") ? UPRIGHT_FRONT : line.StartsWith("back") ? UPRIGHT_BACK : UPRIGHT_SIDE;
            float pelvisWeight  = line.StartsWith("front") ? PELVIS_FRONT : line.StartsWith("back") ? PELVIS_BACK : PELVIS_SIDE;
            float legWeight     = 1f - uprightWeight - pelvisWeight;

            GameObject child = new GameObject($"Root_{root.name}");
            child.transform.SetParent(parent, false);

            MultiRotationConstraint constraint = child.AddComponent<MultiRotationConstraint>();
            MultiRotationConstraintData data = constraint.data;
            data.constrainedObject = root;
            data.constrainedXAxis  = true;
            data.constrainedYAxis  = true;
            data.constrainedZAxis  = true;
            data.maintainOffset    = true;

            WeightedTransformArray sources = new WeightedTransformArray(3);
            sources[0] = new WeightedTransform(upright, uprightWeight);
            sources[1] = new WeightedTransform(pelvis, pelvisWeight);
            sources[2] = new WeightedTransform(thigh, legWeight);
            data.sourceObjects = sources;

            constraint.data = data;
            return true;
        }

        private static void CreateDamped(Transform parent, Transform constrained, Transform source)
        {
            GameObject child = new GameObject($"Damp_{constrained.name}");
            child.transform.SetParent(parent, false);

            DampedTransform damped = child.AddComponent<DampedTransform>();
            DampedTransformData data = damped.data;
            data.constrainedObject = constrained;
            data.sourceObject      = source;
            data.dampPosition      = DAMP_POSITION;
            data.dampRotation      = DAMP_ROTATION;
            data.maintainAim       = true;
            damped.data = data;
        }

        private static Dictionary<string, Transform> CollectBones(Transform model)
        {
            Dictionary<string, Transform> bones = new Dictionary<string, Transform>();

            foreach (Transform bone in model.GetComponentsInChildren<Transform>(true))
            {
                bool isNeeded = bone.name.StartsWith("skirt_") || (bone.name == PELVIS_NAME) || bone.name.StartsWith(THIGH_PREFIX);
                if (isNeeded && !bones.ContainsKey(bone.name))
                {
                    bones.Add(bone.name, bone);
                }
            }

            return bones;
        }

        private static string BoneName(string line, int index)
        {
            int split = line.LastIndexOf('_');
            return $"skirt_{line.Substring(0, split)}_{index:00}{line.Substring(split)}";
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }
    }
}
