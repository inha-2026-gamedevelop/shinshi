// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 별도 개발 씬에서만 스킬창에 카탈로그 기반 가짜 성장 상태를 넣는다, 서버 상태가 있으면 아무것도 하지 않는다 </summary>
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [ExecuteAlways]
#endif
    public sealed class SkillWindowPreviewBinder : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /****************************************
        *                Fields
        ****************************************/

        private const int    SAMPLE_CARDS     = 3;
        private const int    SAMPLE_FAILURES  = 2;
        private const int    SAMPLE_REVISION  = 7;
        private const int    STORAGE_CAPACITY = 100;
        private const int    REGISTERED_EVERY = 2; // 효과 두 개 중 하나를 등록한 상태로 보인다
        private const string PREVIEW_VERSION  = "preview";

        [SerializeField] private SkillWindowBinder     _binder       = null;
        [SerializeField] private SkillCatalogSO        _skills       = null;
        [SerializeField] private TalismanCatalogSO     _talismans    = null;
        [SerializeField] private SkillDisplayCatalogSO _display      = null;
        [SerializeField] private GrowthConfigSO        _growth       = null;
        [SerializeField] private PlayerConfigSO        _player       = null;
        [SerializeField] private EPlayerClass          _playerClass  = EPlayerClass.Swordsman;
        [SerializeField] private int                   _tab          = 1;
        [SerializeField] private long                  _essence      = 4800;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            Show();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 직업을 바꿔 다시 그린다, 화면 캡처 도구가 부른다 </summary>
        public void ShowClass(EPlayerClass playerClass, int tab)
        {
            _playerClass = playerClass;
            _tab         = tab;
            Show();
        }

        private void Show()
        {
            if ((_binder == null) || (_skills == null) || (_talismans == null) || (_display == null) || (_growth == null)
                || (_player == null) || (TownGrowthClient.HasSnapshot))
            {
                return;
            }

            _binder.ApplyPreview(_skills, _talismans, _display, _growth, _player, _playerClass, BuildSnapshot());
            _binder.SelectPreview(_tab, 0, 0);
        }

        // 그 직업 효과를 차례로 돌며 등록, 카드만 보유, 실패 보정만 남은 상태를 섞는다. 등록한 효과는 단계마다 하나씩 장착한다
        private GrowthSnapshotMessage BuildSnapshot()
        {
            var talismans = new List<TownTalismanEntry>();
            var items     = new List<TownItemEntry>();
            var loadout   = new List<TownLoadoutEntry>();
            IReadOnlyList<TalismanCatalogSO.TalismanEffectDefinition> effects = _talismans.Effects;
            int index = 0;
            for (int i = 0; i < effects.Count; ++i)
            {
                TalismanCatalogSO.TalismanEffectDefinition effect = effects[i];
                if ((effect == null) || (effect.PlayerClass != _playerClass))
                {
                    continue;
                }

                bool registered = (index % REGISTERED_EVERY) == 0;
                int  level      = registered ? 1 + (index % _growth.MaxEffectLevel) : 0;
                talismans.Add(new TownTalismanEntry(effect.DefinitionId, level, registered ? 0 : SAMPLE_FAILURES));
                items.Add(new TownItemEntry(effect.DefinitionId, null, SAMPLE_CARDS, 0));
                if ((registered) && (!SkillWindowViewRules.IsEquipped(loadout, effect.SkillId, (int)effect.Tier, null)))
                {
                    SkillWindowViewRules.ToggleEquip(loadout, effect.SkillId, (int)effect.Tier, effect.DefinitionId);
                }

                ++index;
            }

            return new GrowthSnapshotMessage((int)_playerClass, SAMPLE_REVISION, _essence, 0, null, null, null,
                talismans.ToArray(), loadout.ToArray(), items.ToArray(), 0, STORAGE_CAPACITY, PREVIEW_VERSION);
        }
#endif
    }
}
