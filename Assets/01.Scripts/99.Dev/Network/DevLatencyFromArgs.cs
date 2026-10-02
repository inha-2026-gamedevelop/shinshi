// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Dev
{
    /// <summary> 개발 씬 전송 계층의 지연을 실행 인자로 정한다, 인자가 없으면 씬에 둔 값(0)을 쓴다 </summary>
    public sealed class DevLatencyFromArgs : MonoBehaviour
    {
        // LatencySimulation은 보내는 쪽에서만 지연을 넣는다. 서버와 클라이언트에 같은 값을 주면 왕복은 그 두 배다

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private LatencySimulation _latency;

        /****************************************
        *              Unity Event
        ****************************************/

        // 접속은 네트워크 매니저의 Start에서 시작하므로 그보다 먼저 정한다
        private void Awake()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_latency == null)
            {
                return;
            }

            if (!DevArgs.TryReadFloat(Constants.Network.ARG_DEV_LATENCY_MS, out float milliseconds))
            {
                return;
            }

            _latency.latency = Mathf.Max(0f, milliseconds);
            Debug.Log($"[DevLatency] 보내는 메시지 지연 {_latency.latency}ms");
#endif
        }
    }
}
