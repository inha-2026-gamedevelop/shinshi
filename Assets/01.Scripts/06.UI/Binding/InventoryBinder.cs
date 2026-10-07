// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 인벤토리 창을 열 때 마을 서버에 성장, 생활 스냅숏을 요청하고 받은 값으로 칸과 재화를 채운다 </summary>
    // 창이 닫혀 있으면 스냅숏이 바뀌어도 다시 만들지 않고, 다음에 열 때 한 번 채운다
    public sealed class InventoryBinder : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int INITIAL_ITEM_CAPACITY = 64;

        [SerializeField] private InventoryView _view = null;

        private readonly List<InventoryItemData> _items = new List<InventoryItemData>(INITIAL_ITEM_CAPACITY);

        private bool _wasOpen = false;
        private bool _isDirty = true;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            TownGrowthClient.SnapshotChanged += OnGrowthSnapshot;
            TownLifeClient.SnapshotChanged   += OnLifeSnapshot;
            _isDirty = true;
        }

        private void OnDisable()
        {
            TownGrowthClient.SnapshotChanged -= OnGrowthSnapshot;
            TownLifeClient.SnapshotChanged   -= OnLifeSnapshot;
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
                RequestSnapshots();
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

        // 열 때마다 최신 값을 묻는다. 던전에서는 마을 클라이언트가 없어 요청하지 않고 마지막 값을 보여 준다
        private static void RequestSnapshots()
        {
            if (!ClientRoles.IsTown())
            {
                return;
            }

            TownGrowthClient.RequestSnapshot();
            TownLifeClient.RequestSnapshot();
        }

        private void Apply()
        {
            _isDirty = false;
            bool initialized = GameDB.IsInitialized;
            GrowthSnapshotMessage growth = TownGrowthClient.Snapshot;
            LifeSnapshotMessage   life   = TownLifeClient.Snapshot;
            InventoryItemRules.Build(TownGrowthClient.HasSnapshot, in growth, TownLifeClient.HasSnapshot, in life,
                initialized ? GameDB.Rewards : null, initialized ? GameDB.Equipment : null, initialized ? GameDB.Life : null,
                _items, out long yeopjeon, out long essence);

            int capacity = (TownGrowthClient.HasSnapshot) ? growth.StorageCapacity : 0;
            _view.SetItems(_items, Mathf.Max(capacity, _items.Count));
            _view.SetCurrency(yeopjeon, essence);
        }

        private void OnGrowthSnapshot(GrowthSnapshotMessage snapshot)
        {
            _isDirty = true;
        }

        private void OnLifeSnapshot(LifeSnapshotMessage snapshot)
        {
            _isDirty = true;
        }
    }
}
