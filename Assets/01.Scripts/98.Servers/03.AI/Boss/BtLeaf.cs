// System
using System;

namespace Shinshi.AI
{
    /// <summary> 조건이나 행동 하나, 델리게이트는 만들 때 한 번만 넘기고 평가 중에 할당하지 않는다 </summary>
    public sealed class BtLeaf : BtNode
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Func<BossBlackboard, bool> _action;

        /****************************************
        *                Methods
        ****************************************/

        public BtLeaf(Func<BossBlackboard, bool> action)
        {
            _action = action;
        }

        public override EBtStatus Tick(BossBlackboard blackboard)
        {
            return _action(blackboard) ? EBtStatus.Success : EBtStatus.Failure;
        }
    }
}
