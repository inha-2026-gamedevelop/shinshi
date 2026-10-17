namespace Shinshi.AI
{
    /// <summary> 받은 2페이즈 전환 사건을 어떻게 처리했는가 </summary>
    public enum EWolbaekPhase2TransitionDecision
    {
        Invalid      = 0, // 전환 번호가 없거나 종료가 시작보다 이르다
        OtherSession = 1, // 지금 연결한 전투가 아니다(지난 세션, 다른 보스)
        Duplicate    = 2, // 이미 받은 전환 번호이거나 그보다 오래된 번호다
        TooEarly     = 3, // 서버 시작 시각이 시계 오차로 볼 수 있는 한계보다 미래다
        Ended        = 4, // 받은 순간 서버 구간이 이미 끝났다, 처음부터 재생하지 않는다
        Scheduled    = 5, // 서버 시작 시각까지 기다렸다 재생한다
        Play         = 6  // 지금 재생한다, 늦게 받았으면 서버 경과 시간부터 재생한다
    }
}
