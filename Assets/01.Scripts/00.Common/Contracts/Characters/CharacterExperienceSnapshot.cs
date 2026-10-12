// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 영속 경험치와 같은 세션에서 이미 포함된 사건 순번 </summary>
    [DataContract]
    public sealed class CharacterExperienceSnapshot
    {

        /****************************************
        *                Fields
        ****************************************/
        [DataMember(Name = "settingsHash")] private string _settingsHash;
        [DataMember(Name = "totalExperience")] private long _totalExperience;
        [DataMember(Name = "sessionId")] private string _sessionId;
        [DataMember(Name = "lastExperienceEventSeq")] private long _lastExperienceEventSeq;

        public string SettingsHash => _settingsHash;
        public long TotalExperience => _totalExperience;
        public string SessionId => _sessionId;
        public long LastExperienceEventSeq => _lastExperienceEventSeq;

        public CharacterExperienceSnapshot(long totalExperience, string sessionId, long lastExperienceEventSeq, string settingsHash = null)
        {
            _settingsHash = settingsHash;
            _totalExperience = totalExperience;
            _sessionId = sessionId;
            _lastExperienceEventSeq = lastExperienceEventSeq;
        }

        private CharacterExperienceSnapshot() { }
    }
}
