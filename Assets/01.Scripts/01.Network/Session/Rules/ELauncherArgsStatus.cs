namespace Shinshi.Network
{
    /// <summary> 실행 인자에서 런처 파이프 인자를 읽은 결과 </summary>
    public enum ELauncherArgsStatus
    {
        // 두 인자가 모두 없다, 런처 없이 실행했다
        Absent,
        Valid,

        // 하나만 있거나 값이 형식에 맞지 않는다
        Invalid
    }
}
