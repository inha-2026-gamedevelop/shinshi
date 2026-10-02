// System
using System.Runtime.Serialization;

namespace Shinshi.Patch
{
    /// <summary> 채널이 지금 가리키는 릴리스, 채널 봉투의 payload </summary>
    [DataContract]
    public sealed class ChannelPointer
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "formatVersion")]     private int     _formatVersion;
        [DataMember(Name = "product")]           private string? _product;
        [DataMember(Name = "channel")]           private string? _channel;
        [DataMember(Name = "releaseId")]         private string? _releaseId;

        // 게시마다 늘어난다. 런처는 마지막으로 본 값보다 작은 포인터를 거절해 옛 릴리스로 되돌리는 공격을 막는다
        [DataMember(Name = "sequence")]          private long    _sequence;

        // 매니페스트 봉투 payload 바이트의 SHA-256, 포인터와 매니페스트를 한 쌍으로 묶는다
        [DataMember(Name = "manifestSha256")]    private string? _manifestSha256;
        [DataMember(Name = "publishedAtUnixMs")] private long    _publishedAtUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public int    FormatVersion     => _formatVersion;
        public string Product           => _product ?? string.Empty;
        public string Channel           => _channel ?? string.Empty;
        public string ReleaseId         => _releaseId ?? string.Empty;
        public long   Sequence          => _sequence;
        public string ManifestSha256    => _manifestSha256 ?? string.Empty;
        public long   PublishedAtUnixMs => _publishedAtUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public ChannelPointer(string product, string channel, string releaseId, long sequence, string manifestSha256,
            long publishedAtUnixMs)
        {
            _formatVersion     = PatchFormat.FORMAT_VERSION;
            _product           = product;
            _channel           = channel;
            _releaseId         = releaseId;
            _sequence          = sequence;
            _manifestSha256    = manifestSha256;
            _publishedAtUnixMs = publishedAtUnixMs;
        }

        private ChannelPointer() { }
    }
}
