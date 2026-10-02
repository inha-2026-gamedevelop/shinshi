// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

// Project
using Shinshi.Backend.Admin;
using Shinshi.Contracts;

namespace Shinshi.Backend
{
    /// <summary> 인증 없이 읽는 공개 서비스 상태. 연결 문자열이 있으면 DB 캐시를, 없으면 ServiceState 설정을 그대로 준다 </summary>
    [ApiController]
    public sealed class ServiceStateController : ControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly ServiceStateStore _store;
        private readonly TimeProvider _timeProvider;

        /****************************************
        *                Methods
        ****************************************/

        // 생성자를 하나만 두고 저장소를 선택 매개변수로 받는다. 같은 길이의 생성자가 둘이면 활성화가 모호해지고,
        // AddShinshiAdmin 없이 인증 모듈만 올린 호스트는 저장소가 없어 설정만 읽어야 한다
        public ServiceStateController(IOptionsMonitor<ServiceStateOptions> options, TimeProvider timeProvider,
            ServiceStateStore? store = null)
        {
            _store        = store ?? ServiceStateStore.FromOptions(options, timeProvider);
            _timeProvider = timeProvider;
        }

        // 캐시 스냅샷을 읽으므로 DB 모드에서도 프로세스의 첫 읽기 말고는 요청이 DB를 기다리지 않는다
        [AllowAnonymous]
        [HttpGet(ApiRoutes.SERVICE_STATE)]
        public async Task<ActionResult<ServiceStateResponse>> Get(CancellationToken cancellationToken)
        {
            ServiceStateSnapshot snapshot = await _store.GetAsync(cancellationToken);
            return snapshot.ToResponse(_timeProvider.GetUtcNow());
        }
    }
}
