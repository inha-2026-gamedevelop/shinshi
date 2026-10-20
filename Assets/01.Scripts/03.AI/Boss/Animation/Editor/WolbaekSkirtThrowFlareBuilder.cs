// System
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations.Rigging;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Editor
{
    // 던지기 모션(combo02_1_inplace)은 몸이 크게 비틀려 엉덩이와 허벅지가 뒤쪽·옆쪽 치마 천을 뚫고 나와 속옷이 보인다
    // 그 모션 상태일 때만 뒤·옆 치마 줄의 뿌리 본을 골반 둘레 바깥쪽으로 밀어내서 치마를 넓힌다
    // 모션 문(SkirtMotionGate)이 상태를 보고 가중치를 올리고 내리므로 다른 모션의 치마는 그대로다
    // 이미 만들어 둔 것이 있으면 지우고 다시 만들므로 여러 번 실행해도 같은 결과가 된다
    /// <summary> 던지기 모션에서만 치마를 바깥으로 넓히는 위치 제약을 월백 프리팹에 붙이는 에디터 도구 </summary>
    public static class WolbaekSkirtThrowFlareBuilder
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string MENU_PATH   = "Shinshi/Boss/Wolbaek Skirt/Add Throw Flare To PF_WolbaekBoss";
        private const string PREFAB_PATH = "Assets/02.Prefab/Boss/PF_WolbaekBoss.prefab";
        private const string MODEL_NAME  = "SK_KitsuneYuna Blue";
        private const string RIG_NAME    = "SkirtRig";
        private const string PELVIS_NAME = "pelvis";

        private const string DAMPED_PREFIX = "Damp_";
        private const string FLARE_PREFIX  = "ThrowFlare_";
        private const string ANCHOR_PREFIX = "ThrowFlareAnchor_";

        private const string THROW_STATE = "combo02_1_inplace";
        private const float  BLEND       = 0.15f;

        // 모델 단위로 뿌리 본을 바깥으로 미는 거리다, 화면을 보며 맞춘 값이다
        private const float PUSH_DISTANCE = 0.11f;

        private static readonly string[] G_Lines = { "back_r", "side_r", "back_l", "side_l" };

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem(MENU_PATH)]
        public static void PortToBossPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PREFAB_PATH);

            try
            {
                GameObject model = null;
                foreach (Animator candidate in root.GetComponentsInChildren<Animator>(true))
                {
                    if (candidate.gameObject.name == MODEL_NAME)
                    {
                        model = candidate.gameObject;
                        break;
                    }
                }

                if (model == null)
                {
                    Debug.LogError($"[WolbaekSkirtThrowFlareBuilder] '{MODEL_NAME}' 모델을 찾지 못했다: {PREFAB_PATH}");
                    return;
                }

                if (!Apply(model))
                {
                    return;
                }

                PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH, out bool isSaved);
                if (!isSaved)
                {
                    Debug.LogError($"[WolbaekSkirtThrowFlareBuilder] 프리팹 저장에 실패했다: {PREFAB_PATH}");
                    return;
                }

                Debug.Log($"[WolbaekSkirtThrowFlareBuilder] 던지기 치마 넓히기 이식 완료: {PREFAB_PATH}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary> 모델의 SkirtRig에 줄마다 위치 제약과 기준점을 만들고, 던지기 상태에서만 켜는 모션 문을 붙인다. 치마 리그가 없으면 아무것도 바꾸지 않고 false를 돌려준다 </summary>
        public static bool Apply(GameObject model)
        {
            Transform rig = model.transform.Find(RIG_NAME);
            Dictionary<string, Transform> bones = CollectBones(model.transform);

            if ((rig == null) || !bones.TryGetValue(PELVIS_NAME, out Transform pelvis))
            {
                Debug.LogError($"[WolbaekSkirtThrowFlareBuilder] 치마 리그 또는 골반 본을 찾지 못했다: {RIG_NAME}, {PELVIS_NAME}");
                return false;
            }

            RemoveOld(model, rig, pelvis);

            List<(Transform root, Vector3 target)> targets = new List<(Transform, Vector3)>();
            foreach (string line in G_Lines)
            {
                string rootName = BoneName(line, 1);
                if (!bones.TryGetValue(rootName, out Transform root))
                {
                    Debug.LogError($"[WolbaekSkirtThrowFlareBuilder] 치마 본을 찾지 못했다: {rootName}");
                    return false;
                }

                // 기본 자세에서 골반 둘레의 바깥 방향으로 밀린 자리가 목표다
                Vector3 outward = root.position - pelvis.position;
                outward.y = 0f;
                targets.Add((root, root.position + (outward.normalized * PUSH_DISTANCE * model.transform.lossyScale.x)));
            }

            // Damped Transform보다 먼저 계산되어야 아래쪽 본이 밀린 뿌리를 따라간다
            int insertIndex = rig.childCount;
            for (int i = 0; i < rig.childCount; i++)
            {
                if (rig.GetChild(i).name.StartsWith(DAMPED_PREFIX))
                {
                    insertIndex = i;
                    break;
                }
            }

            List<MonoBehaviour> constraints = new List<MonoBehaviour>();
            foreach ((Transform root, Vector3 target) in targets)
            {
                GameObject anchor = new GameObject($"{ANCHOR_PREFIX}{root.name}");
                anchor.transform.SetParent(pelvis, false);
                anchor.transform.position = target;
                anchor.transform.rotation = root.rotation;

                GameObject child = new GameObject($"{FLARE_PREFIX}{root.name}");
                child.transform.SetParent(rig, false);
                child.transform.SetSiblingIndex(insertIndex++);

                MultiPositionConstraint constraint = child.AddComponent<MultiPositionConstraint>();
                MultiPositionConstraintData data = constraint.data;
                data.constrainedObject = root;
                data.constrainedXAxis  = true;
                data.constrainedYAxis  = true;
                data.constrainedZAxis  = true;
                data.maintainOffset    = false;

                WeightedTransformArray sources = new WeightedTransformArray(1);
                sources[0] = new WeightedTransform(anchor.transform, 1f);
                data.sourceObjects = sources;

                constraint.data   = data;
                constraint.weight = 0f;
                constraints.Add(constraint);
            }

            CreateMotionGate(model, constraints);
            return true;
        }

        // 이전에 만든 제약과 기준점과 던지기 문만 지운다, 다른 모션 문은 건드리지 않는다
        private static void RemoveOld(GameObject model, Transform rig, Transform pelvis)
        {
            for (int i = rig.childCount - 1; i >= 0; i--)
            {
                if (rig.GetChild(i).name.StartsWith(FLARE_PREFIX))
                {
                    Object.DestroyImmediate(rig.GetChild(i).gameObject);
                }
            }

            for (int i = pelvis.childCount - 1; i >= 0; i--)
            {
                if (pelvis.GetChild(i).name.StartsWith(ANCHOR_PREFIX))
                {
                    Object.DestroyImmediate(pelvis.GetChild(i).gameObject);
                }
            }

            foreach (SkirtMotionGate gate in model.GetComponents<SkirtMotionGate>())
            {
                if (new SerializedObject(gate).FindProperty("_stateName").stringValue == THROW_STATE)
                {
                    Object.DestroyImmediate(gate);
                }
            }
        }

        private static void CreateMotionGate(GameObject model, List<MonoBehaviour> constraints)
        {
            SkirtMotionGate gate = model.AddComponent<SkirtMotionGate>();
            SerializedObject gateObject = new SerializedObject(gate);
            gateObject.FindProperty("_animator").objectReferenceValue = model.GetComponent<Animator>();
            gateObject.FindProperty("_stateName").stringValue         = THROW_STATE;
            gateObject.FindProperty("_blendSeconds").floatValue       = BLEND;

            SerializedProperty list = gateObject.FindProperty("_constraints");
            list.arraySize = constraints.Count;
            for (int i = 0; i < constraints.Count; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = constraints[i];
            }

            gateObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Dictionary<string, Transform> CollectBones(Transform model)
        {
            Dictionary<string, Transform> bones = new Dictionary<string, Transform>();

            foreach (Transform bone in model.GetComponentsInChildren<Transform>(true))
            {
                bool isNeeded = bone.name.StartsWith("skirt_") || (bone.name == PELVIS_NAME);
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
    }
}
