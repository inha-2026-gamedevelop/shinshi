// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Shinshi.Backend.Observability
{
    /// <summary> 허용 네트워크에서만 Prometheus 지표를 읽는다. 외부 요청은 본문 없이 404다 </summary>
    [ApiController]
    [AllowAnonymous]
    public sealed class MetricsController : ControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly ApiMetrics _metrics;
        private readonly MetricsNetworkPolicy _networkPolicy;
        private readonly IMetricsDatabaseReader _database;

        /****************************************
        *                Methods
        ****************************************/

        public MetricsController(ApiMetrics metrics, MetricsNetworkPolicy networkPolicy, IMetricsDatabaseReader database)
        {
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(networkPolicy);
            ArgumentNullException.ThrowIfNull(database);
            _metrics       = metrics;
            _networkPolicy = networkPolicy;
            _database      = database;
        }

        [Route(MetricsOptions.ROUTE)]
        public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
        {
            if ((!_networkPolicy.IsAllowed(HttpContext)) || (!Microsoft.AspNetCore.Http.HttpMethods.IsGet(Request.Method)))
            {
                Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status404NotFound;
                return new EmptyResult();
            }
            Response.Headers.CacheControl = "no-store";
            DatabaseMetricsSnapshot database = await _database.ReadAsync(cancellationToken);
            string text = PrometheusMetricsWriter.Render(_metrics.GetRequestSnapshots(), _metrics.GetSnapshot(), database);
            return Content(text, MetricsOptions.CONTENT_TYPE);
        }
    }
}
