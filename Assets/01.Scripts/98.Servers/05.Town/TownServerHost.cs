// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 신원은 인증기가 연결에 붙인 SessionBinding에서만 꺼낸다. 메시지 안에는 참가자, 계정, 캐릭터 자리가 없다.
    // 결합이 없거나 끊긴 연결의 메시지는 버린다. 처리기는 서버를 연 뒤 한 번 등록하고 메서드 그룹 대리자도 그때 한 번만 만든다
    /// <summary> 마을 메시지를 Mirror에 등록하고 인증된 연결을 TownMember로 바꿔 TownServerRuntime에 넘긴다 </summary>
    public sealed class TownServerHost
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly TownServerRuntime  _runtime;
        private readonly MirrorTownAudience _audience;

        /****************************************
        *              Properties
        ****************************************/

        public TownServerRuntime Runtime => _runtime;

        /****************************************
        *                Methods
        ****************************************/

        public TownServerHost(TownServerRuntime runtime, MirrorTownAudience audience)
        {
            _runtime  = runtime;
            _audience = audience;
        }

        /// <summary> 서버를 연 뒤 부른다, 그 전에 등록하면 Listen의 초기화에 지워진다 </summary>
        public void RegisterHandlers()
        {
            NetworkServer.ReplaceHandler<PartyRequestMessage>(OnParty);
            NetworkServer.ReplaceHandler<GrowthRequestMessage>(OnGrowth);
            NetworkServer.ReplaceHandler<LifeRequestMessage>(OnLife);
            NetworkServer.ReplaceHandler<IntroCompleteRequestMessage>(OnIntro);
        }

        /// <summary> 매니저의 연결 종료 알림에 건다 </summary>
        public void HandleServerDisconnected(NetworkConnectionToClient connection)
        {
            if (!(connection.authenticationData is SessionBinding binding))
            {
                return;
            }

            _runtime.RemoveMember(binding.ParticipantId);
            _audience.Remove(binding.ParticipantId);
        }

        private void OnParty(NetworkConnectionToClient connection, PartyRequestMessage message)
        {
            if (TryResolve(connection, out TownMember member))
            {
                _runtime.HandleParty(member, in message);
            }
        }

        private void OnGrowth(NetworkConnectionToClient connection, GrowthRequestMessage message)
        {
            if (TryResolve(connection, out TownMember member))
            {
                _runtime.HandleGrowth(member, in message);
            }
        }

        private void OnLife(NetworkConnectionToClient connection, LifeRequestMessage message)
        {
            if (TryResolve(connection, out TownMember member))
            {
                _runtime.HandleLife(member, in message);
            }
        }

        private void OnIntro(NetworkConnectionToClient connection, IntroCompleteRequestMessage message)
        {
            if (TryResolve(connection, out TownMember member))
            {
                _runtime.HandleIntro(member, in message);
            }
        }

        // 첫 메시지 때 접속자로 올린다. 연결 하나에 한 번만 TownMember를 만든다
        private bool TryResolve(NetworkConnectionToClient connection, out TownMember member)
        {
            member = null;
            if ((!(connection.authenticationData is SessionBinding binding)) || (!binding.IsActive))
            {
                return false;
            }

            if (_runtime.TryGetMember(binding.ParticipantId, out member))
            {
                return true;
            }

            member = TownMember.FromBinding(binding);
            _audience.Add(binding.ParticipantId, connection);
            _runtime.AddMember(member);
            return true;
        }
    }
}
