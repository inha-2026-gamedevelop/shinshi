namespace Shinshi.Network
{
    /// <summary> 빌드 신원 파일을 읽은 결과 </summary>
    public enum EBuildIdentityStatus
    {
        Loaded,

        // 에디터는 빌드 후처리가 쓰는 파일이 없어 읽지 않는다
        EditorSkipped,

        Missing,

        // 읽기 실패, JSON 형식 오류, 필수 값 누락
        Corrupt,

        // 파일이 다른 빌드의 것이다. 실행 중인 코드, 데이터와 대조해 어긋났다
        ProtocolMismatch,
        ContentHashMismatch
    }
}
