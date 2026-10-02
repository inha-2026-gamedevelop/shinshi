// System
using System.Runtime.Serialization;

namespace Shinshi.Patch
{
    /// <summary> 서명 봉투 JSON, payload는 base64 원본 바이트라 정규화 없이 서명을 확인한다 </summary>
    [DataContract]
    public sealed class SignedEnvelope
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "formatVersion")] private int     _formatVersion;
        [DataMember(Name = "kind")]          private string? _kind;
        [DataMember(Name = "keyId")]         private string? _keyId;
        [DataMember(Name = "payload")]       private string? _payload;
        [DataMember(Name = "signature")]     private string? _signature;

        /****************************************
        *              Properties
        ****************************************/

        public int     FormatVersion => _formatVersion;
        public string? Kind          => _kind;
        public string? KeyId         => _keyId;
        public string? Payload       => _payload;
        public string? Signature     => _signature;

        /****************************************
        *                Methods
        ****************************************/

        public SignedEnvelope(int formatVersion, string kind, string keyId, string payload, string signature)
        {
            _formatVersion = formatVersion;
            _kind          = kind;
            _keyId         = keyId;
            _payload       = payload;
            _signature     = signature;
        }

        private SignedEnvelope() { }
    }
}
