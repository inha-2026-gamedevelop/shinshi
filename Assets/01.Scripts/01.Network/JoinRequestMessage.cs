// 서드파티
using Mirror;

namespace Shinshi.Network
{
    /// <summary> 클라이언트가 연결 직후 한 번 보내는 인증 요청, 서버 인증기가 받기 전에는 스폰과 입력이 없다 </summary>
    public readonly struct JoinRequestMessage : NetworkMessage
    {
        // 직렬화는 JoinMessageSerializer가 한다. 필드를 더하거나 순서를 바꾸면 그쪽도 같이 고치고 PROTOCOL_VERSION을 올린다

        /****************************************
        *                Fields
        ****************************************/

        private readonly int    _protocolVersion;
        private readonly string _contentHash;
        private readonly string _joinToken;

        /****************************************
        *              Properties
        ****************************************/

        public int ProtocolVersion => _protocolVersion;

        // GameDB 콘텐츠 해시(SHA256 소문자 16진수). 서버와 다르면 데이터가 달라 판정이 어긋난다
        public string ContentHash => _contentHash;

        // 백엔드가 발급한 일회용 접속 토큰. 장기 로그인 토큰을 보내지 않는다
        public string JoinToken => _joinToken;

        /****************************************
        *                Methods
        ****************************************/

        public JoinRequestMessage(int protocolVersion, string contentHash, string joinToken)
        {
            _protocolVersion = protocolVersion;
            _contentHash     = contentHash;
            _joinToken       = joinToken;
        }
    }
}
