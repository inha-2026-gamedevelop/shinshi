namespace Shinshi.AI
{
    /// <summary> 보스 선택 BT의 노드 기반 </summary>
    public abstract class BtNode
    {
        public abstract EBtStatus Tick(BossBlackboard blackboard);
    }
}
