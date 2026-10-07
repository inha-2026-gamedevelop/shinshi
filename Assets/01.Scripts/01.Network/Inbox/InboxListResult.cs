// System
using System;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 우편 목록 조회 결과 </summary>
    public readonly struct InboxListResult
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly bool          _succeeded;
        private readonly InboxEntry[]  _entries;
        private readonly long          _serverTimeUnixMs;
        private readonly ERejectReason _reason;

        /****************************************
        *              Properties
        ****************************************/

        public bool          Succeeded        => _succeeded;
        public InboxEntry[]  Entries          => _entries ?? Array.Empty<InboxEntry>();
        public long          ServerTimeUnixMs => _serverTimeUnixMs;
        public ERejectReason Reason           => _reason;

        /****************************************
        *                Methods
        ****************************************/

        public InboxListResult(bool succeeded, InboxEntry[] entries, long serverTimeUnixMs, ERejectReason reason)
        {
            _succeeded        = succeeded;
            _entries          = entries;
            _serverTimeUnixMs = serverTimeUnixMs;
            _reason           = reason;
        }
    }
}
