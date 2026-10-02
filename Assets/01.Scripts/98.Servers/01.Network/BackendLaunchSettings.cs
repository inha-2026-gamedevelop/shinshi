// System
using System;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 백엔드 연결 실행 설정, 접속 토큰 검증기 선택과 서비스 자격을 읽는다. 오류에는 입력 값을 넣지 않는다 </summary>
    public readonly struct BackendLaunchSettings
    {
        // 서비스 자격은 환경 변수나 파일로만 받는다. 실행 인자에 값을 두면 같은 호스트의 다른 사용자가 프로세스 목록으로 읽는다.
        // 이 값은 ToString, 로그, 예외 메시지에 넣지 않는다

        /****************************************
        *              Properties
        ****************************************/

        public bool   UseApiVerifier { get; }

        // 없으면 null. 개발 검증기만 쓰는 개발 실행은 백엔드 없이도 뜬다
        public string ServiceToken   { get; }

        public bool HasServiceToken => !string.IsNullOrEmpty(ServiceToken);

        /****************************************
        *                Methods
        ****************************************/

        public BackendLaunchSettings(bool useApiVerifier, string serviceToken)
        {
            UseApiVerifier = useApiVerifier;
            ServiceToken   = serviceToken;
        }

        /// <summary> 출시 빌드(allowDevVerifier 거짓)는 api 검증기와 서비스 자격이 반드시 있어야 한다 </summary>
        public static bool TryParse(string[] args, Func<string, string> readEnvironment, Func<string, string> readFile,
            bool allowDevVerifier, out BackendLaunchSettings settings, out string error)
        {
            settings = default;
            if (!ServerLaunchConfig.TryReadValue(args, readEnvironment, Constants.Network.ARG_JOIN_TOKEN_VERIFIER,
                Constants.Network.ENV_JOIN_TOKEN_VERIFIER, out string verifierText, out error))
            {
                return false;
            }

            bool useApiVerifier;
            if (string.IsNullOrEmpty(verifierText))
            {
                useApiVerifier = !allowDevVerifier;
            }
            else if (string.Equals(verifierText, Constants.Network.JOIN_TOKEN_VERIFIER_API, StringComparison.OrdinalIgnoreCase))
            {
                useApiVerifier = true;
            }
            else if (string.Equals(verifierText, Constants.Network.JOIN_TOKEN_VERIFIER_DEV, StringComparison.OrdinalIgnoreCase))
            {
                if (!allowDevVerifier)
                {
                    error = $"{Constants.Network.ENV_JOIN_TOKEN_VERIFIER} ({Constants.Network.ARG_JOIN_TOKEN_VERIFIER}): 출시 빌드는 dev 검증기를 쓸 수 없다";
                    return false;
                }

                useApiVerifier = false;
            }
            else
            {
                error = $"{Constants.Network.ENV_JOIN_TOKEN_VERIFIER} ({Constants.Network.ARG_JOIN_TOKEN_VERIFIER}): api 또는 dev가 필요하다";
                return false;
            }

            if (!TryReadServiceToken(args, readEnvironment, readFile, out string serviceToken, out error))
            {
                return false;
            }

            if ((useApiVerifier) && (serviceToken == null))
            {
                error = $"{Constants.Network.ENV_BACKEND_SERVICE_TOKEN} ({Constants.Network.ARG_BACKEND_SERVICE_TOKEN_FILE}): api 검증기에는 서비스 자격이 필요하다";
                return false;
            }

            settings = new BackendLaunchSettings(useApiVerifier, serviceToken);
            return true;
        }

        /// <summary> 서비스 자격을 실어 보내도 되는 기준 URL인지, 출시 빌드의 평문 HTTP는 같은 호스트만 허용한다 </summary>
        public static bool IsBaseUrlAllowed(string baseUrl, bool allowInsecureHttp)
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            if (uri.Scheme == Uri.UriSchemeHttps)
            {
                return true;
            }

            if (uri.Scheme != Uri.UriSchemeHttp)
            {
                return false;
            }

            return (allowInsecureHttp) || (uri.IsLoopback);
        }

        // 파일 인자가 환경 변수보다 우선한다. 파일 끝 줄바꿈은 편집기가 붙이는 경우가 많아 떼어 낸다
        private static bool TryReadServiceToken(string[] args, Func<string, string> readEnvironment, Func<string, string> readFile,
            out string serviceToken, out string error)
        {
            serviceToken = null;
            if (!ServerLaunchConfig.TryReadValue(args, null, Constants.Network.ARG_BACKEND_SERVICE_TOKEN_FILE,
                Constants.Network.ENV_BACKEND_SERVICE_TOKEN, out string tokenFile, out error))
            {
                return false;
            }

            string raw;
            if (tokenFile != null)
            {
                raw = readFile?.Invoke(tokenFile);
                if (raw == null)
                {
                    error = $"{Constants.Network.ARG_BACKEND_SERVICE_TOKEN_FILE}: 서비스 자격 파일을 읽지 못했다";
                    return false;
                }
            }
            else
            {
                raw = readEnvironment?.Invoke(Constants.Network.ENV_BACKEND_SERVICE_TOKEN);
                if (string.IsNullOrEmpty(raw))
                {
                    return true;
                }
            }

            string token = raw.Trim();
            if (!IsHeaderSafe(token))
            {
                error = $"{Constants.Network.ENV_BACKEND_SERVICE_TOKEN} ({Constants.Network.ARG_BACKEND_SERVICE_TOKEN_FILE}): " +
                    $"공백, 제어 문자 없는 1~{Constants.Network.MAX_BACKEND_SERVICE_TOKEN_LENGTH}자 ASCII 값이 필요하다";
                return false;
            }

            serviceToken = token;
            return true;
        }

        // 헤더 값에 줄바꿈이 섞이면 다른 헤더를 끼워 넣을 수 있다. 보이는 ASCII만 받는다
        internal static bool IsHeaderSafe(string value)
        {
            if ((string.IsNullOrEmpty(value)) || (value.Length > Constants.Network.MAX_BACKEND_SERVICE_TOKEN_LENGTH))
            {
                return false;
            }

            for (int i = 0; i < value.Length; ++i)
            {
                if ((value[i] <= ' ') || (value[i] > '~'))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
