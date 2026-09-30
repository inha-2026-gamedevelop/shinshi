// System
using System;
using System.Globalization;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 서버 프로세스의 역할, 포트, 인스턴스 이름, 실행 인자가 환경 변수보다 우선한다 </summary>
    public readonly struct ServerLaunchConfig
    {
        /****************************************
        *              Constants
        ****************************************/

        private const ushort MIN_PORT = 1;

        /****************************************
        *              Properties
        ****************************************/

        public EServerRole Role       { get; }
        public ushort      Port       { get; }
        public string      InstanceId { get; }

        public string RoleName => (Role == EServerRole.Town) ? Constants.Network.SERVER_ROLE_TOWN
                                                             : Constants.Network.SERVER_ROLE_DUNGEON;

        /****************************************
        *                Methods
        ****************************************/

        public ServerLaunchConfig(EServerRole role, ushort port, string instanceId)
        {
            Role       = role;
            Port       = port;
            InstanceId = instanceId;
        }

        /// <summary> 역할이 없으면 false와 null 오류, 값이 잘못되면 false와 오류 문장을 돌려준다 </summary>
        public static bool TryParse(string[] args, Func<string, string> readEnvironment,
            out ServerLaunchConfig config, out string error)
        {
            config = default;
            error  = null;

            string roleText = ReadValue(args, readEnvironment, Constants.Network.ARG_ROLE, Constants.Network.ENV_SERVER_ROLE);
            if (string.IsNullOrEmpty(roleText))
            {
                return false;
            }

            if (!TryParseRole(roleText, out EServerRole role))
            {
                error = $"알 수 없는 서버 역할이다: {roleText}";
                return false;
            }

            ushort port     = Constants.Network.DEFAULT_GAME_PORT;
            string portText = ReadValue(args, readEnvironment, Constants.Network.ARG_PORT, Constants.Network.ENV_GAME_PORT);
            if (!string.IsNullOrEmpty(portText))
            {
                if ((!ushort.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port)) || (port < MIN_PORT))
                {
                    error = $"포트 값이 잘못됐다: {portText}";
                    return false;
                }
            }

            // 인스턴스 이름이 없으면 로그에서 프로세스를 구분할 수 있게 역할과 포트로 만든다
            string instanceId = ReadValue(args, readEnvironment, Constants.Network.ARG_INSTANCE, Constants.Network.ENV_INSTANCE_ID);
            if (string.IsNullOrEmpty(instanceId))
            {
                instanceId = $"{roleText.ToLowerInvariant()}-{port}";
            }

            config = new ServerLaunchConfig(role, port, instanceId);
            return true;
        }

        private static string ReadValue(string[] args, Func<string, string> readEnvironment, string argName, string envName)
        {
            if (args != null)
            {
                // 값이 뒤따라야 하므로 마지막 칸은 이름으로 보지 않는다
                for (int i = 0; i < (args.Length - 1); ++i)
                {
                    if (string.Equals(args[i], argName, StringComparison.OrdinalIgnoreCase))
                    {
                        return args[i + 1];
                    }
                }
            }

            return (readEnvironment == null) ? null : readEnvironment(envName);
        }

        private static bool TryParseRole(string text, out EServerRole role)
        {
            if (string.Equals(text, Constants.Network.SERVER_ROLE_TOWN, StringComparison.OrdinalIgnoreCase))
            {
                role = EServerRole.Town;
                return true;
            }

            if (string.Equals(text, Constants.Network.SERVER_ROLE_DUNGEON, StringComparison.OrdinalIgnoreCase))
            {
                role = EServerRole.Dungeon;
                return true;
            }

            role = default;
            return false;
        }
    }
}
