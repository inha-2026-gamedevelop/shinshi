#if UNITY_EDITOR || DEVELOPMENT_BUILD
// Unity
using UnityEngine;

// 프로젝트
using Shinshi.Common;
using Shinshi.UI;

namespace Shinshi.Dev
{
    /// <summary>Dev_ItemSlot 씬에서 등급 틀, 수량, 잠금, 착용, 선택 표시를 가짜 값으로 시험한다</summary>
    public sealed class ItemSlotDemo : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private ItemSlotView[] _slots = null;

        private ItemSlotView _selected = null;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            if (_slots == null)
            {
                return;
            }

            ItemSlotData[] samples = BuildSamples();

            for (int i = 0; i < _slots.Length; ++i)
            {
                if (_slots[i] == null)
                {
                    continue;
                }

                _slots[i].SetData((i < samples.Length) ? samples[i] : default(ItemSlotData));
                _slots[i].Clicked += OnSlotClicked;
            }
        }

        private void OnDisable()
        {
            if (_slots == null)
            {
                return;
            }

            for (int i = 0; i < _slots.Length; ++i)
            {
                if (_slots[i] != null)
                {
                    _slots[i].Clicked -= OnSlotClicked;
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnSlotClicked(ItemSlotView slot)
        {
            if (_selected != null)
            {
                _selected.SetSelected(false);
            }

            _selected = slot;
            _selected.SetSelected(true);
        }

        private static ItemSlotData[] BuildSamples()
        {
            return new ItemSlotData[]
            {
                Make("item_essence",                   EItemGrade.Common,    2100, "정기",          "신시에서 1:1로 정제하는 성장 재료"),
                Make("item_refine_shard",              EItemGrade.Common,    35,   "정련편",        "장비 강화에 쓰는 캐릭터 귀속 재료"),
                Make("item_boss_ticket",               EItemGrade.Rare,      3,    "보스 입장권",   "월백 결계를 열 때 1장 소비한다"),
                Make("item_yeopjeon",                  EItemGrade.Common,    1250, "엽전",          "거래에 쓰는 재화"),
                Make("item_boss_material_common",      EItemGrade.Common,    4,    "보스 재료(일반)", "일반 등급 장비 제작 재료"),
                Make("item_boss_material_rare",        EItemGrade.Rare,      6,    "보스 재료(희귀)", "희귀 등급 장비 제작 재료"),
                Make("item_boss_material_epic",        EItemGrade.Epic,      8,    "보스 재료(영웅)", "영웅 등급 장비 제작 재료"),
                Make("item_boss_material_legendary",   EItemGrade.Legendary, 10,   "보스 재료(전설)", "전설 등급 장비 제작 재료"),
                Make("item_herb",                      EItemGrade.Common,    24,   "약초",          "물약 제작 재료"),
                Make("item_potion_small",              EItemGrade.Common,    9,    "소형 물약",     "즉시 체력을 회복한다"),
                Make("item_potion_large",              EItemGrade.Rare,      3,    "대형 물약",     "즉시 체력을 크게 회복한다"),
                Make("item_fox_trace",                 EItemGrade.Epic,      1,    "여우의 흔적",   "월백 결계 해금 조건", true, false),
                Make("equip_weapon_common",            EItemGrade.Common,    1,    "무기(일반)",    "기본 공격력을 높인다", false, true),
                Make("equip_head_rare",                EItemGrade.Rare,      1,    "머리(희귀)",    "생명을 높인다"),
                Make("equip_body_epic",                EItemGrade.Epic,      1,    "몸(영웅)",      "방어를 높인다", false, true),
                Make("equip_hands_legendary",          EItemGrade.Legendary, 1,    "손(전설)",      "치명을 높인다"),
                Make("equip_feet_rare",                EItemGrade.Rare,      1,    "발(희귀)",      "기력을 높인다"),
                Make("equip_accessory_legendary",      EItemGrade.Legendary, 1,    "장신구(전설)",  "경매 재료로 제작한다", true, false),
                Make("skill_warrior_wide_slash",       EItemGrade.Common,    1,    "넓게 베기",     "전방 3m 120도를 벤다"),
                Make("skill_sorcerer_lightning",       EItemGrade.Common,    1,    "낙뢰",          "지정 위치에 번개를 떨어뜨린다"),
                Make("skill_summoner_target_barrier",  EItemGrade.Common,    1,    "표적 결계",     "결계 안의 적에게 동료 피해가 늘어난다"),
                Make("talisman_kind_08",               EItemGrade.Rare,      1,    "피해 증가",     "기술 피해를 높인다"),
                Make("talisman_kind_14",               EItemGrade.Epic,      1,    "후속 타격",     "짧은 간격의 후속 타격을 더한다"),
                Make("class_summoner",                 EItemGrade.Common,    1,    "신령사",        "동료를 부리는 직업")
            };
        }

        private static ItemSlotData Make(string id, EItemGrade grade, int count, string title, string description, bool locked = false, bool equipped = false)
        {
            ItemSlotData data = new ItemSlotData();
            data.Id          = id;
            data.Grade       = grade;
            data.Count       = count;
            data.Title       = title;
            data.Description = description;
            data.IsLocked    = locked;
            data.IsEquipped  = equipped;
            return data;
        }
    }
}
#endif
