namespace Shinshi.AdminCli
{
    /// <summary> 명령줄에서 읽은 자산 하나. AccountItem이 아니면 AssetId, BindingKind는 빈 문자열이다 </summary>
    internal sealed class AssetSpec
    {
        /****************************************
        *              Properties
        ****************************************/

        // AdminGrantCodes.ASSET_* 표기 그대로
        public string Kind { get; }
        public string AssetId { get; }
        public string BindingKind { get; }

        /****************************************
        *                Methods
        ****************************************/

        public AssetSpec(string kind, string assetId, string bindingKind)
        {
            Kind        = kind;
            AssetId     = assetId;
            BindingKind = bindingKind;
        }

        public override string ToString()
        {
            return AssetId.Length == 0 ? Kind : Kind + ":" + AssetId + ":" + BindingKind;
        }
    }
}
