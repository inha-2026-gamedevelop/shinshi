// 프로젝트
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary>아이템 칸 하나가 표시할 값이며 서버 값을 화면용으로 옮긴 사본이다</summary>
    public struct ItemSlotData
    {
        public string     Id;
        public EItemGrade Grade;
        public int        Count;
        public string     Title;
        public string     Description;
        public bool       IsLocked;
        public bool       IsEquipped;

        public bool IsEmpty { get { return string.IsNullOrEmpty(Id); } }
    }
}
