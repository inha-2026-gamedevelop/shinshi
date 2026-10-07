namespace Shinshi.Network
{
    /// <summary> Committing 상태 안의 진행 단계 </summary>
    public enum ETradeCommitPhase
    {
        None      = 0,

        // prepare를 보냈거나 결과를 몰라 같은 operationId로 다시 보내는 중
        Preparing = 1,

        // commit을 보내고 응답을 기다리는 중
        Committing = 2,

        // commit 응답이 없어 resolve로 결과를 묻는 중
        Resolving = 3
    }
}
