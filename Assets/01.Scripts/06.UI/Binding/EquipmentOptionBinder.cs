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
    /// <summary> 장비 옵션 재설정 창과 TownGrowthClient를 잇는다. 열 때 성장 상태를 받고, 재설정 결과가 오면 줄을 갱신한다 </summary>
    public sealed class EquipmentOptionBinder : MonoBehaviour
    {
        // 뽑기와 비용 차감은 마을 서버와 API가 한다. 여기는 요청을 보내고 받은 스냅숏을 보여 줄 뿐이다.
        // 결과를 받지 못한 요청은 비용이 이미 차감됐을 수 있다. 새 요청을 보내면 한 번 더 차감되므로,
        // 같은 요청을 다시 보내 결과를 확인하기 전에는 새 재설정을 막는다

        /****************************************
        *                Fields
        ****************************************/

        private const string REROLL_TEXT       = "재설정";
        private const string CONFIRM_TEXT      = "결과 확인";
        private const string WAITING_TEXT      = "재설정하는 중입니다";
        private const string DONE_TEXT         = "옵션을 새로 뽑았습니다";
        private const string UNKNOWN_TEXT      = "결과를 받지 못했습니다. 결과 확인을 눌러 다시 확인합니다";
        private const string CHECKING_TEXT     = "결과를 확인하는 중입니다";
        private const string TOWN_ONLY_TEXT    = "신시에서만 재설정할 수 있습니다";
        private const string NO_ITEM_TEXT      = "착용한 장비가 없습니다";
        private const string NOT_ALLOWED_TEXT  = "이 장비는 재설정할 수 없습니다";
        private const string NOT_ENOUGH_TEXT   = "정기나 정련편이 모자랍니다";
        private const string FAILED_TEXT       = "재설정하지 못했습니다";

        private static readonly EGrowthAction[] G_OwnActions = { EGrowthAction.EquipmentRerollOptions };

        [SerializeField] private EquipmentOptionView _view = null;

        private readonly GrowthRequestLifetime _request = new GrowthRequestLifetime();

        private int    _selectedSlot = 0;
        private bool   _wasOpen;
        private bool   _isDirty;
        private string _status = string.Empty;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            TownGrowthClient.SnapshotChanged  += OnSnapshot;
            TownGrowthClient.ResultReceived   += OnResult;
            TownGrowthClient.WriteLockChanged += OnWriteLockChanged;
            if (_view != null)
            {
                _view.SlotSelected    += OnSlotSelected;
                _view.RerollRequested += OnRerollRequested;
            }
        }

        private void OnDisable()
        {
            TownGrowthClient.SnapshotChanged  -= OnSnapshot;
            TownGrowthClient.ResultReceived   -= OnResult;
            TownGrowthClient.WriteLockChanged -= OnWriteLockChanged;
            if (_view != null)
            {
                _view.SlotSelected    -= OnSlotSelected;
                _view.RerollRequested -= OnRerollRequested;
            }
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
                // 결과를 기다리는 요청이 있으면 상태 글자를 지우지 않는다
                if (!_request.IsActive)
                {
                    _status = string.Empty;
                }

                if (ClientRoles.IsTown())
                {
                    TownGrowthClient.RequestSnapshot();
                }

                _isDirty = true;
            }

            _wasOpen = isOpen;
            TickRequest();
            if ((isOpen) && (_isDirty))
            {
                _isDirty = false;
                Apply();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        // 창이 닫혀 있어도 매 프레임 돈다. 다시 접속해 공용 잠금이 비면 이전 요청을 놓고 서버 상태를 새로 읽어,
        // 이전 연결의 결과 불명이 이 창을 영구히 막지 않게 한다. 결과는 성공이나 실패로 단정하지 않는다
        private void TickRequest()
        {
            if (_request.DropIfLockLost(TownGrowthClient.HasUnresolvedWrite, TownGrowthClient.UnresolvedOperationId))
            {
                _status  = string.Empty;
                _isDirty = true;
                if (ClientRoles.IsTown())
                {
                    TownGrowthClient.RequestSnapshot();
                }
            }

            if (_request.Expire(Time.unscaledTime))
            {
                _status  = UNKNOWN_TEXT;
                _isDirty = true;
            }
        }

        private void OnSnapshot(GrowthSnapshotMessage snapshot)
        {
            _isDirty = true;
        }

        private void OnWriteLockChanged()
        {
            _isDirty = true;
        }

        private void OnSlotSelected(int slot)
        {
            if ((slot < 0) || (slot >= Constants.Combat.EQUIPMENT_SLOT_COUNT))
            {
                return;
            }

            _selectedSlot = slot;
            if (!_request.IsActive)
            {
                _status = string.Empty;
            }

            _isDirty = true;
        }

        private void OnRerollRequested()
        {
            if (_request.IsOutcomeUnknown)
            {
                // 같은 요청을 같은 번호로 다시 보낸다. 서버와 API가 처음 결과를 돌려주고 다시 뽑거나 차감하지 않는다.
                // 공용 보관 칸이 이 창의 요청을 들고 있을 때만 보낸다. 아니면 다른 창의 요청을 다시 보내게 된다
                if ((_request.CanRetry(TownGrowthClient.HasUnresolvedWrite, TownGrowthClient.IsUnresolvedOutcomeUnknown,
                    TownGrowthClient.UnresolvedAction, TownGrowthClient.UnresolvedOperationId, G_OwnActions))
                    && (TownGrowthClient.RetryLast()))
                {
                    _request.MarkRetrying(Time.unscaledTime, Constants.Network.API_TIMEOUT_SECONDS);
                    _status  = CHECKING_TEXT;
                    _isDirty = true;
                }

                return;
            }

            if ((_request.IsActive) || (!TownGrowthClient.HasSnapshot) || (!ClientRoles.IsTown())
                || (GrowthWriteGuard.IsBlockedByOther(_request.OperationId)))
            {
                return;
            }

            GrowthSnapshotMessage snapshot = TownGrowthClient.Snapshot;
            if ((!EquipmentOptionViewRules.TryFindEquipped(snapshot.Equipment, _selectedSlot, out TownEquipmentEntry entry))
                || (!EquipmentOptionViewRules.CanReroll(in entry)))
            {
                return;
            }

            Guid operationId = TownGrowthClient.RerollEquipmentOptions(entry.ItemId);
            if (!_request.Begin(operationId, EGrowthAction.EquipmentRerollOptions, Time.unscaledTime,
                Constants.Network.API_TIMEOUT_SECONDS))
            {
                return;
            }

            _status  = WAITING_TEXT;
            _isDirty = true;
        }

        private void OnResult(GrowthResultMessage result)
        {
            EGrowthRequestResolve resolve = _request.Resolve(result.OperationId, result.Reason, out _);
            if (resolve == EGrowthRequestResolve.Ignored)
            {
                return;
            }

            if (resolve == EGrowthRequestResolve.Unknown)
            {
                // 확정도 확정 거절도 아니다. 요청을 붙잡아 두고 같은 번호로 다시 확인하게 한다
                _status = UNKNOWN_TEXT;
            }
            else if (result.IsAccepted)
            {
                _status = DONE_TEXT;
            }
            else
            {
                string text = RejectText.Describe(result.Reason);
                _status = string.IsNullOrEmpty(text) ? FAILED_TEXT : text;
            }

            _isDirty = true;
        }

        private void Apply()
        {
            _view.SetSelectedSlot(_selectedSlot);
            if ((!TownGrowthClient.HasSnapshot) || (!GameDB.IsInitialized))
            {
                ShowEmpty(string.Empty);
                return;
            }

            GrowthSnapshotMessage snapshot  = TownGrowthClient.Snapshot;
            EquipmentCatalogSO    equipment = GameDB.Equipment;
            for (int slot = 0; slot < Constants.Combat.EQUIPMENT_SLOT_COUNT; ++slot)
            {
                bool hasItem = EquipmentOptionViewRules.TryFindEquipped(snapshot.Equipment, slot, out TownEquipmentEntry worn);
                string displayName = hasItem ? EquipmentEnhanceRules.DisplayName(equipment, worn.DefinitionId) : string.Empty;
                _view.SetSlot(slot, hasItem ? displayName + "\n+" + worn.Enhancement
                    : EquipmentOptionViewRules.SlotName(slot) + "\n착용 장비 없음");
            }

            _view.SetCost(EquipmentOptionViewRules.CostText(equipment.OptionRerollEssenceCost, equipment.OptionRerollShardCost,
                snapshot.Essence, snapshot.Shards));
            if (!EquipmentOptionViewRules.TryFindEquipped(snapshot.Equipment, _selectedSlot, out TownEquipmentEntry entry))
            {
                ShowEmpty(NO_ITEM_TEXT);
                return;
            }

            _view.SetItem(EquipmentEnhanceRules.DisplayName(equipment, entry.DefinitionId));
            ApplyLine(equipment, in entry, 0, entry.Option1Kind, entry.Option1Value);
            ApplyLine(equipment, in entry, 1, entry.Option2Kind, entry.Option2Value);

            int[] pity   = snapshot.OptionPity;
            int   misses = ((pity != null) && (_selectedSlot < pity.Length)) ? pity[_selectedSlot] : 0;
            _view.SetPity(EquipmentOptionViewRules.PityText(misses, equipment.OptionPityThreshold));
            ApplyReroll(equipment, in snapshot, in entry);
        }

        private void ApplyLine(EquipmentCatalogSO equipment, in TownEquipmentEntry entry, int line, int kind, int value)
        {
            bool hasRange = equipment.TryGetOptionRange(kind, (EItemGrade)entry.Grade, out int min, out int max);
            bool isHigh   = (hasRange)
                && (EquipmentOptionViewRules.IsHighTier(kind, value, min, max, equipment.OptionHighTierBasisPoints));
            _view.SetLine(line, EquipmentOptionViewRules.LineText(kind, value),
                hasRange ? EquipmentOptionViewRules.RangeText(min, max) : string.Empty, isHigh);
        }

        // 버튼 상태는 한곳에서 정한다. 결과 불명이면 확인만, 기다리는 중이면 잠금, 그 밖에는 조건을 본다
        private void ApplyReroll(EquipmentCatalogSO equipment, in GrowthSnapshotMessage snapshot, in TownEquipmentEntry entry)
        {
            if (_request.IsOutcomeUnknown)
            {
                _view.SetStatus(_status);
                _view.SetReroll(CONFIRM_TEXT, true);
                return;
            }

            if (_request.IsActive)
            {
                _view.SetStatus(_status);
                _view.SetReroll(REROLL_TEXT, false);
                return;
            }

            string blocked = string.Empty;
            if (!ClientRoles.IsTown())
            {
                blocked = TOWN_ONLY_TEXT;
            }
            else if (GrowthWriteGuard.IsBlockedByOther(_request.OperationId))
            {
                blocked = GrowthWriteGuard.OTHER_WINDOW_TEXT;
            }
            else if (!EquipmentOptionViewRules.CanReroll(in entry))
            {
                blocked = NOT_ALLOWED_TEXT;
            }
            else if (!EquipmentOptionViewRules.CanAfford(equipment.OptionRerollEssenceCost, equipment.OptionRerollShardCost,
                snapshot.Essence, snapshot.Shards))
            {
                blocked = NOT_ENOUGH_TEXT;
            }

            bool allowed = string.IsNullOrEmpty(blocked);
            _view.SetStatus(allowed ? _status : blocked);
            _view.SetReroll(REROLL_TEXT, allowed);
        }

        private void ShowEmpty(string status)
        {
            _view.SetItem(string.Empty);
            _view.SetLine(0, string.Empty, string.Empty, false);
            _view.SetLine(1, string.Empty, string.Empty, false);
            _view.SetPity(string.Empty);
            bool isUnknown = _request.IsOutcomeUnknown;
            _view.SetStatus(isUnknown ? _status : status);
            _view.SetReroll(isUnknown ? CONFIRM_TEXT : REROLL_TEXT, isUnknown);
        }
    }
}
