// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 수령할 수 있는 우편 하나. 만료 판정은 서버 DB 시각으로 하므로 expiresAtUnixMs는 표시용이다 </summary>
    [DataContract]
    public sealed class InboxEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "inboxId")]           private string        _inboxId;

        // AdminGrant(운영자 지급), RecoveryCompensation(장애 보정)
        [DataMember(Name = "sourceKind")]        private string        _sourceKind;
        [DataMember(Name = "createdAtUnixMs")]   private long          _createdAtUnixMs;
        [DataMember(Name = "expiresAtUnixMs")]   private long          _expiresAtUnixMs;
        [DataMember(Name = "rewards")]           private InboxReward[] _rewards;

        // Essence, Shards가 있으면 수령 요청에 받을 캐릭터 ID를 넣어야 한다
        [DataMember(Name = "requiresCharacter")] private bool          _requiresCharacter;

        /****************************************
        *              Properties
        ****************************************/

        public string        InboxId           => _inboxId;
        public string        SourceKind        => _sourceKind;
        public long          CreatedAtUnixMs   => _createdAtUnixMs;
        public long          ExpiresAtUnixMs   => _expiresAtUnixMs;
        public InboxReward[] Rewards           => _rewards;
        public bool          RequiresCharacter => _requiresCharacter;

        /****************************************
        *                Methods
        ****************************************/

        public InboxEntry(string inboxId, string sourceKind, long createdAtUnixMs, long expiresAtUnixMs,
            InboxReward[] rewards, bool requiresCharacter)
        {
            _inboxId           = inboxId;
            _sourceKind        = sourceKind;
            _createdAtUnixMs   = createdAtUnixMs;
            _expiresAtUnixMs   = expiresAtUnixMs;
            _rewards           = rewards;
            _requiresCharacter = requiresCharacter;
        }

        private InboxEntry() { }
    }
}
