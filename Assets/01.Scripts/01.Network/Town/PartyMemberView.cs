// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 방 상태의 참가자 한 명, 입장 순서대로 온다 </summary>
    public readonly struct PartyMemberView
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int              _participantId;
        private readonly string           _name;
        private readonly EPlayerClass     _playerClass;
        private readonly EPartyReadyState _ready;
        private readonly int              _smallPotions;
        private readonly int              _largePotions;
        private readonly long             _totalExperience;

        /****************************************
        *              Properties
        ****************************************/

        // 이 마을 서버가 연결에 붙인 번호, 재접속하면 바뀐다
        public int              ParticipantId => _participantId;
        public string           Name          => _name;
        public EPlayerClass     PlayerClass   => _playerClass;
        public EPartyReadyState Ready         => _ready;

        // 준비할 때 고른 지참 수량, 준비가 아니면 0
        public int              SmallPotions  => _smallPotions;
        public int              LargePotions  => _largePotions;

        // 마을 입장 교환이 확인한 누적 경험치. -1은 값이 없는 개발·시험 참가자다
        public long             TotalExperience => _totalExperience;

        /****************************************
        *                Methods
        ****************************************/

        public PartyMemberView(int participantId, string name, EPlayerClass playerClass, EPartyReadyState ready,
            int smallPotions, int largePotions, long totalExperience = -1)
        {
            _participantId   = participantId;
            _name            = name;
            _playerClass     = playerClass;
            _ready           = ready;
            _smallPotions    = smallPotions;
            _largePotions    = largePotions;
            _totalExperience = totalExperience;
        }
    }
}
