// System
using System;

// Third Party
using Newtonsoft.Json;

namespace Shinshi.Data.Growth
{
    /// <summary> equipment_instances 한 행. item_history의 이전, 이후 상태 JSON도 이 값으로 만든다 </summary>
    public sealed class EquipmentRecord
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid   ItemId       { get; set; }
        public Guid   AccountId    { get; set; }
        public Guid   CharacterId  { get; set; }
        public string DefinitionId { get; set; } = string.Empty;
        public int    Slot         { get; set; }
        public int    Grade        { get; set; }
        public bool   IsTraining   { get; set; }
        public string Origin       { get; set; } = string.Empty;
        public int    Enhancement  { get; set; }
        public bool   Equipped     { get; set; }
        public bool   Locked       { get; set; }
        public bool   Bound        { get; set; }

        /****************************************
        *                Methods
        ****************************************/

        public EquipmentRecord Clone()
        {
            return (EquipmentRecord)MemberwiseClone();
        }

        /// <summary> item_history 상태 객체. 키 순서와 이름은 운영 조회가 읽으므로 바꾸지 않는다 </summary>
        public string ToHistoryJson()
        {
            return JsonConvert.SerializeObject(new
            {
                characterId = CharacterId.ToString("D"),
                definitionId = DefinitionId,
                slot = Slot,
                grade = Grade,
                isTraining = IsTraining,
                origin = Origin,
                enhancement = Enhancement,
                equipped = Equipped,
                locked = Locked,
                bound = Bound
            }, Formatting.None);
        }
    }
}
