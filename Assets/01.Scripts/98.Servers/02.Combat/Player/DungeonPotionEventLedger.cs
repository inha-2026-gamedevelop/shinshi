// 프로젝트 내부
using Shinshi.Network;

namespace Shinshi.Combat
{
    /// <summary> 던전 출정의 이벤트 큐에 물약 이벤트를 넣는다, 출정이 시작되기 전과 끝난 뒤에는 받지 않는다 </summary>
    public sealed class DungeonPotionEventLedger : IPotionEventLedger
    {
        // 세션의 종료 보존 버퍼(저장이 멈춰 쌓인 드롭)가 남아 있으면 넣지 않는다. 큐에 바로 넣으면 보존한 사실보다 앞 순번을
        // 받아 저장 순서가 뒤집힌다. 그때는 전투도 이미 멈춘 상태다

        /****************************************
        *                Fields
        ****************************************/

        private readonly DungeonSessionHost _host;
        private long                        _prologueSequence = 0;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsSessionRunning => _host.IsSessionRunning;
        public bool IsBossSession    => (_host.Session != null) && (_host.Session.IsBossSession);
        public bool IsStorageStopped => (_host.Session != null) && (_host.Session.HasTerminalStorage);

        /****************************************
        *                Methods
        ****************************************/

        public DungeonPotionEventLedger(DungeonSessionHost host)
        {
            _host = host ?? throw new System.ArgumentNullException(nameof(host));
        }

        public bool TryRecordUse(string participantKey, string definitionId, string bindingKind, double serverTime,
            out long sequence)
        {
            // 프롤로그 임시 물약은 출정 예약분이 아니라 API가 받지 않는다. 저장 이벤트 없이 바로 확정으로 본다
            if (_host.Sortie.IsPrologue)
            {
                sequence = ++_prologueSequence;
                return true;
            }

            SessionEventData data = SessionEventData.PotionUsed(serverTime, participantKey, definitionId, bindingKind);
            return TryEnqueue(in data, out sequence);
        }

        public bool TryRecordRevert(string participantKey, long useSequence, double serverTime, out long sequence)
        {
            if (_host.Sortie.IsPrologue)
            {
                sequence = ++_prologueSequence;
                return true;
            }

            SessionEventData data = SessionEventData.PotionUseReverted(serverTime, participantKey, useSequence);
            return TryEnqueue(in data, out sequence);
        }

        public bool IsCommitted(long sequence)
        {
            if (_host.Sortie.IsPrologue)
            {
                return sequence > 0;
            }

            DungeonSession session = _host.Session;
            return (session != null) && (sequence > 0) && (session.Events.LastAcknowledgedSequence >= sequence);
        }

        private bool TryEnqueue(in SessionEventData data, out long sequence)
        {
            sequence = 0;
            DungeonSession session = _host.Session;
            if ((session == null) || (!session.IsRunning) || (session.HasStorageFault) || (session.PendingEventCount > 0))
            {
                return false;
            }

            return session.Events.TryEnqueue(in data, out sequence);
        }
    }
}
