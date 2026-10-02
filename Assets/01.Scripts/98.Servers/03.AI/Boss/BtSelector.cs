namespace Shinshi.AI
{
    /// <summary> 자식을 차례로 평가하고 처음 성공한 자식에서 멈춘다 </summary>
    public sealed class BtSelector : BtNode
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly BtNode[] _children;

        /****************************************
        *                Methods
        ****************************************/

        public BtSelector(params BtNode[] children)
        {
            _children = children;
        }

        public override EBtStatus Tick(BossBlackboard blackboard)
        {
            for (int i = 0; i < _children.Length; ++i)
            {
                if (_children[i].Tick(blackboard) == EBtStatus.Success)
                {
                    return EBtStatus.Success;
                }
            }

            return EBtStatus.Failure;
        }
    }
}
