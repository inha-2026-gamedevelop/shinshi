// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 지급 보상 한 줄. Currency, Tickets, Essence, Shards는 assetId, bindingKind를 비우고 AccountItem은 둘 다 채운다 </summary>
    [DataContract]
    public sealed class AdminGrantReward
    {
        /****************************************
        *                Fields
        ****************************************/

        // AdminGrantCodes.ASSET_*
        [DataMember(Name = "assetKind")]   private string _assetKind;
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

        public AdminGrantReward(string assetKind, string assetId, string bindingKind, long quantity)
        {
            _assetKind   = assetKind;
            _assetId     = assetId;
            _bindingKind = bindingKind;
            _quantity    = quantity;
        }

        private AdminGrantReward() { }
    }
}
