// System
using System;
using System.Globalization;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    // 다른 서버 프로세스가 이 서버의 접속 캐릭터 앞으로 넣은 행(귓속말, 길드 채팅, 소셜 알림)을 주기마다 꺼내 대상 연결로 보낸다.
    // 요청은 동시에 1건만 둔다. 같은 인스턴스의 꺼내기가 겹치지 않아야 API가 같은 행을 두 번 고르지 않는다. 확인 목록에는 클라이언트가
    // 수신 확인을 보낸 행만 싣고, 요청이 실패하면 같은 확인을 다음 요청에 다시 싣는다(이미 지운 행을 다시 지워도 효과가 없다).
    // 409 SESSION_MISMATCH는 서버 임대가 이 프로세스의 bootId에 묶여 있지 않다는 뜻이라 같은 프로세스로는 회복할 수 없다.
    // 그때는 꺼내기를 멈추고 존재 신호의 종료 경로에 맡긴다. 회로가 열렸거나 Retry-After가 오면 그만큼 쉰다
    /// <summary> 마을, 던전 공통 전달함 꺼내기 주기 실행기, 메인 스레드에서 Tick을 부른다 </summary>
    public sealed class SocialDeliveryPump : IBackendResponseListener
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly BackendGateway       _gateway;
        private readonly ISocialAudience      _audience;
        private readonly SocialDeliveryRouter _router;
        private readonly Func<string>         _bootId;
        private readonly Func<double>         _clock;
        private readonly Action<string>       _warn;
        private readonly string               _path;

        private int                 _requestId = 0;
        private SocialDeliveryAck[] _sentAcks  = null;
        private double              _nextPull  = 0.0;
        private bool                _isHalted  = false;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsInFlight => _requestId != 0;

        // bootId 불일치로 멈췄다
        public bool IsHalted   => _isHalted;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> bootId는 존재 신호의 이 프로세스 bootId를 돌려준다(아직 없으면 null) </summary>
        public SocialDeliveryPump(BackendGateway gateway, ISocialAudience audience, SocialDeliveryRouter router, string instanceId,
            Func<string> bootId, Func<double> clock, Action<string> warn)
        {
            if ((gateway == null) || (audience == null) || (router == null) || (bootId == null))
            {
                throw new ArgumentNullException(nameof(gateway), "전달함 꺼내기에 필요한 참조가 없다");
            }

            if (string.IsNullOrEmpty(instanceId))
            {
                throw new ArgumentException("인스턴스 ID가 필요하다", nameof(instanceId));
            }

            _gateway  = gateway;
            _audience = audience;
            _router   = router;
            _bootId   = bootId;
            _clock    = clock ?? BackendGateway.MonotonicSeconds;
            _warn     = warn;
            _path     = string.Format(CultureInfo.InvariantCulture, SocialRoutes.DELIVERY_PULL_FORMAT, instanceId);
        }

        /// <summary> 주기가 되었고 꺼낼 대상(접속 캐릭터 또는 보낼 확인)이 있으면 한 번 보낸다, 보냈으면 참 </summary>
        public bool Tick()
        {
            double now = _clock();
            if ((_isHalted) || (_requestId != 0) || (now < _nextPull))
            {
                return false;
            }

            _nextPull = now + Constants.Social.DELIVERY_PULL_INTERVAL_SECONDS;
            if ((_router.Ledger.OutgoingAckCount == 0) && (!_audience.HasConnectedCharacters()))
            {
                return false;
            }

            string bootId = _bootId();
            if (string.IsNullOrEmpty(bootId))
            {
                return false;
            }

            _sentAcks = _router.Ledger.TakeAcks(SocialLimits.MAX_ACKS_PER_PULL);
            BackendRequest request = BackendRequest.Post(_path, new SocialDeliveryPullRequest(bootId, SocialLimits.PULL_WAIT_MS, _sentAcks),
                typeof(SocialDeliveryPullResponse), null, Constants.Network.API_TIMEOUT_SECONDS);
            _requestId = _gateway.Send(request, this);
            return true;
        }

        // 게이트웨이가 메인 스레드의 Pump에서 부른다
        public void OnBackendResponse(in BackendResponse response)
        {
            if ((_requestId == 0) || (response.RequestId != _requestId))
            {
                return;
            }

            _requestId = 0;
            SocialDeliveryAck[] acks = _sentAcks;
            _sentAcks = null;
            if ((response.IsSucceeded) && (response.Payload is SocialDeliveryPullResponse pulled))
            {
                _router.RouteAll(pulled.Deliveries);
                return;
            }

            _router.Ledger.RestoreAcks(acks);
            if (response.RetryAfterSeconds > 0.0)
            {
                _nextPull = Math.Max(_nextPull, _clock() + response.RetryAfterSeconds);
            }

            if ((response.Outcome == EBackendOutcome.Rejected) && (response.ReasonCode == ContractCodes.RejectReason.SESSION_MISMATCH))
            {
                _isHalted = true;
                _warn?.Invoke("[Social] 전달함 꺼내기가 bootId 불일치로 거절되어 멈춘다");
            }
        }
    }
}
