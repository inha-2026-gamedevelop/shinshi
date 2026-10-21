namespace Shinshi.Launcher.Account
{
    internal sealed class RefreshTokenDocument
    {
        /****************************************
        *              Properties
        ****************************************/

        public string Token { get; set; } = string.Empty;
        public string? RequestId { get; set; }
    }
}
