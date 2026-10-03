// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 로비 화면에 보일 안내 문구. 거절 코드와 직업을 사람이 읽는 말로 바꾼다 </summary>
    public static class LobbyText
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long HTTP_UNAUTHORIZED = 401;
        private const long HTTP_SERVER_ERROR = 500;

        /****************************************
        *                Methods
        ****************************************/

        public static string ClassName(int classId)
        {
            switch (classId)
            {
                case ContractCodes.PlayerClass.WARRIOR:
                    return "무사";
                case ContractCodes.PlayerClass.SORCERER:
                    return "도술사";
                case ContractCodes.PlayerClass.SUMMONER:
                    return "신령사";
                default:
                    return "알 수 없는 직업";
            }
        }

        public static string ClassDescription(int classId)
        {
            switch (classId)
            {
                case ContractCodes.PlayerClass.WARRIOR:
                    return "대검으로 앞을 막고 반격하는 근접 직업";
                case ContractCodes.PlayerClass.SORCERER:
                    return "장판과 속박, 낙뢰로 싸우는 원거리 직업";
                case ContractCodes.PlayerClass.SUMMONER:
                    return "동료를 부르고 강림하는 소환 직업";
                default:
                    return string.Empty;
            }
        }

        /// <summary> API 실패를 안내 문구로 바꾼다 </summary>
        public static string Describe<T>(LobbyApiReply<T> reply) where T : class
        {
            if (reply == null)
            {
                return "알 수 없는 오류가 났다.";
            }

            if (reply.ReasonCode != ContractCodes.RejectReason.NONE)
            {
                return Describe((ERejectReason)reply.ReasonCode);
            }

            if (reply.StatusCode == 0)
            {
                return "서버에 연결하지 못했다. API 서버가 켜져 있는지 확인한다.";
            }

            if (reply.StatusCode == HTTP_UNAUTHORIZED)
            {
                return "로그인이 만료됐다. 다시 로그인한다.";
            }

            if (reply.StatusCode >= HTTP_SERVER_ERROR)
            {
                return $"서버 오류다(HTTP {reply.StatusCode}). 잠시 뒤 다시 시도한다.";
            }

            return reply.IsSuccess ? string.Empty : $"요청이 실패했다(HTTP {reply.StatusCode}).";
        }

        public static string Describe(ERejectReason reason)
        {
            switch (reason)
            {
                case ERejectReason.None:
                    return string.Empty;
                case ERejectReason.TokenExpired:
                case ERejectReason.TokenRevoked:
                    return "로그인이 만료됐다. 다시 로그인한다.";
                case ERejectReason.VersionMismatch:
                    return "게임 버전이 서버와 다르다. 최신 빌드로 실행한다.";
                case ERejectReason.BuildNotAllowed:
                    return "서버가 허용하지 않은 빌드다.";
                case ERejectReason.Maintenance:
                    return "점검 중이다.";
                case ERejectReason.Sanctioned:
                    return "이용이 제한된 계정이다.";
                case ERejectReason.AlreadyConnected:
                    return "이미 접속 중인 계정이다. 다른 게임 창을 닫고 다시 시도한다.";
                case ERejectReason.BackendUnavailable:
                    return "서버가 일시적으로 응답하지 않는다. 잠시 뒤 다시 시도한다.";
                case ERejectReason.RateLimited:
                    return "요청이 너무 잦다. 잠시 뒤 다시 시도한다.";
                case ERejectReason.NoCapacity:
                    return "자리가 없다. 캐릭터 수 상한이거나 열린 마을 서버가 없다.";
                case ERejectReason.InvalidAppearance:
                    return "외형 값이 올바르지 않다.";
                case ERejectReason.CharacterNameTaken:
                    return "이미 쓰는 이름이다.";
                case ERejectReason.InvalidCharacterName:
                    return $"이름은 {CharacterNameRules.MIN_LENGTH}~{CharacterNameRules.MAX_LENGTH}자의 한글, 영문, 숫자로 정한다.";
                case ERejectReason.FeatureDisabled:
                    return "지금은 쓸 수 없는 기능이다.";
                case ERejectReason.LoginIdTaken:
                    return "이미 쓰는 아이디다.";
                case ERejectReason.InvalidLoginId:
                    return $"아이디는 {AccountCredentialRules.LOGIN_ID_MIN_LENGTH}~{AccountCredentialRules.LOGIN_ID_MAX_LENGTH}자의 영문, 숫자, 밑줄로 정한다.";
                case ERejectReason.InvalidEmail:
                    return "가입에는 올바른 이메일이 필요하다.";
                case ERejectReason.WeakPassword:
                    return $"비밀번호는 {AccountCredentialRules.PASSWORD_MIN_LENGTH}자 이상, 영문과 숫자를 모두 넣고 아이디와 달라야 한다.";
                case ERejectReason.InvalidCredentials:
                    return "아이디나 비밀번호가 맞지 않는다.";
                case ERejectReason.AccountLocked:
                    return "로그인 실패가 많아 잠시 막혔다. 잠시 뒤 다시 시도한다.";
                default:
                    return $"요청이 거절됐다({reason}).";
            }
        }
    }
}
