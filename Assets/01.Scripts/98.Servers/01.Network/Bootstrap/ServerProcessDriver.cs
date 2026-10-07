// Unity
using UnityEngine;

namespace Shinshi.Network
{
    /// <summary> 서버 존재 신호와 종료 경로를 매 프레임 돌리는 부품, 서버 부트스트랩이 실행 중에 매니저 개체에 붙인다 </summary>
    public sealed class ServerProcessDriver : MonoBehaviour
    {
        // 서버 어셈블리 타입이라 프리팹, 씬에 붙이지 않는다. 전투 틱과 따로 돌아야 출정이 없을 때도 heartbeat가 나간다

        /****************************************
        *                Fields
        ****************************************/

        private ServerPresence           _presence;
        private ServerShutdownController _shutdown;
        private ServerMetricsSampler     _metrics;
        private ServerMetricsExporter    _exporter;
        private ServerTrafficCounter     _traffic;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            _presence?.Tick();
            _shutdown?.Tick();
            double now = BackendGateway.MonotonicSeconds();
            _metrics?.Tick(now);
            _exporter?.Tick(now);
        }

        // 수신 스레드와 트랜스포트 구독을 프로세스 종료나 매니저 파괴 때 함께 닫는다
        private void OnDestroy()
        {
            _exporter?.Dispose();
            _traffic?.Dispose();
            _exporter = null;
            _traffic  = null;
        }

        /****************************************
        *                Methods
        ****************************************/

        public void Bind(ServerPresence presence, ServerShutdownController shutdown)
        {
            _presence = presence;
            _shutdown = shutdown;
        }

        /// <summary> 구조화 로그를 설치한 서버 빌드에서만 붙인다 </summary>
        public void BindMetrics(ServerMetricsSampler metrics)
        {
            _metrics = metrics;
        }

        /// <summary> 지표 포트를 설정한 서버에서만 붙인다. 이전에 붙인 내보내기는 닫는다 </summary>
        public void BindExporter(ServerMetricsExporter exporter, ServerTrafficCounter traffic)
        {
            if (_exporter != exporter)
            {
                _exporter?.Dispose();
            }

            if (_traffic != traffic)
            {
                _traffic?.Dispose();
            }

            _exporter = exporter;
            _traffic  = traffic;
        }
    }
}
