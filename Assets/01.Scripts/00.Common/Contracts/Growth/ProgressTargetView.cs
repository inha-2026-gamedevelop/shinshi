// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 지역 또는 보스전 하나의 해금, 성공 기록 </summary>
    [DataContract]
    public sealed class ProgressTargetView
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "targetId")] private string _targetId;
        [DataMember(Name = "unlocked")] private bool   _unlocked;
        [DataMember(Name = "cleared")]  private bool   _cleared;

        /****************************************
        *              Properties
        ****************************************/

        public string TargetId => _targetId;
        public bool   Unlocked => _unlocked;
        public bool   Cleared  => _cleared;

        /****************************************
        *                Methods
        ****************************************/

        public ProgressTargetView(string targetId, bool unlocked, bool cleared)
        {
            _targetId = targetId;
            _unlocked = unlocked;
            _cleared  = cleared;
        }

        private ProgressTargetView() { }
    }
}
