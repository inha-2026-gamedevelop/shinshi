// System
using System.Collections.Generic;

namespace Shinshi.Backend.Observability
{
    /// <summary> 경로 템플릿과 상태 분류별 누적 완료 요청 분포 </summary>
    public sealed class ApiRequestMetricSnapshot
    {
        /****************************************
        *              Properties
        ****************************************/

        public string Route { get; }
        public string StatusClass { get; }
        public long Count { get; }
        public double SumMs { get; }
        public IReadOnlyList<long> Buckets { get; }

        /****************************************
        *                Methods
        ****************************************/

        internal ApiRequestMetricSnapshot(string route, string statusClass, long count, double sumMs,
            IReadOnlyList<long> buckets)
        {
            Route       = route;
            StatusClass = statusClass;
            Count       = count;
            SumMs       = sumMs;
            Buckets     = buckets;
        }
    }
}
