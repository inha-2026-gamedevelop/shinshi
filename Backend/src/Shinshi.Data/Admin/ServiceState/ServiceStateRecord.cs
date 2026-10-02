// System
using System;
using System.Collections.Generic;

namespace Shinshi.Data.Admin
{
    /// <summary> service_state 단일 행. 목록은 저장된 순서 그대로다 </summary>
    public sealed class ServiceStateRecord
    {
        /****************************************
        *              Properties
        ****************************************/

        public bool Maintenance { get; }
        public IReadOnlyList<string> DisabledFeatures { get; }
        public IReadOnlyList<string> AllowedBuildIds { get; }
        public long Revision { get; }
        public DateTimeOffset UpdatedAt { get; }
        public Guid? UpdatedBy { get; }

        /****************************************
        *                Methods
        ****************************************/

        public ServiceStateRecord(bool maintenance, IReadOnlyList<string> disabledFeatures,
            IReadOnlyList<string> allowedBuildIds, long revision, DateTimeOffset updatedAt, Guid? updatedBy)
        {
            ArgumentNullException.ThrowIfNull(disabledFeatures);
            ArgumentNullException.ThrowIfNull(allowedBuildIds);
            Maintenance      = maintenance;
            DisabledFeatures = disabledFeatures;
            AllowedBuildIds  = allowedBuildIds;
            Revision         = revision;
            UpdatedAt        = updatedAt;
            UpdatedBy        = updatedBy;
        }
    }
}
