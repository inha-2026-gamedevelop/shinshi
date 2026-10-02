// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 계정에 발급된 초대 한 건. 코드 원문과 해시는 싣지 않는다 </summary>
    [DataContract]
    public sealed class AdminInviteView
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "inviteId")]        private string _inviteId;
        [DataMember(Name = "expiresAtUnixMs")] private long   _expiresAtUnixMs;
        [DataMember(Name = "maxUses")]         private long   _maxUses;
        [DataMember(Name = "usedCount")]       private long   _usedCount;

        // 마지막 사용과 폐기 시각. 없으면 null
        [DataMember(Name = "usedAtUnixMs")]    private long?  _usedAtUnixMs;
        [DataMember(Name = "revokedAtUnixMs")] private long?  _revokedAtUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string InviteId        => _inviteId;
        public long   ExpiresAtUnixMs => _expiresAtUnixMs;
        public long   MaxUses         => _maxUses;
        public long   UsedCount       => _usedCount;
        public long?  UsedAtUnixMs    => _usedAtUnixMs;
        public long?  RevokedAtUnixMs => _revokedAtUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public AdminInviteView(string inviteId, long expiresAtUnixMs, long maxUses, long usedCount, long? usedAtUnixMs,
            long? revokedAtUnixMs)
        {
            _inviteId        = inviteId;
            _expiresAtUnixMs = expiresAtUnixMs;
            _maxUses         = maxUses;
            _usedCount       = usedCount;
            _usedAtUnixMs    = usedAtUnixMs;
            _revokedAtUnixMs = revokedAtUnixMs;
        }

        private AdminInviteView() { }
    }
}
