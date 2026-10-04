// System
using System.Collections.Generic;

// 프로젝트
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary>결과 화면이 표시할 값이며 서버가 확정한 정산 결과를 화면용으로 옮긴 사본이다</summary>
    public sealed class ResultScreenData
    {
        public ESessionEndReason          Reason;
        public string                     RegionName;
        public int                        BaseEssence;
        public int                        ExtraEssence;

        // 실패나 포기로 잃은 추가 정기. 0이면 손실 줄을 숨긴다
        public int                        LostEssence;
        public List<ItemSlotData>         ConfirmedItems = new List<ItemSlotData>();
        public List<ItemSlotData>         ChanceItems    = new List<ItemSlotData>();
    }
}
