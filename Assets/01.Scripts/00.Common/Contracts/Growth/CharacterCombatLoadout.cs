// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 출정 예약 때 고정한 전투 장비, 수련, 부적. 던전은 이 값으로 최종 능력치를 계산한다 </summary>
    [DataContract]
    public sealed class CharacterCombatLoadout
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "classId")]        private int                    _classId;
        // EEquipmentSlot 순서의 6칸, 빈 부위는 null
        [DataMember(Name = "equipped")]       private CombatEquipmentEntry[] _equipped;
        // ETrainingStat 순서의 다섯 투자 단계
        [DataMember(Name = "training")]       private int[]                  _training;
        [DataMember(Name = "talismans")]      private CombatTalismanEntry[]  _talismans;
        // 예약한 출정의 콘텐츠 버전
        [DataMember(Name = "contentVersion")] private string                 _contentVersion;

        /****************************************
        *              Properties
        ****************************************/

        public int                    ClassId        => _classId;
        public CombatEquipmentEntry[] Equipped       => _equipped;
        public int[]                  Training       => _training;
        public CombatTalismanEntry[]  Talismans      => _talismans;
        public string                 ContentVersion => _contentVersion;

        /****************************************
        *                Methods
        ****************************************/

        public CharacterCombatLoadout(int classId, CombatEquipmentEntry[] equipped, int[] training,
            CombatTalismanEntry[] talismans, string contentVersion)
        {
            _classId        = classId;
            _equipped       = equipped;
            _training       = training;
            _talismans      = talismans;
            _contentVersion = contentVersion;
        }

        private CharacterCombatLoadout() { }
    }
}
