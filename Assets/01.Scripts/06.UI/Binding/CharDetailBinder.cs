// System
using System.Globalization;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 캐릭터 상세 창과 TownGrowthClient를 잇는다. 열 때 성장 스냅숏을 요청하고 받은 값으로 장비, 수련, 표시용 능력치를 채운다 </summary>
    public sealed class CharDetailBinder : MonoBehaviour
    {
        // 다른 플레이어의 상세는 범위 밖이다. 이 창은 내 캐릭터만 보인다. 이름은 서버가 내려준 네트워크 이름을 쓴다

        /****************************************
        *                Fields
        ****************************************/

        private const string TALISMAN_FORMAT = "등록 {0}개, 장착 {1}칸";

        [SerializeField] private CharDetailView _view = null;

        private bool _wasOpen = false;
        private bool _isDirty = true;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            TownGrowthClient.SnapshotChanged += OnSnapshot;
            _isDirty = true;
        }

        private void OnDisable()
        {
            TownGrowthClient.SnapshotChanged -= OnSnapshot;
        }

        private void Update()
        {
            if (_view == null)
            {
                return;
            }

            bool isOpen = _view.IsOpen;
            if ((isOpen) && (!_wasOpen))
            {
                if (ClientRoles.IsTown())
                {
                    TownGrowthClient.RequestSnapshot();
                }

                _isDirty = true;
            }

            _wasOpen = isOpen;
            if ((isOpen) && (_isDirty))
            {
                Apply();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnSnapshot(GrowthSnapshotMessage snapshot)
        {
            _isDirty = true;
        }

        private void Apply()
        {
            _isDirty = false;
            string characterName = ClientRoles.LocalCharacterName();
            if (!TownGrowthClient.HasSnapshot)
            {
                _view.SetIdentity(characterName, string.Empty);
                return;
            }

            GrowthSnapshotMessage growth = TownGrowthClient.Snapshot;
            _view.SetIdentity(characterName, CharDetailRules.ClassName(growth.ClassId));
            ApplyStats(in growth);
            for (int slot = 0; slot < Constants.Combat.EQUIPMENT_SLOT_COUNT; ++slot)
            {
                CharDetailRules.EquippedText(in growth, slot, out string equipmentName, out string enhancement);
                _view.SetSlot(slot, equipmentName, enhancement);
            }

            int[] training = growth.Training;
            for (int stat = 0; stat < training.Length; ++stat)
            {
                _view.SetTraining(stat, CharDetailRules.TrainingText(stat, training[stat]));
            }

            _view.SetTalismanSummary(string.Format(CultureInfo.InvariantCulture, TALISMAN_FORMAT, growth.Talismans.Length,
                growth.Loadout.Length));
        }

        private void ApplyStats(in GrowthSnapshotMessage growth)
        {
            Shinshi.Combat.CharacterStats stats = default;
            bool ready = (GameDB.IsInitialized)
                && (CharDetailRules.TryComputeStats(in growth, GameDB.Player, GameDB.Equipment, GameDB.Growth, out stats, GameDB.CharacterLevel.ToSettings()));
            if (!ready)
            {
                _view.SetStats(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
                _view.SetLifeSteal(string.Empty);
                _view.SetCombatPower(CharDetailRules.CombatPowerText(0));
                return;
            }

            _view.SetStats(CharDetailRules.Number(stats.Attack), CharDetailRules.Number(stats.MaxHealth),
                CharDetailRules.Number(stats.Defense), CharDetailRules.Percent(stats.CritChance), CharDetailRules.Number(stats.MaxEnergy),
                CharDetailRules.Number(stats.MoveSpeed));
            _view.SetLifeSteal(CharDetailRules.Percent(stats.LifeSteal));
            _view.SetCombatPower(CharDetailRules.CombatPowerText(
                Shinshi.Combat.CombatPowerRules.Compute(in stats, GameDB.Player)));
        }
    }
}
