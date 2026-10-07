namespace Shinshi.Launcher.Presentation
{
    /// <summary> 소식 목록을 서비스 상태에서 받은 진행 상태 </summary>
    public enum ENewsLoadState
    {
        // 아직 받은 적이 없다
        Idle,
        Loading,
        Loaded,

        // 마지막 요청이 실패했다. 이전에 받은 목록이 있으면 그대로 보인다
        Failed
    }
}
