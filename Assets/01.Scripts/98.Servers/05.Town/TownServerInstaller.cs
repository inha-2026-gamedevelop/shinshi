// System
using System;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Network
{
    /// <summary> 마을 역할에서 파티, 성장 중계, 생활 서버를 만들고 메시지 처리기와 연결 종료 정리를 건다 </summary>
    public static class TownServerInstaller
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 서버를 연 뒤 한 번 부른다. allowUnverifiedProgress는 개발 빌드에서만 참으로 준다. townInstanceId는 거래 prepare에
        /// 싣는 이 마을의 인스턴스 ID이고 없으면 비운다 </summary>
        public static TownServerHost Install(ShinshiNetworkManager manager, BackendGateway gateway, string contentVersion,
            bool allowUnverifiedProgress, Action<string> warn, string townInstanceId = null)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            // 프로세스 수명 동안 하나만 쓴다. 매니저가 사라지면 프로세스도 내려가므로 따로 해제하지 않는다
            var rollSource = new GrowthRollSource();
            var audience   = new MirrorTownAudience(Constants.Session.MAX_SESSION_PARTICIPANTS);
            var runtime    = new TownServerRuntime(gateway, audience, new RegistryGatherNodeLayout(), GameDB.Session, GameDB.Life,
                GameDB.Progression, GameDB.Growth.TrainingTotalCap, contentVersion, allowUnverifiedProgress, rollSource.Next, null, warn, townInstanceId,
                GameDB.Rewards.TradeCurrencyItemId);
            var host       = new TownServerHost(runtime, audience);
            host.RegisterHandlers();

            // 서버 수명 시작에 한 번만 건다. 매니저 이벤트라 Mirror 정적 이벤트처럼 덮어써지지 않는다
            manager.ServerDisconnected += host.HandleServerDisconnected;

            if (!manager.TryGetComponent(out TownServerDriver driver))
            {
                driver = manager.gameObject.AddComponent<TownServerDriver>();
            }

            driver.Bind(runtime);
            InstallInterest(manager, runtime);
            return host;
        }

        // 서버를 연 뒤 붙여도 된다. 마을에는 씬에서 스폰되는 네트워크 객체가 없고 접속은 이 호출 뒤에 처리된다.
        // 컴포넌트가 켜질 때 NetworkServer.aoi에 등록되고, 서버를 다시 열어도 같은 컴포넌트를 쓴다
        private static void InstallInterest(ShinshiNetworkManager manager, TownServerRuntime runtime)
        {
            if (!manager.TryGetComponent(out TownInterestManagement interest))
            {
                interest = manager.gameObject.AddComponent<TownInterestManagement>();
            }

            interest.Bind(new TownChatPartyLookup(runtime.Party));
        }
    }
}
