// System
using System;

namespace Shinshi.Backend.Admin
{
    /// <summary> 어드민 요청 형식 오류. AdminExceptionFilter가 400과 INVALID_REQUEST로 바꾸며 메시지는 응답에 싣지 않는다 </summary>
    public sealed class AdminRequestException : Exception
    {
        /****************************************
        *                Methods
        ****************************************/

        public AdminRequestException(string message) : base(message)
        {
        }
    }
}
