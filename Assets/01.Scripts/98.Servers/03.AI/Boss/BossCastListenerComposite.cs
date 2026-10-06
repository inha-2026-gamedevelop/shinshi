namespace Shinshi.AI
{
    // BossBrainDependencies.Casts는 하나뿐이라 서버 몸체가 받는 시전 알림과 기믹이 받는 알림을 함께 쓰려면 묶음이 필요하다
    /// <summary> 시전 알림을 여러 수신자에게 순서대로 전달한다, 비어 있는 수신자는 건너뛴다 </summary>
    public sealed class BossCastListenerComposite : IBossCastListener
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IBossCastListener[] _listeners;

        /****************************************
        *                Methods
        ****************************************/

        public BossCastListenerComposite(params IBossCastListener[] listeners)
        {
            _listeners = (listeners == null) ? new IBossCastListener[0] : (IBossCastListener[])listeners.Clone();
        }

        public void OnCastExecuted(int castInstanceId, double serverTime)
        {
            for (int i = 0; i < _listeners.Length; ++i)
            {
                _listeners[i]?.OnCastExecuted(castInstanceId, serverTime);
            }
        }

        public void OnCastFinished(int castInstanceId, double serverTime)
        {
            for (int i = 0; i < _listeners.Length; ++i)
            {
                _listeners[i]?.OnCastFinished(castInstanceId, serverTime);
            }
        }
    }
}
