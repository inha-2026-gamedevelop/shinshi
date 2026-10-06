#if UNITY_EDITOR || DEVELOPMENT_BUILD
// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using TMPro;

// 프로젝트
using Shinshi.Common;
using Shinshi.UI;

namespace Shinshi.Dev
{
    /// <summary>Dev_Trade 씬에서 거래 창의 양쪽 칸과 엽전을 가짜 값으로 채워 화면만 확인한다</summary>
    public sealed class TradeDemo : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string CURRENCY_FORMAT = "{0:N0}";

        [Header("참조")]
        [SerializeField] private ItemSlotGridView _theirGrid  = null;
        [SerializeField] private ItemSlotGridView _myGrid     = null;
        [SerializeField] private TMP_Text         _theirGold  = null;
        [SerializeField] private TMP_Text         _myGold     = null;

        [Header("시험 값")]
        [SerializeField] private long _theirYeopjeon = 500;
        [SerializeField] private long _myYeopjeon    = 1200;

        private readonly List<ItemSlotData> _theirItems = new List<ItemSlotData>();
        private readonly List<ItemSlotData> _myItems    = new List<ItemSlotData>();

        /****************************************
        *              Unity Event
        ****************************************/

        private void Start()
        {
            _theirItems.Add(new ItemSlotData("equip_hands_legendary", EItemGrade.Legendary, 1, "손(전설)", "치명을 높인다"));
            _theirItems.Add(new ItemSlotData("item_boss_material_epic", EItemGrade.Epic, 8, "보스 재료(영웅)", "영웅 등급 장비 제작 재료"));

            _myItems.Add(new ItemSlotData("item_potion_large", EItemGrade.Rare, 3, "대형 물약", "즉시 체력을 크게 회복한다"));
            _myItems.Add(new ItemSlotData("item_herb", EItemGrade.Common, 24, "약초", "물약 제작 재료"));
            _myItems.Add(new ItemSlotData("equip_head_rare", EItemGrade.Rare, 1, "머리(희귀)", "생명을 높인다"));

            if (_theirGrid != null)
            {
                _theirGrid.SetItems(_theirItems);
            }

            if (_myGrid != null)
            {
                _myGrid.SetItems(_myItems);
            }

            SetText(_theirGold, string.Format(CURRENCY_FORMAT, _theirYeopjeon));
            SetText(_myGold, string.Format(CURRENCY_FORMAT, _myYeopjeon));
        }

        /****************************************
        *                Methods
        ****************************************/

        private static void SetText(TMP_Text label, string text)
        {
            if (label != null)
            {
                label.text = text;
            }
        }
    }
}
#endif
