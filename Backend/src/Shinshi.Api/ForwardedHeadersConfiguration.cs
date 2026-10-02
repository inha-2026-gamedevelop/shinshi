// System
using System;
using System.Net;

// Third-party
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;

namespace Shinshi.Backend
{
    /// <summary> 로드밸런서 뒤에서 원래 클라이언트 IP를 복원한다. 설정에 신뢰할 프록시 주소가 있을 때만 켠다.
    /// 아무 프록시나 믿으면 클라이언트가 X-Forwarded-For로 IP를 바꿔 IP별 한도를 피한다 </summary>
    public static class ForwardedHeadersConfiguration
    {
        /****************************************
        *                Fields
        ****************************************/

        // ForwardedHeaders__KnownProxies__0 = "10.0.0.5" 처럼 환경으로 주입한다
        public const string KNOWN_PROXIES_SECTION = "ForwardedHeaders:KnownProxies";

        // 로드밸런서 한 단만 믿는다. 그 앞에서 클라이언트가 붙인 값은 쓰지 않는다
        private const int FORWARD_LIMIT = 1;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 신뢰 프록시 목록을 읽는다. 주소 형식이 틀리면 시작을 실패시킨다 </summary>
        public static IPAddress[] ReadKnownProxies(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            string[] values = configuration.GetSection(KNOWN_PROXIES_SECTION).Get<string[]>() ?? Array.Empty<string>();
            IPAddress[] proxies = new IPAddress[values.Length];
            for (int index = 0; index < values.Length; ++index)
            {
                if (!IPAddress.TryParse(values[index], out IPAddress? address))
                {
                    throw new InvalidOperationException("ForwardedHeaders:KnownProxies must contain IP addresses.");
                }
                proxies[index] = address;
            }

            return proxies;
        }

        /// <summary> 신뢰 프록시가 있을 때만 X-Forwarded-For를 적용한다. 라우팅과 요청 한도보다 먼저 호출한다 </summary>
        public static IApplicationBuilder UseShinshiForwardedHeaders(this IApplicationBuilder app, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(app);
            IPAddress[] proxies = ReadKnownProxies(configuration);
            if (proxies.Length == 0)
            {
                return app;
            }

            ForwardedHeadersOptions options = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor,
                ForwardLimit     = FORWARD_LIMIT
            };
            // 기본값은 루프백 대역과 주소를 믿는다. 설정한 프록시만 남긴다
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (IPAddress proxy in proxies)
            {
                options.KnownProxies.Add(proxy);
            }

            return app.UseForwardedHeaders(options);
        }
    }
}
