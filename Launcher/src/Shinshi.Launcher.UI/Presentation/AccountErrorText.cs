// Project
using Shinshi.Launcher.Account;

namespace Shinshi.Launcher.Presentation
{
    /// <summary> 로그인, 게임 세션 발급, 세션 전달 실패를 사용자에게 보일 한국어 문장으로 바꾼다 </summary>
    public static class AccountErrorText
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string INVALID_CREDENTIALS = "아이디 또는 비밀번호가 올바르지 않습니다.";
        public const string ACCOUNT_LOCKED      = "로그인 시도가 많아 잠시 잠겼습니다. 15분 뒤 다시 시도해 주세요.";
        public const string RATE_LIMITED        = "요청이 너무 많습니다. 잠시 뒤 다시 시도해 주세요.";
        public const string SESSION_LOST        = "로그인이 만료됐습니다. 다시 로그인해 주세요.";
        public const string API_NOT_CONFIGURED  = "런처 설정에 API 주소(apiBaseUrl)가 없어 로그인할 수 없습니다.";
        public const string NOT_INSTALLED       = "게임을 먼저 설치해 주세요.";
        public const string BUILD_UNREADABLE    = "설치된 게임의 버전 정보를 읽지 못했습니다. 파일 검증 및 복구를 실행해 주세요.";
        public const string UPDATE_REQUIRED     = "업데이트가 필요합니다. 게임을 업데이트한 뒤 다시 시작해 주세요.";
        public const string MAINTENANCE         = "서버 점검 중입니다. 점검이 끝난 뒤 다시 시도해 주세요.";
        public const string SERVICE_UNREACHABLE = "서버에 연결할 수 없어 게임을 시작할 수 없습니다. 인터넷 연결을 확인하고 잠시 뒤 다시 시도해 주세요.";

        /****************************************
        *                Methods
        ****************************************/

        public static string ToLoginMessage(SessionResult result)
        {
            switch (result.Error)
            {
                case ESessionError.None:
                case ESessionError.NoStoredLogin:
                    return string.Empty;
                case ESessionError.InvalidInput:
                    return "아이디는 4~16자의 영문 소문자, 숫자, _이고 비밀번호를 입력해야 합니다.";
                case ESessionError.Busy:
                    return "이미 로그인 중입니다.";
                case ESessionError.RefreshSuspended:
                    return "게임이 실행 중이라 로그인을 갱신하지 않았습니다. 게임을 종료한 뒤 다시 시도해 주세요.";
                case ESessionError.NotLoggedIn:
                case ESessionError.SessionLost:
                    return SESSION_LOST;
                default:
                    return ToApiMessage(result.ApiError, result.ReasonCode);
            }
        }

        public static string ToApiMessage(EAuthApiError error, int reasonCode)
        {
            // 잠금과 요청 제한은 서버가 어떤 HTTP 상태로 보내도 같은 문장을 보인다
            if (reasonCode == AuthRejectCodes.ACCOUNT_LOCKED)
            {
                return ACCOUNT_LOCKED;
            }

            if (reasonCode == AuthRejectCodes.INVALID_CREDENTIALS)
            {
                return INVALID_CREDENTIALS;
            }

            switch (error)
            {
                case EAuthApiError.None:
                    return string.Empty;
                case EAuthApiError.Unreachable:
                    return "서버에 연결할 수 없습니다. 인터넷 연결을 확인하고 잠시 뒤 다시 시도해 주세요.";
                case EAuthApiError.ResponseLost:
                    return "서버 응답을 받지 못했습니다. 잠시 뒤 다시 시도해 주세요.";
                case EAuthApiError.Unauthorized:
                    return SESSION_LOST;
                case EAuthApiError.VersionMismatch:
                    return "게임 버전이 서버와 맞지 않습니다. 게임을 업데이트한 뒤 다시 시도해 주세요.";
                case EAuthApiError.Maintenance:
                    return MAINTENANCE;
                case EAuthApiError.RateLimited:
                    return RATE_LIMITED;
                case EAuthApiError.ServerError:
                    return "서버에 문제가 생겼습니다. 잠시 뒤 다시 시도해 주세요.";
                case EAuthApiError.Malformed:
                    return "서버 응답을 읽지 못했습니다. 런처를 최신 버전으로 받아 주세요.";
                default:
                    return "요청이 거절됐습니다. (코드 " + reasonCode + ")";
            }
        }

        public static string ToHandoffMessage(EGameHandoffError error)
        {
            switch (error)
            {
                case EGameHandoffError.None:
                    return string.Empty;
                case EGameHandoffError.Timeout:
                    return "게임이 제한 시간 안에 로그인 정보를 받지 못해 종료했습니다. 다시 시작해 주세요.";
                case EGameHandoffError.Cancelled:
                    return "게임 시작을 취소했습니다.";
                case EGameHandoffError.PidMismatch:
                    return "실행한 게임을 확인하지 못해 시작을 취소했습니다.";
                case EGameHandoffError.BadAck:
                    return "게임이 로그인 정보를 받지 못했습니다. 파일 검증 및 복구를 실행한 뒤 다시 시도해 주세요.";
                case EGameHandoffError.PipeBroken:
                    return "게임과의 연결이 끊어졌습니다. 다시 시작해 주세요.";
                case EGameHandoffError.StartFailed:
                    return "게임을 시작하지 못했습니다.";
                default:
                    return "게임 실행을 준비하지 못했습니다. 다시 시도해 주세요.";
            }
        }
    }
}
