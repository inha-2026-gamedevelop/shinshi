namespace Shinshi.Launcher.Presentation
{
    /// <summary> 본문 영역에 보이는 화면 </summary>
    public enum ELauncherView
    {
        Home,
        News,
        PatchNotes,
        Settings,
        Login,

        // 소식 카드 하나의 본문. 뒤로 가면 들어온 탭(Home, News, PatchNotes)으로 돌아간다
        NewsDetail
    }
}
