// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 인증을 통과한 연결과 출정 참가자의 결합, 인증기가 연결의 authenticationData에 넣는다 </summary>
    public sealed class SessionBinding
    {
        // 연결 하나에 하나만 만든다. 연결이 끊기면 ServerSessionRegistry.Release가 비활성으로 바꾼다

        /****************************************
        *                Fields
        ****************************************/

        private readonly string              _participantKey;
        private readonly int                 _participantId;
        private readonly string              _accountLeaseId;
        private readonly EPlayerClass        _playerClass;
        private readonly string              _characterName;
        private readonly CharacterAppearance _appearance;

        /****************************************
        *              Properties
        ****************************************/

        // 토큰이 증명한 영구 참가자 키. 재접속해도 같다
        public string ParticipantKey => _participantKey;

        // 이 서버 프로세스가 키에 붙인 번호, 입력 헤더의 참가자 번호로 쓴다
        public int ParticipantId => _participantId;

        // 마을 계정 접속 임대 ID, 연결이 끊기면 인증기가 해제를 보낸다. 없으면 null
        public string AccountLeaseId => _accountLeaseId;

        // 토큰을 교환한 API가 알려 준 캐릭터 직업, 전투 개체는 이 값으로 만든다. 재접속마다 새 교환에서 다시 받는다
        public EPlayerClass PlayerClass => _playerClass;

        // DB 인증 닉네임, 명시적 레거시 캐릭터는 null
        public string CharacterName => _characterName;

        // TryResolve가 정규화를 끝낸 외형. 개체의 공개 상태(PlayerCombatState)에 그대로 적용한다
        public CharacterAppearance Appearance => _appearance;

        public bool IsActive { get; internal set; }

        // 마을이 던전 입장권을 보낸 참가자다. 이 연결이 끊겨도 마을은 계정 접속 임대를 풀지 않는다. 던전이 이관 경로로 옮기고,
        // 이관되지 않으면 heartbeat 만료로 풀린다. 먼저 풀면 던전의 이관이 SESSION_MISMATCH로 거절된다
        public bool IsDeparting { get; set; }

        // 둘 다 ServerSessionRegistry가 결합을 만든 직후 토큰 내용(JoinTokenClaims)에서 넣고 이후 바꾸지 않는다.
        // 계정 ID는 마을 입장 교환만 준다(생활, 준비 확인, 출정 예약에 쓴다). 던전과 개발 토큰은 null이다
        public string AccountId   { get; internal set; }

        // 캐릭터 ID는 마을과 던전 입장 교환 응답의 characterId다. 개발 토큰과 값이 없는 응답은 null이다
        public string CharacterId { get; internal set; }

        // 던전 입장만 채운다. 출정 예약 때 고정한 전투 준비와 남은 물약 예약분이며 전투 런타임이 능력치, 부적, 물약 수량을
        // 여기서 정한다. 개발 토큰, 0011 전 출정, 이 필드를 모르는 API면 null이다
        public CharacterExperienceSnapshot Experience { get; internal set; }
        public CharacterCombatLoadout CombatLoadout   { get; internal set; }
        public ReservationPotion[]    ReservedPotions { get; internal set; }

        // 던전 입장만 채운다. 입장 교환이 준 출정 계약이며 던전 호스트가 첫 입장에서 고정한 값과 대조한다
        public SortieDescriptor       Sortie          { get; internal set; }

        // 출정이 끝난 뒤 경매에만 다시 들어온 참가자다. 전투 개체를 만들지 않고 경매 상태와 입찰만 주고받는다
        public bool                   IsAuctionOnly   { get; internal set; }

        /****************************************
        *                Methods
        ****************************************/

        public SessionBinding(string participantKey, int participantId, string accountLeaseId, EPlayerClass playerClass)
            : this(participantKey, participantId, accountLeaseId, playerClass, null, null)
        {
        }

        public SessionBinding(string participantKey, int participantId, string accountLeaseId, EPlayerClass playerClass,
            string characterName)
            : this(participantKey, participantId, accountLeaseId, playerClass, characterName, null)
        {
        }

        public SessionBinding(string participantKey, int participantId, string accountLeaseId, EPlayerClass playerClass,
            string characterName, CharacterAppearance appearance)
        {
            _participantKey = participantKey;
            _participantId  = participantId;
            _accountLeaseId = accountLeaseId;
            _playerClass    = playerClass;
            _characterName  = characterName;
            _appearance     = appearance;
            IsActive        = true;
        }
    }
}
