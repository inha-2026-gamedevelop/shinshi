// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.Admin.Web.Models
{
    /// <summary> 지급 작성 폼의 보상 한 줄. 확인 화면을 만들 때 계약 DTO로 복사한다 </summary>
    public sealed class GrantRewardInput
    {
        /****************************************
        *              Properties
        ****************************************/

        public string AssetKind { get; set; } = AdminGrantCodes.ASSET_CURRENCY;
        public string AssetId { get; set; } = string.Empty;
        public long Quantity { get; set; } = 1;
    }
}