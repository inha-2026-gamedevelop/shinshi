using System;
using System.Collections.Generic;
using System.IO;
using Mirror;
using UnityEngine;

namespace Shinshi.Editor
{
    /// <summary> 샌드박스 씬 복사의 경로 · 소유권 · 위험 컴포넌트 판정 규칙. 모두 순수 판정이라 씬을 건드리지 않고 테스트할 수 있다 </summary>
    public static class UiSandboxSafetyPolicy
    {
        /****************************************
        *                Fields
        ****************************************/

        /// <summary> 샌드박스가 쓰기를 허용하는 유일한 폴더 </summary>
        public const string SANDBOX_FOLDER = "Assets/00.Scenes/Dev_UiSandbox";

        /// <summary> 샌드박스가 만든 씬 에셋에 붙이는 에셋 라벨. 이 라벨이 있는 파일만 덮어쓴다 </summary>
        public const string OWNERSHIP_LABEL = "UiSandboxGenerated";

        public const string SCENE_FILE_PREFIX = "UiSandbox_";
        public const string SCENE_EXTENSION   = ".unity";

        private const int MAX_PAGE_ID_LENGTH = 64;

        // 호출하면 씬 이동 · 접속 · 요청이 일어나는 메서드 이름 조각
        private static readonly string[] G_UnsafeMethodTokens =
        {
            "LoadScene", "GoTo", "Quit", "Connect", "Login", "Logout", "Http", "Send", "Request"
        };

        // 네임스페이스나 접미사 규칙에 안 걸리지만 서버 · 접속 상태를 읽거나 보내는 UI 클래스
        private static readonly string[] G_UnsafeTypeNames =
        {
            "PlayerInteractionPanelOpener", "NetworkOverlayView", "UiMoveClickMarkerBinder", "SceneLoader", "SceneTransition",
            "HudChatView"
        };

        // 네임스페이스와 무관하게 통째로 위험한 어셈블리. 서버 어셈블리는 Shinshi.Combat 같은 도메인 네임스페이스를 쓴다
        private static readonly string[] G_UnsafeAssemblyNames =
        {
            "Shinshi.Server", "Shinshi.Dev"
        };

        /****************************************
        *                Paths
        ****************************************/

        public static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Replace('\\', '/');
        }

