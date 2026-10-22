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
        public int ExperienceSourceKind { get; }
        public string ExperienceSettingsHash { get; }

        // SessionEnded의 참가자별 정산안, 종료 판정 때 한 번 만들고 저장 재시도에도 바꾸지 않는다
        public SettlementGrant[] Grants              { get; }

        // PotionUsed의 물약 정의와 귀속(출정 예약과 같은 값). 다른 종류는 null이다
        public string            DefinitionId        { get; }
        public string            BindingKind         { get; }

        // PotionUseReverted가 되돌리는 PotionUsed의 저장 순번. 다른 종류는 0이다
        public long              RevertsSequence     { get; }

        /****************************************
        *                Methods
        ****************************************/

        // 운영 코드는 아래 목적별 팩토리로 만든다. 값 검증은 하지 않고(틱 안에서 예외를 내지 않는다) 변환 경계가 거른다

        public static SessionEventData ParticipantJoined(double serverTime, string participantKey, bool isBossSession)
        {
            return new SessionEventData(ESessionEventType.ParticipantJoined, serverTime,
                participantId: participantKey, isBossSession: isBossSession);
        }

        /// <summary> 연결이 끊긴 기록, 종료 사유는 없다 </summary>
        public static SessionEventData ParticipantDisconnected(double serverTime, string participantKey, bool isBossSession)
        {
            return new SessionEventData(ESessionEventType.ParticipantLeft, serverTime,
                participantId: participantKey, isBossSession: isBossSession);
        }

        /// <summary> 사유가 확정돼 명단에서 닫힌 기록 </summary>
        public static SessionEventData ParticipantClosed(double serverTime, string participantKey, ESessionEndReason reason,
            bool isBossSession)
        {
            return new SessionEventData(ESessionEventType.ParticipantLeft, serverTime,
                participantId: participantKey, reason: reason, isBossSession: isBossSession);
        }

        /// <summary> 참가자 정산 확정, 포기와 유예 만료는 개인 이탈로 저장된다 </summary>
        public static SessionEventData ParticipantSettled(double serverTime, string participantKey, ESessionEndReason reason,
            bool isBossSession)
        {
            return new SessionEventData(ESessionEventType.ParticipantSettled, serverTime,
                participantId: participantKey, reason: reason, isBossSession: isBossSession);
        }

        public static SessionEventData EnemyKilled(double serverTime, string participantKey, string sourceId,
            string killerParticipantId, long killSequence, int waveIndex, in LootRollResult loot)
        {
            return new SessionEventData(ESessionEventType.EnemyKilled, serverTime,
                participantId: participantKey, sourceId: sourceId, killerParticipantId: killerParticipantId,
                killSequence: killSequence, waveIndex: waveIndex, loot: loot);
        }

        public static SessionEventData WaveCleared(double serverTime, string participantKey, int waveIndex)
        {
            return new SessionEventData(ESessionEventType.WaveCleared, serverTime,
                participantId: participantKey, waveIndex: waveIndex);
        }

        /// <summary> 출정 종료와 참가자별 정산안, ServiceCancelled는 API 복구 작업만 기록하므로 변환 경계가 SESSION_ENDED로 보내지 않는다 </summary>
        public static SessionEventData SessionEnded(double serverTime, ESessionEndReason reason, bool isBossSession,
            SettlementGrant[] grants)
        {
            return new SessionEventData(ESessionEventType.SessionEnded, serverTime,
                reason: reason, isBossSession: isBossSession, grants: grants);
        }

        /// <summary> 추가 정기 적립, 양은 0보다 커야 API 적립 이벤트가 된다 </summary>
        public static SessionEventData EssenceAccrued(double serverTime, string participantKey, long amount, int waveIndex,
            long killSequence)
        {
            return new SessionEventData(ESessionEventType.EssenceAccrued, serverTime,
                participantId: participantKey, killSequence: killSequence, waveIndex: waveIndex, amount: amount);
        }

        /// <summary> 완료 웨이브 기본 정기 적립, 양은 0보다 커야 API 적립 이벤트가 된다 </summary>
        public static SessionEventData BaseEssenceAccrued(double serverTime, string participantKey, long amount, int waveIndex,
            long killSequence)
        {
            return new SessionEventData(ESessionEventType.BaseEssenceAccrued, serverTime,
                participantId: participantKey, killSequence: killSequence, waveIndex: waveIndex, amount: amount);
        }

        public static SessionEventData ExperienceGranted(double serverTime, string participantKey, string sourceId, long amount,
            int experienceSourceKind, string experienceSettingsHash)
        {
            return new SessionEventData(ESessionEventType.ExperienceGranted, serverTime,
                participantId: participantKey, sourceId: sourceId, amount: amount,
                experienceSourceKind: experienceSourceKind, experienceSettingsHash: experienceSettingsHash);
        }

        /// <summary> 물약 사용, 정의와 귀속이 비면 변환 경계가 진단 LOOT로만 남긴다 </summary>
        public static SessionEventData PotionUsed(double serverTime, string participantKey, string definitionId,
            string bindingKind)
        {
            return new SessionEventData(ESessionEventType.PotionUsed, serverTime,
                participantId: participantKey, definitionId: definitionId, bindingKind: bindingKind);
        }

        /// <summary> 물약 사용 되돌림, useSequence는 되돌릴 PotionUsed의 저장 순번 </summary>
        public static SessionEventData PotionUseReverted(double serverTime, string participantKey, long useSequence)
        {
            return new SessionEventData(ESessionEventType.PotionUseReverted, serverTime,
                participantId: participantKey, revertsSequence: useSequence);
        }

        public SessionEventData(ESessionEventType type, double serverTime,
            string participantId = null, string sourceId = null, string killerParticipantId = null,
            long killSequence = 0, int waveIndex = 0, ESessionEndReason reason = default,
            bool isBossSession = false, in LootRollResult loot = default, SettlementGrant[] grants = null,
            long amount = 0, string definitionId = null, string bindingKind = null, long revertsSequence = 0, int experienceSourceKind = 0, string experienceSettingsHash = null)
        {
            ExperienceSourceKind = experienceSourceKind;
            ExperienceSettingsHash = experienceSettingsHash;
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
            DefinitionId        = definitionId;
            BindingKind         = bindingKind;
            RevertsSequence     = revertsSequence;
        }
    }
}
