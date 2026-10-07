// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 계정의 명시 해금 또는 클리어 기록 </summary>
    [DataContract]
    public sealed class AdminCharacterUnlockView
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "unlockKind")]       private string _unlockKind;
        [DataMember(Name = "targetId")]         private string _targetId;
        [DataMember(Name = "operationId")]      private string _operationId;
        [DataMember(Name = "unlockedAtUnixMs")] private long   _unlockedAtUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string UnlockKind       => _unlockKind;
        public string TargetId         => _targetId;
        public string OperationId      => _operationId;
        public long   UnlockedAtUnixMs => _unlockedAtUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public AdminCharacterUnlockView(string unlockKind, string targetId, string operationId,
            long unlockedAtUnixMs)
        {
            _unlockKind       = unlockKind;
            _targetId         = targetId;
            _operationId      = operationId;
            _unlockedAtUnixMs = unlockedAtUnixMs;
        }

        private AdminCharacterUnlockView() { }
    }
}