        /// <summary> 샌드박스 폴더 바로 아래의 .unity 파일인지 검사한다. 상위 폴더 이동이나 하위 폴더는 허용하지 않는다 </summary>
        public static bool IsSandboxScenePath(string assetPath)
        {
            string path = NormalizePath(assetPath);
            string prefix = SANDBOX_FOLDER + "/";
            if ((path.Length <= prefix.Length) || (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            if (HasUnsafeSegment(path))
            {
                return false;
            }

            string fileName = path.Substring(prefix.Length);
            if ((fileName.IndexOf('/') >= 0) || (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            {
                return false;
            }

            return (fileName.Length > SCENE_EXTENSION.Length)
                && (fileName.EndsWith(SCENE_EXTENSION, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary> 화면 Id로 샌드박스 씬 경로를 만든다. 파일 이름에 쓸 수 없는 글자는 밑줄로 바꾼다 </summary>
        public static string BuildSandboxScenePath(string pageId)
        {
            string safe = SanitizeFileToken(pageId);
            return $"{SANDBOX_FOLDER}/{SCENE_FILE_PREFIX}{safe}{SCENE_EXTENSION}";
        }

        /// <summary> 복사 원본으로 쓸 수 있는 씬 경로인지 검사한다. 샌드박스 폴더의 사본을 원본으로 쓰는 것은 막는다 </summary>
        public static bool IsValidSourceScenePath(string sourcePath, out string error)
        {
            string path = NormalizePath(sourcePath);
            if (path.Length == 0)
            {
                error = "원본 씬 경로가 비어 있다. 계약 전용 화면은 씬 복사를 만들 수 없다";
                return false;
            }

            if ((!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                || (!path.EndsWith(SCENE_EXTENSION, StringComparison.OrdinalIgnoreCase))
                || (HasUnsafeSegment(path)))
            {
                error = $"원본 씬 경로가 올바르지 않다: {path}. Assets/ 아래 .unity 파일이어야 한다";
                return false;
            }

            if (path.StartsWith(SANDBOX_FOLDER + "/", StringComparison.OrdinalIgnoreCase))
            {
                error = $"샌드박스 폴더의 씬은 복사 원본으로 쓸 수 없다: {path}";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary> 대상 경로를 덮어써도 되는지 판정한다. 파일이 없으면 새로 만들고, 있으면 샌드박스 소유 라벨이 있어야 한다 </summary>
        public static bool CanWriteScene(string targetPath, bool fileExists, IReadOnlyCollection<string> labels, out string error)
        {
            if (!IsSandboxScenePath(targetPath))
            {
                error = $"샌드박스 폴더({SANDBOX_FOLDER}) 밖에는 쓰지 않는다: {targetPath}";
                return false;
            }

            if (!fileExists)
            {
                error = null;
                return true;
            }

            if (HasOwnershipLabel(labels))
            {
                error = null;
                return true;
            }

            error = $"{targetPath}는 샌드박스가 만든 파일이 아니라 덮어쓰지 않는다. 다른 파일이면 이름을 바꾸거나 직접 정리한 뒤 다시 시도한다";
            return false;
        }

        public static bool HasOwnershipLabel(IReadOnlyCollection<string> labels)
        {
            if (labels == null)
            {
                return false;
            }

            foreach (string label in labels)
            {
                if (string.Equals(label, OWNERSHIP_LABEL, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasUnsafeSegment(string normalizedPath)
        {
            if ((normalizedPath.Contains("//")) || (normalizedPath.IndexOf(':') >= 0))
            {
                return true;
            }

            string[] segments = normalizedPath.Split('/');
            for (int i = 0; i < segments.Length; ++i)
            {
                if ((segments[i].Length == 0) || (segments[i] == ".") || (segments[i] == ".."))
                {
                    return true;
                }
            }

            return false;
        }

        private static string SanitizeFileToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Page";
            }

            char[] buffer = new char[Math.Min(value.Length, MAX_PAGE_ID_LENGTH)];
            for (int i = 0; i < buffer.Length; ++i)
            {
                char c = value[i];
                bool allowed = ((c < 128) && (char.IsLetterOrDigit(c))) || (c == '_') || (c == '-');
                buffer[i] = allowed ? c : '_';
            }

            return new string(buffer);
        }

        /****************************************
        *                 Host
        ****************************************/

        /// <summary> 폴더 경로와 소유 라벨을 모두 만족하는 샌드박스 사본인지 판정한다. 폴더에 사용자가 직접 만든 씬은 해당하지 않는다 </summary>
        public static bool IsOwnedSandboxScene(string assetPath, IReadOnlyCollection<string> labels)
        {
            return (IsSandboxScenePath(assetPath)) && (HasOwnershipLabel(labels));
        }

        /// <summary> 저장하지 않은 씬을 닫아도 되는지 판정한다. 변경이 없으면 닫고, 변경이 있으면 버리기를 명시했고 샌드박스가 만든 사본일 때만 닫는다 </summary>
        public static bool CanDiscardScene(string assetPath, IReadOnlyCollection<string> labels, bool isDirty, bool discardIntent)
        {
            if (!isDirty)
            {
                return true;
            }

            return (discardIntent) && (IsOwnedSandboxScene(assetPath, labels));
        }

        /// <summary> 화면 코드를 연결해도 되는 문맥인지 판정한다. 접속이 살아 있으면 어떤 화면도 거절하고, View를 만들거나 바꾸는 화면은 샌드박스가 만든 안전 씬이 활성일 때만 허용한다 </summary>
        public static bool IsHostContextSafe(bool pageHasView, bool ownedSandboxSceneActive, bool networkActive)
        {
            if (networkActive)
            {
                return false;
            }

            return (!pageHasView) || (ownedSandboxSceneActive);
        }

        public static bool IsHostContextSafe(bool pageHasView, bool ownedSandboxSceneActive)
        {
            return IsHostContextSafe(pageHasView, ownedSandboxSceneActive, false);
        }

        /// <summary> 활성 씬 경로와 라벨로 호스트 안전을 판정한다. 폴더 안의 사용자 파일은 소유 라벨이 없어 안전하지 않다 </summary>
        public static bool IsHostContextSafe(bool pageHasView, string activeScenePath, IReadOnlyCollection<string> activeSceneLabels, bool networkActive)
        {
            return IsHostContextSafe(pageHasView, IsOwnedSandboxScene(activeScenePath, activeSceneLabels), networkActive);
        }

        /// <summary> 가짜 요청을 보내도 되는지 판정한다. 접속이 살아 있거나 활성 씬이 샌드박스가 만든 사본이 아니면 사유를 돌려준다. 안전하면 null이다 </summary>
        public static string GetRequestBlockReason(bool networkActive, bool ownedSandboxSceneActive)
        {
            if (networkActive)
            {
                return "Mirror 클라이언트나 서버가 켜져 있어 가짜 요청을 보내지 않는다. Play를 끝내고 접속을 닫은 뒤 다시 시도한다";
            }

            if (!ownedSandboxSceneActive)
            {
                return "활성 씬이 샌드박스가 만든 테스트 씬이 아니라 가짜 요청을 보내지 않는다. '안전한 테스트 씬 열기'로 만든 씬에서 Play한다";
            }

            return null;
        }

        /****************************************
        *               Network
        ****************************************/

        /// <summary> Mirror 연결이 살아 있는지 확인한다. 살아 있으면 이유를 돌려준다 </summary>
        public static bool IsNetworkActive(out string reason)
        {
            if ((NetworkClient.active) || (NetworkServer.active))
            {
                reason = "Mirror 클라이언트나 서버가 켜져 있어 테스트 데이터를 적용하지 않는다. Play를 끝내고 접속을 닫은 뒤 다시 시도한다";
                return true;
            }

            reason = null;
            return false;
        }

        /****************************************
        *           Unsafe Components
        ****************************************/

        /// <summary> 샌드박스 사본에서 제거해야 하는 컴포넌트 형식인지 판정한다. Mirror · 네트워크 · 개발용 데모 · 실제 Binder가 대상이다 </summary>
        public static bool IsUnsafeComponentType(Type type)
        {
            if (type == null)
            {
                return false;
            }

            if (IsUnsafeAssemblyName(type.Assembly.GetName().Name))
            {
                return true;
            }

            string ns = type.Namespace ?? string.Empty;
            if ((IsNamespace(ns, "Mirror"))
                || (IsNamespace(ns, "Shinshi.Network"))
                || (IsNamespace(ns, "Shinshi.Dev")))
            {
                return true;
            }

            if ((typeof(NetworkBehaviour).IsAssignableFrom(type))
                || (typeof(NetworkManager).IsAssignableFrom(type))
                || (typeof(Transport).IsAssignableFrom(type)))
            {
                return true;
            }

            if ((IsNamespace(ns, "Shinshi")) && (type.Name.EndsWith("Binder", StringComparison.Ordinal)))
            {
                return true;
            }

            return Array.IndexOf(G_UnsafeTypeNames, type.Name) >= 0;
        }

        /// <summary> 어셈블리 이름이 서버 · 개발용 어셈블리(하위 이름 포함)인지 판정한다 </summary>
        public static bool IsUnsafeAssemblyName(string assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName))
            {
                return false;
            }

            for (int i = 0; i < G_UnsafeAssemblyNames.Length; ++i)
            {
                if (IsNamespace(assemblyName, G_UnsafeAssemblyNames[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary> 씬 이동 · 접속 · 요청처럼 실제 부작용을 일으킬 것 같은 메서드 이름인지 판정한다 </summary>
        public static bool IsUnsafeMethodName(string methodName)
        {
            if (string.IsNullOrEmpty(methodName))
            {
                return false;
            }

            for (int i = 0; i < G_UnsafeMethodTokens.Length; ++i)
            {
                if (methodName.IndexOf(G_UnsafeMethodTokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary> UnityEvent 영속 호출 한 개를 제거해야 하는지 판정한다. 대상이 없거나 에셋이거나 위험 형식 · 이름이면 제거한다 </summary>
        public static bool IsUnsafePersistentCall(UnityEngine.Object target, string assemblyTypeName, string methodName, out string reason)
        {
            if (target == null)
            {
                reason = "호출 대상이 없다";
                return true;
            }

            if (UnityEditor.EditorUtility.IsPersistent(target))
            {
                reason = $"에셋({target.name})을 대상으로 한 호출";
                return true;
            }

            if (IsUnsafeComponentType(target.GetType()))
            {
                reason = $"위험 형식 {target.GetType().Name}";
                return true;
            }

            if (!string.IsNullOrEmpty(assemblyTypeName))
            {
                Type declared = Type.GetType(assemblyTypeName, false);
                if ((declared != null) && (IsUnsafeComponentType(declared)))
                {
                    reason = $"위험 형식 {declared.Name}";
                    return true;
                }
            }

            if (IsUnsafeMethodName(methodName))
            {
                reason = $"위험 메서드 {methodName}";
                return true;
            }

            reason = null;
            return false;
        }

        private static bool IsNamespace(string ns, string root)
        {
            return (ns == root) || (ns.StartsWith(root + ".", StringComparison.Ordinal));
        }
    }
}
