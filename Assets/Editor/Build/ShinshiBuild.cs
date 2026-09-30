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

namespace Shinshi.Editor
{
    /// <summary> 클라이언트, 서버 빌드와 컴파일 검사를 한곳에서 실행한다. 메뉴와 -executeMethod 둘 다 쓴다 </summary>
    public static class ShinshiBuild
    {
        /****************************************
        *              Constants
        ****************************************/

        public const string BOOTSTRAP_SCENE_PATH = "Assets/00.Scenes/Bootstrap.unity";

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

        private static readonly string[] G_Scenes = { BOOTSTRAP_SCENE_PATH };

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
            if (!TryResolveTarget(targetName, out BuildTarget target, out StandaloneBuildSubtarget subtarget, out string extension))
            {
                Debug.LogError($"[Build] 알 수 없는 빌드 대상이다: {targetName}");
                return false;
            }

            var options = new BuildPlayerOptions
            {
                scenes           = G_Scenes,
                target           = target,
                targetGroup      = BuildTargetGroup.Standalone,
                subtarget        = (int)subtarget,
                locationPathName = Path.Combine(OUTPUT_ROOT, targetName, PRODUCT_NAME + extension),
                options          = development ? BuildOptions.Development : BuildOptions.None
            };

            BuildReport report    = BuildPipeline.BuildPlayer(options);
            bool        succeeded = report.summary.result == BuildResult.Succeeded;

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
