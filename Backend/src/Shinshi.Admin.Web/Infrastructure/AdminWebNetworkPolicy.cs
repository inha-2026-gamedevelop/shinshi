// System
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Shinshi.Admin.Web.Infrastructure
{
    /// <summary> 웹의 모든 경로에 적용하는 허용 목록. 프록시 자체를 허용하면 전달 헤더가 빠졌을 때 모두 통과하므로 시작을 막는다 </summary>
    public sealed class AdminWebNetworkPolicy
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string NETWORKS_SECTION = "AdminWeb:AllowedNetworks";
        public const string PROXIES_SECTION = "ForwardedHeaders:KnownProxies";

        private const int IPV4_HOST_PREFIX = 32;
        private const int IPV6_HOST_PREFIX = 128;
        private const int MAPPED_IPV4_PREFIX = 96;
        private readonly System.Net.IPNetwork[] _networks;

        /****************************************
        *              Properties
        ****************************************/

        public bool TrustsForwardedHeaders { get; }

        /****************************************
        *                Methods
        ****************************************/

        private AdminWebNetworkPolicy(System.Net.IPNetwork[] networks, bool trustsForwardedHeaders)
        {
            _networks = networks;
            TrustsForwardedHeaders = trustsForwardedHeaders;
        }

        public static AdminWebNetworkPolicy Parse(string[]? allowedNetworks,
            IReadOnlyList<IPAddress>? knownProxies = null)
        {
            List<System.Net.IPNetwork> networks = new();
            foreach (string value in allowedNetworks ?? Array.Empty<string>())
            {
                string trimmed = (value ?? string.Empty).Trim();
                int separator = trimmed.IndexOf('/');
                string addressText = separator < 0 ? trimmed : trimmed.Substring(0, separator);
                if (!IPAddress.TryParse(addressText, out IPAddress? address))
                {
                    throw InvalidConfiguration();
                }

                int prefix = address.AddressFamily == AddressFamily.InterNetwork
                    ? IPV4_HOST_PREFIX : IPV6_HOST_PREFIX;
                if ((separator >= 0) && !int.TryParse(trimmed.Substring(separator + 1),
                    NumberStyles.None, CultureInfo.InvariantCulture, out prefix))
                {
                    throw InvalidConfiguration();
                }

                if (address.IsIPv4MappedToIPv6)
                {
                    if ((prefix < MAPPED_IPV4_PREFIX) || (prefix > IPV6_HOST_PREFIX))
                    {
                        throw InvalidConfiguration();
                    }
                    prefix -= MAPPED_IPV4_PREFIX;
                    address = address.MapToIPv4();
                }

                int maximum = address.AddressFamily == AddressFamily.InterNetwork
                    ? IPV4_HOST_PREFIX : IPV6_HOST_PREFIX;
                if ((prefix < 0) || (prefix > maximum))
                {
                    throw InvalidConfiguration();
                }

                // 파서는 호스트 비트를 지우고 받을 수 있으므로 입력 주소와 파싱한 기준 주소를 별도로 비교한다
                string normalizedNetwork = address + "/" + prefix.ToString(CultureInfo.InvariantCulture);
                if ((!System.Net.IPNetwork.TryParse(normalizedNetwork, out System.Net.IPNetwork network))
                    || (!address.Equals(network.BaseAddress)))
                {
                    throw InvalidConfiguration();
                }
                networks.Add(network);
            }

            AdminWebNetworkPolicy policy = new(networks.ToArray(), (knownProxies?.Count ?? 0) > 0);
            foreach (IPAddress proxy in knownProxies ?? Array.Empty<IPAddress>())
            {
                if (policy.IsAllowed(proxy))
                {
                    throw new InvalidOperationException(
                        "ForwardedHeaders:KnownProxies must not fall inside AdminWeb:AllowedNetworks (empty allows loopback).");
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

            IPAddress normalized = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
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

        private static InvalidOperationException InvalidConfiguration()
        {
            return new InvalidOperationException(
                "AdminWeb:AllowedNetworks must contain IP addresses or CIDR networks with no host bits set.");
        }
    }
}
