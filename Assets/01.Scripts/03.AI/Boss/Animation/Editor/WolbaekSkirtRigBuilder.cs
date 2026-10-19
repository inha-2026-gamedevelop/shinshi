// System
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEditor.Animations;
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
        private const string CALF_PREFIX  = "calf_";
        private const string PUSH_NAME    = "LegPush";
        private const int    TIP_BONE     = 3;

        // 허벅지가 수직 아래에서 시작 각도를 넘어 들리면 치마 줄을 밀기 시작해 가득 각도에서 최대가 된다
        // 걷기나 대기처럼 작은 다리 움직임은 시작 각도 아래라 치마를 건드리지 않는다
        private const float PUSH_START_ANGLE  = 40f;
        private const float PUSH_FULL_ANGLE   = 75f;
        private const float PUSH_MARGIN_ANGLE = 10f;
        private const float PUSH_MAX_FOLLOW   = 0.9f;
        private const float PUSH_FACING_OFFSET = 0.6f;

        // 격자 빔처럼 다리를 크게 뻗는 모션에서 왼쪽 다리가 치마 밖으로 나오지 않게 그 모션 상태일 때만 다리를 조금 내린다
        private const string LOWER_NAME        = "LegLowerLeft";
        private const string LOWER_STATE       = "attack04_inplace";
        private const float  LOWER_START_ANGLE = 20f;
        private const float  LOWER_RATIO       = 0.5f;
        private const float  LOWER_MAX         = 35f;
        private const float  LOWER_BLEND       = 0.15f;

        // 격자 빔 유지 구간에서는 다리를 내려도 허벅지가 40도 아래라 기본 다리 밀기가 켜지지 않아 엉덩이 쪽이 비므로,
        // 같은 모션 상태에서만 켜지는 낮은 문턱의 다리 밀기를 하나 더 둔다
        private const string GRID_PUSH_NAME        = "LegPushGrid";
        private const float  GRID_PUSH_START_ANGLE = 20f;
        private const float  GRID_PUSH_FULL_ANGLE  = 40f;

        // 이 밀기는 앞 두 줄에만 걸어 뒤쪽 줄이 움직여 뒤가 비는 것을 막는다, 모든 줄에 걸면 뒤쪽 속옷 노출이 생긴다
        private static readonly string[] G_GridPushLines = { "front_l", "front_r" };

        // 격자 빔 유지 구간에서 허벅지가 나오는 왼쪽 옆면은 앞 왼쪽 줄과 옆 왼쪽 줄 사이 넓은 틈에 놓여 속옷이 보이므로,
        // 같은 모션 상태에서만 두 줄을 허벅지 관절의 방위 쪽으로 돌려 틈을 메운다
        private const string COVER_NAME     = "HipCover";
        private const string COVER_LINE_A   = "front_l";
        private const string COVER_LINE_B   = "side_l";
        private const float  COVER_FACTOR_A = 0f;
        private const float  COVER_FACTOR_B = 0.9f;
        private const float  COVER_MAX_YAW  = 80f;

        private const string LEFT_SUFFIX      = "l";
        private const string RIGHT_SUFFIX     = "r";

        // 골반을 너무 줄이면 치마가 허리 높이에서 수직으로만 늘어져 뒤로 튀어나온 엉덩이를 덮지 못한다
        // 뒤와 옆은 골반을 더 섞어 치마 윗부분이 엉덩이 곡면을 따라 덮이게 한다
        private const float UPRIGHT_FRONT = 0.5f;
        private const float UPRIGHT_SIDE  = 0.2f;
        private const float UPRIGHT_BACK  = 0.15f;
        private const float PELVIS_FRONT  = 0.1f;
        private const float PELVIS_SIDE   = 0.7f;
        private const float PELVIS_BACK   = 0.8f;

        // 뿌리 제약의 기준 자세는 프리팹의 기본 자세라 Idle과 크게 달라서, 선 자세부터 줄마다 각도가 어긋나 면 사이가 벌어진다
        // 그래서 Idle 한 자세에서 애니메이션 원래 회전과 정확히 같아지도록 오프셋을 계산해 넣는다
        private const string IDLE_STATE          = "Idle";
        private const float  IDLE_SAMPLE_SECONDS = 0f;

        private struct RootConstraint
        {
            public MultiRotationConstraint Constraint;
            public Transform               Root;
            public Transform[]             Sources;
        }

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
                RemoveMissingScripts(root);

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

                PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH, out bool isSaved);
                if (!isSaved)
                {
                    Debug.LogError($"[WolbaekSkirtRigBuilder] 프리팹 저장에 실패했다: {PREFAB_PATH}");
                    return;
                }

                Debug.Log($"[WolbaekSkirtRigBuilder] 치마 리그 이식 완료: Damped Transform {count}개 -> {PREFAB_PATH}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // 지운 스크립트의 컴포넌트가 프리팹에 남아 있으면 저장이 거부되므로, 이전 실행이 남긴 빈 컴포넌트를 먼저 걷어 낸다
        private static void RemoveMissingScripts(GameObject root)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(child.gameObject);
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

            // 다리 내리기는 다리 밀기와 뿌리 제약이 낮아진 다리를 기준으로 계산하도록 가장 먼저 만든다
            if (!CreateLegLower(rigObject.transform, bones, out SkirtLegLower lower))
            {
                Object.DestroyImmediate(rigObject);
                return -1;
            }

            // 뿌리 제약이 Damped Transform보다 먼저 계산되어야 하므로 자식 순서상 앞에 만든다
            List<RootConstraint> roots = new List<RootConstraint>();
            foreach (string line in G_Lines)
            {
                if (!CreateRoot(rigObject.transform, bones, upright, line, roots))
                {
                    Object.DestroyImmediate(rigObject);
                    return -1;
                }
            }

            if (!CreateLegPush(rigObject.transform, bones, roots, PUSH_NAME, PUSH_START_ANGLE, PUSH_FULL_ANGLE, null, out _) ||
                !CreateLegPush(rigObject.transform, bones, roots, GRID_PUSH_NAME, GRID_PUSH_START_ANGLE, GRID_PUSH_FULL_ANGLE, G_GridPushLines, out SkirtLegPush gridPush))
            {
                Object.DestroyImmediate(rigObject);
                return -1;
            }

            if (!CreateHipCover(rigObject.transform, bones, roots, out SkirtHipCover cover))
            {
                Object.DestroyImmediate(rigObject);
                return -1;
            }

            CreateMotionGate(model, new MonoBehaviour[] { lower, gridPush, cover });

            foreach ((Transform constrained, Transform source) in pairs)
            {
                CreateDamped(rigObject.transform, constrained, source);
            }

            if (!CalibrateToIdle(model.GetComponent<Animator>(), roots))
            {
                Object.DestroyImmediate(rigObject);
                return -1;
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
        private static bool CreateRoot(Transform parent, Dictionary<string, Transform> bones, Transform upright, string line, List<RootConstraint> roots)
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
            roots.Add(new RootConstraint { Constraint = constraint, Root = root, Sources = new[] { upright, pelvis, thigh } });
            return true;
        }

        // 왼쪽 다리를 내리는 제약을 만든다, 처음에는 꺼 두고 모션 문이 켠다
        private static bool CreateLegLower(Transform parent, Dictionary<string, Transform> bones, out SkirtLegLower lower)
        {
            lower = null;

            string thighName = THIGH_PREFIX + LEFT_SUFFIX;
            string calfName  = CALF_PREFIX + LEFT_SUFFIX;

            if (!bones.TryGetValue(thighName, out Transform thigh) || !bones.TryGetValue(calfName, out Transform calf))
            {
                Debug.LogError($"[WolbaekSkirtRigBuilder] 다리 내리기 제약에 쓸 본을 찾지 못했다: {thighName}, {calfName}");
                return false;
            }

            GameObject child = new GameObject(LOWER_NAME);
            child.transform.SetParent(parent, false);

            lower = child.AddComponent<SkirtLegLower>();
            lower.data.Thigh      = thigh;
            lower.data.Calf       = calf;
            lower.data.StartAngle = LOWER_START_ANGLE;
            lower.data.Ratio      = LOWER_RATIO;
            lower.data.MaxLower   = LOWER_MAX;
            lower.weight          = 0f;
            return true;
        }

        // 두 치마 줄을 허벅지 관절의 방위 쪽으로 돌리는 제약을 만든다, 처음에는 꺼 두고 모션 문이 켠다
        private static bool CreateHipCover(Transform parent, Dictionary<string, Transform> bones, List<RootConstraint> roots, out SkirtHipCover cover)
        {
            cover = null;

            string hipName = THIGH_PREFIX + LEFT_SUFFIX;
            if (!bones.TryGetValue(hipName, out Transform hip) ||
                !bones.TryGetValue(BoneName(COVER_LINE_A, 1), out Transform rootA) ||
                !bones.TryGetValue(BoneName(COVER_LINE_A, TIP_BONE), out Transform tipA) ||
                !bones.TryGetValue(BoneName(COVER_LINE_B, 1), out Transform rootB) ||
                !bones.TryGetValue(BoneName(COVER_LINE_B, TIP_BONE), out Transform tipB))
            {
                Debug.LogError($"[WolbaekSkirtRigBuilder] 엉덩이 덮개 제약에 쓸 본을 찾지 못했다: {hipName}, {COVER_LINE_A}, {COVER_LINE_B}");
                return false;
            }

            GameObject child = new GameObject(COVER_NAME);
            child.transform.SetParent(parent, false);

            cover = child.AddComponent<SkirtHipCover>();
            cover.data.Pelvis  = roots[0].Sources[1];
            cover.data.Hip     = hip;
            cover.data.RootA   = rootA;
            cover.data.TipA    = tipA;
            cover.data.RootB   = rootB;
            cover.data.TipB    = tipB;
            cover.data.FactorA = COVER_FACTOR_A;
            cover.data.FactorB = COVER_FACTOR_B;
            cover.data.MaxYaw  = COVER_MAX_YAW;
            cover.weight       = 0f;
            return true;
        }

        // 격자 빔 모션 상태일 때만 지정한 제약들을 켜는 문을 만든다, 이전 문은 지우고 하나만 남긴다
        private static void CreateMotionGate(GameObject model, MonoBehaviour[] constraints)
        {
            foreach (SkirtMotionGate old in model.GetComponents<SkirtMotionGate>())
            {
                Object.DestroyImmediate(old);
            }

            SkirtMotionGate gate = model.AddComponent<SkirtMotionGate>();
            SerializedObject gateObject = new SerializedObject(gate);
            gateObject.FindProperty("_animator").objectReferenceValue = model.GetComponent<Animator>();
            gateObject.FindProperty("_stateName").stringValue         = LOWER_STATE;
            gateObject.FindProperty("_blendSeconds").floatValue       = LOWER_BLEND;

            SerializedProperty list = gateObject.FindProperty("_constraints");
            list.arraySize = constraints.Length;
            for (int i = 0; i < constraints.Length; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = constraints[i];
            }

            gateObject.ApplyModifiedPropertiesWithoutUndo();
        }

        // 허벅지가 크게 들릴 때 치마 줄이 허벅지 위로 밀려 오르게 하는 제약을 만든다, 뿌리 제약 뒤 Damped Transform 앞에 계산된다
        // 줄마다 양쪽 다리를 모두 보도록 왼쪽과 오른쪽 허벅지와 종아리를 한 번씩만 넘긴다
        private static bool CreateLegPush(Transform parent, Dictionary<string, Transform> bones, List<RootConstraint> roots,
            string name, float startAngle, float fullAngle, string[] onlyLines, out SkirtLegPush push)
        {
            push = null;

            string leftThighName  = THIGH_PREFIX + LEFT_SUFFIX;
            string rightThighName = THIGH_PREFIX + RIGHT_SUFFIX;
            string leftCalfName   = CALF_PREFIX + LEFT_SUFFIX;
            string rightCalfName  = CALF_PREFIX + RIGHT_SUFFIX;

            if (!bones.TryGetValue(leftThighName, out Transform leftThigh) ||
                !bones.TryGetValue(rightThighName, out Transform rightThigh) ||
                !bones.TryGetValue(leftCalfName, out Transform leftCalf) ||
                !bones.TryGetValue(rightCalfName, out Transform rightCalf))
            {
                Debug.LogError($"[WolbaekSkirtRigBuilder] 다리 밀기 제약에 쓸 다리 본을 찾지 못했다: {leftThighName}, {rightThighName}, {leftCalfName}, {rightCalfName}");
                return false;
            }

            List<Transform> rootBones = new List<Transform>();
            List<Transform> tips      = new List<Transform>();

            for (int i = 0; i < roots.Count; i++)
            {
                if ((onlyLines != null) && (System.Array.IndexOf(onlyLines, G_Lines[i]) < 0))
                {
                    continue;
                }

                string tipName = BoneName(G_Lines[i], TIP_BONE);
                if (!bones.TryGetValue(tipName, out Transform tip))
                {
                    Debug.LogError($"[WolbaekSkirtRigBuilder] 다리 밀기 제약에 쓸 치마 끝 본을 찾지 못했다: {tipName}");
                    return false;
                }

                rootBones.Add(roots[i].Root);
                tips.Add(tip);
            }

            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);

            push = child.AddComponent<SkirtLegPush>();
            push.data.Pelvis       = roots[0].Sources[1];
            push.data.Upright      = roots[0].Sources[0];
            push.data.LeftThigh    = leftThigh;
            push.data.LeftCalf     = leftCalf;
            push.data.RightThigh   = rightThigh;
            push.data.RightCalf    = rightCalf;
            push.data.Roots        = rootBones.ToArray();
            push.data.Tips         = tips.ToArray();
            push.data.StartAngle   = startAngle;
            push.data.FullAngle    = fullAngle;
            push.data.MarginAngle  = PUSH_MARGIN_ANGLE;
            push.data.MaxFollow    = PUSH_MAX_FOLLOW;
            push.data.FacingOffset = PUSH_FACING_OFFSET;
            push.weight            = (name == GRID_PUSH_NAME) ? 0f : 1f;
            return true;
        }

        // Idle를 한 번 샘플링해, 그 자세에서 뿌리 제약의 결과가 애니메이션 원래 회전과 같아지는 오프셋을 계산한다
        // 제약의 결과는 가중 평균한 원본 회전 변화에 기본 자세의 회전을 곱하고 오프셋을 오른쪽에 곱한 값이다
        private static bool CalibrateToIdle(Animator animator, List<RootConstraint> roots)
        {
            AnimationClip idle = FindClip(animator, IDLE_STATE);
            if (idle == null)
            {
                Debug.LogError($"[WolbaekSkirtRigBuilder] '{IDLE_STATE}' 클립을 찾지 못했다");
                return false;
            }

            Transform[]  all       = animator.GetComponentsInChildren<Transform>(true);
            Vector3[]    positions = new Vector3[all.Length];
            Quaternion[] rotations = new Quaternion[all.Length];
            Vector3[]    scales    = new Vector3[all.Length];

            for (int i = 0; i < all.Length; i++)
            {
                positions[i] = all[i].localPosition;
                rotations[i] = all[i].localRotation;
                scales[i]    = all[i].localScale;
            }

            Quaternion[]   bindRoots   = new Quaternion[roots.Count];
            Quaternion[][] bindSources = new Quaternion[roots.Count][];
            for (int i = 0; i < roots.Count; i++)
            {
                bindRoots[i]   = roots[i].Root.rotation;
                bindSources[i] = ReadRotations(roots[i].Sources);
            }

            idle.SampleAnimation(animator.gameObject, IDLE_SAMPLE_SECONDS);

            Quaternion[]   idleRoots   = new Quaternion[roots.Count];
            Quaternion[][] idleSources = new Quaternion[roots.Count][];
            for (int i = 0; i < roots.Count; i++)
            {
                idleRoots[i]   = roots[i].Root.rotation;
                idleSources[i] = ReadRotations(roots[i].Sources);
            }

            // 샘플링이 바꾼 본 자세를 되돌려 프리팹에 기본 자세가 저장되게 한다
            for (int i = 0; i < all.Length; i++)
            {
                all[i].localPosition = positions[i];
                all[i].localRotation = rotations[i];
                all[i].localScale    = scales[i];
            }

            for (int i = 0; i < roots.Count; i++)
            {
                WeightedTransformArray sources = roots[i].Constraint.data.sourceObjects;
                Quaternion accumulated = new Quaternion(0f, 0f, 0f, 0f);

                for (int k = 0; k < sources.Count; k++)
                {
                    Quaternion change = idleSources[i][k] * Quaternion.Inverse(bindSources[i][k]);
                    float      sign   = (Dot(accumulated, change) < 0f) ? -1f : 1f;
                    float      weight = sources[k].weight;

                    accumulated = new Quaternion(
                        accumulated.x + (sign * change.x * weight),
                        accumulated.y + (sign * change.y * weight),
                        accumulated.z + (sign * change.z * weight),
                        accumulated.w + (sign * change.w * weight));
                }

                float length = Mathf.Sqrt(Dot(accumulated, accumulated));
                accumulated = new Quaternion(accumulated.x / length, accumulated.y / length, accumulated.z / length, accumulated.w / length);

                Quaternion offset = Quaternion.Inverse(accumulated * bindRoots[i]) * idleRoots[i];

                MultiRotationConstraintData data = roots[i].Constraint.data;
                data.offset = offset.eulerAngles;
                roots[i].Constraint.data = data;
            }

            return true;
        }

        private static AnimationClip FindClip(Animator animator, string stateName)
        {
            AnimatorController controller = animator.runtimeAnimatorController as AnimatorController;
            if (controller == null)
            {
                return null;
            }

            foreach (AnimatorControllerLayer layer in controller.layers)
            {
                foreach (ChildAnimatorState child in layer.stateMachine.states)
                {
                    if (child.state.name == stateName)
                    {
                        return child.state.motion as AnimationClip;
                    }
                }
            }

            return null;
        }

        private static Quaternion[] ReadRotations(Transform[] transforms)
        {
            Quaternion[] result = new Quaternion[transforms.Length];

            for (int i = 0; i < transforms.Length; i++)
            {
                result[i] = transforms[i].rotation;
            }

            return result;
        }

        private static float Dot(Quaternion a, Quaternion b)
        {
            return (a.x * b.x) + (a.y * b.y) + (a.z * b.z) + (a.w * b.w);
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
                bool isNeeded = bone.name.StartsWith("skirt_") || (bone.name == PELVIS_NAME) || bone.name.StartsWith(THIGH_PREFIX) || bone.name.StartsWith(CALF_PREFIX);
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
