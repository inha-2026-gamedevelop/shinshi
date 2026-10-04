// System
using System;
using System.Collections.Generic;
using System.Linq;

namespace Shinshi.Data.Growth
{
    /// <summary> 계정 진행 기록 한 벌 </summary>
    public sealed class AccountProgressState
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly HashSet<(EAccountProgressKind, string)> _records;

        /****************************************
        *              Properties
        ****************************************/

        public bool IntroCompleted { get; }
        public int  FoxTraces      { get; }
        public long Revision       { get; }

        /****************************************
        *                Methods
        ****************************************/

        public AccountProgressState(bool introCompleted, int foxTraces, long revision,
            IEnumerable<(EAccountProgressKind Kind, string TargetId)> records)
        {
            ArgumentNullException.ThrowIfNull(records);
            IntroCompleted = introCompleted;
            FoxTraces      = foxTraces;
            Revision       = revision;
            _records       = new HashSet<(EAccountProgressKind, string)>(records);
        }

        public bool Has(EAccountProgressKind kind, string targetId)
        {
            return _records.Contains((kind, targetId));
        }

        /// <summary> 한 종류의 기록 대상 ID, 순서 비교 오름차순 </summary>
        public string[] IdsOf(EAccountProgressKind kind)
        {
            return _records.Where(record => record.Item1 == kind).Select(record => record.Item2)
                .OrderBy(id => id, StringComparer.Ordinal).ToArray();
        }
    }
}
