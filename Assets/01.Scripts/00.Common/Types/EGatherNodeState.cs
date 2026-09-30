namespace Shinshi.Common
{
    /// <summary> 채집 노드 상태 </summary>
    public enum EGatherNodeState
    {
        Available  = 0,
        Claimed    = 1,
        Completing = 2,
        Depleted   = 3
    }
}
