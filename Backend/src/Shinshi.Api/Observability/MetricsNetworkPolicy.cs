// System
using System;
using System.Threading;

// Third-party
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Project
using Shinshi.Backend.Admin;

namespace Shinshi.Backend.Observability
{
    /// <summary> 운영자 경로와 같은 IP, CIDR, 신뢰 프록시 경계로 지표 조회를 제한한다 </summary>
    public sealed class MetricsNetworkPolicy
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string FORWARDED_FOR_HEADER = "X-Forwarded-For";
        private const string FORWARDED_HEADER     = "Forwarded";

        private readonly AdminNetworkPolicy _policy;
        private readonly ILogger<MetricsNetworkPolicy> _logger;
        private readonly bool _enabled;
        private int _forwardingWarningLogged;

        /****************************************
        *                Methods
        ****************************************/

        public MetricsNetworkPolicy(IOptions<MetricsOptions> options, IConfiguration configuration,
            ILogger<MetricsNetworkPolicy> logger)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(logger);
            _enabled = options.Value.Enabled;
            _logger  = logger;
            try
            {
                _policy = _enabled ? AdminNetworkPolicy.Parse(options.Value.AllowedNetworks,
                    ForwardedHeadersConfiguration.ReadKnownProxies(configuration))
                    : AdminNetworkPolicy.Parse(Array.Empty<string>());
            }
            catch (InvalidOperationException exception)
            {
                // 재사용 정책의 설정 키만 바꾼다. 허용 주소나 프록시 값은 예외에 넣지 않는다
                throw new InvalidOperationException(exception.Message.Replace("Admin:AllowedNetworks",
                    "Metrics:AllowedNetworks", StringComparison.Ordinal));
            }
        }

        public bool IsAllowed(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (!_enabled)
            {
                return false;
            }
            if ((!_policy.TrustsForwardedHeaders)
                && (context.Request.Headers.ContainsKey(FORWARDED_FOR_HEADER)
                    || context.Request.Headers.ContainsKey(FORWARDED_HEADER)))
            {
                if (Interlocked.Exchange(ref _forwardingWarningLogged, 1) == 0)
                {
                    try
                    {
                        _logger.LogError("Metrics request carried forwarding headers but ForwardedHeaders:KnownProxies "
                            + "is empty; metrics stay hidden until the trusted proxy is configured.");
                    }
                    catch (Exception)
                    {
                        // 로그 공급자 장애에도 네트워크 거절은 404로 유지한다
                    }
                }
                return false;
            }
            return _policy.IsAllowed(context.Connection.RemoteIpAddress);
        }
    }
}
