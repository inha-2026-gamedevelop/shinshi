// System
using System;

// Third Party
using Newtonsoft.Json;

namespace Shinshi.Data.Admin.Grants
{
    /// <summary> 개인 조정 결과. 감사 행의 after_state에 이 형태 그대로 남기고, 같은 operationId 재전송은 그 행에서 다시 읽는다 </summary>
    public sealed class AdminAdjustmentResult
    {
        /****************************************
        *              Properties
        ****************************************/

        [JsonProperty("assetKind")]      public string  AssetKind      { get; set; } = string.Empty;
        [JsonProperty("assetId")]        public string  AssetId        { get; set; } = string.Empty;
        [JsonProperty("bindingKind")]    public string  BindingKind    { get; set; } = string.Empty;
        [JsonProperty("characterId")]    public Guid?   CharacterId    { get; set; }
        [JsonProperty("requestedDelta")] public long    RequestedDelta { get; set; }

        // 실제 변경량. 가용량이 없어 0이면 원장 행이 없다
        [JsonProperty("appliedDelta")]   public long    AppliedDelta   { get; set; }

        // 차감하지 못한 양(0 이상)
        [JsonProperty("shortfall")]      public long    Shortfall      { get; set; }
        [JsonProperty("balanceBefore")]  public long    BalanceBefore  { get; set; }
        [JsonProperty("balanceAfter")]   public long    BalanceAfter   { get; set; }

        // 차감 시점의 예약분. 증가 조정이면 0으로 둔다(읽지 않았다)
        [JsonProperty("reserved")]       public long    Reserved       { get; set; }
        [JsonProperty("ledgerId")]       public Guid?   LedgerId       { get; set; }
    }
}
