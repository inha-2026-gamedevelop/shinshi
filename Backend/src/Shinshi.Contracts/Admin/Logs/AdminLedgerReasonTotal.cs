// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 같은 필터의 사유, 자산별 원장 합계. 서로 다른 자산의 delta를 더하지 않도록 자산 키까지 묶는다.
    /// deltaTotal은 bigint 합이 long을 넘을 수 있어 10진 문자열이다 </summary>
    [DataContract]
    public sealed class AdminLedgerReasonTotal
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "reason")]      private int    _reason;
        [DataMember(Name = "assetKind")]   private string _assetKind;
        [DataMember(Name = "assetId")]     private string _assetId;
        [DataMember(Name = "bindingKind")] private string _bindingKind;
        [DataMember(Name = "deltaTotal")]  private string _deltaTotal;
        [DataMember(Name = "count")]       private long   _count;

        /****************************************
        *              Properties
        ****************************************/

        public int    Reason      => _reason;
        public string AssetKind   => _assetKind;
        public string AssetId     => _assetId;
        public string BindingKind => _bindingKind;
        public string DeltaTotal  => _deltaTotal;
        public long   Count       => _count;

        /****************************************
        *                Methods
        ****************************************/

        public AdminLedgerReasonTotal(int reason, string assetKind, string assetId, string bindingKind, string deltaTotal,
            long count)
        {
            _reason      = reason;
            _assetKind   = assetKind;
            _assetId     = assetId;
            _bindingKind = bindingKind;
            _deltaTotal  = deltaTotal;
            _count       = count;
        }

        private AdminLedgerReasonTotal() { }
    }
}
