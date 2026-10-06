namespace Shinshi.Combat
{
    /// <summary> 물약 사용과 되돌림을 출정 이벤트로 저장하는 창구, 확정은 저장 순번으로 확인한다 </summary>
    public interface IPotionEventLedger
    {
        // 던전에서는 DungeonPotionEventLedger가 출정 이벤트 큐에 넣는다. 큐는 순번 순서로 ACK되므로 LastAcknowledged가 순번 이상이면
        // 그 이벤트와 앞선 이벤트가 모두 확정된 것이다

        /****************************************
        *              Properties
        ****************************************/

        // 이벤트를 받을 수 있는 진행 중인 출정인지
        bool IsSessionRunning { get; }

        // 보스전이면 참가자별 물약 사용 횟수 상한을 적용한다
        bool IsBossSession    { get; }

        // 저장이 확정 거절로 멈췄으면 참이다. 대기 중인 사용은 더 확정되지 않는다
        bool IsStorageStopped { get; }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 물약 1개 사용을 저장 큐에 넣는다, 큐가 멈췄거나 가득 찼으면 거짓 </summary>
        bool TryRecordUse(string participantKey, string definitionId, string bindingKind, double serverTime, out long sequence);

        /// <summary> 앞서 넣은 사용 하나를 되돌리는 이벤트를 넣는다(사용의 확정 전이어도 된다), 큐가 멈췄거나 가득 찼으면 거짓 </summary>
        bool TryRecordRevert(string participantKey, long useSequence, double serverTime, out long sequence);

        /// <summary> 저장 순번이 API에 확정됐는지 </summary>
        bool IsCommitted(long sequence);
    }
}
