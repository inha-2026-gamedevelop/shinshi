// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 강화 실패 보정 하나, 키는 캐릭터, 부위, 현재 단계다 </summary>
    [DataContract]
    public sealed class EnhancementPityView
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "slot")]     private int _slot;
        [DataMember(Name = "stage")]    private int _stage;
        [DataMember(Name = "failures")] private int _failures;

        /****************************************
        *              Properties
        ****************************************/

        public int Slot     => _slot;
        public int Stage    => _stage;
        public int Failures => _failures;

        /****************************************
        *                Methods
        ****************************************/

        public EnhancementPityView(int slot, int stage, int failures)
        {
            _slot     = slot;
            _stage    = stage;
            _failures = failures;
        }

        private EnhancementPityView() { }
    }
}
