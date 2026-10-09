using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace Shinshi.Editor
{
    /// <summary> 안전한 테스트 씬 생성 결과 </summary>
    public sealed class UiSandboxBuildResult
    {
        public bool Success { get; internal set; }
        public string Message { get; internal set; } = string.Empty;
        public string ScenePath { get; internal set; } = string.Empty;
        public Scene Scene { get; internal set; }
        public GameObject[] Roots { get; internal set; } = Array.Empty<GameObject>();
        public UiSandboxSanitizeReport Report { get; internal set; }
    }

    public enum EUiSandboxActiveRestore
    {
        None,
        Original,
        Reopened
    }

    /// <summary> 테스트 씬 생성이 중간에 실패했을 때 무엇을 되돌려야 하는지 정하는 순수 판정. 씬 · 파일 조작은 호출자가 한다 </summary>
    public sealed class UiSandboxRollbackPlan
    {
        /// <summary> 복사 씬을 닫으면 열린 씬이 하나도 남지 않을 때 true. 임시 빈 씬을 먼저 만들어야 한다 </summary>
        public bool NeedsPlaceholder { get; private set; }

        /// <summary> 대상 파일을 쓰려 했고 원래 있던 파일이면 true. 백업한 내용으로 되돌린다 </summary>
        public bool RestoreTargetFile { get; private set; }

        /// <summary> 대상 파일을 쓰려 했고 원래 없던 파일이면 true. 이번에 만든 파일을 지운다 </summary>
        public bool DeleteCreatedTarget { get; private set; }

        private UiSandboxRollbackPlan()
        {
        }

        public static UiSandboxRollbackPlan Create(bool copyOpen, int openSceneCount, bool targetExistedBefore, bool targetTouched)
        {
            return new UiSandboxRollbackPlan
            {
                NeedsPlaceholder    = (copyOpen) && (openSceneCount <= 1),
                RestoreTargetFile   = (targetTouched) && (targetExistedBefore),
                DeleteCreatedTarget = (targetTouched) && (!targetExistedBefore)
            };
        }

        /// <summary> 닫으려는 씬이 빌드 시작 때 활성이던 씬이면 true. 복사 씬을 활성으로 바꾼 뒤의 현재 활성 씬이 아니라 시작 때 잡아 둔 씬과 비교한다 </summary>
        public static bool IsOriginalActive(Scene scene, Scene originalActive)
        {
            return (originalActive.IsValid()) && (scene.IsValid()) && (scene.handle == originalActive.handle);
        }

        /// <summary> 롤백 뒤 활성 씬을 어디로 되돌릴지. 시작 때 활성 씬이 아직 열려 있으면 그대로, 닫혔다면 다시 연 씬으로 복원한다 </summary>
        public static EUiSandboxActiveRestore ChooseActiveRestore(bool originalActiveStillLoaded, bool reopenedActiveLoaded)
        {
            if (originalActiveStillLoaded)
            {
                return EUiSandboxActiveRestore.Original;
            }

            return reopenedActiveLoaded ? EUiSandboxActiveRestore.Reopened : EUiSandboxActiveRestore.None;
        }
    }

    /// <summary> 화면의 원본 씬을 미리보기 씬으로 읽어 새 씬에 복사하고, 네트워크와 실제 Binder를 정리한 뒤 Assets/00.Scenes/Dev_UiSandbox/ 에만 저장한다. 원본 씬 · 프리팹은 저장하지 않고 저장 안 된 사용자 씬은 닫지 않는다 </summary>
    public static class UiSandboxSceneBuilder
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SCENES_ROOT_FOLDER = "Assets/00.Scenes";
        private const string SANDBOX_FOLDER_NAME = "Dev_UiSandbox";
        private const string MARKER_OBJECT_NAME = "[UiSandbox 테스트 사본]";

        /****************************************
        *               Queries
        ****************************************/

        /// <summary> 지금 활성 씬이 샌드박스가 만든 사본(폴더 경로와 소유 라벨 모두 일치)이면 true. 폴더 안의 사용자 파일은 해당하지 않는다 </summary>
        public static bool TryGetActiveSandboxScene(out Scene scene)
        {
            scene = SceneManager.GetActiveScene();
            return (scene.IsValid()) && (scene.isLoaded) && (IsOwnedSandboxScenePath(scene.path));
        }

        /// <summary> 화면 코드를 연결해도 되는 유일한 씬을 찾는다. TryGetActiveSandboxScene과 같은 규칙이다 </summary>
        public static bool TryGetActiveOwnedSandboxScene(out Scene scene)
        {
            return TryGetActiveSandboxScene(out scene);
        }

        /// <summary> 경로가 샌드박스 폴더의 씬이고 에셋에 소유 라벨이 붙어 있으면 true </summary>
        public static bool IsOwnedSandboxScenePath(string assetPath)
        {
            if (!UiSandboxSafetyPolicy.IsSandboxScenePath(assetPath))
            {
                return false;
            }

            return UiSandboxSafetyPolicy.IsOwnedSandboxScene(assetPath, GetSceneLabels(assetPath));
        }

        private static string[] GetSceneLabels(string assetPath)
        {
            SceneAsset asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(assetPath);
            return (asset != null) ? AssetDatabase.GetLabels(asset) : Array.Empty<string>();
        }

        /// <summary> 화면에 따라 원본 씬 사본을 만들지 빈 씬을 만들지 정한다. 원본 경로가 있는 화면만 사본을 만든다 </summary>
        public static bool UsesSourceCopy(IUiSandboxPage page)
        {
            return (page != null) && (page.HasView) && (!string.IsNullOrWhiteSpace(page.SourceScenePath));
        }

        /// <summary> 안전한 테스트 씬 열기 컨트롤을 쓸 수 있는지 판정한다. 계약 전용 화면도 빈 씬을 열 수 있고, Play 중에만 막는다 </summary>
        public static bool CanOpenSafeScene(IUiSandboxPage page, bool playingOrWillPlay)
        {
            return (page != null) && (!playingOrWillPlay);
        }

        /// <summary> 씬의 루트 오브젝트를 배열로 돌려준다 </summary>
        public static GameObject[] GetRoots(Scene scene)
        {
            return (scene.IsValid() && scene.isLoaded) ? scene.GetRootGameObjects() : Array.Empty<GameObject>();
        }

        /// <summary> 저장하지 않은 변경이 있는 열린 씬 이름 목록. 샌드박스가 만든 사본은 다시 만들 수 있는 산출물이라 제외하고, 폴더 안의 사용자 파일은 포함한다 </summary>
        public static List<string> FindDirtySceneNames()
        {
            List<string> names = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if ((scene.IsValid()) && (scene.isLoaded) && (scene.isDirty)
                    && (!IsOwnedSandboxScenePath(scene.path)))
                {
                    names.Add(string.IsNullOrEmpty(scene.path) ? "(저장 안 된 새 씬)" : scene.path);
                }
            }

            return names;
        }

        /// <summary> 저장하지 않은 변경이 있는 열린 샌드박스 소유 사본 경로 목록. 다시 만들면 이 변경이 사라진다 </summary>
        public static List<string> FindDirtySandboxSceneNames()
        {
            List<string> names = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if ((scene.IsValid()) && (scene.isLoaded) && (scene.isDirty)
                    && (IsOwnedSandboxScenePath(scene.path)))
                {
                    names.Add(scene.path);
                }
            }

            return names;
        }

        /// <summary> 복사를 시작해도 되는지 검사한다. 하나라도 어긋나면 구체적인 안내를 error에 담는다. 저장 안 된 샌드박스 사본이 열려 있으면 거절한다 </summary>
        public static bool CheckPreconditions(IUiSandboxPage page, out string error, out string sourcePath, out string targetPath)
        {
            return CheckPreconditions(page, false, out error, out sourcePath, out targetPath);
        }

        /// <summary> discardDirtySandbox가 true면 저장 안 된 샌드박스 사본을 버리고 다시 만드는 것을 사용자가 명시한 것으로 본다 </summary>
        public static bool CheckPreconditions(IUiSandboxPage page, bool discardDirtySandbox, out string error, out string sourcePath, out string targetPath)
        {
            sourcePath = string.Empty;
            targetPath = string.Empty;

            if (page == null)
            {
                error = "화면이 선택되지 않았다";
                return false;
            }

            if (string.IsNullOrWhiteSpace(page.SourceScenePath))
            {
                error = $"'{page.Title}'은 계약 전용 화면이라 복사할 원본 씬이 없다. 창의 편집 컨트롤과 가짜 요청으로 값을 확인한다. 필요한 화면: {page.Requirement}";
                return false;
            }

            if (!UiSandboxSafetyPolicy.IsValidSourceScenePath(page.SourceScenePath, out error))
            {
                return false;
            }

            sourcePath = UiSandboxSafetyPolicy.NormalizePath(page.SourceScenePath);
            targetPath = UiSandboxSafetyPolicy.BuildSandboxScenePath(page.Id);

            if (!CheckRuntimeState(out error))
            {
                return false;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(sourcePath) == null)
            {
                error = $"원본 씬을 찾을 수 없다: {sourcePath}. 씬이 아직 없다면 '{page.Requirement}' 요구 사항을 먼저 만들고, 경로가 바뀌었다면 화면의 SourceScenePath를 고친다";
                return false;
            }

            return CheckTarget(targetPath, discardDirtySandbox, out error);
        }

        /// <summary> 원본 씬 없이 빈 안전 씬을 만들 수 있는지 검사한다. 계약 전용 화면이 Play 전용 요청을 바로 시험할 때 쓴다 </summary>
        public static bool CheckEmptyPreconditions(IUiSandboxPage page, bool discardDirtySandbox, out string error, out string targetPath)
        {
            targetPath = string.Empty;

            if (page == null)
            {
                error = "화면이 선택되지 않았다";
                return false;
            }

            if (string.IsNullOrWhiteSpace(page.Id))
            {
                error = "화면 Id가 비어 있어 테스트 씬 이름을 정할 수 없다";
                return false;
            }

            targetPath = UiSandboxSafetyPolicy.BuildSandboxScenePath(page.Id);

            if (!CheckRuntimeState(out error))
            {
                return false;
            }

            return CheckTarget(targetPath, discardDirtySandbox, out error);
        }

        private static bool CheckRuntimeState(out string error)
        {
            if ((EditorApplication.isPlayingOrWillChangePlaymode) || (Application.isPlaying))
            {
                error = "Play 중에는 테스트 씬을 만들 수 없다. Play를 끝낸 뒤 다시 시도한다";
                return false;
            }

            if (UiSandboxSafetyPolicy.IsNetworkActive(out string networkReason))
            {
                error = networkReason;
                return false;
            }

            error = null;
            return true;
        }

        private static bool CheckTarget(string targetPath, bool discardDirtySandbox, out string error)
        {
            SceneAsset existing = AssetDatabase.LoadAssetAtPath<SceneAsset>(targetPath);
            bool exists = (existing != null) || (File.Exists(targetPath));
            string[] labels = (existing != null) ? AssetDatabase.GetLabels(existing) : Array.Empty<string>();
            if (!UiSandboxSafetyPolicy.CanWriteScene(targetPath, exists, labels, out error))
            {
                return false;
            }

            List<string> dirty = FindDirtySceneNames();
            if (dirty.Count > 0)
            {
                error = $"저장하지 않은 변경이 있는 씬이 열려 있다: {string.Join(", ", dirty)}. 저장하거나 변경을 되돌린 뒤 다시 시도한다. 샌드박스는 저장 안 된 씬을 자동으로 버리지 않는다";
                return false;
            }

            if (!discardDirtySandbox)
            {
                List<string> dirtySandbox = FindDirtySandboxSceneNames();
                if (dirtySandbox.Count > 0)
                {
                    error = $"저장하지 않은 편집이 있는 테스트 씬이 열려 있다: {string.Join(", ", dirtySandbox)}. 이 편집을 버리고 다시 만든다고 명시해야 진행한다";
                    return false;
                }
            }

            error = null;
            return true;
        }

        /****************************************
        *                Build
        ****************************************/

        /// <summary> 원본 씬을 안전하게 복사해 샌드박스 사본을 만들고 그 씬만 연다. 기존 대상 씬은 복제 · 정리가 끝난 뒤에야 닫는다. 그 뒤 실패하면 닫은 씬을 다시 열고 활성 씬을 되돌리며, 쓰려던 대상 파일은 백업한 원래 내용으로 되돌린다(없던 파일이면 지운다). 경로 없는 빈 새 씬은 복원하지 못한다. 저장 안 된 샌드박스 사본이 있으면 거절한다 </summary>
        public static UiSandboxBuildResult BuildAndOpen(IUiSandboxPage page)
        {
            return BuildAndOpen(page, false);
        }

        public static UiSandboxBuildResult BuildAndOpen(IUiSandboxPage page, bool discardDirtySandbox)
        {
            if (!CheckPreconditions(page, discardDirtySandbox, out string error, out string sourcePath, out string targetPath))
            {
                return Fail(error);
            }

            return Build(page, sourcePath, targetPath, discardDirtySandbox);
        }

        /// <summary> 원본 없이 카메라 · 조명 · EventSystem만 있는 빈 안전 씬을 만들어 연다 </summary>
        public static UiSandboxBuildResult BuildEmptyAndOpen(IUiSandboxPage page)
        {
            return BuildEmptyAndOpen(page, false);
        }

        public static UiSandboxBuildResult BuildEmptyAndOpen(IUiSandboxPage page, bool discardDirtySandbox)
        {
            if (!CheckEmptyPreconditions(page, discardDirtySandbox, out string error, out string targetPath))
            {
                return Fail(error);
            }

            return Build(page, string.Empty, targetPath, discardDirtySandbox);
        }

        private static UiSandboxBuildResult Build(IUiSandboxPage page, string sourcePath, string targetPath, bool discardDirtySandbox)
        {
            bool fromSource = sourcePath.Length > 0;
            string error;
            Scene preview = default;
            Scene copy = default;
            Scene previousActive = SceneManager.GetActiveScene();
            bool previewOpened = false;
            bool copyCreated = false;
            bool finished = false;
            List<ClosedScene> closed = new List<ClosedScene>();
            bool targetExisted = false;
            bool targetTouched = false;
            byte[] targetBackup = null;

            try
            {
                if (fromSource)
                {
                    preview = EditorSceneManager.OpenPreviewScene(sourcePath);
                    previewOpened = true;
                    if (!preview.IsValid())
                    {
                        return Fail($"원본 씬을 미리보기로 열지 못했다: {sourcePath}");
                    }
                }

                copy = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                copyCreated = true;
                SceneManager.SetActiveScene(copy);

                GameObject[] clones = fromSource
                    ? CloneRoots(preview.GetRootGameObjects(), copy, preview)
                    : Array.Empty<GameObject>();

                UiSandboxSanitizeReport report = UiSandboxSceneSanitizer.Sanitize(clones);
                AddHelperObjects(report.RemainingRoots, page, fromSource ? sourcePath : "빈 씬");

                if (!EnsureOutputFolder(out error))
                {
                    return Fail(error);
                }

                // 복제 · 정리가 모두 끝난 뒤에만 기존 대상 씬을 닫고 파일을 쓴다. 이 아래에서 실패하면 RollBack이 닫은 씬을 다시 열고 기존 파일 내용을 되돌린다
                targetExisted = File.Exists(targetPath);
                targetBackup  = targetExisted ? File.ReadAllBytes(targetPath) : null;
                CloseOpenSceneAt(targetPath, copy, previousActive, discardDirtySandbox, closed);

                targetTouched = true;
                if (!EditorSceneManager.SaveScene(copy, targetPath))
                {
                    return Fail($"테스트 씬을 저장하지 못했다: {targetPath}");
                }

                MarkOwned(targetPath, report);

                CloseOtherScenes(copy, previousActive, discardDirtySandbox, closed);
                SceneManager.SetActiveScene(copy);
                finished = true;

                return new UiSandboxBuildResult
                {
                    Success   = true,
                    Message   = BuildSummary(page, targetPath, report, fromSource),
                    ScenePath = targetPath,
                    Scene     = copy,
                    Roots     = copy.GetRootGameObjects(),
                    Report    = report
                };
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return Fail($"테스트 씬을 만드는 중 예외가 났다 ({exception.GetType().Name}): {exception.Message}");
            }
            finally
            {
                if (previewOpened)
                {
                    EditorSceneManager.ClosePreviewScene(preview);
                }

                if ((!finished) && (copyCreated))
                {
                    RollBack(copy, previousActive, closed, targetPath, targetBackup, targetExisted, targetTouched);
                }
            }
        }

        private static GameObject[] CloneRoots(GameObject[] sourceRoots, Scene targetScene, Scene previewScene)
        {
            Dictionary<UnityEngine.Object, UnityEngine.Object> map = new Dictionary<UnityEngine.Object, UnityEngine.Object>();
            GameObject[] clones = new GameObject[sourceRoots.Length];

            for (int i = 0; i < sourceRoots.Length; ++i)
            {
                GameObject clone = UnityEngine.Object.Instantiate(sourceRoots[i]);
                clone.name = sourceRoots[i].name;
                if (clone.scene != targetScene)
                {
                    SceneManager.MoveGameObjectToScene(clone, targetScene);
                }

                clones[i] = clone;
                MapHierarchy(sourceRoots[i].transform, clone.transform, map);
            }

            RemapSceneReferences(clones, map, previewScene);
            return clones;
        }

        private static void MapHierarchy(Transform source, Transform clone, Dictionary<UnityEngine.Object, UnityEngine.Object> map)
        {
            map[source.gameObject] = clone.gameObject;

            Component[] sourceComponents = source.GetComponents<Component>();
            Component[] cloneComponents  = clone.GetComponents<Component>();
            int componentCount = Math.Min(sourceComponents.Length, cloneComponents.Length);
            for (int i = 0; i < componentCount; ++i)
            {
                if ((sourceComponents[i] != null) && (cloneComponents[i] != null)
                    && (sourceComponents[i].GetType() == cloneComponents[i].GetType()))
                {
                    map[sourceComponents[i]] = cloneComponents[i];
                }
            }

            int childCount = Math.Min(source.childCount, clone.childCount);
            for (int i = 0; i < childCount; ++i)
            {
                MapHierarchy(source.GetChild(i), clone.GetChild(i), map);
            }
        }

        // Instantiate는 복제한 계층 밖을 가리키는 참조를 원본 그대로 둔다. 다른 루트를 가리키는 참조는 사본으로 바꾸고, 짝이 없으면 미리보기 씬을 가리키지 않게 비운다
        private static void RemapSceneReferences(GameObject[] clones, Dictionary<UnityEngine.Object, UnityEngine.Object> map, Scene previewScene)
        {
            for (int i = 0; i < clones.Length; ++i)
            {
                Component[] components = clones[i].GetComponentsInChildren<Component>(true);
                for (int j = 0; j < components.Length; ++j)
                {
                    if ((components[j] == null) || (components[j] is Transform))
                    {
                        continue;
                    }

                    SerializedObject serialized = new SerializedObject(components[j]);
                    SerializedProperty iterator = serialized.GetIterator();
                    bool changed = false;
                    while (iterator.Next(true))
                    {
                        if ((iterator.propertyType != SerializedPropertyType.ObjectReference)
                            || (iterator.objectReferenceValue == null))
                        {
                            continue;
                        }

                        UnityEngine.Object value = iterator.objectReferenceValue;
                        if (!IsInScene(value, previewScene))
                        {
                            continue;
                        }

                        iterator.objectReferenceValue = map.TryGetValue(value, out UnityEngine.Object mapped) ? mapped : null;
                        changed = true;
                    }

                    if (changed)
                    {
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
            }
        }

        private static bool IsInScene(UnityEngine.Object value, Scene scene)
        {
            if (EditorUtility.IsPersistent(value))
            {
                return false;
            }

            GameObject go = value as GameObject;
            if (go == null)
            {
                Component component = value as Component;
                go = (component != null) ? component.gameObject : null;
            }

            return (go != null) && (go.scene == scene);
        }

        private static void AddHelperObjects(GameObject[] roots, IUiSandboxPage page, string sourcePath)
        {
            if (!HasComponent<Camera>(roots))
            {
                GameObject cameraObject = new GameObject("Sandbox Camera");
                cameraObject.tag = "MainCamera";
                cameraObject.transform.position = new Vector3(0.0f, 1.0f, -10.0f);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags      = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.12f, 0.12f, 0.14f, 1.0f);
                cameraObject.AddComponent<AudioListener>();
            }

            if (!HasComponent<Light>(roots))
            {
                GameObject lightObject = new GameObject("Sandbox Light");
                lightObject.transform.rotation = Quaternion.Euler(50.0f, -30.0f, 0.0f);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
            }

            if (!HasComponent<EventSystem>(roots))
            {
                GameObject eventObject = new GameObject("EventSystem");
                eventObject.AddComponent<EventSystem>();
                eventObject.AddComponent<StandaloneInputModule>();
            }

            new GameObject($"{MARKER_OBJECT_NAME} {page.Id} <- {sourcePath}");
        }

        private static bool HasComponent<T>(GameObject[] roots) where T : Component
        {
            for (int i = 0; i < roots.Length; ++i)
            {
                if ((roots[i] != null) && (roots[i].GetComponentInChildren<T>(true) != null))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool EnsureOutputFolder(out string error)
        {
            if (!AssetDatabase.IsValidFolder(SCENES_ROOT_FOLDER))
            {
                error = $"{SCENES_ROOT_FOLDER} 폴더가 없어 테스트 씬을 저장할 수 없다";
                return false;
            }

            if (!AssetDatabase.IsValidFolder(UiSandboxSafetyPolicy.SANDBOX_FOLDER))
            {
                string guid = AssetDatabase.CreateFolder(SCENES_ROOT_FOLDER, SANDBOX_FOLDER_NAME);
                if (string.IsNullOrEmpty(guid))
                {
                    error = $"{UiSandboxSafetyPolicy.SANDBOX_FOLDER} 폴더를 만들지 못했다";
                    return false;
                }
            }

            error = null;
            return true;
        }

        private static void MarkOwned(string targetPath, UiSandboxSanitizeReport report)
        {
            AssetDatabase.ImportAsset(targetPath);
            SceneAsset asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(targetPath);
            if (asset == null)
            {
                report.Warnings.Add("저장한 씬 에셋을 읽지 못해 소유 표식을 붙이지 못했다. 다음 생성 때 덮어쓰기가 거절될 수 있다");
                return;
            }

            List<string> labels = new List<string>(AssetDatabase.GetLabels(asset));
            if (!labels.Contains(UiSandboxSafetyPolicy.OWNERSHIP_LABEL))
            {
                labels.Add(UiSandboxSafetyPolicy.OWNERSHIP_LABEL);
                AssetDatabase.SetLabels(asset, labels.ToArray());
            }
        }

        // 같은 경로의 이전 사본이 열려 있으면 덮어쓰기 전에 닫는다. 저장 안 된 편집은 사용자가 버리기를 명시했을 때만 버린다
        private static void CloseOpenSceneAt(string path, Scene keep, Scene originalActive, bool discardDirtySandbox, List<ClosedScene> closed)
        {
            for (int i = SceneManager.sceneCount - 1; i >= 0; --i)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if ((scene.handle != keep.handle) && (string.Equals(scene.path, path, StringComparison.OrdinalIgnoreCase)))
                {
                    if (!UiSandboxSafetyPolicy.CanDiscardScene(scene.path, GetSceneLabels(scene.path), scene.isDirty, discardDirtySandbox))
                    {
                        throw new InvalidOperationException($"저장 안 된 편집이 있거나 샌드박스가 만들지 않은 씬은 닫지 않는다: {scene.path}");
                    }

                    CloseAndRecord(scene, originalActive, closed);
                }
            }
        }

        private static void CloseOtherScenes(Scene keep, Scene originalActive, bool discardDirtySandbox, List<ClosedScene> closed)
        {
            for (int i = SceneManager.sceneCount - 1; i >= 0; --i)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.handle == keep.handle)
                {
                    continue;
                }

                bool discardable = UiSandboxSafetyPolicy.CanDiscardScene(scene.path, GetSceneLabels(scene.path), scene.isDirty, discardDirtySandbox);
                if (discardable)
                {
                    CloseAndRecord(scene, originalActive, closed);
                }
            }
        }

        private static void CloseAndRecord(Scene scene, Scene originalActive, List<ClosedScene> closed)
        {
            ClosedScene record = new ClosedScene(scene.path, scene.isLoaded, UiSandboxRollbackPlan.IsOriginalActive(scene, originalActive));
            EditorSceneManager.CloseScene(scene, true);
            closed.Add(record);
        }

        // 실패한 생성을 되돌린다. 복사 씬을 닫고, 저장하려던 대상 파일을 원래 내용으로 되돌리거나(없던 파일이면 지우고), 닫았던 씬을 다시 열고 활성 씬을 복원한다.
        // 경로 없이 저장되지 않은 빈 새 씬은 다시 열 수 없다. 저장 안 된 사용자 씬은 애초에 닫지 않으므로 이 경우에 해당하지 않는다
        private static void RollBack(Scene copy, Scene previousActive, List<ClosedScene> closed, string targetPath, byte[] targetBackup,
            bool targetExisted, bool targetTouched)
        {
            Scene placeholder = default;
            try
            {
                bool copyOpen = (copy.IsValid()) && (copy.isLoaded);
                UiSandboxRollbackPlan plan = UiSandboxRollbackPlan.Create(copyOpen, SceneManager.sceneCount, targetExisted, targetTouched);

                if (plan.NeedsPlaceholder)
                {
                    placeholder = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                }

                if (copyOpen)
                {
                    EditorSceneManager.CloseScene(copy, true);
                }

                if ((plan.RestoreTargetFile) && (targetBackup != null))
                {
                    File.WriteAllBytes(targetPath, targetBackup);
                    AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceUpdate);
                }
                else if ((plan.DeleteCreatedTarget) && (UiSandboxSafetyPolicy.IsSandboxScenePath(targetPath)))
                {
                    AssetDatabase.DeleteAsset(targetPath);
                }

                Scene restoredActive = default;
                for (int i = 0; i < closed.Count; ++i)
                {
                    ClosedScene record = closed[i];
                    if ((string.IsNullOrEmpty(record.Path)) || (!File.Exists(record.Path)))
                    {
                        continue;
                    }

                    Scene reopened = EditorSceneManager.OpenScene(record.Path, record.WasLoaded ? OpenSceneMode.Additive : OpenSceneMode.AdditiveWithoutLoading);
                    if (record.WasActive)
                    {
                        restoredActive = reopened;
                    }
                }

                EUiSandboxActiveRestore restore = UiSandboxRollbackPlan.ChooseActiveRestore(
                    (previousActive.IsValid()) && (previousActive.isLoaded),
                    (restoredActive.IsValid()) && (restoredActive.isLoaded));
                if (restore == EUiSandboxActiveRestore.Original)
                {
                    SceneManager.SetActiveScene(previousActive);
                }
                else if (restore == EUiSandboxActiveRestore.Reopened)
                {
                    SceneManager.SetActiveScene(restoredActive);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                if ((placeholder.IsValid()) && (placeholder.isLoaded) && (SceneManager.sceneCount > 1))
                {
                    EditorSceneManager.CloseScene(placeholder, true);
                }
            }
        }

        private readonly struct ClosedScene
        {
            public readonly string Path;
            public readonly bool WasLoaded;
            public readonly bool WasActive;

            public ClosedScene(string path, bool wasLoaded, bool wasActive)
            {
                Path      = path;
                WasLoaded = wasLoaded;
                WasActive = wasActive;
            }
        }

        private static string BuildSummary(IUiSandboxPage page, string targetPath, UiSandboxSanitizeReport report, bool fromSource)
        {
            if (!fromSource)
            {
                return $"'{page.Title}' 빈 테스트 씬을 열었다: {targetPath}. 원본 씬 없이 카메라 · 조명 · EventSystem만 있고 네트워크 컴포넌트는 없다";
            }

            StringBuilder builder = new StringBuilder();
            builder.Append($"'{page.Title}' 테스트 씬을 열었다: {targetPath}. ");
            builder.Append($"프리팹 인스턴스 {report.UnpackedPrefabs}개 해제, 컴포넌트 {report.RemovedComponents}개 제거, ");
            builder.Append($"이벤트 호출 {report.StrippedPersistentCalls}개 제거, 빈 오브젝트 {report.RemovedObjects}개 제거");
            if (report.HasWarnings)
            {
                builder.Append($". 경고 {report.Warnings.Count}건: {string.Join(" / ", report.Warnings)}");
            }

            return builder.ToString();
        }

        private static UiSandboxBuildResult Fail(string message)
        {
            return new UiSandboxBuildResult { Success = false, Message = message };
        }
    }
}
