// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.Extensions.Options;

// Project
using Shinshi.Backend.Admin;
using Shinshi.Contracts;

namespace Shinshi.Backend
{
    /// <summary> 코드 소비 전에 현재 서비스 상태와 설치 빌드를 검증한다. 점검과 허용 빌드는 ServiceStateStore 캐시 스냅샷을 읽는다 </summary>
    public sealed class AuthBuildPolicy
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IOptionsMonitor<AuthBuildOptions> _build;
        private readonly ServiceStateStore _service;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> store가 없으면 DB 없이 ServiceState 설정만 읽는다. DI는 ServiceStateStore가 등록돼 있으면 넣어 준다 </summary>
        public AuthBuildPolicy(IOptionsMonitor<AuthBuildOptions> build,
            IOptionsMonitor<ServiceStateOptions> service, ServiceStateStore? store = null)
        {
            ArgumentNullException.ThrowIfNull(build);
            ArgumentNullException.ThrowIfNull(service);
            _build   = build;
            _service = store ?? ServiceStateStore.FromOptions(service);
        }

        /// <summary> 성공은 NONE, 그 밖은 기존 인증 거절 코드다. 빈 배포 설정은 접속을 허용하지 않는다.
        /// DB 모드에서 첫 읽기 전이면 기다리지 않고 MAINTENANCE다. 요청 경로는 ValidateAsync를 쓴다 </summary>
        public int Validate(ClientBuildInfo? build)
        {
            return Validate(_service.Current, build);
        }

        /// <summary> Validate와 같고, DB 모드에서 첫 읽기 전이면 그 읽기를 비동기로 기다린다 </summary>
        public async ValueTask<int> ValidateAsync(ClientBuildInfo? build, CancellationToken cancellationToken = default)
        {
            return Validate(await _service.GetAsync(cancellationToken), build);
        }

        /// <summary> 아이디, 비밀번호 로그인용. build가 없으면(홈페이지) 점검만 보고, 있으면 ValidateAsync와 같다.
        /// 빌드 없이 받은 토큰은 게임 세션 발급과 게임 서버 입장의 빌드 검사를 따로 통과해야 게임에 들어간다 </summary>
        public async ValueTask<int> ValidateOptionalBuildAsync(ClientBuildInfo? build,
            CancellationToken cancellationToken = default)
        {
            ServiceStateSnapshot service = await _service.GetAsync(cancellationToken);
            if (build == null)
            {
                return service.Maintenance ? ContractCodes.RejectReason.MAINTENANCE : ContractCodes.RejectReason.NONE;
            }

            return Validate(service, build);
        }

        /// <summary> 빌드와 무관한 경로(가입)의 점검 검사. 점검이면 MAINTENANCE, 아니면 NONE이다 </summary>
        public async ValueTask<int> CheckMaintenanceAsync(CancellationToken cancellationToken = default)
        {
            ServiceStateSnapshot service = await _service.GetAsync(cancellationToken);
            return service.Maintenance ? ContractCodes.RejectReason.MAINTENANCE : ContractCodes.RejectReason.NONE;
        }

        private int Validate(ServiceStateSnapshot service, ClientBuildInfo? build)
        {
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
            if (!service.IsBuildAllowed(build.BuildId))
            {
                return ContractCodes.RejectReason.BUILD_NOT_ALLOWED;
            }

            return ContractCodes.RejectReason.NONE;
        }
    }
}
