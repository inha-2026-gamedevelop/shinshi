namespace Shinshi.Contracts
{
    /// <summary> 클라이언트와 런처가 부르는 API 경로, v1 안에서는 필드 추가만 허용하고 의미 변경은 v2로 낸다 </summary>
    public static class ApiRoutes
    {
        // 이 폴더의 DTO는 Unity와 백엔드가 모두 Newtonsoft.Json으로 읽고 쓴다. 필드는 private으로 두고
        // DataContract, DataMember(Name)으로 JSON 키를 정하며, 키는 camelCase이고 바꾸면 와이어 계약이 바뀐다.
        // JsonUtility와 System.Text.Json은 DataMember가 붙은 private 필드를 읽지 않아 빈 객체가 되므로 쓰지 않는다.
        // 시각은 UTC 유닉스 밀리초 long으로 보내고, 받는 쪽은 null과 빈 문자열을 모두 값 없음으로 본다.
        // 토큰을 담은 DTO는 로그에 직렬화하지 않고 필드를 출력하는 ToString도 만들지 않는다

        /****************************************
        *                Fields
        ****************************************/

        // 초대 코드나 갱신 토큰을 접근 토큰으로 교환한다. 버전 불일치도 여기서 먼저 거절한다
        public const string AUTH_EXCHANGE     = "/v1/auth/exchange";

        // 로그인한 런처가 게임 전용 하위 세션을 받는다
        public const string AUTH_GAME_SESSION = "/v1/auth/game-session";

        // 게임이 게임 세션 토큰을 bearer로 보내 만료 전에 새 하위 세션으로 바꾼다. 이전 토큰은 즉시 폐기된다
        public const string AUTH_GAME_SESSION_REFRESH = "/v1/auth/game-session/refresh";

        // 런처가 세션 전달에 실패했을 때 방금 발급한 게임 세션을 bearer로 보내 폐기한다
        public const string AUTH_GAME_SESSION_REVOKE  = "/v1/auth/game-session/revoke";

        // 런처가 저장된 갱신 토큰 원문(AuthLogoutRequest)으로 로그인 묶음과 하위 게임 세션을 폐기한다.
        // bearer 없이 부르고, 만료된 토큰이나 이미 폐기된 묶음, 모르는 토큰도 204라 재시도해도 같은 결과다
        public const string AUTH_LOGOUT       = "/v1/auth/logout";

        // 점검 여부, 공지, 기능 차단, 허용 buildId
        public const string SERVICE_STATE     = "/v1/service-state";

        // string.Format으로 operationId를 넣는다. 응답을 잃은 작업은 새 ID로 다시 보내지 않고 이 경로로 조회한다
        public const string OPERATION_FORMAT  = "/v1/operations/{0}";

        // POST로 캐릭터를 만든다. 본문은 CharacterCreateRequest, 응답은 CharacterCreateResponse
        public const string CHARACTERS        = "/v1/characters";

        // string.Format으로 characterId를 넣는다. POST로 캐릭터를 소프트 삭제한다. 본문은 CharacterDeleteRequest, 응답은 OperationResult.
        // 접속 중(ALREADY_CONNECTED), 출정 중(RESERVATION_CONFLICT), revision 불일치(STALE_REVISION),
        // 없거나 남의 캐릭터(CHARACTER_NOT_FOUND)는 거절한다. 이름은 반환하지 않고 슬롯만 돌려받는다
        public const string CHARACTER_DELETE_FORMAT = "/v1/characters/{0}/delete";

        // string.Format으로 characterId를 넣는다. POST로 삭제를 예약하거나 예약을 취소한다. 본문은 CharacterDeleteRequest, 응답은 OperationResult.
        // 삭제(CHARACTER_DELETE_FORMAT)는 예약 뒤 대기 시간이 지나야 확정되고, 예약 전이면 CHARACTER_DELETE_NOT_REQUESTED,
        // 대기 중이면 CHARACTER_DELETE_PENDING으로 거절한다. 예약과 취소도 캐릭터 revision을 올린다
        public const string CHARACTER_DELETE_REQUEST_FORMAT = "/v1/characters/{0}/delete-request";
        public const string CHARACTER_DELETE_CANCEL_FORMAT  = "/v1/characters/{0}/delete-cancel";

        // string.Format으로 sessionId를 넣는다. 진행 중 출정에서 끊긴 플레이어가 복귀용 일회 접속 토큰을 다시 받는다
        public const string REJOIN_TICKET_FORMAT = "/v1/game-sessions/{0}/rejoin-ticket";

        // 플레이어 접근 토큰으로 서버가 고른 마을의 일회 입장 토큰을 받는다
        public const string TOWN_JOIN_TICKET = "/v1/town/join-ticket";

        // GET은 자기 계정의 수령 가능한 우편 목록(InboxListResponse), POST는 우편 하나 수령(InboxClaimRequest -> OperationResult)
        public const string INBOX       = "/v1/inbox";
        public const string INBOX_CLAIM = "/v1/inbox/claim";

        // POST로 공개 가입한다. 본문은 AccountSignupRequest, 응답은 AccountSignupResponse
        public const string ACCOUNTS = "/v1/accounts";

        // GET ?loginId= 로 아이디 사용 가능 여부(AvailabilityResponse)를 본다. 안내용이며 확정은 가입 요청이 한다
        public const string ACCOUNT_LOGIN_ID_AVAILABILITY = "/v1/accounts/login-id-availability";

        // GET으로 로그인한 계정의 아이디, 이메일, 인증 여부, 가입 시각(AccountProfileResponse)을 읽는다
        public const string ACCOUNT_ME = "/v1/accounts/me";

        // POST로 비밀번호를 바꾸거나 아이디가 없는 계정의 아이디, 이메일, 비밀번호를 정한다(AccountCredentialsRequest).
        // 성공하면 그 계정의 모든 로그인 묶음과 게임 세션이 폐기되므로 새 자격으로 다시 로그인한다
        public const string ACCOUNT_CREDENTIALS = "/v1/accounts/credentials";

        // GET ?name= 으로 캐릭터 이름 사용 가능 여부(AvailabilityResponse)를 본다. 확정은 캐릭터 생성의 유일 제약이 한다
        public const string CHARACTER_NAME_AVAILABILITY = "/v1/characters/name-availability";

        // TODO(민성): 캐릭터 목록 조회와 선택 경로는 기획 미정이라 정하지 않았다
        // TODO(민성): 갱신 토큰 교환을 AUTH_EXCHANGE로 합칠지 별도 경로로 둘지 기획 미정

        // 접근 토큰은 Authorization: Bearer {token}으로 싣는다
        public const string AUTHORIZATION_HEADER = "Authorization";
        public const string BEARER_PREFIX        = "Bearer ";
    }
}
