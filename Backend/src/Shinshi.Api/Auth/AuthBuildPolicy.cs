// System
using System;

// Third Party
using Microsoft.Extensions.Options;

// Project
using Shinshi.Contracts;

namespace Shinshi.Backend
{
    /// <summary> 코드 소비 전에 현재 서비스 상태와 설치 빌드를 검증한다 </summary>
    public sealed class AuthBuildPolicy
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IOptionsMonitor<AuthBuildOptions> _build;
        private readonly IOptionsMonitor<ServiceStateOptions> _service;

        /****************************************
        *                Methods
        ****************************************/

        public AuthBuildPolicy(IOptionsMonitor<AuthBuildOptions> build,
            IOptionsMonitor<ServiceStateOptions> service)
        {
            ArgumentNullException.ThrowIfNull(build);
            ArgumentNullException.ThrowIfNull(service);
            _build   = build;
            _service = service;
        }

        /// <summary> 성공은 NONE, 그 밖은 기존 인증 거절 코드다. 빈 배포 설정은 접속을 허용하지 않는다 </summary>
        public int Validate(ClientBuildInfo? build)
        {
            ServiceStateOptions service = _service.CurrentValue;
            AuthBuildOptions expected = _build.CurrentValue;
            if (service.Maintenance)
            {
                return ContractCodes.RejectReason.MAINTENANCE;
            }
            if ((build == null) || (expected.ProtocolVersion <= 0)
                || string.IsNullOrWhiteSpace(expected.ContentHash)
                || (build.ProtocolVersion != expected.ProtocolVersion)
                || !string.Equals(build.ContentHash, expected.ContentHash, StringComparison.Ordinal))
            {
                return ContractCodes.RejectReason.VERSION_MISMATCH;
            }
            if (string.IsNullOrWhiteSpace(build.BuildId)
                || (Array.IndexOf(service.AllowedBuildIds ?? Array.Empty<string>(), build.BuildId) < 0))
            {
                return ContractCodes.RejectReason.BUILD_NOT_ALLOWED;
            }

            return ContractCodes.RejectReason.NONE;
        }
    }
}
