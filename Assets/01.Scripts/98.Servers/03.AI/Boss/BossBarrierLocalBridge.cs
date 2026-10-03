// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 실제 구성에서는 복제 계층이 맡을 일을 한 프로세스 안에서 직접 부른다. 표현이 서버 상태를 되쓰지 않는다
    /// <summary> 결계 서버 알림을 표현 호출로 바꾸는 시험용 연결부, 받은 알림 횟수도 센다 </summary>
    public sealed class BossBarrierLocalBridge : IBossBarrierListener
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly BossBarrierView _view;

        /****************************************
        *              Properties
        ****************************************/

        public int StartedCount   { get; private set; }
        public int ActivatedCount { get; private set; }
        public int CompletedCount { get; private set; }
        public int ClearedCount   { get; private set; }

        /****************************************
        *                Methods
        ****************************************/

        public BossBarrierLocalBridge(BossBarrierView view)
        {
            _view = view;
        }

        public void OnBarrierStarted(in BossBarrierStart start)
        {
            ++StartedCount;
            _view.Begin(in start);
        }

        public void OnBarrierActivated(int barrierId, double activateServerTime)
        {
            ++ActivatedCount;
        }

        public void OnBarrierCompleted(int barrierId, double completeServerTime)
        {
            ++CompletedCount;
        }

        public void OnBarrierCleared(int barrierId, EBossInterruptReason reason)
        {
            ++ClearedCount;
            _view.Clear();
        }
    }
}
