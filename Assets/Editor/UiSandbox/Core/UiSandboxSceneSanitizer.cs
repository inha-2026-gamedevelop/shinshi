using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Shinshi.Editor
{
    /// <summary> 샌드박스 사본 정리 결과 </summary>
    public sealed class UiSandboxSanitizeReport
    {
        public int UnpackedPrefabs { get; internal set; }
        public int RemovedComponents { get; internal set; }
        public int StrippedPersistentCalls { get; internal set; }
        public int RemovedObjects { get; internal set; }
        public GameObject[] RemainingRoots { get; internal set; } = Array.Empty<GameObject>();
        public List<string> Details { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();

        public bool HasWarnings
        {
            get { return Warnings.Count > 0; }
        }
    }

    /// <summary> 복사한 씬에서 네트워크 · 실제 Binder · 옛 데모 컴포넌트와 위험한 UnityEvent 영속 호출을 제거한다. 비활성화로는 UnityEvent 호출이 막히지 않으므로 컴포넌트를 파괴하고 호출 항목을 지운다 </summary>
    public static class UiSandboxSceneSanitizer
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string PERSISTENT_CALLS_SUFFIX = ".m_PersistentCalls.m_Calls";
        private const string PERSISTENT_CALLS_ROOT   = "m_PersistentCalls.m_Calls";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 루트 아래를 제자리에서 정리한다. 프리팹 원본은 건드리지 않고 씬 안의 인스턴스만 풀어 쓴다 </summary>
        public static UiSandboxSanitizeReport Sanitize(IList<GameObject> roots)
        {
            UiSandboxSanitizeReport report = new UiSandboxSanitizeReport();
            if ((roots == null) || (roots.Count == 0))
            {
                return report;
            }

            report.UnpackedPrefabs = UnpackPrefabInstances(roots, report);

            HashSet<GameObject> affected = new HashSet<GameObject>();
            report.RemovedComponents = RemoveUnsafeComponents(roots, affected, report);
            report.StrippedPersistentCalls = StripPersistentCalls(roots, report);
            report.RemovedObjects = RemoveEmptyObjects(affected, report);

            List<GameObject> remaining = new List<GameObject>(roots.Count);
            for (int i = 0; i < roots.Count; ++i)
            {
                if (roots[i] != null)
                {
                    remaining.Add(roots[i]);
                }
            }

            report.RemainingRoots = remaining.ToArray();
            return report;
        }

        /// <summary> 루트 아래에 남은 위험 컴포넌트를 찾는다. 정리 후 검증과 Play 직전 점검에 쓴다 </summary>
        public static List<Component> FindUnsafeComponents(IEnumerable<GameObject> roots)
        {
            List<Component> found = new List<Component>();
            if (roots == null)
            {
                return found;
            }

            foreach (GameObject root in roots)
            {
                if (root == null)
                {
                    continue;
                }

                Component[] all = root.GetComponentsInChildren<Component>(true);
                for (int i = 0; i < all.Length; ++i)
                {
                    if ((all[i] != null) && (UiSandboxSafetyPolicy.IsUnsafeComponentType(all[i].GetType())))
                    {
                        found.Add(all[i]);
                    }
                }
            }

            return found;
        }

        private static int UnpackPrefabInstances(IList<GameObject> roots, UiSandboxSanitizeReport report)
        {
            HashSet<GameObject> unpacked = new HashSet<GameObject>();
            List<Transform> transforms = new List<Transform>();
            for (int i = 0; i < roots.Count; ++i)
            {
                if (roots[i] != null)
                {
                    transforms.AddRange(roots[i].GetComponentsInChildren<Transform>(true));
                }
            }

            int count = 0;
            for (int i = 0; i < transforms.Count; ++i)
            {
                GameObject go = transforms[i].gameObject;
                if (!PrefabUtility.IsPartOfPrefabInstance(go))
                {
                    continue;
                }

                GameObject outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
                if ((outermost == null) || (!unpacked.Add(outermost)))
                {
                    continue;
                }

                if (PrefabUtility.GetPrefabInstanceStatus(outermost) != PrefabInstanceStatus.Connected)
                {
                    report.Warnings.Add($"프리팹 원본을 찾을 수 없어 풀지 못했다: {outermost.name}");
                    continue;
                }

                PrefabUtility.UnpackPrefabInstance(outermost, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                ++count;
            }

            return count;
        }

        private static int RemoveUnsafeComponents(IList<GameObject> roots, HashSet<GameObject> affected, UiSandboxSanitizeReport report)
        {
            List<Component> candidates = FindUnsafeComponents(roots);
            int removed = 0;
            bool progress = true;

            while ((candidates.Count > 0) && (progress))
            {
                progress = false;
                for (int i = candidates.Count - 1; i >= 0; --i)
                {
                    Component component = candidates[i];
                    if (component == null)
                    {
                        candidates.RemoveAt(i);
                        continue;
                    }

                    if (IsRequiredByOther(component))
                    {
                        continue;
                    }

                    GameObject owner = component.gameObject;
                    string typeName  = component.GetType().Name;
                    affected.Add(owner);
                    UnityEngine.Object.DestroyImmediate(component);
                    report.Details.Add($"컴포넌트 제거: {owner.name}/{typeName}");
                    candidates.RemoveAt(i);
                    ++removed;
                    progress = true;
                }
            }

            for (int i = 0; i < candidates.Count; ++i)
            {
                if (candidates[i] == null)
                {
                    continue;
                }

                Behaviour behaviour = candidates[i] as Behaviour;
                if (behaviour != null)
                {
                    behaviour.enabled = false;
                }

                report.Warnings.Add($"다른 컴포넌트가 요구해 제거하지 못했다(비활성화만 함): {candidates[i].gameObject.name}/{candidates[i].GetType().Name}");
            }

            return removed;
        }

        private static bool IsRequiredByOther(Component target)
        {
            Type targetType = target.GetType();
            Component[] siblings = target.GetComponents<Component>();
            for (int i = 0; i < siblings.Length; ++i)
            {
                Component sibling = siblings[i];
                if ((sibling == null) || (sibling == target))
                {
                    continue;
                }

                object[] attributes = sibling.GetType().GetCustomAttributes(typeof(RequireComponent), true);
                for (int j = 0; j < attributes.Length; ++j)
                {
                    RequireComponent require = (RequireComponent)attributes[j];
                    if ((IsAssignable(require.m_Type0, targetType))
                        || (IsAssignable(require.m_Type1, targetType))
                        || (IsAssignable(require.m_Type2, targetType)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsAssignable(Type required, Type actual)
        {
            return (required != null) && (required.IsAssignableFrom(actual));
        }

        private static int StripPersistentCalls(IList<GameObject> roots, UiSandboxSanitizeReport report)
        {
            int stripped = 0;
            for (int i = 0; i < roots.Count; ++i)
            {
                if (roots[i] == null)
                {
                    continue;
                }

                Component[] components = roots[i].GetComponentsInChildren<Component>(true);
                for (int j = 0; j < components.Length; ++j)
                {
                    if (components[j] != null)
                    {
                        stripped += StripPersistentCalls(components[j], report);
                    }
                }
            }

            return stripped;
        }

        private static int StripPersistentCalls(Component component, UiSandboxSanitizeReport report)
        {
            if (component is Transform)
            {
                return 0;
            }

            SerializedObject serialized = new SerializedObject(component);
            List<string> arrayPaths = CollectCallArrayPaths(serialized);
            if (arrayPaths.Count == 0)
            {
                return 0;
            }

            int stripped = 0;
            for (int p = 0; p < arrayPaths.Count; ++p)
            {
                SerializedProperty calls = serialized.FindProperty(arrayPaths[p]);
                if (calls == null)
                {
                    continue;
                }

                for (int index = calls.arraySize - 1; index >= 0; --index)
                {
                    SerializedProperty call = calls.GetArrayElementAtIndex(index);
                    UnityEngine.Object target = call.FindPropertyRelative("m_Target")?.objectReferenceValue;
                    string typeName   = call.FindPropertyRelative("m_TargetAssemblyTypeName")?.stringValue;
                    string methodName = call.FindPropertyRelative("m_MethodName")?.stringValue;

                    if (!UiSandboxSafetyPolicy.IsUnsafePersistentCall(target, typeName, methodName, out string reason))
                    {
                        continue;
                    }

                    calls.DeleteArrayElementAtIndex(index);
                    ++stripped;
                    report.Details.Add($"이벤트 호출 제거: {component.gameObject.name}/{component.GetType().Name} -> {methodName} ({reason})");
                }
            }

            if (stripped > 0)
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            return stripped;
        }

        private static List<string> CollectCallArrayPaths(SerializedObject serialized)
        {
            List<string> paths = new List<string>();
            SerializedProperty iterator = serialized.GetIterator();
            bool enterChildren = true;
            while (iterator.Next(enterChildren))
            {
                enterChildren = true;
                if ((iterator.isArray)
                    && ((iterator.propertyPath.EndsWith(PERSISTENT_CALLS_SUFFIX, StringComparison.Ordinal))
                        || (iterator.propertyPath == PERSISTENT_CALLS_ROOT)))
                {
                    paths.Add(iterator.propertyPath);
                    enterChildren = false;
                }
            }

            return paths;
        }

        private static int RemoveEmptyObjects(HashSet<GameObject> affected, UiSandboxSanitizeReport report)
        {
            List<GameObject> ordered = new List<GameObject>(affected);
            ordered.Sort((a, b) => GetDepth(b).CompareTo(GetDepth(a)));

            int removed = 0;
            for (int i = 0; i < ordered.Count; ++i)
            {
                GameObject go = ordered[i];
                if ((go == null) || (go.transform.childCount > 0) || (go.GetComponents<Component>().Length > 1))
                {
                    continue;
                }

                string name = go.name;
                UnityEngine.Object.DestroyImmediate(go);
                report.Details.Add($"빈 오브젝트 제거: {name}");
                ++removed;
            }

            return removed;
        }

        private static int GetDepth(GameObject go)
        {
            if (go == null)
            {
                return -1;
            }

            int depth = 0;
            Transform current = go.transform.parent;
            while (current != null)
            {
                ++depth;
                current = current.parent;
            }

            return depth;
        }
    }
}
