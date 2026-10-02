// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 아이템 하나의 생성부터 현재까지 이력. seq 오름차순이며 이력이 없는 ID는 404다 </summary>
    [DataContract]
    public sealed class AdminItemHistoryResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "itemId")]  private string                  _itemId;
        [DataMember(Name = "entries")] private AdminItemHistoryEntry[] _entries;

        /****************************************
        *              Properties
        ****************************************/

        public string                  ItemId  => _itemId;
        public AdminItemHistoryEntry[] Entries => _entries;

        /****************************************
        *                Methods
        ****************************************/

        public AdminItemHistoryResponse(string itemId, AdminItemHistoryEntry[] entries)
        {
            _itemId  = itemId;
            _entries = entries;
        }

        private AdminItemHistoryResponse() { }
    }
}
