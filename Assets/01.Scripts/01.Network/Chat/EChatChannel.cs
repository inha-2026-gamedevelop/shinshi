namespace Shinshi.Network
{
    /// <summary> 채팅 채널, 메시지에 번호로 실리므로 값을 바꾸지 않고 끝에 붙인다 </summary>
    public enum EChatChannel
    {
        // 같은 서버(마을 또는 던전 세션)에 접속한 모두
        All         = 0,

        // 마을은 같은 파티 방, 던전은 같은 세션 참가자
        Party       = 1,

        // 서버와 클라이언트가 본인에게만 보이는 안내, 클라이언트가 이 채널로 보낼 수 없다
        System      = 2,

        // 같은 길드원. 마을과 던전 어디에 있든 백엔드 전달함을 거쳐 받는다
        Guild       = 3,

        // 받은 귓속말, Sender가 보낸 사람이다. 클라이언트는 ChatWhisperRequestMessage로만 보낸다
        Whisper     = 4,

        // 보낸 귓속말의 확인 줄. 서버가 보낸 사람에게만 돌려주며 Sender는 받는 사람 이름이다
        WhisperSent = 5
    }
}
