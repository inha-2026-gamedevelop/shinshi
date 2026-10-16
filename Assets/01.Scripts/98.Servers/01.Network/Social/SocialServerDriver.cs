// Unity
using UnityEngine;

namespace Shinshi.Network
{
    /// <summary> 전달함 꺼내기 주기를 매 프레임 돌리는 부품, 소셜 설치가 실행 중에 매니저 개체에 붙인다 </summary>
    public sealed class SocialServerDriver : MonoBehaviour
    {
        // 서버 어셈블리 타입이라 프리팹, 씬에 붙이지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private SocialServerHost _host;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            _host?.Tick();
        }

        /****************************************
        *                Methods
        ****************************************/

        public void Bind(SocialServerHost host)
        {
            _host = host;
        }
    }
}
