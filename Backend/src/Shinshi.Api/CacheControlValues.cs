namespace Shinshi.Backend
{
    /// <summary> 응답 Cache-Control 값. 토큰, 계정 데이터, 비밀값이 실릴 수 있는 응답은 중간 캐시와 브라우저에 남기지 않는다 </summary>
    public static class CacheControlValues
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string NO_STORE = "no-store";
    }
}
