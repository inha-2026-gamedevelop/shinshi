namespace Shinshi.AI
{
    // 경량 BT는 한 틱 안에서 끝나는 노드만 다룬다, 여러 틱에 걸치는 Running은 두지 않았다
    /// <summary> BT 노드의 평가 결과 </summary>
    public enum EBtStatus
    {
        Success = 0,
        Failure = 1
    }
}
