// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// Project
using Shinshi.Data.Admin;

namespace Shinshi.Backend.Admin
{
    /// <summary> 점검, 기능 차단, 허용 빌드, 공지의 프로세스 캐시(싱글턴). 저장소가 없으면 ServiceState 설정을 그대로 쓰고,
    /// 있으면 DB를 RefreshSeconds마다 다시 읽는다. 읽기 실패는 마지막 성공 값을 유지하고, 한 번도 읽지 못했으면 점검 중으로 막는다 </summary>
    public sealed class ServiceStateStore
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IOptionsMonitor<ServiceStateOptions> _options;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<ServiceStateStore> _logger;
        private readonly PostgresServiceStateRepository? _repository;
        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);

        private ServiceStateSnapshot? _databaseSnapshot;
        private long _lastAttemptUtcTicks = DateTimeOffset.MinValue.UtcTicks;

        // 설정 모드에서 같은 설정 인스턴스로 만든 스냅샷을 재사용한다. 설정이 다시 로드되면 인스턴스가 바뀐다.
        // 설정과 스냅샷을 한 참조로 바꿔야 다른 스레드가 짝이 어긋난 값을 읽지 않는다
        private Tuple<ServiceStateOptions, ServiceStateSnapshot>? _optionsSnapshot;

        /****************************************
        *              Properties
        ****************************************/

        public bool UsesDatabase => _repository != null;

        /// <summary> 동기 경로용 현재 값. 기다리지 않는다. 주기가 지났으면 백그라운드로 다시 읽고 지금은 이전 값을 주며,
        /// DB 모드에서 아직 한 번도 읽지 못했으면 점검 중(Unavailable)으로 답한다. 요청 경로는 GetAsync를 쓴다 </summary>
        public ServiceStateSnapshot Current
        {
            get
            {
                if (_repository == null)
                {
                    return FromOptions();
                }

                // 첫 읽기 전에도 스레드를 막지 않는다. 동기 대기는 스레드 풀을 묶어 DB 장애 때 요청 전체를 멈춘다
                TriggerBackgroundRefreshIfStale();
                return Volatile.Read(ref _databaseSnapshot) ?? Unavailable();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public ServiceStateStore(IOptionsMonitor<ServiceStateOptions> options, TimeProvider timeProvider,
            ILogger<ServiceStateStore> logger, PostgresServiceStateRepository? repository)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(timeProvider);
            ArgumentNullException.ThrowIfNull(logger);
            _options      = options;
            _timeProvider = timeProvider;
            _logger       = logger;
            _repository   = repository;
            int refreshSeconds = options.CurrentValue.RefreshSeconds;
            if ((refreshSeconds < ServiceStateOptions.MIN_REFRESH_SECONDS)
                || (refreshSeconds > ServiceStateOptions.MAX_REFRESH_SECONDS))
            {
                throw new ArgumentOutOfRangeException(nameof(options), "ServiceState:RefreshSeconds must be 1..60.");
            }
        }

        /// <summary> DB 없이 설정만 읽는 저장소. 테스트와 연결 문자열 없는 실행에서 쓴다 </summary>
        public static ServiceStateStore FromOptions(IOptionsMonitor<ServiceStateOptions> options,
            TimeProvider? timeProvider = null)
        {
            return new ServiceStateStore(options, timeProvider ?? TimeProvider.System,
                NullLogger<ServiceStateStore>.Instance, null);
        }

        /// <summary> 비동기 경로의 현재 값. 주기가 지났으면 이전 값을 주고 백그라운드로 다시 읽는다.
        /// 아직 한 번도 읽지 못했으면 첫 읽기를 비동기로 기다린다 </summary>
        public async ValueTask<ServiceStateSnapshot> GetAsync(CancellationToken cancellationToken = default)
        {
            if (_repository == null)
            {
                return FromOptions();
            }

            ServiceStateSnapshot? snapshot = Volatile.Read(ref _databaseSnapshot);
            if (snapshot != null)
            {
                TriggerBackgroundRefreshIfStale();
                return snapshot;
            }

            // 첫 읽기 전이면 진행 중인 읽기(시작 예열, 다른 요청)가 끝나기를 기다린다. 락을 얻은 뒤
            // 주기 안의 시도가 이미 있었으면 다시 읽지 않으므로 DB 장애 중에도 주기마다 한 번만 읽는다
            await RefreshCoreAsync(true, false, cancellationToken);
            return Volatile.Read(ref _databaseSnapshot) ?? Unavailable();
        }

        /// <summary> 이 인스턴스가 상태나 공지를 쓴 직후 부른다. 진행 중인 읽기가 끝나기를 기다렸다가 새로 읽는다 </summary>
        public Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            return (_repository == null) ? Task.CompletedTask : RefreshCoreAsync(true, true, cancellationToken);
        }

        /// <summary> 프로세스 시작 직후 한 번 부른다. 요청이 먼저 읽었으면 주기 안에서는 다시 읽지 않는다 </summary>
        public Task WarmUpAsync(CancellationToken cancellationToken = default)
        {
            return (_repository == null) ? Task.CompletedTask : RefreshCoreAsync(true, false, cancellationToken);
        }

        // force가 아니면 락을 얻은 뒤 신선도를 다시 본다. 첫 읽기를 기다리던 요청들이 앞선 읽기가 끝난 뒤
        // 하나씩 DB를 다시 읽지 않게 한다(장애 중이면 앞선 실패가 주기 동안 유효하다)
        private async Task RefreshCoreAsync(bool waitForLock, bool force, CancellationToken cancellationToken)
        {
            if (waitForLock)
            {
                await _refreshLock.WaitAsync(cancellationToken);
            }
            else if (!await _refreshLock.WaitAsync(0, CancellationToken.None))
            {
                return;
            }

            try
            {
                if ((!force) && (!IsStale()))
                {
                    return;
                }

                Interlocked.Exchange(ref _lastAttemptUtcTicks, _timeProvider.GetUtcNow().UtcTicks);
                ServiceStateReadResult result;
                try
                {
                    result = await _repository!.ReadCurrentAsync(cancellationToken);
                }
                catch (Exception exception) when (!(exception is OperationCanceledException)
                    || !cancellationToken.IsCancellationRequested)
                {
                    // 예외 메시지에 연결 정보가 섞일 수 있어 형식 이름만 남긴다. 마지막 성공 값은 그대로 둔다
                    _logger.LogWarning("Service state refresh failed ({ExceptionType}); keeping the last snapshot.",
                        exception.GetType().Name);
                    return;
                }

                Volatile.Write(ref _databaseSnapshot, new ServiceStateSnapshot(result.State.Maintenance,
                    result.State.DisabledFeatures, result.State.AllowedBuildIds, result.State.Revision,
                    EServiceStateSource.Database, string.Empty, result.Notices));
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        private void TriggerBackgroundRefreshIfStale()
        {
            if (!IsStale())
            {
                return;
            }

            // 요청 취소와 무관하게 끝까지 읽는다. 예외는 RefreshCoreAsync가 기록하고 삼키지 않은 취소만 남는다
            _ = Task.Run(async () =>
            {
                try
                {
                    await RefreshCoreAsync(false, false, CancellationToken.None);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning("Background service state refresh stopped ({ExceptionType}).",
                        exception.GetType().Name);
                }
            });
        }

        private bool IsStale()
        {
            long last = Interlocked.Read(ref _lastAttemptUtcTicks);
            TimeSpan interval = TimeSpan.FromSeconds(_options.CurrentValue.RefreshSeconds);
            return (_timeProvider.GetUtcNow().UtcTicks - last) >= interval.Ticks;
        }

        private ServiceStateSnapshot FromOptions()
        {
            ServiceStateOptions options = _options.CurrentValue;
            Tuple<ServiceStateOptions, ServiceStateSnapshot>? cached = Volatile.Read(ref _optionsSnapshot);
            if ((cached != null) && ReferenceEquals(cached.Item1, options))
            {
                return cached.Item2;
            }

            ServiceStateSnapshot snapshot = new ServiceStateSnapshot(options.Maintenance, options.DisabledFeatures,
                options.AllowedBuildIds, 0, EServiceStateSource.Configuration, options.Notice, null);
            Volatile.Write(ref _optionsSnapshot, Tuple.Create(options, snapshot));
            return snapshot;
        }

        // 한 번도 읽지 못한 DB 모드의 안전 값. 신규 로그인과 출정을 막고, 진행 중 기능은 막지 않는다
        private ServiceStateSnapshot Unavailable()
        {
            return new ServiceStateSnapshot(true, null, null, 0, EServiceStateSource.Unavailable,
                _options.CurrentValue.Notice, null);
        }
    }
}
