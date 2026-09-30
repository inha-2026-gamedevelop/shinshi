namespace Shinshi.Common
{
    /// <summary> 서비스 장애 취소 때 정산 전 추가 정기를 어떻게 처리할지, 기획 결정 전까지 Undecided다 </summary>
    public enum EExtraEssencePolicy
    {
        Undecided = 0,
        HalfLoss  = 1,
        FullGrant = 2,
        NoGrant   = 3
    }
}
