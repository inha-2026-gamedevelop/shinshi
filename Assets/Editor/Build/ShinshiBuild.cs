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
        public const string TARGET_WINDOWS_DEVTEST = "windows-devtest";
        public const string TARGET_WINDOWS_PERF    = "windows-perf";

        private const string SERVER_DEFINE      = "UNITY_SERVER";
        private const string DEV_TEST_DEFINE    = "SHINSHI_DEV_TEST";
        private const string PERF_LOG_DEFINE    = "SHINSHI_PERF_LOG";
        private const string SERVER_ASSEMBLY    = "Shinshi.Server.dll";
        private const string COMPILE_CHECK_ROOT = "Temp/ShinshiCompileCheck";

        private const string WINDOWS_EXTENSION = ".exe";
        private const string LINUX_EXTENSION   = ".x86_64";

        private const int EXIT_SUCCESS = 0;
        private const int EXIT_FAILURE = 1;

        // 클라이언트는 로비, 캐릭터 선택, 생성을 거쳐 마을로 가고 출정하면 던전 씬으로 간다. 서버는 Bootstrap에서 역할을 읽고 마을 또는 던전 씬을 연다.
        // SceneManager와 Mirror onlineScene은 Constants.Scene의 이름으로 찾으므로 빌드 목록에 빠지면 이동이 실패한다
        private static readonly string[] G_ClientScenes      = { BOOTSTRAP_SCENE_PATH, ScenePath(Constants.Scene.LOBBY),
            ScenePath(Constants.Scene.CHARACTER_SELECT), ScenePath(Constants.Scene.CHARACTER_CREATE),
            ScenePath(Constants.Scene.SHINSHI_TOWN), ScenePath(Constants.Scene.DUNGEON_FOREST),
            ScenePath(Constants.Scene.DUNGEON_MUCHEOL), ScenePath(Constants.Scene.DUNGEON_WOLBAEK) };
        private static readonly string[] G_ServerScenes      = { BOOTSTRAP_SCENE_PATH, ScenePath(Constants.Scene.SHINSHI_TOWN),
            ScenePath(Constants.Scene.DUNGEON_FOREST), ScenePath(Constants.Scene.DUNGEON_MUCHEOL),
            ScenePath(Constants.Scene.DUNGEON_WOLBAEK) };

        // 서버 접속 없이 마을 렌더링 비용을 확인하는 개발 빌드. 시험 장치가 Shinshi.Server에 있어 SHINSHI_DEV_TEST로 그 어셈블리를 클라이언트 빌드에 넣는다.
        // 마을은 NetworkManager 없이 열려 플레이어 없이 배경만 그리며, -devPerfTown 측정(DevTownPerfViews)에 쓴다
        private static readonly string[] G_DevTestScenes     = { ScenePath(Constants.Scene.SHINSHI_TOWN) };
        private static readonly string[] G_DevTestDefines    = { DEV_TEST_DEFINE };

        // 릴리즈 클라이언트와 같은 씬으로 만드는 계측 빌드. DevPerfLogger만 SHINSHI_PERF_LOG로 넣고 서버 어셈블리와 시험 장치는 넣지 않는다
        private static readonly string[] G_PerfDefines       = { PERF_LOG_DEFINE };

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

        [MenuItem(MENU_ROOT + "Dev Test/Windows Town (Development)")]
        private static void BuildDevTestMenu()
        {
            Build(TARGET_WINDOWS_DEVTEST, true);
        }

        // Builds/windows-perf에 만든다. 프로파일러 자동 연결은 켜지 않는다
        [MenuItem(MENU_ROOT + "Perf/Windows Client (Development, Perf Log)")]
        private static void BuildPerfMenu()
        {
            Build(TARGET_WINDOWS_PERF, true);
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

        /// <summary> 서버 빌드에 넣는 씬 경로 </summary>
        public static string[] GetServerScenes()
        {
            return (string[])G_ServerScenes.Clone();
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
            if (targetName == TARGET_WINDOWS_DEVTEST)
            {
                return Build(targetName, true, G_DevTestScenes, G_DevTestDefines);
            }

            if (targetName == TARGET_WINDOWS_PERF)
            {
                return Build(targetName, true, G_ClientScenes, G_PerfDefines);
            }

            bool server = (targetName == TARGET_WINDOWS_SERVER) || (targetName == TARGET_LINUX_SERVER);
            return Build(targetName, development, server ? G_ServerScenes : G_ClientScenes);
        }

        private static bool Build(string targetName, bool development, string[] scenes)
        {
            return Build(targetName, development, scenes, Array.Empty<string>());
        }

        private static bool Build(string targetName, bool development, string[] scenes, string[] extraDefines)
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
                options          = development ? BuildOptions.Development : BuildOptions.None,
                extraScriptingDefines = extraDefines
            };

            // 서버 빌드는 에디터의 대상 하위 설정을 Server로 바꿔 둔다. 그대로 두면 에디터가 UNITY_SERVER로 컴파일돼
            // 클라이언트 플레이가 서버 부트스트랩에서 멈추므로 빌드 전 설정으로 되돌린다
            StandaloneBuildSubtarget editorSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget;

            // 시험, 계측 빌드의 프레임 기록(DevPerfLogger)이 GPU 시간을 읽으려면 Frame Timing 통계가 켜져 있어야 한다. 다른 빌드에는 남기지 않는다
            bool frameTimingStats = PlayerSettings.enableFrameTimingStats;
            if ((Array.IndexOf(extraDefines, DEV_TEST_DEFINE) >= 0) || (Array.IndexOf(extraDefines, PERF_LOG_DEFINE) >= 0))
            {
                PlayerSettings.enableFrameTimingStats = true;
            }

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

                PlayerSettings.enableFrameTimingStats = frameTimingStats;
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

            // 출력 폴더가 없으면 컴파일러가 폴더 생성 실패를 되풀이하며 끝나지 않는다. Temp를 지운 뒤에도 돌도록 먼저 만든다
            string output = Path.Combine(COMPILE_CHECK_ROOT, server ? "server" : "client");
            Directory.CreateDirectory(output);
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
                case TARGET_WINDOWS_DEVTEST:
                case TARGET_WINDOWS_PERF:
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
