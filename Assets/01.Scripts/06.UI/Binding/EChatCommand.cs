namespace Shinshi.UI
{
    /// <summary> 채팅 입력 줄의 명령 종류. None은 명령이 아닌 일반 채팅이다 </summary>
    public enum EChatCommand
    {
        None    = 0,

        // /w 이름 내용, /귓 이름 내용
        Whisper = 1,

        // /r 내용, 마지막으로 귓속말을 보내 온 상대에게 답장
        Reply   = 2,

        // 귓속말 명령인데 대상 이름이 없다
        Invalid = 3
    }
}
