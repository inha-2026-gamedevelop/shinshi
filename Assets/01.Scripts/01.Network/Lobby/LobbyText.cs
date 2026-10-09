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
                case ContractCodes.PlayerClass.SWORDSMAN:
                    return "검사";
                case ContractCodes.PlayerClass.MAGEKNIGHT:
                    return "마검사";
                case ContractCodes.PlayerClass.ARCHER:
                    return "궁수";
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
                case ContractCodes.PlayerClass.SWORDSMAN:
                    return "검격의 방향과 접근을 조절하며 연격하는 근접 직업";
                case ContractCodes.PlayerClass.MAGEKNIGHT:
                    return "검에 술식을 실어 범위를 넓히고 적을 묶는 근접 직업";
                case ContractCodes.PlayerClass.ARCHER:
                    return "직접 조준한 화살로 일렬의 적을 꿰뚫는 원거리 직업";
                default:
                    return string.Empty;
            }
        }

        public static string PreviewFailed() { return "선택한 조합을 미리 볼 수 없다. 다른 조합을 고르거나 게임을 다시 실행한다."; }

        // P09 커스터마이징 행 값
        public const string NONE = "없음";

        public static string SexName(int bodyType) { return (bodyType == Shinshi.Contracts.CharacterAppearance.BODY_TYPE_MALE) ? "남성" : "여성"; }

        public static string HairName(int hairId)
        {
            return (hairId == Shinshi.Contracts.CharacterAppearanceCatalog.P09_HAIR_SKINHEAD) ? "민머리" : hairId.ToString();
        }

        public static string BustName(int bustSize)
        {
            switch (bustSize)
            {
                case 1:  return "작게";
                case 3:  return "크게";
                default: return "보통";
            }
        }

        public static string ArmorState(bool isOn) { return isOn ? "착용" : "벗음"; }

        public static string HairLabel(int hairId)
        {
            switch (hairId)
            {
                case CharacterAppearanceCatalog.HAIR_WARRIOR:
                    return "헤어 1";
                case CharacterAppearanceCatalog.HAIR_SORCERER:
                    return "헤어 2";
                case CharacterAppearanceCatalog.HAIR_SUMMONER:
                    return "헤어 3";
                default:
                    return "알 수 없는 헤어";
            }
        }

        public static string OutfitLabel(int outfitId)
        {
            switch (outfitId)
            {
                case CharacterAppearanceCatalog.OUTFIT_WARRIOR:
                    return "의상 1";
                case CharacterAppearanceCatalog.OUTFIT_SORCERER:
                    return "의상 2";
                case CharacterAppearanceCatalog.OUTFIT_SUMMONER:
                    return "의상 3";
                default:
                    return "알 수 없는 의상";
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
                    return "선택한 외형을 쓸 수 없다. 게임을 최신 버전으로 실행한다.";
                case ERejectReason.IdempotencyConflict:
                    return "이전 요청과 다른 선택이다. 다시 확인하고 생성한다.";
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
