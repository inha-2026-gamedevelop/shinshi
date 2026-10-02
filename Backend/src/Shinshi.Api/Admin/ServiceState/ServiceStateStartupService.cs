// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Shinshi.Backend.Admin
{
    /// <summary> DB 모드 서비스 상태의 시작 작업. 첫 로그인, 출정 요청이 DB 첫 읽기를 기다리지 않게 백그라운드로 한 번 읽어 두고,
    /// 입장 토큰 교환이 아직 설정의 점검 값을 읽는다는 것을 놓치지 않게 경고한다. 설정 모드에서는 아무것도 하지 않는다 </summary>
    public sealed class ServiceStateStartupService : BackgroundService
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string MAINTENANCE_KEY = ServiceStateOptions.SECTION_NAME + ":"
            + nameof(ServiceStateOptions.Maintenance);

        private readonly ServiceStateStore _store;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ServiceStateStartupService> _logger;

        /****************************************
        *                Methods
        ****************************************/

        public ServiceStateStartupService(ServiceStateStore store, IConfiguration configuration,
            ILogger<ServiceStateStartupService> logger)
        {
            ArgumentNullException.ThrowIfNull(store);
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(logger);
            _store         = store;
            _configuration = configuration;
            _logger        = logger;
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            // 진행 중 출정의 재접속을 DB 점검으로 막지 않으려고 입장 토큰 교환은 설정 값을 그대로 읽는다(의도한 동작).
            // 그 설정의 기본값이 true라 DB 모드에서 빠뜨리면 모든 입장 교환이 MAINTENANCE가 되므로 시작 때 알린다
            if (_store.UsesDatabase && (_configuration[MAINTENANCE_KEY] == null))
            {
                _logger.LogWarning(
                    "{Key} is not set while service state is read from the database. Join-token exchange still reads this "
                    + "setting (default true) and rejects every join with MAINTENANCE; set it explicitly.",
                    MAINTENANCE_KEY);
            }

            return base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_store.UsesDatabase)
            {
                return;
            }

            // 호스트 시작을 DB 연결 시간만큼 붙잡지 않는다. 실패는 저장소가 기록하고 첫 요청이 주기에 맞춰 다시 읽는다
            await Task.Yield();
            await _store.WarmUpAsync(stoppingToken);
        }
    }
}
