// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 판정 시점에 확정한 저장 payload, ID는 연결 번호가 아닌 영구 참가자 키다 </summary>
    public readonly struct SessionEventData
    {
        /****************************************
        *              Properties
        ****************************************/

        public ESessionEventType Type                { get; }
        public double            ServerTime          { get; }
        public string            ParticipantId       { get; }
        public string            SourceId            { get; }
        public string            KillerParticipantId { get; }
        public long              KillSequence        { get; }
        public int               WaveIndex           { get; }
        public ESessionEndReason Reason              { get; }
        public bool              IsBossSession       { get; }
        public LootRollResult    Loot                { get; }

        // EssenceAccrued, BaseEssenceAccrued의 적립량, 0보다 크다. 다른 종류는 0이다
        public long              Amount              { get; }

        // SessionEnded의 참가자별 정산안, 종료 판정 때 한 번 만들고 저장 재시도에도 바꾸지 않는다
        public SettlementGrant[] Grants              { get; }

        /****************************************
        *                Methods
        ****************************************/

        public SessionEventData(ESessionEventType type, double serverTime,
            string participantId = null, string sourceId = null, string killerParticipantId = null,
            long killSequence = 0, int waveIndex = 0, ESessionEndReason reason = default,
            bool isBossSession = false, in LootRollResult loot = default, SettlementGrant[] grants = null,
            long amount = 0)
        {
            Type                = type;
            ServerTime          = serverTime;
            ParticipantId       = participantId;
            SourceId            = sourceId;
            KillerParticipantId = killerParticipantId;
            KillSequence        = killSequence;
            WaveIndex           = waveIndex;
            Reason              = reason;
            IsBossSession       = isBossSession;
            Loot                = loot;
            Grants              = grants;
            Amount              = amount;
        }
    }
}
