// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 보낸 사람은 서버가 인증된 연결의 결합에서 정한다. 대상 이름은 서버가 캐릭터 이름 규칙으로 다시 정규화한다
    /// <summary> 캐릭터 이름으로 보내는 1:1 귓속말 요청. 성공하면 WhisperSent 줄, 거절되면 ChatRejectMessage가 온다 </summary>
    public readonly struct ChatWhisperRequestMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string _targetName;
        private readonly string _text;

        /****************************************
        *              Properties
        ****************************************/

        public string TargetName => _targetName;
        public string Text       => _text;

        /****************************************
        *                Methods
        ****************************************/

        public ChatWhisperRequestMessage(string targetName, string text)
        {
            _targetName = targetName;
            _text       = text;
        }
    }
}
