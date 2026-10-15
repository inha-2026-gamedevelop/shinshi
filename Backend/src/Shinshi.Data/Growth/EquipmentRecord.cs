// System
using System;

// Third Party
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Project
using Shinshi.Contracts;

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

        // 옵션 두 줄. 종류는 EquipmentOptionRules의 KIND_* 값, 값은 만분율 정수이고 (0, 0)이 옵션 없음이다
        public int    Option1Kind  { get; set; }
        public int    Option1Value { get; set; }
        public int    Option2Kind  { get; set; }
        public int    Option2Value { get; set; }

        // 경매장 보관 중이면 "Market", 아니면 null. 성장 쓰기는 이 값이 있으면 MarketEscrowed로 거절한다
        public string? EscrowKind  { get; set; }

        // 보관한 등록 ID. 경매장 쓰기만 읽는다
        public Guid?   EscrowRef   { get; set; }

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
            // 보관 표시는 값이 있을 때만 끝에 붙여 기존 이력의 키와 순서를 그대로 둔다
            JObject state = new JObject
            {
                ["characterId"] = CharacterId.ToString("D"),
                ["definitionId"] = DefinitionId,
                ["slot"] = Slot,
                ["grade"] = Grade,
                ["isTraining"] = IsTraining,
                ["origin"] = Origin,
                ["enhancement"] = Enhancement,
                ["equipped"] = Equipped,
                ["locked"] = Locked,
                ["bound"] = Bound
            };
            if (EscrowKind != null)
            {
                state["escrowKind"] = EscrowKind;
            }

            // 옵션도 있을 때만 끝에 붙인다. 옵션 없는 장비의 이력은 옵션 도입 전과 같은 모양이다
            if ((Option1Kind != EquipmentOptionRules.KIND_NONE) || (Option2Kind != EquipmentOptionRules.KIND_NONE))
            {
                state["option1Kind"] = Option1Kind;
                state["option1Value"] = Option1Value;
                state["option2Kind"] = Option2Kind;
                state["option2Value"] = Option2Value;
            }

            return state.ToString(Formatting.None);
        }
    }
}
