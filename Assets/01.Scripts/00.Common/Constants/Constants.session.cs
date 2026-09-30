namespace Shinshi.Common
{
    public static partial class Constants
    {
        /// <summary> 출정 인원처럼 세션 규칙의 뼈대가 되는 계약값 </summary>
        public static class Session
        {
            /****************************************
            *                Fields
            ****************************************/

            // 시작 인원별 난이도 표의 배열 길이와 같아야 한다
            public const int MIN_PARTY_SIZE = 1;
            public const int MAX_PARTY_SIZE = 4;
        }
    }
}
