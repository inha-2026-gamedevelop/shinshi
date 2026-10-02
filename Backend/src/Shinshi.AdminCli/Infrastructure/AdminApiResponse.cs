namespace Shinshi.AdminCli
{
    /// <summary> 성공 응답 하나. --json 출력은 RawBody를 그대로 쓰고 사람용 출력은 Value를 쓴다 </summary>
    internal sealed class AdminApiResponse<T>
    {
        /****************************************
        *              Properties
        ****************************************/

        public int StatusCode { get; }
        public string RawBody { get; }
        public T Value { get; }

        /****************************************
        *                Methods
        ****************************************/

        public AdminApiResponse(int statusCode, string rawBody, T value)
        {
            StatusCode = statusCode;
            RawBody    = rawBody;
            Value      = value;
        }
    }
}
