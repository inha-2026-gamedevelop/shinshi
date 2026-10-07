// Unity
using UnityEngine;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 마을 서버가 내려준 생활 스냅숏으로 퀵슬롯의 소형, 대형 물약 개수를 채운다 </summary>
    // 마을에 접속한 뒤 한 번 스냅숏을 요청하고 이후에는 스냅숏이 바뀔 때마다 갱신한다. 던전에서는 요청하지 않고 마지막 값을 보여 준다
    public sealed class QuickSlotBinder : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SMALL_FORMAT = "소형 {0}";
        private const string LARGE_FORMAT = "대형 {0}";

        [SerializeField] private TMP_Text _smallCountLabel = null;
        [SerializeField] private TMP_Text _largeCountLabel = null;

        private bool _requested = false;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            TownLifeClient.SnapshotChanged += OnSnapshot;
            _requested = false;
            TryRequest();
            Refresh();
        }

        private void OnDisable()
        {
            TownLifeClient.SnapshotChanged -= OnSnapshot;
        }

        // 켜질 때 아직 접속 전일 수 있어, 마을에 접속하는 즉시 한 번 요청한다
        private void Update()
        {
            if (!_requested)
            {
                TryRequest();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void TryRequest()
        {
            if (!ClientRoles.IsTown())
            {
                return;
            }

            _requested = true;
            TownLifeClient.RequestSnapshot();
        }

        private void OnSnapshot(LifeSnapshotMessage snapshot)
        {
            Refresh();
        }

        private void Refresh()
        {
            bool known = (TownLifeClient.HasSnapshot) && (GameDB.IsInitialized);
            long small = 0;
            long large = 0;
            if (known)
            {
                TownItemEntry[] potions = TownLifeClient.Snapshot.Potions;
                small = QuickSlotRules.CountAvailable(potions, DefinitionIdOf(EPotionSize.Small));
                large = QuickSlotRules.CountAvailable(potions, DefinitionIdOf(EPotionSize.Large));
            }

            SetText(_smallCountLabel, string.Format(SMALL_FORMAT, QuickSlotRules.Format(known, small)));
            SetText(_largeCountLabel, string.Format(LARGE_FORMAT, QuickSlotRules.Format(known, large)));
        }

        private static string DefinitionIdOf(EPotionSize size)
        {
            LifeConfigSO.PotionDefinition definition = GameDB.Life.GetPotion(size);
            return (definition != null) ? definition.DefinitionId : null;
        }

        private static void SetText(TMP_Text label, string text)
        {
            if (label != null)
            {
                label.text = text;
            }
        }
    }
}
