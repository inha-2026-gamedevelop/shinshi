// System
using System.Collections.Generic;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Network;

namespace Shinshi.Tests
{
    /// <summary> 소셜 서버 시험용 가짜 창구, 보낸 메시지를 순서대로 기록하고 접속 캐릭터를 시험이 정한다 </summary>
    public sealed class FakeSocialAudience : ISocialAudience
    {
        // 시험 기록이라 메시지를 object로 담는다(박싱은 시험에서만 생긴다)

        /****************************************
        *                Fields
        ****************************************/

        private readonly Dictionary<string, int>         _characters = new Dictionary<string, int>();
        private readonly List<KeyValuePair<int, object>> _sent       = new List<KeyValuePair<int, object>>();

        /****************************************
        *                Methods
        ****************************************/

        public void Connect(string characterId, int participantId)
        {
            _characters[characterId] = participantId;
        }

        public void Disconnect(string characterId)
        {
            _characters.Remove(characterId);
        }

        public void Send<T>(int participantId, T message) where T : struct, NetworkMessage
        {
            _sent.Add(new KeyValuePair<int, object>(participantId, message));
        }

        public bool TryFindParticipant(string characterId, out int participantId)
        {
            return _characters.TryGetValue(characterId, out participantId);
        }

        public bool HasConnectedCharacters()
        {
            return _characters.Count > 0;
        }

        /// <summary> 이 참가자에게 보낸 T를 보낸 순서대로 </summary>
        public List<T> All<T>(int participantId) where T : struct
        {
            var found = new List<T>();
            for (int i = 0; i < _sent.Count; ++i)
            {
                if ((_sent[i].Key == participantId) && (_sent[i].Value is T message))
                {
                    found.Add(message);
                }
            }

            return found;
        }

        public void Clear()
        {
            _sent.Clear();
        }
    }
}
