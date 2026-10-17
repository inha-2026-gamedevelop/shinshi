// System
using System;
using System.Collections.Generic;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 인증 완료 사건이 매니저에 없어 대응표는 찾지 못했을 때 연결 목록을 한 번 훑어 다시 만든다. 전달함 행은 이 인스턴스에 임대가
    // 있는 캐릭터 앞으로만 오므로 못 찾는 경우는 이동 중인 대상 정도라 드물다. 찾은 뒤에도 결합이 활성이고 같은 캐릭터인지 다시 본다.
    // Dictionary.Values의 foreach는 구조체 열거자라 할당이 없다
    /// <summary> 캐릭터 ID, 참가자 번호와 Mirror 연결의 대응 </summary>
    public sealed class MirrorSocialAudience : ISocialAudience
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Dictionary<string, NetworkConnectionToClient> _byCharacter;
        private readonly Dictionary<int, NetworkConnectionToClient>    _byParticipant;

        /****************************************
        *                Methods
        ****************************************/

        public MirrorSocialAudience(int capacity)
        {
            _byCharacter   = new Dictionary<string, NetworkConnectionToClient>(capacity, StringComparer.OrdinalIgnoreCase);
            _byParticipant = new Dictionary<int, NetworkConnectionToClient>(capacity);
        }

        /// <summary> 매니저의 연결 종료 알림에서 부른다 </summary>
        public void Remove(NetworkConnectionToClient connection)
        {
            if (!(connection.authenticationData is SessionBinding binding))
            {
                return;
            }

            if ((_byParticipant.TryGetValue(binding.ParticipantId, out NetworkConnectionToClient known)) && (known == connection))
            {
                _byParticipant.Remove(binding.ParticipantId);
            }

            if ((!string.IsNullOrEmpty(binding.CharacterId))
                && (_byCharacter.TryGetValue(binding.CharacterId, out known)) && (known == connection))
            {
                _byCharacter.Remove(binding.CharacterId);
            }
        }

        public void Send<T>(int participantId, T message) where T : struct, NetworkMessage
        {
            if ((!TryGetActive(participantId, out NetworkConnectionToClient connection)) && (!Rebuild(participantId, out connection)))
            {
                return;
            }

            connection.Send(message);
        }

        public bool TryFindParticipant(string characterId, out int participantId)
        {
            participantId = 0;
            if (string.IsNullOrEmpty(characterId))
            {
                return false;
            }

            if (!TryGetActiveCharacter(characterId, out NetworkConnectionToClient connection))
            {
                Rebuild(0, out _);
                if (!TryGetActiveCharacter(characterId, out connection))
                {
                    return false;
                }
            }

            participantId = ((SessionBinding)connection.authenticationData).ParticipantId;
            return true;
        }

        public bool HasConnectedCharacters()
        {
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
            {
                if ((connection.authenticationData is SessionBinding binding) && (binding.IsActive)
                    && (!string.IsNullOrEmpty(binding.CharacterId)))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetActive(int participantId, out NetworkConnectionToClient connection)
        {
            return (_byParticipant.TryGetValue(participantId, out connection))
                && (connection.authenticationData is SessionBinding binding) && (binding.IsActive)
                && (binding.ParticipantId == participantId);
        }

        private bool TryGetActiveCharacter(string characterId, out NetworkConnectionToClient connection)
        {
            return (_byCharacter.TryGetValue(characterId, out connection))
                && (connection.authenticationData is SessionBinding binding) && (binding.IsActive)
                && (string.Equals(binding.CharacterId, characterId, StringComparison.OrdinalIgnoreCase));
        }

        // 대응표를 현재 연결 목록으로 다시 만든다. wantedParticipant의 연결을 찾았으면 참
        private bool Rebuild(int wantedParticipant, out NetworkConnectionToClient wanted)
        {
            wanted = null;
            _byCharacter.Clear();
            _byParticipant.Clear();
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
            {
                if ((!(connection.authenticationData is SessionBinding binding)) || (!binding.IsActive))
                {
                    continue;
                }

                _byParticipant[binding.ParticipantId] = connection;
                if (!string.IsNullOrEmpty(binding.CharacterId))
                {
                    _byCharacter[binding.CharacterId] = connection;
                }

                if (binding.ParticipantId == wantedParticipant)
                {
                    wanted = connection;
                }
            }

            return wanted != null;
        }
    }
}
