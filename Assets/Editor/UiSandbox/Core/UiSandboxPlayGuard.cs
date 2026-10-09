using System.Collections.Generic;
using Mirror;
using Shinshi.Common;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shinshi.Editor
{
    /// <summary> 샌드박스 씬에서 Play가 실제 네트워크를 건드리지 않게 막는 안전장치. 샌드박스 사본이 활성 씬일 때만 동작한다 </summary>
    public static class UiSandboxPlayGuard
    {
        /****************************************
        *                Methods
        ****************************************/

        [InitializeOnLoadMethod]
        private static void Register()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary> 씬에 위험 컴포넌트가 남아 있는지 검사한다. 안전하면 true </summary>
        public static bool CheckScene(Scene scene, out string error)
        {
            List<Component> unsafeComponents = UiSandboxSceneSanitizer.FindUnsafeComponents(UiSandboxSceneBuilder.GetRoots(scene));
            if (unsafeComponents.Count == 0)
            {
                error = null;
                return true;
            }

            Component first = unsafeComponents[0];
            error = $"샌드박스 씬에 네트워크 · 실제 Binder 컴포넌트가 남아 있다 ({first.gameObject.name}/{first.GetType().Name} 외 {unsafeComponents.Count - 1}개). 테스트 사본을 다시 만든다";
            return false;
        }

        /// <summary> 열려 있는 모든 씬을 검사한다. 샌드박스 사본과 함께 열린 다른 씬의 위험 컴포넌트도 Play에서 살아나기 때문이다 </summary>
        public static bool CheckAllLoadedScenes(out string error)
        {
            List<Scene> scenes = new List<Scene>(SceneManager.sceneCount);
            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                scenes.Add(SceneManager.GetSceneAt(i));
            }

            return CheckScenes(scenes, out error);
        }

        /// <summary> 주어진 씬 목록만 검사한다. 위험한 씬이 하나라도 있으면 false이고 error에는 그 씬의 이름이 들어간다. 목록 순서와 다른 씬의 존재에 영향받지 않는다 </summary>
        public static bool CheckScenes(IReadOnlyList<Scene> scenes, out string error)
        {
            if (scenes != null)
            {
                for (int i = 0; i < scenes.Count; ++i)
                {
                    Scene scene = scenes[i];
                    if ((!scene.IsValid()) || (!scene.isLoaded))
                    {
                        continue;
                    }

                    if (!CheckScene(scene, out string sceneError))
                    {
                        string label = string.IsNullOrEmpty(scene.path) ? scene.name : scene.path;
                        error = $"[{label}] {sceneError}";
                        return false;
                    }
                }
            }

            error = null;
            return true;
        }

        /// <summary> 서버 역할 Play 예약 값이 남아 있으면 거절한다. 값은 읽기만 하고 지우거나 바꾸지 않는다 </summary>
        public static bool CheckPendingServerRole(string pendingRoleValue, out string error)
        {
            if (string.IsNullOrEmpty(pendingRoleValue))
            {
                error = null;
                return true;
            }

            error = $"서버 역할 Play 예약({pendingRoleValue})이 남아 있어 부팅 때 서버가 켜진다. Shinshi Play 메뉴의 클라이언트 Play로 예약을 해제한 뒤 다시 시도한다";
            return false;
        }

        private static bool CheckPlayPreconditions(out string error)
        {
            string pending = SessionState.GetString(Constants.Network.EDITOR_SERVER_ROLE_KEY, string.Empty);
            if (!CheckPendingServerRole(pending, out error))
            {
                return false;
            }

            return CheckAllLoadedScenes(out error);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.ExitingEditMode:
                    if ((UiSandboxSceneBuilder.TryGetActiveSandboxScene(out _)) && (!CheckPlayPreconditions(out string error)))
                    {
                        Debug.LogError($"[UiSandbox] Play를 막았다: {error}");
                        EditorApplication.isPlaying = false;
                    }

                    break;

                case PlayModeStateChange.EnteredPlayMode:
                    if (UiSandboxSceneBuilder.TryGetActiveSandboxScene(out _))
                    {
                        GuardRuntime();
                        StartRuntimeMonitor();
                    }

                    break;

                case PlayModeStateChange.ExitingPlayMode:
                case PlayModeStateChange.EnteredEditMode:
                    StopRuntimeMonitor();
                    break;
            }
        }

        // Play 진입 뒤에 접속이 생기는 경우도 잡도록 샌드박스 씬 Play 동안만 매 업데이트 검사한다. 전역 연결 해제는 하지 않고 Play만 끝낸다
        private static void StartRuntimeMonitor()
        {
            EditorApplication.update -= OnRuntimeUpdate;
            EditorApplication.update += OnRuntimeUpdate;
        }

        private static void StopRuntimeMonitor()
        {
            EditorApplication.update -= OnRuntimeUpdate;
        }

        private static void OnRuntimeUpdate()
        {
            if (!EditorApplication.isPlaying)
            {
                StopRuntimeMonitor();
                return;
            }

            if (!UiSandboxSceneBuilder.TryGetActiveSandboxScene(out _))
            {
                return;
            }

            GuardRuntime();
        }

        // Play 중 RuntimeInitializeOnLoad로 생기는 접속 객체가 살아나면 즉시 Play를 끝낸다
        private static void GuardRuntime()
        {
            if (UiSandboxSafetyPolicy.IsNetworkActive(out string reason))
            {
                Debug.LogError($"[UiSandbox] 네트워크가 켜져 Play를 끝냈다: {reason}");
                EditorApplication.isPlaying = false;
                StopRuntimeMonitor();
                return;
            }

            if (NetworkManager.singleton != null)
            {
                Debug.LogError("[UiSandbox] NetworkManager가 생겨 Play를 끝냈다. 테스트 사본에 NetworkManager가 있으면 안 된다");
                EditorApplication.isPlaying = false;
                StopRuntimeMonitor();
            }
        }
    }
}
