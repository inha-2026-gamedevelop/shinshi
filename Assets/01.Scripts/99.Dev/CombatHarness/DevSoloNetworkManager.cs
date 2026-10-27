// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Dev
{
    /// <summary> 솔로 호스트 시험에서 인증 없이 호스트 연결에 개발 참가자를 결합하고 플레이어를 만드는 네트워크 매니저 </summary>
    public sealed class DevSoloNetworkManager : NetworkManager
    {
        // 실제 서버는 인증기가 연결에 참가자 결합(SessionBinding)을 붙인 뒤에만 플레이어를 만든다. 이 매니저는 인증기와 API 없이
        // 호스트 연결에 개발 결합을 직접 붙인다. 전투 서버 런타임은 이 결합으로 직업과 이름을 읽어 판정을 붙인다.
        // 이 컴포넌트는 에디터 전용 어셈블리에 있어 빌드에 들어가지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private const string PARTICIPANT_KEY   = "dev-solo";
        private const string ACCOUNT_LEASE_ID  = "dev-solo-lease";
        private const int    PARTICIPANT_ID    = 1;

        /****************************************
        *              Properties
        ****************************************/

        public EPlayerClass PlayerClass { get; set; } = EPlayerClass.Archer;

        public string CharacterName { get; set; } = "솔로";

        /****************************************
        *            Server Callbacks
        ****************************************/

        public override void OnServerAddPlayer(NetworkConnectionToClient connection)
        {
            // 외형이 비어 있으면 캐릭터 모델이 조립되지 않아 캡슐만 보이므로 직업 기본 외형을 준다
            CharacterAppearance appearance = CharacterAppearanceCatalog.CreateDefault((int)PlayerClass);
            connection.authenticationData = new SessionBinding(PARTICIPANT_KEY, PARTICIPANT_ID, ACCOUNT_LEASE_ID, PlayerClass,
                CharacterName, appearance);
            connection.isAuthenticated    = true;
            base.OnServerAddPlayer(connection);
        }
    }
}
