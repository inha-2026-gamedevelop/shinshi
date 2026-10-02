// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 운영자 목록 한 줄. 비밀번호와 TOTP 관련 값은 싣지 않는다 </summary>
    [DataContract]
    public sealed class AdminOperatorSummary
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "adminId")]           private string _adminId;
        [DataMember(Name = "loginName")]         private string _loginName;

        // AdminContractCodes.Role
        [DataMember(Name = "role")]              private int    _role;
        [DataMember(Name = "createdAtUnixMs")]   private long   _createdAtUnixMs;

        // 값이 없으면 null
        [DataMember(Name = "disabledAtUnixMs")]  private long?  _disabledAtUnixMs;
        [DataMember(Name = "lockedUntilUnixMs")] private long?  _lockedUntilUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string AdminId           => _adminId;
        public string LoginName         => _loginName;
        public int    Role              => _role;
        public long   CreatedAtUnixMs   => _createdAtUnixMs;
        public long?  DisabledAtUnixMs  => _disabledAtUnixMs;
        public long?  LockedUntilUnixMs => _lockedUntilUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public AdminOperatorSummary(string adminId, string loginName, int role, long createdAtUnixMs,
            long? disabledAtUnixMs, long? lockedUntilUnixMs)
        {
            _adminId           = adminId;
            _loginName         = loginName;
            _role              = role;
            _createdAtUnixMs   = createdAtUnixMs;
            _disabledAtUnixMs  = disabledAtUnixMs;
            _lockedUntilUnixMs = lockedUntilUnixMs;
        }

        private AdminOperatorSummary() { }
    }
}
