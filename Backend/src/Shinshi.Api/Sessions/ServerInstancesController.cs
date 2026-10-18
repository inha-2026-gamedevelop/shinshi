// System
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

// Project
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Sessions
{
    /// <summary> 마을, 던전 프로세스의 풀 등록, 5초 heartbeat, 드레인. 자격에 묶인 인스턴스만 자기 행을 바꾼다 </summary>
    [ApiController]
    [RequireServerLease]
    public sealed class ServerInstancesController : ControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        // 서버가 보고할 수 있는 접속 수, 정원의 상한. Mirror maxConnections보다 훨씬 크게 잡은 오설정 방지선이다
        private const int MAX_REPORTED_CAPACITY = 100000;

        private readonly IServerCallerAuthenticator _callers;
        private readonly PostgresServerPoolRepository _pool;
        private readonly PostgresAccountLeaseRepository _leases;
        private readonly TimeProvider _timeProvider;

        /****************************************
        *                Methods
        ****************************************/

        public ServerInstancesController(IServerCallerAuthenticator callers, PostgresServerPoolRepository pool,
            PostgresAccountLeaseRepository leases, TimeProvider timeProvider)
        {
            _callers      = callers;
            _pool         = pool;
            _leases       = leases;
            _timeProvider = timeProvider;
        }

        [HttpPost(GameServerRouteTemplates.SERVER_REGISTER)]
        public async Task<IActionResult> Register([FromBody] ServerRegisterRequest request, CancellationToken cancellationToken)
        {
            ServerCaller? caller = await _callers.AuthenticateAsync(HttpContext, cancellationToken);
            if (caller == null)
            {
                return StatusCode(StatusCodes.Status401Unauthorized);
            }
            if (!Guid.TryParse(request.InstanceId, out Guid instanceId) || !Guid.TryParse(request.BootId, out Guid bootId))
            {
                return BadRequest();
            }
            if ((caller.InstanceId != instanceId) || (caller.Role != request.ServerRole))
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }
            if (!IsLeaseBoot(bootId))
            {
                return RejectBootMismatch();
            }

            ServerRegistration registration = new ServerRegistration(instanceId, bootId, request.ServerRole,
                request.Address, request.Port, request.BuildId, request.ContentVersion);
            if (!registration.IsValid())
            {
                return BadRequest();
            }

            return ToResponse(instanceId, await _pool.RegisterAsync(registration, cancellationToken));
        }

        [HttpPost(GameServerRouteTemplates.SERVER_HEARTBEAT)]
        public async Task<IActionResult> Heartbeat(string instanceId, [FromBody] ServerHeartbeatRequest request,
            CancellationToken cancellationToken)
        {
            (Guid id, Guid bootId, IActionResult? rejection) = await AuthorizeOwnerAsync(instanceId, request, cancellationToken);
            if (rejection != null)
            {
                return rejection;
            }

            // 보고값은 선택이다. 범위를 벗어난 값은 오설정이므로 거절해 정원이 조용히 틀어지지 않게 한다
            if ((request.Connections < 0) || (request.Capacity <= 0) || (request.Capacity > MAX_REPORTED_CAPACITY)
                || (request.Connections > MAX_REPORTED_CAPACITY))
            {
                return BadRequest();
            }

            return ToResponse(id, await _pool.HeartbeatAsync(id, bootId, request.Connections, request.Capacity,
                cancellationToken));
        }

        [HttpPost(GameServerRouteTemplates.SERVER_DRAIN)]
        public async Task<IActionResult> Drain(string instanceId, [FromBody] ServerHeartbeatRequest request,
            CancellationToken cancellationToken)
        {
            (Guid id, Guid bootId, IActionResult? rejection) = await AuthorizeOwnerAsync(instanceId, request, cancellationToken);
            if (rejection != null)
            {
                return rejection;
            }

            return ToResponse(id, await _pool.DrainAsync(id, bootId, cancellationToken));
        }

        /// <summary> 접속을 끊은 계정의 접속 임대를 푼다. 본문 없음, 멱등. 이미 풀렸거나 이관된 임대도 204이며 다른 인스턴스의 임대는 403이다 </summary>
        [HttpPost(GameServerRouteTemplates.ACCOUNT_LEASE_RELEASE)]
        public async Task<IActionResult> ReleaseAccountLease(string instanceId, string leaseId,
            CancellationToken cancellationToken)
        {
            ServerCaller? caller = await _callers.AuthenticateAsync(HttpContext, cancellationToken);
            if (caller == null)
            {
                return StatusCode(StatusCodes.Status401Unauthorized);
            }
            if (!Guid.TryParse(instanceId, out Guid id) || !Guid.TryParse(leaseId, out Guid lease)
                || (id == Guid.Empty) || (lease == Guid.Empty))
            {
                return BadRequest();
            }
            if (caller.InstanceId != id)
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }

            EAccountLeaseReleaseStatus status = await _leases.ReleaseOwnedAsync(id, lease, cancellationToken);
            if (status == EAccountLeaseReleaseStatus.OtherOwner)
            {
                return ApiResponseFactory.Reject(StatusCodes.Status403Forbidden, ContractCodes.RejectReason.SESSION_MISMATCH,
                    string.Empty, _timeProvider);
            }

            return NoContent();
        }

        private async Task<(Guid InstanceId, Guid BootId, IActionResult? Rejection)> AuthorizeOwnerAsync(string instanceId,
            ServerHeartbeatRequest request, CancellationToken cancellationToken)
        {
            ServerCaller? caller = await _callers.AuthenticateAsync(HttpContext, cancellationToken);
            if (caller == null)
            {
                return (Guid.Empty, Guid.Empty, StatusCode(StatusCodes.Status401Unauthorized));
            }
            if (!Guid.TryParse(instanceId, out Guid id) || !Guid.TryParse(request.BootId, out Guid bootId)
                || (id == Guid.Empty) || (bootId == Guid.Empty))
            {
                return (Guid.Empty, Guid.Empty, BadRequest());
            }
            if (caller.InstanceId != id)
            {
                return (Guid.Empty, Guid.Empty, StatusCode(StatusCodes.Status403Forbidden));
            }
            if (!IsLeaseBoot(bootId))
            {
                return (Guid.Empty, Guid.Empty, RejectBootMismatch());
            }

            return (id, bootId, null);
        }

        // 임대가 묶인 boot와 요청 boot가 다르다. 이 임대로는 그 boot를 영영 살릴 수 없으므로 프로세스를 내리는 409로 응답한다
        private ObjectResult RejectBootMismatch()
        {
            return ApiResponseFactory.Reject(StatusCodes.Status409Conflict, ContractCodes.RejectReason.SESSION_MISMATCH,
                string.Empty, _timeProvider);
        }

        // 임대는 발급받은 프로세스의 bootId에만 쓴다. 다른 boot의 등록, heartbeat로 그 프로세스를 되살리거나 덮지 못한다.
        // boot 결합 전(0005 이전) 임대는 값이 없어 검사하지 않는다
        private bool IsLeaseBoot(Guid bootId)
        {
            Guid? leaseBoot = AuthActors.GetServer(HttpContext)?.BootId;
            return !leaseBoot.HasValue || (leaseBoot.Value == bootId);
        }

        private ObjectResult ToResponse(Guid instanceId, ServerPoolOutcome outcome)
        {
            // OtherBootAlive: 다른 boot가 아직 살아 있다. HeartbeatExpired: 갱신과 재조회 사이 상태가 바뀐 경합이다.
            // 둘 다 회복 가능한 일시 상태라 프로세스를 내리는 409 대신 503으로 다시 보내게 한다. 회복 불가능한 boot는
            // Offline(SESSION_TERMINAL)과 BootMismatch(SESSION_MISMATCH)의 409뿐이다
            if ((outcome.Status == EServerPoolStatus.OtherBootAlive) || (outcome.Status == EServerPoolStatus.HeartbeatExpired))
            {
                // 같은 인스턴스의 다른 boot가 아직 살아 있다. 저장한 것이 없으므로 잠시 뒤 같은 등록을 다시 보낸다
                Response.Headers.RetryAfter = ServerPoolTimings.LIVENESS_RETRY_AFTER_SECONDS.ToString(CultureInfo.InvariantCulture);
                return ApiResponseFactory.Reject(StatusCodes.Status503ServiceUnavailable,
                    ContractCodes.RejectReason.BACKEND_UNAVAILABLE, string.Empty, _timeProvider);
            }

            int reason = outcome.Status switch
            {
                EServerPoolStatus.Accepted         => ContractCodes.RejectReason.NONE,
                EServerPoolStatus.Offline          => ContractCodes.RejectReason.SESSION_TERMINAL,
                _                                  => ContractCodes.RejectReason.SESSION_MISMATCH
            };
            ServerInstanceResponse body = new ServerInstanceResponse(instanceId.ToString("D"), ToStateCode(outcome.State),
                reason, outcome.ServerTime.ToUnixTimeMilliseconds());
            return new ObjectResult(body)
            {
                StatusCode = reason == ContractCodes.RejectReason.NONE ? StatusCodes.Status200OK : StatusCodes.Status409Conflict
            };
        }

        private static int ToStateCode(string state)
        {
            return state switch
            {
                GameSessionStates.INSTANCE_IDLE     => GameSessionCodes.InstanceState.IDLE,
                GameSessionStates.INSTANCE_RESERVED => GameSessionCodes.InstanceState.RESERVED,
                GameSessionStates.INSTANCE_ACTIVE   => GameSessionCodes.InstanceState.ACTIVE,
                GameSessionStates.INSTANCE_DRAINING => GameSessionCodes.InstanceState.DRAINING,
                _                                   => GameSessionCodes.InstanceState.OFFLINE
            };
        }
    }
}
