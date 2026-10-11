// System
using System;

// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 재접속 조회와 지연 ACK를 같은 세션 순번으로 합친다 </summary>
    public sealed class CharacterExperienceTracker
    {

        /****************************************
        *                Fields
        ****************************************/
        private readonly CharacterLevelSettings _settings;
        private string _sessionId;
        private long _sequence;
        private long _total;

        public long TotalExperience => _total;
        public long LastSequence => _sequence;

        public CharacterExperienceTracker(CharacterLevelSettings settings, CharacterExperienceSnapshot snapshot)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            _sessionId = snapshot.SessionId;
            _total = snapshot.TotalExperience;
            _sequence = snapshot.LastExperienceEventSeq;
        }

        public void Merge(CharacterExperienceSnapshot snapshot)
        {
            if ((snapshot == null) || (snapshot.TotalExperience < _total))
            {
                return;
            }
            if (string.Equals(_sessionId, snapshot.SessionId, StringComparison.Ordinal)
                && (snapshot.LastExperienceEventSeq < _sequence))
            {
                return;
            }
            _sessionId = snapshot.SessionId;
            _sequence = snapshot.LastExperienceEventSeq;
            _total = snapshot.TotalExperience;
        }

        public void Commit(string sessionId, long sequence, long amount)
        {
            if (!string.Equals(_sessionId, sessionId, StringComparison.Ordinal) || (sequence <= _sequence))
            {
                return;
            }
            _total = CharacterLevelRules.Add(_settings, _total, amount);
            _sequence = sequence;
        }
    }
}
