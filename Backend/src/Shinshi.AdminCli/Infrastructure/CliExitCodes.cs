namespace Shinshi.AdminCli
{
    /// <summary> 명령줄 도구 종료 코드. 스크립트가 거절과 연결 문제를 구분해 재시도 여부를 정한다 </summary>
    internal static class CliExitCodes
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int SUCCESS     = 0;

        // 인자 오류와 확인 거절. 서버에는 아무것도 보내지 않았다
        public const int USAGE       = 1;

        // 서버가 요청을 받고 거절했다. 같은 요청을 다시 보내도 결과는 같다
        public const int REJECTED    = 2;

        // 연결 실패, 로그인 없음, 세션 만료. 다시 로그인하거나 같은 operationId로 재시도한다
        public const int UNAVAILABLE = 3;
    }
}
