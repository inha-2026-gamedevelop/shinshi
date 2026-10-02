// System
using System;
using System.Collections.Generic;

namespace Shinshi.Data.Admin
{
    /// <summary> 검증을 마친 서비스 상태 저장 요청. expectedRevision이 현재 행과 다르면 저장하지 않는다 </summary>
    public sealed class ServiceStateChange
    {
        /****************************************
        *              Properties
        ****************************************/

        public bool Maintenance { get; }
        public IReadOnlyList<string> DisabledFeatures { get; }
        public IReadOnlyList<string> AllowedBuildIds { get; }
        public long ExpectedRevision { get; }

        /****************************************
        *                Methods
        ****************************************/

        public ServiceStateChange(bool maintenance, IReadOnlyList<string> disabledFeatures,
            IReadOnlyList<string> allowedBuildIds, long expectedRevision)
        {
            ArgumentNullException.ThrowIfNull(disabledFeatures);
            ArgumentNullException.ThrowIfNull(allowedBuildIds);
            ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
            Maintenance      = maintenance;
            DisabledFeatures = disabledFeatures;
            AllowedBuildIds  = allowedBuildIds;
            ExpectedRevision = expectedRevision;
        }
    }
}
