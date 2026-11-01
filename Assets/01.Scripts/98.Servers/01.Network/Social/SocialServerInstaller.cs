// System
using System;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 마을, 던전 역할 모두에서 소셜 중계와 전달함 꺼내기를 만들고 메시지 처리기와 연결 종료 정리를 건다 </summary>
    public static class SocialServerInstaller
    {
        /****************************************
        *                Methods
        ****************************************/

        // 채팅 설치보다 먼저 부른다. 채팅 서버가 귓속말, 길드 채팅을 돌려받은 Chat 중계로 넘긴다.
        // 게이트웨이가 없으면(백엔드 없는 개발 실행) 처리기만 걸고 요청은 BackendUnavailable로 거절하며 꺼내기를 만들지 않는다
        /// <summary> 서버를 연 뒤 한 번 부른다. bootId는 존재 신호의 bootId(아직 없으면 null), instanceId는 이 서버의 인스턴스 ID </summary>
        public static SocialServerHost Install(ShinshiNetworkManager manager, BackendGateway gateway, EServerRole role,
            string instanceId, Func<string> bootId, Action<string> warn)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            var audience = new MirrorSocialAudience(Constants.Session.MAX_SESSION_PARTICIPANTS);
            var router   = new SocialDeliveryRouter(audience, new SocialDeliveryLedger());
            var relay    = new SocialRelayServer(gateway, audience, role, null);
            var chat     = new SocialChatRelay(gateway, audience, router);
            relay.GuildResolved += ApplyGuildName;
            SocialDeliveryPump pump = ((gateway != null) && (!string.IsNullOrEmpty(instanceId)) && (bootId != null))
                ? new SocialDeliveryPump(gateway, audience, router, instanceId, bootId, null, warn)
                : null;

            var host = new SocialServerHost(relay, chat, router, pump, audience, null);
            host.RegisterHandlers();

            // 서버 수명 시작에 한 번만 건다. 매니저 이벤트라 Mirror 정적 이벤트처럼 덮어써지지 않는다
            manager.ServerDisconnected += host.HandleServerDisconnected;

            if (!manager.TryGetComponent(out SocialServerDriver driver))
            {
                driver = manager.gameObject.AddComponent<SocialServerDriver>();
            }

            driver.Bind(host);
            return host;
        }

        // 접속 후 가입, 탈퇴, 강퇴가 접속 토큰의 길드 이름을 낡게 만든다. 스냅샷이 확인한 값으로 결합과 머리 위 표시를 맞춘다
        private static void ApplyGuildName(int participantId, string guildName)
        {
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
            {
                if ((!(connection.authenticationData is SessionBinding binding)) || (!binding.IsActive)
                    || (binding.ParticipantId != participantId))
                {
                    continue;
                }

                binding.GuildName = guildName;
                if ((connection.identity != null) && (connection.identity.TryGetComponent(out PlayerCombatState combat)))
                {
                    combat.SetGuildName(guildName);
                }

                return;
            }
        }
    }
}
