// System
using System;
using System.Diagnostics;
using System.IO;

// Unity
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// 서드파티
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Network;

using Debug = UnityEngine.Debug;

namespace Shinshi.Editor
{
    /// <summary> 플레이어 빌드가 끝나면 데이터 폴더의 StreamingAssets에 build-identity.json을 쓴다 </summary>
    public class BuildIdentityWriter : IPostprocessBuildWithReport
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string DATA_FOLDER_SUFFIX = "_Data";
        private const string STREAMING_ASSETS   = "StreamingAssets";
        private const string GIT_EXECUTABLE     = "git";
        private const string GIT_COMMIT_ARGS    = "rev-parse --short=12 HEAD";
        private const string GIT_STATUS_ARGS    = "status --porcelain";
        private const string DIRTY_SUFFIX       = "+dirty";
        private const int    GIT_TIMEOUT_MS     = 5000;

        /****************************************
        *              Properties
        ****************************************/

        public int callbackOrder => 0;

        /****************************************
        *                Methods
        ****************************************/

        public void OnPostprocessBuild(BuildReport report)
        {
            string path = GetIdentityPath(report.summary.outputPath);
            if (string.IsNullOrEmpty(path))
            {
                throw new BuildFailedException($"[Build] 빌드 신원 경로를 정할 수 없다: {report.summary.outputPath}");
            }

            bool development = (report.summary.options & BuildOptions.Development) != 0;

            // 빌드 GUID는 boot.config의 build-guid와 같은 값이라 설치 폴더만 보고도 대조할 수 있다
            var identity = new BuildIdentity(report.summary.guid.ToString(), Constants.Network.PROTOCOL_VERSION,
                ComputeContentHash(), PlayerSettings.bundleVersion,
                development ? BuildIdentity.KIND_DEVELOPMENT : BuildIdentity.KIND_RELEASE,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ReadCommit());

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonConvert.SerializeObject(identity, CreateSettings()));
            Debug.Log($"[Build] 빌드 신원 기록 - {identity.BuildId} ({identity.BuildKind}), {path}");
        }

        /// <summary> 실행 파일 경로에서 빌드 신원 파일 경로를 만든다, Windows와 Linux 플레이어가 같은 배치를 쓴다 </summary>
        public static string GetIdentityPath(string executablePath)
        {
            if (string.IsNullOrEmpty(executablePath))
            {
                return null;
            }

            string folder = Path.GetDirectoryName(executablePath);
            string name   = Path.GetFileNameWithoutExtension(executablePath);
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(name))
            {
                return null;
            }

            return Path.Combine(folder, name + DATA_FOLDER_SUFFIX, STREAMING_ASSETS, BuildIdentity.FILE_NAME);
        }

        private static string ComputeContentHash()
        {
            GameDatabaseSO root = Resources.Load<GameDatabaseSO>(Constants.Resource.GAME_DB_PATH);
            if (root == null)
            {
                throw new BuildFailedException("[Build] GameDB 루트 에셋이 없다");
            }

            // 에디터 정적 상태에 같은 루트를 등록한다. 플레이 진입 때 GameDBBootstrap이 같은 루트로 다시 불러도 안전하다
            GameDB.Initialize(root);
            return GameDBContentHash.Compute();
        }

        // git이 없거나 저장소 밖이면 빈 값으로 둔다. 추적용이라 빌드를 막지 않는다
        private static string ReadCommit()
        {
            string commit = RunGit(GIT_COMMIT_ARGS);
            if (string.IsNullOrEmpty(commit))
            {
                return string.Empty;
            }

            string status = RunGit(GIT_STATUS_ARGS);
            return string.IsNullOrEmpty(status) ? commit : commit + DIRTY_SUFFIX;
        }

        private static string RunGit(string arguments)
        {
            var info = new ProcessStartInfo(GIT_EXECUTABLE, arguments)
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
                WorkingDirectory       = Directory.GetCurrentDirectory()
            };

            Process process;
            try
            {
                process = Process.Start(info);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Build] git을 실행하지 못해 commit을 비운다: {e.Message}");
                return null;
            }

            if (process == null)
            {
                return null;
            }

            using (process)
            {
                // 표준 에러를 비우지 않으면 출력이 많을 때 git이 버퍼에 막혀 끝나지 않는다
                process.ErrorDataReceived += (sender, args) => { };
                process.BeginErrorReadLine();
                string output = process.StandardOutput.ReadToEnd();
                if ((!process.WaitForExit(GIT_TIMEOUT_MS)) || (process.ExitCode != 0))
                {
                    return null;
                }

                return output.Trim();
            }
        }

        // 백엔드 ContractJson과 같은 해석 방식, 사람이 열어 볼 파일이라 들여쓰기만 다르다
        private static JsonSerializerSettings CreateSettings()
        {
            return new JsonSerializerSettings
            {
                ContractResolver  = new DefaultContractResolver(),
                TypeNameHandling  = TypeNameHandling.None,
                NullValueHandling = NullValueHandling.Include,
                Formatting        = Formatting.Indented
            };
        }
    }
}
