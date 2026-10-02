// System
using System;

namespace Shinshi.Data
{
    /// <summary> DB 제한 초과로 결과를 확정하지 못했다. 호출자는 같은 작업 ID로 결과 조회나 재시도를 할 수 있다 </summary>
    public sealed class RetryableDatabaseException : Exception
    {
        /****************************************
        *              Properties
        ****************************************/

        public bool IsRetryable => true;

        /****************************************
        *                Methods
        ****************************************/

        public RetryableDatabaseException(Exception innerException)
            : base("The database operation timed out and can be retried.", innerException)
        {
            ArgumentNullException.ThrowIfNull(innerException);
        }
    }
}
