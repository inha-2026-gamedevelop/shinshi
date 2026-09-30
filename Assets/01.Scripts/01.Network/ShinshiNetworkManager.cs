// 서드파티
using Mirror;

namespace Shinshi.Network
{
    /// <summary> 프로젝트 네트워크 매니저, 서버 시작은 서버 부트스트랩만 한다 </summary>
    public class ShinshiNetworkManager : NetworkManager
    {
        /****************************************
        *              Unity Event
        ****************************************/

        // 헤드리스 자동 시작을 켜 두면 역할과 포트를 읽기 전에 서버가 떠 버린다
        public override void OnValidate()
        {
            base.OnValidate();
            headlessStartMode = HeadlessStartOptions.DoNothing;
        }
    }
}
