#if UNITY_EDITOR || DEVELOPMENT_BUILD
// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트
using Shinshi.Common;
using Shinshi.UI;

namespace Shinshi.Dev
{
    /// <summary>Dev_ItemSlot 씬에서 인벤토리 창의 탭, 정렬, 칸 사용량을 가짜 값으로 시험한다</summary>
    public sealed class InventoryDemo : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("참조")]
        [SerializeField] private InventoryView _inventory = null;

        [Header("시험 값")]
        [SerializeField] private int     _capacity  = 100;
        [SerializeField] private long    _yeopjeon  = 1250;
        [SerializeField] private long    _essence   = 2100;
        [SerializeField] private KeyCode _toggleKey = KeyCode.I;

        private readonly List<InventoryItemData> _items = new List<InventoryItemData>();

        /****************************************
        *              Unity Event
        ****************************************/

        private void Start()
        {
            if (_inventory == null)
            {
                return;
            }

            BuildSamples();
            _inventory.SetItems(_items, _capacity);
            _inventory.SetCurrency(_yeopjeon, _essence);
        }

        private void Update()
        {
            if ((_inventory != null) && Input.GetKeyDown(_toggleKey))
            {
                _inventory.SetOpen(!_inventory.IsOpen);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void BuildSamples()
        {
            _items.Clear();

            Add("item_refine_shard",            EItemGrade.Common,    35,  "정련편",          "장비 강화에 쓰는 캐릭터 귀속 재료",   EInventoryCategory.Material);
            Add("item_boss_ticket",             EItemGrade.Rare,      3,   "보스 입장권",     "월백 결계를 열 때 1장 소비한다",       EInventoryCategory.Etc);
            Add("item_boss_material_common",    EItemGrade.Common,    4,   "보스 재료(일반)", "일반 등급 장비 제작 재료",             EInventoryCategory.Material);
            Add("equip_weapon_common",          EItemGrade.Common,    1,   "무기(일반)",      "기본 공격력을 높인다",                 EInventoryCategory.Equipment);
            Add("item_boss_material_rare",      EItemGrade.Rare,      6,   "보스 재료(희귀)", "희귀 등급 장비 제작 재료",             EInventoryCategory.Material);
            Add("item_potion_small",            EItemGrade.Common,    9,   "소형 물약",       "즉시 체력을 회복한다",                 EInventoryCategory.Consumable);
            Add("equip_head_rare",              EItemGrade.Rare,      1,   "머리(희귀)",      "생명을 높인다",                        EInventoryCategory.Equipment);
            Add("item_boss_material_epic",      EItemGrade.Epic,      8,   "보스 재료(영웅)", "영웅 등급 장비 제작 재료",             EInventoryCategory.Material);
            Add("talisman_kind_08",             EItemGrade.Rare,      2,   "피해 증가",       "기술 피해를 높인다",                   EInventoryCategory.Talisman);
            Add("item_herb",                    EItemGrade.Common,    24,  "약초",            "물약 제작 재료",                       EInventoryCategory.Material);
            Add("equip_body_epic",              EItemGrade.Epic,      1,   "몸(영웅)",        "방어를 높인다",                        EInventoryCategory.Equipment);
            Add("item_fox_trace",               EItemGrade.Epic,      1,   "여우의 흔적",     "월백 결계 해금 조건",                  EInventoryCategory.Etc, true);
            Add("item_potion_large",            EItemGrade.Rare,      3,   "대형 물약",       "즉시 체력을 크게 회복한다",            EInventoryCategory.Consumable);
            Add("equip_hands_legendary",        EItemGrade.Legendary, 1,   "손(전설)",        "치명을 높인다",                        EInventoryCategory.Equipment);
            Add("talisman_kind_14",             EItemGrade.Epic,      1,   "후속 타격",       "짧은 간격의 후속 타격을 더한다",       EInventoryCategory.Talisman);
            Add("item_boss_material_legendary", EItemGrade.Legendary, 10,  "보스 재료(전설)", "전설 등급 장비 제작 재료",             EInventoryCategory.Material);
            Add("equip_feet_rare",              EItemGrade.Rare,      1,   "발(희귀)",        "기력을 높인다",                        EInventoryCategory.Equipment);
            Add("equip_accessory_legendary",    EItemGrade.Legendary, 1,   "장신구(전설)",    "경매 재료로 제작한다",                 EInventoryCategory.Equipment, true);
        }

        // 넣는 순서를 획득 순서로 쓴다
        private void Add(string id, EItemGrade grade, int count, string title, string description,
            EInventoryCategory category, bool locked = false)
        {
            ItemSlotData slot = new ItemSlotData(id, grade, count, title, description, locked);
            _items.Add(new InventoryItemData(slot, category, _items.Count));
        }
    }
}
#endif
