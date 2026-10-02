namespace Shinshi.AI
{
    /// <summary> 자식을 차례로 평가하고 하나라도 실패하면 실패한다 </summary>
    public sealed class BtSequence : BtNode
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly BtNode[] _children;

        /****************************************
        *                Methods
        ****************************************/

        public BtSequence(params BtNode[] children)
        {
            _children = children;
        }

        public override EBtStatus Tick(BossBlackboard blackboard)
        {
            for (int i = 0; i < _children.Length; ++i)
            {
                if (_children[i].Tick(blackboard) == EBtStatus.Failure)
                {
                    return EBtStatus.Failure;
                }
            }

            return EBtStatus.Success;
        }
    }
}
