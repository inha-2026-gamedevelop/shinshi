// System
using System;
using System.Globalization;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 서버 실행 설정, 실행 인자가 환경 변수보다 우선하며 오류에는 입력 값을 넣지 않는다 </summary>
    public readonly struct ServerLaunchConfig
    {
        /****************************************
        *              Constants
        ****************************************/

        private const ushort MIN_PORT         = 1;
        private const int    MIN_POOL_SIZE    = 1;
        private const int    MAX_TOKEN_LENGTH = 128;

        /****************************************
        *              Properties
        ****************************************/

        public EServerRole Role            { get; }
        public ushort      Port            { get; }
        public string      InstanceId      { get; }
        public string      BackendBaseUrl  { get; }
        public string      ContentVersion  { get; }
        public int         DungeonPoolSize { get; }

        public string RoleName => (Role == EServerRole.Town) ? Constants.Network.SERVER_ROLE_TOWN
            : Constants.Network.SERVER_ROLE_DUNGEON;

        /****************************************
        *                Methods
        ****************************************/

        public ServerLaunchConfig(EServerRole role, ushort port, string instanceId,
            string backendBaseUrl, string contentVersion, int dungeonPoolSize)
        {
            Role            = role;
            Port            = port;
            InstanceId      = instanceId;
            BackendBaseUrl  = backendBaseUrl;
            ContentVersion  = contentVersion;
            DungeonPoolSize = dungeonPoolSize;
        }

        // 기본 인스턴스 ID는 역할과 포트로 만든 고정 GUID라 서버 여러 대가 같은 값을 낸다. 출시 빌드에서 쓰면 다른 호스트의
        // 같은 포트 프로세스가 한 인스턴스로 합쳐져 서로의 임대와 출정을 덮는다. 그래서 개발 실행에서만 허용한다
        private static bool AllowsDefaultInstanceId
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary> 역할 자체가 없으면 클라이언트이므로 false와 null 오류, 서버 설정 오류는 false와 안전한 오류를 반환한다 </summary>
        public static bool TryParse(string[] args, Func<string, string> readEnvironment,
            out ServerLaunchConfig config, out string error)
        {
            return TryParse(args, readEnvironment, AllowsDefaultInstanceId, out config, out error);
        }

        /// <summary> allowDefaultInstanceId가 거짓이면(출시 빌드) INSTANCE_ID를 반드시 받는다, 시험은 이 값으로 출시 경로를 본다 </summary>
        public static bool TryParse(string[] args, Func<string, string> readEnvironment, bool allowDefaultInstanceId,
            out ServerLaunchConfig config, out string error)
        {
            config = default;
            error  = null;

            if (!TryReadValue(args, readEnvironment, Constants.Network.ARG_ROLE, Constants.Network.ENV_SERVER_ROLE,
                out string roleText, out error))
            {
                return false;
            }

            if (roleText == null)
            {
                return false;
            }

            if (!TryParseRole(roleText, out EServerRole role))
            {
                error = $"{Constants.Network.ENV_SERVER_ROLE} ({Constants.Network.ARG_ROLE}): town 또는 dungeon이 필요하다";
                return false;
            }

            if (!TryReadValue(args, readEnvironment, Constants.Network.ARG_PORT, Constants.Network.ENV_GAME_PORT,
                out string portText, out error))
            {
                return false;
            }

            ushort port = Constants.Network.DEFAULT_GAME_PORT;
            if ((portText != null) && ((!ushort.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port)) || (port < MIN_PORT)))
            {
                error = $"{Constants.Network.ENV_GAME_PORT} ({Constants.Network.ARG_PORT}): 1~65535 정수가 필요하다";
                return false;
            }

            if (!TryReadValue(args, readEnvironment, Constants.Network.ARG_INSTANCE, Constants.Network.ENV_INSTANCE_ID,
                out string instanceId, out error))
            {
                return false;
            }

            // API가 인스턴스 ID를 GUID로만 받는다. 개발 실행에서 값을 주지 않으면 역할과 포트로 같은 값을 만든다
            if ((instanceId == null) && (!allowDefaultInstanceId))
            {
                error = $"{Constants.Network.ENV_INSTANCE_ID} ({Constants.Network.ARG_INSTANCE}): 출시 빌드는 인스턴스 ID(GUID)가 필수다";
                return false;
            }

            if (instanceId == null)
            {
                string roleName = (role == EServerRole.Town) ? Constants.Network.SERVER_ROLE_TOWN : Constants.Network.SERVER_ROLE_DUNGEON;
                instanceId = SessionContractIds.CreateDefaultInstanceId(roleName, port);
            }

            if (!SessionContractIds.IsApiId(instanceId))
            {
                error = $"{Constants.Network.ENV_INSTANCE_ID} ({Constants.Network.ARG_INSTANCE}): 비어 있지 않은 GUID가 필요하다";
                return false;
            }

            if (!TryReadRequired(args, readEnvironment, Constants.Network.ARG_BACKEND_BASE_URL, Constants.Network.ENV_BACKEND_BASE_URL,
                out string backendBaseUrl, out error))
            {
                return false;
            }

            if ((!Uri.TryCreate(backendBaseUrl, UriKind.Absolute, out Uri backendUri))
                || ((backendUri.Scheme != Uri.UriSchemeHttp) && (backendUri.Scheme != Uri.UriSchemeHttps))
                || (string.IsNullOrEmpty(backendUri.Host)) || (!string.IsNullOrEmpty(backendUri.UserInfo))
                || (!string.IsNullOrEmpty(backendUri.Query)) || (!string.IsNullOrEmpty(backendUri.Fragment))
                || (ContainsWhitespace(backendBaseUrl)))
            {
                error = $"{Constants.Network.ENV_BACKEND_BASE_URL} ({Constants.Network.ARG_BACKEND_BASE_URL}): 자격 정보, 쿼리, fragment, 공백 없는 절대 HTTP/HTTPS URL이 필요하다";
                return false;
            }

            if (!TryReadRequired(args, readEnvironment, Constants.Network.ARG_CONTENT_VERSION, Constants.Network.ENV_CONTENT_VERSION,
                out string contentVersion, out error))
            {
                return false;
            }

            if (!IsSafeToken(contentVersion))
            {
                error = $"{Constants.Network.ENV_CONTENT_VERSION} ({Constants.Network.ARG_CONTENT_VERSION}): 영문 또는 숫자로 시작하는 1~128자 버전이 필요하다 (영문, 숫자, '.', '_', '-', '+')";
                return false;
            }

            if (!TryReadRequired(args, readEnvironment, Constants.Network.ARG_DUNGEON_POOL_SIZE, Constants.Network.ENV_DUNGEON_POOL_SIZE,
                out string poolText, out error))
            {
                return false;
            }

            if ((!int.TryParse(poolText, NumberStyles.None, CultureInfo.InvariantCulture, out int poolSize)) || (poolSize < MIN_POOL_SIZE))
            {
                error = $"{Constants.Network.ENV_DUNGEON_POOL_SIZE} ({Constants.Network.ARG_DUNGEON_POOL_SIZE}): 1~2147483647 정수가 필요하다";
                return false;
            }

            config = new ServerLaunchConfig(role, port, instanceId, backendUri.AbsoluteUri, contentVersion, poolSize);
            return true;
        }

        private static bool TryReadRequired(string[] args, Func<string, string> readEnvironment, string argName, string envName,
            out string value, out string error)
        {
            if (!TryReadValue(args, readEnvironment, argName, envName, out value, out error))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                error = $"{envName} ({argName}): 필수 설정이 누락됐다";
                return false;
            }

            return true;
        }

        internal static bool TryReadValue(string[] args, Func<string, string> readEnvironment, string argName, string envName,
            out string value, out string error)
        {
            value = null;
            error = null;
            bool found = false;

            if (args != null)
            {
                for (int i = 0; i < args.Length; ++i)
                {
                    if (!string.Equals(args[i], argName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (found)
                    {
                        error = $"{envName} ({argName}): 실행 인자가 중복됐다";
                        return false;
                    }

                    if ((i == (args.Length - 1)) || (string.IsNullOrWhiteSpace(args[i + 1]))
                        || (args[i + 1].StartsWith("-", StringComparison.Ordinal)))
                    {
                        error = $"{envName} ({argName}): 실행 인자 뒤에 값이 필요하다";
                        return false;
                    }

                    value = args[i + 1];
                    found = true;
                    ++i;
                }
            }

            if (!found)
            {
                value = readEnvironment?.Invoke(envName);
            }

            return true;
        }

        private static bool ContainsWhitespace(string value)
        {
            for (int i = 0; i < value.Length; ++i)
            {
                if ((char.IsWhiteSpace(value[i])) || (char.IsControl(value[i])))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSafeToken(string value)
        {
            if ((string.IsNullOrEmpty(value)) || (value.Length > MAX_TOKEN_LENGTH))
            {
                return false;
            }

            if (!IsAsciiLetterOrDigit(value[0]))
            {
                return false;
            }

            for (int i = 0; i < value.Length; ++i)
            {
                char letter = value[i];
                if ((IsAsciiLetterOrDigit(letter)) || (letter == '.') || (letter == '_')
                    || (letter == '-') || (letter == '+'))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static bool IsAsciiLetterOrDigit(char letter)
        {
            return ((letter >= 'a') && (letter <= 'z')) || ((letter >= 'A') && (letter <= 'Z'))
                || ((letter >= '0') && (letter <= '9'));
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
