// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 개발 씬에서 서버 상태가 없는 장비 강화 창에 저장하지 않는 예시 장비를 제공한다 </summary>
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [ExecuteAlways]
#endif
    public sealed class EquipmentEnhancePreviewBinder : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /****************************************
        *                Fields
        ****************************************/

        private const int SLOT_COUNT = Constants.Combat.EQUIPMENT_SLOT_COUNT;

        private const string SAMPLE_ITEM_PREFIX = "preview-equipment-";

        [Header("샘플 데이터 (개발 전용, EEquipmentSlot 순서)")]
        [SerializeField] private long       _sampleEssence  = 18400;
        [SerializeField] private long       _sampleShards   = 64;
        [SerializeField] private EItemGrade _sampleGrade    = EItemGrade.Rare;
        [SerializeField] private int[]      _sampleLevels   = { 16, 12, 17, 9, 25, 14 };
        [SerializeField] private int[]      _sampleFailures = { 2, 0, 0, 0, 0, 1 };

        [Header("씬 미리보기 참조")]
        [SerializeField] private EquipmentEnhanceView _view    = null;
        [SerializeField] private EquipmentCatalogSO   _catalog = null;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            if ((_view == null) || (_catalog == null) || ((Application.isPlaying) && (TownGrowthClient.HasSnapshot)))
            {
                return;
            }

            _view.ApplyPreviewSnapshot(_catalog, _sampleEssence, _sampleShards, BuildEquipment(), BuildPity());
        }

        /****************************************
        *                Methods
        ****************************************/

        // 샘플 등급의 제작 장비가 카탈로그에 없는 부위는 비워 둔다
        private TownEquipmentEntry[] BuildEquipment()
        {
            var entries = new TownEquipmentEntry[SLOT_COUNT];
            int count   = 0;
            for (int i = 0; i < SLOT_COUNT; ++i)
            {
                if (!_catalog.TryGetCrafted((EEquipmentSlot)i, _sampleGrade, out EquipmentCatalogSO.EquipmentDefinition definition))
                {
                    continue;
                }

                int level = Mathf.Clamp(LevelAt(i), 0, _catalog.MaxEnhancement);
                entries[count] = new TownEquipmentEntry(SAMPLE_ITEM_PREFIX + i, definition.DefinitionId, (byte)i,
                    (byte)definition.Grade, definition.IsTraining, GrowthCodes.EquipmentOrigin.CRAFT, level, true, false, true);
                ++count;
            }

            Array.Resize(ref entries, count);
            return entries;
        }

        private TownPityEntry[] BuildPity()
        {
            var pity = new TownPityEntry[SLOT_COUNT];
            for (int i = 0; i < SLOT_COUNT; ++i)
            {
                int failures = ((_sampleFailures != null) && (i < _sampleFailures.Length)) ? Mathf.Max(0, _sampleFailures[i]) : 0;
                pity[i] = new TownPityEntry((byte)i, LevelAt(i), failures);
            }

            return pity;
        }

        private int LevelAt(int index)
        {
            return ((_sampleLevels != null) && (index < _sampleLevels.Length)) ? _sampleLevels[index] : 0;
        }
#endif
    }
}
