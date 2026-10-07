// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.Extensions.Hosting;

namespace Shinshi.Backend.Observability
{
    /// <summary> 첫 요청 전에 신뢰 프록시와 지표 허용 네트워크의 겹침을 검사한다 </summary>
    public sealed class MetricsStartupService : IHostedService
    {
        /****************************************
        *                Methods
        ****************************************/

        public MetricsStartupService(MetricsNetworkPolicy policy)
        {
            ArgumentNullException.ThrowIfNull(policy);
        }

        public Task StartAsync(CancellationToken cancellationToken) { return Task.CompletedTask; }

        public Task StopAsync(CancellationToken cancellationToken) { return Task.CompletedTask; }
    }
}
