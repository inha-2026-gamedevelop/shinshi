// System
using System;
using System.Collections.Generic;
using System.IO;

// Unity
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Player;
using UnityEditor.Build.Reporting;
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Editor
{
    /// <summary> 클라이언트, 서버 빌드와 컴파일 검사를 한곳에서 실행한다. 메뉴와 -executeMethod 둘 다 쓴다 </summary>
    public static class ShinshiBuild
    {
        /****************************************
        *              Constants
        ****************************************/

        public const string BOOTSTRAP_SCENE_PATH = Constants.Scene.FOLDER + Constants.Scene.BOOTSTRAP + Constants.Scene.EXTENSION;

        private const string MENU_ROOT    = "Shinshi/Build/";
        private const string OUTPUT_ROOT  = "Builds";
        private const string PRODUCT_NAME = "Shinshi";

        // 명령줄 예: -executeMethod Shinshi.Editor.ShinshiBuild.BuildFromCommandLine -shinshiTarget linux-server
        private const string ARG_TARGET  = "-shinshiTarget";
        private const string ARG_RELEASE = "-shinshiRelease";

        public const string TARGET_WINDOWS_CLIENT = "windows-client";
        public const string TARGET_WINDOWS_SERVER = "windows-server";
        public const string TARGET_LINUX_SERVER   = "linux-server";

        private const string SERVER_DEFINE      = "UNITY_SERVER";
        private const string SERVER_ASSEMBLY    = "Shinshi.Server.dll";
        private const string COMPILE_CHECK_ROOT = "Temp/ShinshiCompileCheck";

        private const string WINDOWS_EXTENSION = ".exe";
        private const string LINUX_EXTENSION   = ".x86_64";

        private const int EXIT_SUCCESS = 0;
        private const int EXIT_FAILURE = 1;

        // 이동 예측과 회피를 서버, 클라이언트 빌드로 시험하는 씬. Bootstrap 구성에 바닥, 내비 메시, 쿼터뷰 카메라, 지연 시뮬레이션을 더했다
        public const string DEV_MOVEMENT_SCENE_PATH = "Assets/00.Scenes/Dev_PlayerMovement.unity";

        // 클라이언트는 로비, 캐릭터 선택, 생성을 거쳐 마을로 간다. 서버는 Bootstrap에서 역할을 읽고 마을 씬을 연다.
        // SceneManager와 Mirror onlineScene은 Constants.Scene의 이름으로 찾으므로 빌드 목록에 빠지면 이동이 실패한다
        private static readonly string[] G_ClientScenes      = { BOOTSTRAP_SCENE_PATH, ScenePath(Constants.Scene.LOBBY),
            ScenePath(Constants.Scene.CHARACTER_SELECT), ScenePath(Constants.Scene.CHARACTER_CREATE),
            ScenePath(Constants.Scene.SHINSHI_TOWN) };
        private static readonly string[] G_ServerScenes      = { BOOTSTRAP_SCENE_PATH, ScenePath(Constants.Scene.SHINSHI_TOWN) };
        private static readonly string[] G_DevMovementScenes = { DEV_MOVEMENT_SCENE_PATH };

        /****************************************
        *                 Menu
        ****************************************/

        [MenuItem(MENU_ROOT + "Windows Client (Development)")]
        private static void BuildWindowsClientMenu()
        {
            Build(TARGET_WINDOWS_CLIENT, true);
        }

        [MenuItem(MENU_ROOT + "Windows Server (Development)")]
        private static void BuildWindowsServerMenu()
        {
            Build(TARGET_WINDOWS_SERVER, true);
        }

        [MenuItem(MENU_ROOT + "Linux Server (Development)")]
        private static void BuildLinuxServerMenu()
        {
            Build(TARGET_LINUX_SERVER, true);
        }

        // 같은 출력 폴더를 쓰므로 tools/Start-M2.ps1로 바로 띄울 수 있다. Bootstrap 빌드를 덮어쓴다
        [MenuItem(MENU_ROOT + "Dev Movement/Windows Client (Development)")]
        private static void BuildDevMovementClientMenu()
        {
            Build(TARGET_WINDOWS_CLIENT, true, G_DevMovementScenes);
        }

        [MenuItem(MENU_ROOT + "Dev Movement/Windows Server (Development)")]
        private static void BuildDevMovementServerMenu()
        {
            Build(TARGET_WINDOWS_SERVER, true, G_DevMovementScenes);
        }

        [MenuItem(MENU_ROOT + "Check Client Compile")]
        private static void CheckClientCompileMenu()
        {
            CompileCheck(false);
        }

        [MenuItem(MENU_ROOT + "Check Server Compile")]
        private static void CheckServerCompileMenu()
        {
            CompileCheck(true);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 클라이언트 빌드에 넣는 씬 경로, 에디터 빌드 설정도 이 목록으로 맞춘다 </summary>
        public static string[] GetClientScenes()
        {
            return (string[])G_ClientScenes.Clone();
        }

        /// <summary> 실제 실행 흐름 씬(Release 폴더)의 경로 </summary>
        public static string ScenePath(string sceneName)
        {
            return Constants.Scene.RELEASE_FOLDER + sceneName + Constants.Scene.EXTENSION;
        }

        /// <summary> 명령줄 진입점, 성공하면 0, 실패하면 1로 에디터를 끝낸다 </summary>
        public static void BuildFromCommandLine()
        {
            string target  = ReadArg(ARG_TARGET);
            bool   release = HasArg(ARG_RELEASE);

            bool succeeded = (!string.IsNullOrEmpty(target)) && Build(target, !release);
            EditorApplication.Exit(succeeded ? EXIT_SUCCESS : EXIT_FAILURE);
        }

        public static bool Build(string targetName, bool development)
        {
            bool server = (targetName == TARGET_WINDOWS_SERVER) || (targetName == TARGET_LINUX_SERVER);
            return Build(targetName, development, server ? G_ServerScenes : G_ClientScenes);
        }

        /// <summary> 개발 이동 씬으로 서버, 클라이언트 개발 빌드를 만든다, 에디터 코드 실행으로도 부른다 </summary>
        public static bool BuildDevMovement(string targetName)
        {
            return Build(targetName, true, G_DevMovementScenes);
        }

        private static bool Build(string targetName, bool development, string[] scenes)
        {
            if (!TryResolveTarget(targetName, out BuildTarget target, out StandaloneBuildSubtarget subtarget, out string extension))
            {
                Debug.LogError($"[Build] 알 수 없는 빌드 대상이다: {targetName}");
                return false;
            }

            var options = new BuildPlayerOptions
            {
                scenes           = scenes,
                target           = target,
                targetGroup      = BuildTargetGroup.Standalone,
                subtarget        = (int)subtarget,
                locationPathName = Path.Combine(OUTPUT_ROOT, targetName, PRODUCT_NAME + extension),
                options          = development ? BuildOptions.Development : BuildOptions.None
            };

            // 서버 빌드는 에디터의 대상 하위 설정을 Server로 바꿔 둔다. 그대로 두면 에디터가 UNITY_SERVER로 컴파일돼
            // 클라이언트 플레이가 서버 부트스트랩에서 멈추므로 빌드 전 설정으로 되돌린다
            StandaloneBuildSubtarget editorSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget;
            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                if (EditorUserBuildSettings.standaloneBuildSubtarget != editorSubtarget)
                {
                    EditorUserBuildSettings.standaloneBuildSubtarget = editorSubtarget;
                }
            }

            bool succeeded = report.summary.result == BuildResult.Succeeded;

            // 후처리 예외가 빌드 결과에 반영되지 않는 경우가 있어 게시 도구가 읽을 파일을 직접 확인한다
            string identityPath = BuildIdentityWriter.GetIdentityPath(options.locationPathName);
            if (succeeded && (!File.Exists(identityPath)))
            {
                Debug.LogError($"[Build] 빌드 신원 파일이 없다: {identityPath}");
                succeeded = false;
            }

            string message = $"[Build] {targetName} ({(development ? "개발" : "배포")}) - {report.summary.result}, {options.locationPathName}";
            if (succeeded)
            {
                Debug.Log(message);
            }
            else
            {
                Debug.LogError(message);
            }

            return succeeded;
        }

        /// <summary> 플레이어 스크립트만 컴파일해 서버 어셈블리가 빠지거나 들어가는지 확인한다 </summary>
        public static bool CompileCheck(bool server)
        {
            var settings = new ScriptCompilationSettings
            {
                target                = BuildTarget.StandaloneWindows64,
                group                 = BuildTargetGroup.Standalone,
                options               = ScriptCompilationOptions.None,
                extraScriptingDefines = server ? new[] { SERVER_DEFINE } : Array.Empty<string>()
            };

            string                  output = Path.Combine(COMPILE_CHECK_ROOT, server ? "server" : "client");
            ScriptCompilationResult result = PlayerBuildInterface.CompilePlayerScripts(settings, output);

            int  count     = (result.assemblies == null) ? 0 : result.assemblies.Count;
            bool hasServer = Contains(result.assemblies, SERVER_ASSEMBLY);
            bool succeeded = (count > 0) && (hasServer == server);

            string message = $"[Build] 컴파일 검사 {(server ? "서버" : "클라이언트")} - 어셈블리 {count}개, Shinshi.Server 포함 {hasServer}";
            if (succeeded)
            {
                Debug.Log(message);
            }
            else
            {
                Debug.LogError(message);
            }

            return succeeded;
        }

        private static bool TryResolveTarget(string name, out BuildTarget target, out StandaloneBuildSubtarget subtarget, out string extension)
        {
            switch (name)
            {
                case TARGET_WINDOWS_CLIENT:
                    target    = BuildTarget.StandaloneWindows64;
                    subtarget = StandaloneBuildSubtarget.Player;
                    extension = WINDOWS_EXTENSION;
                    return true;

                case TARGET_WINDOWS_SERVER:
                    target    = BuildTarget.StandaloneWindows64;
                    subtarget = StandaloneBuildSubtarget.Server;
                    extension = WINDOWS_EXTENSION;
                    return true;

                case TARGET_LINUX_SERVER:
                    target    = BuildTarget.StandaloneLinux64;
                    subtarget = StandaloneBuildSubtarget.Server;
                    extension = LINUX_EXTENSION;
                    return true;

                default:
                    target    = BuildTarget.NoTarget;
                    subtarget = StandaloneBuildSubtarget.Player;
                    extension = string.Empty;
                    return false;
            }
        }

        private static bool Contains(IReadOnlyList<string> assemblies, string name)
        {
            if (assemblies == null)
            {
                return false;
            }

            for (int i = 0; i < assemblies.Count; ++i)
            {
                if (string.Equals(assemblies[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ReadArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();

            for (int i = 0; i < (args.Length - 1); ++i)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static bool HasArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();

            for (int i = 0; i < args.Length; ++i)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
