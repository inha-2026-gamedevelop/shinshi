// System
using System;

// Third-party
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

// Project
using Shinshi.Contracts;

namespace Shinshi.Backend
{
    [ApiController]
    public sealed class ServiceStateController : ControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IOptionsMonitor<ServiceStateOptions> _options;
        private readonly TimeProvider _timeProvider;

        /****************************************
        *                Methods
        ****************************************/

        public ServiceStateController(IOptionsMonitor<ServiceStateOptions> options, TimeProvider timeProvider)
        {
            _options      = options;
            _timeProvider = timeProvider;
        }

        [AllowAnonymous]
        [HttpGet(ApiRoutes.SERVICE_STATE)]
        public ActionResult<ServiceStateResponse> Get()
        {
            ServiceStateOptions state = _options.CurrentValue;
            return new ServiceStateResponse(state.Maintenance, state.Notice, state.DisabledFeatures,
                state.AllowedBuildIds, _timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
        }
    }
}
