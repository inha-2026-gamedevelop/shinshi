namespace Shinshi.Network
{
    /// <summary> 채팅 채널, 메시지에 번호로 실리므로 값을 바꾸지 않고 끝에 붙인다 </summary>
    public enum EChatChannel
    {
        // 같은 서버(마을 또는 던전 세션)에 접속한 모두
        All    = 0,

        // 마을은 같은 파티 방, 던전은 같은 세션 참가자
        Party  = 1,

        // 서버와 클라이언트가 본인에게만 보이는 안내, 클라이언트가 이 채널로 보낼 수 없다
        System = 2
    }
}
