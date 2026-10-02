// System
using System;

namespace Shinshi.Data.Admin
{
    /// <summary> 효과 전에 영속 결속한 운영자 작업 키의 요청 일치 여부와 DB 시각 </summary>
    public sealed class AdminOperationBinding
    {
        /****************************************
        *              Properties
        ****************************************/

        public bool Matches { get; }
        public DateTimeOffset ServerTime { get; }

        /****************************************
        *                Methods
        ****************************************/

        internal AdminOperationBinding(bool matches, DateTimeOffset serverTime)
        {
            Matches    = matches;
            ServerTime = serverTime;
        }
    }
}
