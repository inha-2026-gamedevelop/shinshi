// System
using System;
using System.Threading;

// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 서버 프로세스의 백엔드 자격, 서비스 자격은 임대 발급에만 쓰고 나머지 서버 경로는 현재 임대 토큰을 싣는다 </summary>
    public sealed class ServerCredentials
    {
        // 임대는 메인 스레드(ServerPresence)가 바꾸고 전송 계층이 스레드 풀에서 읽는다. 토큰과 만료를 한 개체로 묶어
        // 참조 하나만 교체하므로 읽는 쪽이 새 토큰과 옛 만료를 섞어 보지 않는다. 교체는 임대 발급 때만(수 분에 한 번) 일어난다.
        // 서비스 자격과 임대 토큰은 ToString, 로그, 예외 메시지에 넣지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private readonly string       _serviceAuthorization;
        private readonly string       _instanceId;
        private readonly Func<double> _clock;

        private Lease _lease   = null;
        private int   _version = 0;

        /****************************************
        *              Properties
        ****************************************/

        public string InstanceId => _instanceId;

        /// <summary> 지금 쓸 수 있는 임대가 있는지, 만료 시각이 지났으면 거짓 </summary>
        public bool HasValidLease
        {
            get
            {
                Lease lease = Volatile.Read(ref _lease);
                return (lease != null) && (_clock() < lease.ExpiresAt);
            }
        }

        /// <summary> 현재 임대의 만료 단조 시각(초), 임대가 없으면 0 </summary>
        public double LeaseExpiresAt
        {
            get
            {
                Lease lease = Volatile.Read(ref _lease);
                return (lease != null) ? lease.ExpiresAt : 0.0;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> clock은 단조 증가 초, null이면 게이트웨이와 같은 시계를 쓴다 </summary>
        public ServerCredentials(string serviceToken, string instanceId, Func<double> clock)
        {
            if (string.IsNullOrEmpty(serviceToken))
            {
                throw new ArgumentException("서비스 자격이 필요하다", nameof(serviceToken));
            }

            if (string.IsNullOrEmpty(instanceId))
            {
                throw new ArgumentException("인스턴스 ID가 필요하다", nameof(instanceId));
            }

            _serviceAuthorization = ApiRoutes.BEARER_PREFIX + serviceToken;
            _instanceId           = instanceId;
            _clock                = clock ?? BackendGateway.MonotonicSeconds;
        }

        /// <summary> 임대 발급 경로인지, 이 경로만 서비스 자격으로 부른다 </summary>
        public static bool IsLeasePath(string path)
        {
            return string.Equals(path, GameServerRoutes.SERVER_LEASE, StringComparison.Ordinal);
        }

        /// <summary> 새 임대를 쓴다, 메인 스레드에서만 부른다. expiresAt은 단조 시각(초) </summary>
        public void SetLease(string leaseToken, double expiresAt)
        {
            if (string.IsNullOrEmpty(leaseToken))
            {
                throw new ArgumentException("임대 토큰이 필요하다", nameof(leaseToken));
            }

            ++_version;
            Volatile.Write(ref _lease, new Lease(ApiRoutes.BEARER_PREFIX + leaseToken, expiresAt, _version));
        }

        /// <summary> API가 임대를 거절했을 때 버린다, 이후 서버 경로는 새 임대를 받을 때까지 보내지 않는다 </summary>
        public void ClearLease()
        {
            Volatile.Write(ref _lease, null);
        }

        /// <summary> 경로에 맞는 Authorization 값, 임대가 필요한데 유효한 임대가 없으면 거짓 </summary>
        public bool TryGetAuthorization(string path, out string authorization)
        {
            return TryGetAuthorization(path, out authorization, out _);
        }

        /// <summary> leaseVersion은 실어 보낼 임대의 번호다. 서비스 자격이면 0이다. 401 뒤 새 임대가 생겼는지 비교하는 데 쓴다 </summary>
        public bool TryGetAuthorization(string path, out string authorization, out int leaseVersion)
        {
            leaseVersion = 0;
            if (IsLeasePath(path))
            {
                authorization = _serviceAuthorization;
                return true;
            }

            Lease lease = Volatile.Read(ref _lease);
            if ((lease == null) || (_clock() >= lease.ExpiresAt))
            {
                authorization = null;
                return false;
            }

            authorization = lease.Authorization;
            leaseVersion  = lease.Version;
            return true;
        }

        /****************************************
        *               Records
        ****************************************/

        private sealed class Lease
        {
            public string Authorization { get; }
            public double ExpiresAt     { get; }
            public int    Version       { get; }

            public Lease(string authorization, double expiresAt, int version)
            {
                Authorization = authorization;
                ExpiresAt     = expiresAt;
                Version       = version;
            }
        }
    }
}
