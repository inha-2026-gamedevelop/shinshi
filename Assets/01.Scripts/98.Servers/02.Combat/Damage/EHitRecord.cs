namespace Shinshi.Combat
{
    /// <summary> 적중 기록 시도 결과 </summary>
    public enum EHitRecord
    {
        Recorded  = 0,

        // 같은 동작이 같은 대상을 이미 맞혔다
        Duplicate = 1,

        // 보관 기간 안의 기록이 상한을 채웠다. 정상 전투에서는 나오지 않으며 나오면 상한이나 번호 발급을 의심한다
        Full      = 2
    }
}
