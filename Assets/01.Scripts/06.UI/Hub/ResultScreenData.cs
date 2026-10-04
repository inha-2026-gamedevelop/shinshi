// System
using System.Collections.Generic;

// 프로젝트
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary>결과 화면이 표시할 값이며 서버가 확정한 정산 결과를 화면용으로 옮긴 사본이다</summary>
    public sealed class ResultScreenData
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly ESessionEndReason           _reason;
        private readonly string                      _regionName;
        private readonly int                         _baseEssence;
        private readonly int                         _extraEssence;
        private readonly int                         _lostEssence;
        private readonly IReadOnlyList<ItemSlotData> _confirmedItems;
        private readonly IReadOnlyList<ItemSlotData> _chanceItems;

        /****************************************
        *              Properties
        ****************************************/

        public ESessionEndReason           Reason         => _reason;
        public string                      RegionName     => _regionName;
        public int                         BaseEssence    => _baseEssence;
        public int                         ExtraEssence   => _extraEssence;
        public IReadOnlyList<ItemSlotData> ConfirmedItems => _confirmedItems;
        public IReadOnlyList<ItemSlotData> ChanceItems    => _chanceItems;

        // 실패나 포기로 잃은 추가 정기. 0이면 손실 줄을 숨긴다
        public int LostEssence => _lostEssence;

        /****************************************
        *                Methods
        ****************************************/

        public ResultScreenData(ESessionEndReason reason, string regionName, int baseEssence, int extraEssence,
            int lostEssence, IReadOnlyList<ItemSlotData> confirmedItems, IReadOnlyList<ItemSlotData> chanceItems)
        {
            _reason         = reason;
            _regionName     = regionName;
            _baseEssence    = baseEssence;
            _extraEssence   = extraEssence;
            _lostEssence    = lostEssence;
            _confirmedItems = confirmedItems;
            _chanceItems    = chanceItems;
        }
    }
}
