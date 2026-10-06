// 프로젝트
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary>아이템 칸 하나가 표시할 값이며 서버 값을 화면용으로 옮긴 사본이다. 기본값은 빈 칸이다</summary>
    public readonly struct ItemSlotData
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string     _id;
        private readonly EItemGrade _grade;
        private readonly int        _count;
        private readonly string     _title;
        private readonly string     _description;
        private readonly bool       _isLocked;
        private readonly bool       _isEquipped;

        /****************************************
        *              Properties
        ****************************************/

        public string     Id          => _id;
        public EItemGrade Grade       => _grade;
        public int        Count       => _count;
        public string     Title       => _title;
        public string     Description => _description;
        public bool       IsLocked    => _isLocked;
        public bool       IsEquipped  => _isEquipped;
        public bool       IsEmpty     => string.IsNullOrEmpty(_id);

        /****************************************
        *                Methods
        ****************************************/

        public ItemSlotData(string id, EItemGrade grade, int count, string title, string description,
            bool isLocked = false, bool isEquipped = false)
        {
            _id          = id;
            _grade       = grade;
            _count       = count;
            _title       = title;
            _description = description;
            _isLocked    = isLocked;
            _isEquipped  = isEquipped;
        }
    }
}
