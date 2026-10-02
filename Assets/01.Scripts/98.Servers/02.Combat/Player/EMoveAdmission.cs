namespace Shinshi.Combat
{
    /// <summary> 이동 목적지 사전 검사 결과 </summary>
    public enum EMoveAdmission
    {
        Accepted        = 0,

        // 현재 위치에서 목적지 거리 상한을 넘었다
        TooFar          = 1,

        // 진행 중인 경로와 같은 목적지라 다시 계산하지 않는다
        SameDestination = 2
    }
}
