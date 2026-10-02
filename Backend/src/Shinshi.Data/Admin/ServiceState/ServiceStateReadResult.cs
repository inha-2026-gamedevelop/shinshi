// System
using System;
using System.Collections.Generic;

namespace Shinshi.Data.Admin
{
    /// <summary> 서비스 상태 캐시가 한 번에 읽는 값. 공지는 아직 끝나지 않은 것(예정 포함)만 담는다 </summary>
    public sealed class ServiceStateReadResult
    {
        /****************************************
        *              Properties
        ****************************************/

        public ServiceStateRecord State { get; }
        public IReadOnlyList<ServiceNoticeRecord> Notices { get; }

        /****************************************
        *                Methods
        ****************************************/

        public ServiceStateReadResult(ServiceStateRecord state, IReadOnlyList<ServiceNoticeRecord> notices)
        {
            ArgumentNullException.ThrowIfNull(state);
            ArgumentNullException.ThrowIfNull(notices);
            State   = state;
            Notices = notices;
        }
    }
}
