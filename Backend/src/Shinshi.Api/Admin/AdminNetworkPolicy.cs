// System
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Shinshi.Backend.Admin
{
    /// <summary> 어드민 경로를 열 원격 주소 목록. ForwardedHeaders가 복원한 RemoteIpAddress로 판정하며 목록이 비면 루프백만 연다 </summary>
    public sealed class AdminNetworkPolicy
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int  IPV4_HOST_PREFIX = 32;
        private const int  IPV6_HOST_PREFIX = 128;
        private const char PREFIX_SEPARATOR = '/';

        private readonly System.Net.IPNetwork[] _networks;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> ForwardedHeaders:KnownProxies가 설정돼 RemoteIpAddress를 X-Forwarded-For에서 복원하는지 여부 </summary>
        public bool TrustsForwardedHeaders { get; }

        /****************************************
        *                Methods
        ****************************************/

        public AdminNetworkPolicy(IEnumerable<System.Net.IPNetwork> networks, bool trustsForwardedHeaders = false)
        {
            ArgumentNullException.ThrowIfNull(networks);
            _networks              = new List<System.Net.IPNetwork>(networks).ToArray();
            TrustsForwardedHeaders = trustsForwardedHeaders;
        }

        /// <summary> 형식이 틀린 항목이 있으면 시작을 실패시킨다. 호스트 비트가 남은 CIDR도 오타로 보고 거절한다.
        /// 신뢰 프록시 주소가 허용 범위 안에 있어도(목록이 비면 루프백 프록시) 시작을 실패시킨다 </summary>
        public static AdminNetworkPolicy Parse(string[]? values, IReadOnlyList<IPAddress>? knownProxies = null)
        {
            List<System.Net.IPNetwork> networks = new List<System.Net.IPNetwork>();
            foreach (string value in values ?? Array.Empty<string>())
            {
                string trimmed = (value ?? string.Empty).Trim();
                if (trimmed.IndexOf(PREFIX_SEPARATOR) < 0)
                {
                    if (!IPAddress.TryParse(trimmed, out IPAddress? address))
                    {
                        throw new InvalidOperationException("Admin:AllowedNetworks must contain CIDR networks or IP addresses.");
                    }
                    networks.Add(new System.Net.IPNetwork(Normalize(address),
                        address.AddressFamily == AddressFamily.InterNetwork ? IPV4_HOST_PREFIX : IPV6_HOST_PREFIX));
                    continue;
                }

                // TryParse는 호스트 비트를 지우고 받아들이므로 적은 주소와 기준 주소가 같은지 따로 본다
                if ((!System.Net.IPNetwork.TryParse(trimmed, out System.Net.IPNetwork network))
                    || (!IPAddress.TryParse(trimmed.Substring(0, trimmed.IndexOf(PREFIX_SEPARATOR)), out IPAddress? written))
                    || (!written.Equals(network.BaseAddress)))
                {
                    throw new InvalidOperationException("Admin:AllowedNetworks must contain CIDR networks or IP addresses.");
                }
                networks.Add(network);
            }

            // 프록시가 X-Forwarded-For를 빠뜨리거나 한도를 넘겨 복원하지 못한 요청은 RemoteIpAddress가 프록시 주소로 남는다.
            // 그 주소가 허용 범위면 프록시를 지나는 모든 외부 요청이 어드민 경로를 통과하므로 설정 자체를 막는다
            AdminNetworkPolicy policy = new AdminNetworkPolicy(networks,
                (knownProxies != null) && (knownProxies.Count > 0));
            foreach (IPAddress proxy in knownProxies ?? Array.Empty<IPAddress>())
            {
                if (policy.IsAllowed(proxy))
                {
                    throw new InvalidOperationException(
                        "ForwardedHeaders:KnownProxies must not fall inside Admin:AllowedNetworks "
                        + "(an empty list allows loopback, so a loopback proxy needs explicit operator networks).");
                }
            }

            return policy;
        }

        public bool IsAllowed(IPAddress? address)
        {
            if (address == null)
            {
                return false;
            }

            IPAddress normalized = Normalize(address);
            if (_networks.Length == 0)
            {
                return IPAddress.IsLoopback(normalized);
            }

            foreach (System.Net.IPNetwork network in _networks)
            {
                if (network.Contains(normalized))
                {
                    return true;
                }
            }

            return false;
        }

        // 듀얼 스택 소켓은 IPv4 연결을 ::ffff:a.b.c.d로 보고한다. IPv4 CIDR과 비교하려면 되돌려야 한다
        private static IPAddress Normalize(IPAddress address)
        {
            return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        }
    }
}
