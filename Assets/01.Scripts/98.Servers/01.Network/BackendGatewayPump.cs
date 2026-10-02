// Unity
using UnityEngine;

namespace Shinshi.Network
{
    /// <summary> 백엔드 응답을 메인 스레드로 넘기는 부품, 서버 부트스트랩이 실행 중에 매니저 개체에 붙인다 </summary>
    public sealed class BackendGatewayPump : MonoBehaviour
    {
        // 서버 어셈블리 타입이라 프리팹, 씬에 붙이지 않는다. 매니저 개체가 사라지면 게이트웨이도 함께 닫는다

        /****************************************
        *                Fields
        ****************************************/

        private BackendGateway _gateway;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            _gateway?.Pump();
        }

        private void OnDestroy()
        {
            _gateway?.Dispose();
            _gateway = null;
        }

        /****************************************
        *                Methods
        ****************************************/

        public void Bind(BackendGateway gateway)
        {
            _gateway = gateway;
        }
    }
}
