// Unity
using UnityEngine;

namespace Shinshi.Network
{
    /// <summary> 마을 서버 틱(카운트다운, 대기열, 채집 시간)을 매 프레임 돌리는 부품, 마을 설치가 실행 중에 매니저 개체에 붙인다 </summary>
    public sealed class TownServerDriver : MonoBehaviour
    {
        // 서버 어셈블리 타입이라 프리팹, 씬에 붙이지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private TownServerRuntime _runtime;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            _runtime?.Tick();
        }

        /****************************************
        *                Methods
        ****************************************/

        public void Bind(TownServerRuntime runtime)
        {
            _runtime = runtime;
        }
    }
}
