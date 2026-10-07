// System
using System;

namespace Shinshi.Backend.Observability
{
    /// <summary> Prometheus 조회는 기본적으로 루프백에서만 연다 </summary>
    public sealed class MetricsOptions
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string SECTION_NAME = "Metrics";
        public const string ROUTE        = "/metrics";
        public const string CONTENT_TYPE = "text/plain; version=0.0.4; charset=utf-8";

        /****************************************
        *              Properties
        ****************************************/

        public bool Enabled { get; set; } = true;
        public string[] AllowedNetworks { get; set; } = Array.Empty<string>();
    }
}
