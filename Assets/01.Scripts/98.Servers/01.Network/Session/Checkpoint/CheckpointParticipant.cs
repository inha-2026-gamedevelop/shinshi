// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 체크포인트의 참가자 한 명, 명단 값(연결, 생사, 유예 만료)과 전투 개체 값(체력, 기력, 공명, 사망 시각)을 함께 담는다 </summary>
    public readonly struct CheckpointParticipant
    {
        /****************************************
        *              Properties
        ****************************************/

        public string                      ParticipantKey  { get; }
        public EParticipantConnectionState ConnectionState { get; }
        public EParticipantLifeState       LifeState       { get; }

        // Departed일 때만 뜻이 있다
        public ESessionEndReason           CloseReason     { get; }

        // ReconnectGrace일 때 유예 만료 서버 시각
        public double                      GraceUntil      { get; }

        // 전투 개체가 없으면(지워짐, 시작 전) 거짓이고 Vitals는 기본값이다
        public bool                        HasVitals       { get; }
        public CheckpointVitals            Vitals          { get; }

        /****************************************
        *                Methods
        ****************************************/

        public CheckpointParticipant(string participantKey, EParticipantConnectionState connectionState,
            EParticipantLifeState lifeState, ESessionEndReason closeReason, double graceUntil, bool hasVitals,
            in CheckpointVitals vitals)
        {
            ParticipantKey  = participantKey;
            ConnectionState = connectionState;
            LifeState       = lifeState;
            CloseReason     = closeReason;
            GraceUntil      = graceUntil;
            HasVitals       = hasVitals;
            Vitals          = vitals;
        }
    }
}
