// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 서버 참가자의 연결 상태와 생사 상태를 함께 읽는 불변 스냅샷, 네트워크 패킷으로 쓰지 않는다 </summary>
    public readonly struct ParticipantSnapshot
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int                         _participantId;
        private readonly string                      _participantKey;
        private readonly EParticipantConnectionState _connectionState;
        private readonly EParticipantLifeState       _lifeState;
        private readonly double                      _graceUntil;
        private readonly ESessionEndReason           _closeReason;

        /****************************************
        *              Properties
        ****************************************/

        public int                         ParticipantId    => _participantId;
        public string                      ParticipantKey   => _participantKey;
        public EParticipantConnectionState ConnectionState  => _connectionState;
        public EParticipantLifeState       LifeState        => _lifeState;
        public double                      GraceUntil       => _graceUntil;

        // Departed일 때만 유효하다. 생사와 무관하게 닫힘 원인을 보존한다
        public ESessionEndReason CloseReason => _closeReason;

        /****************************************
        *                Methods
        ****************************************/

        public ParticipantSnapshot(int participantId, string participantKey,
            EParticipantConnectionState connectionState, EParticipantLifeState lifeState,
            double graceUntil, ESessionEndReason closeReason)
        {
            _participantId   = participantId;
            _participantKey  = participantKey;
            _connectionState = connectionState;
            _lifeState       = lifeState;
            _graceUntil      = graceUntil;
            _closeReason     = closeReason;
        }
    }
}
