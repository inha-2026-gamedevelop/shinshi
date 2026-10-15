// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 별도 개발 씬에서만 옵션 화면에 카탈로그 기반 표시 데이터를 제공한다 </summary>
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [ExecuteAlways]
#endif
    public sealed class EquipmentOptionPreviewBinder : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private EquipmentOptionView _view = null;
        [SerializeField] private EquipmentCatalogSO _catalog = null;
        [SerializeField] private long _essence = 18400;
        [SerializeField] private long _shards = 64;
        private const EItemGrade SAMPLE_GRADE = EItemGrade.Rare;
        private const int SAMPLE_ENHANCEMENT = 16;
        private const int SAMPLE_MISSES = 2;
        private int _selected;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            if ((_view == null) || (_catalog == null) || (TownGrowthClient.HasSnapshot))
            {
                return;
            }

            _view.SlotSelected += Select;
            Select(_selected);
        }

        private void OnDisable()
        {
            if (_view != null)
            {
                _view.SlotSelected -= Select;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void Select(int slot)
        {
            if (TownGrowthClient.HasSnapshot)
            {
                return;
            }

            _selected = slot;
            for (int i = 0; i < Constants.Combat.EQUIPMENT_SLOT_COUNT; ++i)
            {
                bool exists = _catalog.TryGetCrafted((EEquipmentSlot)i, SAMPLE_GRADE, out EquipmentCatalogSO.EquipmentDefinition item);
                string name = exists ? EquipmentEnhanceRules.DisplayName(_catalog, item.DefinitionId) : string.Empty;
                _view.SetSlot(i, exists ? name + "\n+" + SAMPLE_ENHANCEMENT
                    : EquipmentOptionViewRules.SlotName(i) + "\n착용 장비 없음");
            }

            _view.SetSelectedSlot(slot);
            bool hasItem = _catalog.TryGetCrafted((EEquipmentSlot)slot, SAMPLE_GRADE, out EquipmentCatalogSO.EquipmentDefinition selected);
            _view.SetItem(hasItem ? EquipmentEnhanceRules.DisplayName(_catalog, selected.DefinitionId) : "착용한 장비 없음");
            IReadOnlyList<int> pool = _catalog.GetOptionPool((EEquipmentSlot)slot);
            SetLine(0, ((pool != null) && (pool.Count > 0)) ? pool[0] : EquipmentOptionRules.KIND_NONE);
            SetLine(1, ((pool != null) && (pool.Count > 1)) ? pool[1] : EquipmentOptionRules.KIND_NONE);
            _view.SetCost(EquipmentOptionViewRules.CostText(_catalog.OptionRerollEssenceCost,
                _catalog.OptionRerollShardCost, _essence, _shards));
            _view.SetPity(EquipmentOptionViewRules.PityText(SAMPLE_MISSES, _catalog.OptionPityThreshold));
            _view.SetStatus("미리보기 · 재화는 소모되지 않습니다");
            _view.SetReroll("미리보기", false);
        }

        private void SetLine(int line, int kind)
        {
            if (_catalog.TryGetOptionRange(kind, SAMPLE_GRADE, out int min, out int max))
            {
                _view.SetLine(line, EquipmentOptionViewRules.LineText(kind, min),
                    EquipmentOptionViewRules.RangeText(min, max), false);
            }
            else
            {
                _view.SetLine(line, "옵션 없음", string.Empty, false);
            }
        }
#endif
    }
}
