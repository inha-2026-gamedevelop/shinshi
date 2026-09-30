// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Network
{
    /// <summary> 서버 프로세스의 시작 순서, 역할을 읽고 GameDB를 확인한 뒤 서버를 연다 </summary>
    public static class ServerBootstrap
    {
        /****************************************
        *              Properties
        ****************************************/

        public static bool               IsRunning { get; private set; }
        public static ServerLaunchConfig Config    { get; private set; }

        // 서버 빌드는 역할 없이 실행되면 클라이언트로 넘어가지 않고 멈춘다
        private static bool IsServerBuild
        {
            get
            {
#if UNITY_SERVER
                return true;
#else
                return false;
#endif
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        // 도메인 리로드를 끈 에디터에서 이전 플레이의 상태가 남지 않게 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            IsRunning = false;
            Config    = default;
        }

        // 씬의 네트워크 매니저가 Awake로 전송 계층을 잡은 뒤에 시작해야 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string[] args       = Environment.GetCommandLineArgs();
            string   editorRole = TakeEditorRole();
            if (!string.IsNullOrEmpty(editorRole))
            {
                args = new[] { Constants.Network.ARG_ROLE, editorRole };
            }

            if (!ServerLaunchConfig.TryParse(args, Environment.GetEnvironmentVariable, out ServerLaunchConfig config, out string error))
            {
                if (error != null)
                {
                    BootFailure.Stop(error);
                    return;
                }

                if (IsServerBuild)
                {
                    BootFailure.Stop("서버 빌드인데 역할이 없다. -role town 또는 -role dungeon으로 실행한다");
                }

                return;
            }

            // GameDBBootstrap이 실패를 이미 기록하고 멈췄다
            if (!GameDB.IsInitialized)
            {
                return;
            }

            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                BootFailure.Stop("씬에 네트워크 매니저가 없다. Bootstrap 씬으로 실행한다");
                return;
            }

            if (!(Transport.active is PortTransport portTransport))
            {
                BootFailure.Stop("포트를 지정할 수 있는 전송 계층이 없다");
                return;
            }

            portTransport.Port = config.Port;
            manager.StartServer();

            if (!NetworkServer.active)
            {
                BootFailure.Stop($"서버를 열지 못했다, 포트 {config.Port}");
                return;
            }

            Config    = config;
            IsRunning = true;

            Debug.Log($"[ServerBoot] 서버 시작 - role={config.RoleName}, instance={config.InstanceId}, port={config.Port}, " +
                      $"contentVersion={GameDB.ContentVersion}, protocol={Constants.Network.PROTOCOL_VERSION}");
        }

        // 에디터 메뉴로 서버 역할을 골랐으면 한 번만 쓰고 지운다. 다음 플레이는 다시 클라이언트다
        private static string TakeEditorRole()
        {
#if UNITY_EDITOR
            string role = UnityEditor.SessionState.GetString(Constants.Network.EDITOR_SERVER_ROLE_KEY, string.Empty);
            UnityEditor.SessionState.EraseString(Constants.Network.EDITOR_SERVER_ROLE_KEY);
            return role;
#else
            return string.Empty;
#endif
        }
    }
}
