namespace Shinshi.AI
{
    // 서버 사건에 패턴 번호가 실리지 않는 월백 2페이즈 패턴은 사건의 종류가 패턴을 정한다
    // 이지 2P P01은 회전 별, P02는 평행 빔 둘, P03은 반월 휩쓸기이고 하드 2P P01~P06은 단계 별, 격자, 잔류 부채꼴, 추적 장판, 난사, 원형 폭발이다
    /// <summary> 패턴 번호 없이 사건 종류로 모션을 정하는 월백 2페이즈 패턴 </summary>
    public enum EWolbaekEventMotion
    {
        None          = 0,
        StarRotating  = 1,
        BeamPair      = 2,
        SweepHalfMoon = 3,
        StarStage     = 4,
        GridBeam      = 5,
        ChainFan      = 6,
        TrackingArea  = 7,
        Barrage       = 8,
        CircleBurst   = 9
    }
}
