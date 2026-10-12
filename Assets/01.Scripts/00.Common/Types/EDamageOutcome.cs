namespace Shinshi.Common
{
    /// <summary> 피해 요청 한 건의 처리 결과, Applied 외에는 체력이 변하지 않는다 </summary>
    public enum EDamageOutcome
    {
        // 정상 피격, 후방이나 방어 불가로 반격 방어를 통과한 경우 포함
        Applied        = 0,

        // 전방 반격 방어 창이 막았다
        Guarded        = 1,

        // 같은 동작이 같은 대상을 이미 맞혔다
        DuplicateHit   = 2,

        // 대상이 없거나 전장에서 제거되었다
        TargetNotFound = 3,

        // 대상이 사망, 부활 대기, 관전 상태다
        TargetNotAlive = 4,

        // 공격자와 대상의 소속이 같다
        SameFaction    = 5,

        // 대상이 피해를 받지 않는 구간이다. 공명도 주지 않는다
        Invulnerable   = 6,

        // 서버 내부 요청이나 대상 상태의 수치가 유효하지 않다
        InvalidRequest = 7
    }
}
