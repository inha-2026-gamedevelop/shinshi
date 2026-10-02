// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 우편 하나에 든 자산 한 줄. 같은 우편 안에서 assetKind, assetId, bindingKind 조합은 겹치지 않는다 </summary>
    [DataContract]
    public sealed class InboxReward
    {
        /****************************************
        *                Fields
        ****************************************/

        // Currency, Tickets는 계정 지갑, Essence, Shards는 수령할 캐릭터, AccountItem은 계정 아이템이다
        [DataMember(Name = "assetKind")]   private string _assetKind;

        // AccountItem일 때만 아이템 정의 ID와 귀속이 있고, 나머지 종류는 빈 문자열이다
        [DataMember(Name = "assetId")]     private string _assetId;
        [DataMember(Name = "bindingKind")] private string _bindingKind;
        [DataMember(Name = "quantity")]    private long   _quantity;

        /****************************************
        *              Properties
        ****************************************/

        public string AssetKind   => _assetKind;
        public string AssetId     => _assetId;
        public string BindingKind => _bindingKind;
        public long   Quantity    => _quantity;

        /****************************************
        *                Methods
        ****************************************/

        public InboxReward(string assetKind, string assetId, string bindingKind, long quantity)
        {
            _assetKind   = assetKind;
            _assetId     = assetId;
            _bindingKind = bindingKind;
            _quantity    = quantity;
        }

        private InboxReward() { }
    }
}
