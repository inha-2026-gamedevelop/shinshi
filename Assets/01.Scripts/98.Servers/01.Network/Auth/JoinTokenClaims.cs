// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 검증기가 접속 토큰에서 확인한 내용 </summary>
    public readonly struct JoinTokenClaims
    {
        // 백엔드 검증기는 instanceId와 역할을 이 서버 실행 설정과 대조한 뒤에만 만든다. 개발 검증기는 키만 채운다.
        // 직업은 API 응답이 준 값만 쓴다. 키만 받는 생성자는 개발과 시험 전용이라 직업을 무사로 둔다

        /****************************************
        *                Fields
        ****************************************/

        private readonly string       _participantKey;
        private readonly string       _characterId;
        private readonly string       _instanceId;
        private readonly string       _sortieId;
        private readonly string       _accountLeaseId;
        private readonly EPlayerClass _playerClass;
        private readonly string       _characterName;
        private readonly string       _accountId;
        private readonly CharacterCombatLoadout _combatLoadout;
        private readonly ReservationPotion[]    _reservedPotions;
        private readonly SortieDescriptor       _sortie;
        private readonly CharacterAppearance    _appearance;

        /****************************************
        *              Properties
        ****************************************/

        // 참가자를 영구히 가리키는 키. 같은 키로 다시 들어오면 재접속이다.
        // 던전은 출정 참가자 ID, 마을은 캐릭터 ID에서 만든다(JoinTokenClaimsRules)
        public string ParticipantKey => _participantKey;

        public string CharacterId    => _characterId;

        // 토큰이 묶인 서버 인스턴스, 검증기가 이 서버와 같은지 이미 확인했다
        public string InstanceId     => _instanceId;

        // 던전 출정 ID, 마을 입장과 개발 토큰은 null
        public string SortieId       => _sortieId;

        // 마을 입장이 받은 계정 접속 임대 ID(GUID), 연결이 끊기면 서버가 풀어 준다. 던전, 개발 토큰, 값이 없는 응답은 null
        public string AccountLeaseId => _accountLeaseId;

        // 토큰이 발급된 캐릭터의 직업, API 교환 응답의 classId다. 클라이언트가 보낸 값은 쓰지 않는다
        public EPlayerClass PlayerClass => _playerClass;

        // 머리 위에 띄우는 표시 이름, 없으면 null
        public string CharacterName => _characterName;

        // 마을 입장 교환이 알려 준 계정 ID(GUID), 마을 서버가 생활, 준비 확인, 출정 예약에 쓴다. 던전, 개발 토큰은 null
        public string AccountId     => _accountId;

        // 던전 교환이 준 출정 고정 전투 준비, 마을, 개발 토큰, 0011 전 출정은 null이다
        public CharacterCombatLoadout CombatLoadout => _combatLoadout;

        // 던전 교환이 준 남은 물약 예약분, 모르는 API와 마을, 개발 토큰은 null이다
        public ReservationPotion[] ReservedPotions => _reservedPotions;

        // 던전 교환이 준 출정 계약(종류, 맵, 전투, 보스, 난이도), 마을, 개발 토큰, mode를 모르는 API는 계약 없음이다
        public SortieDescriptor Sortie => _sortie;

        // TryResolve가 정규화한 공개 외형, null/v1은 직업 기본값으로, 유효한 v2는 그대로 들어 있다. 마을 토큰도 채운다
        public CharacterAppearance Appearance => _appearance;

        /****************************************
        *                Methods
        ****************************************/

        public JoinTokenClaims(string participantKey)
        {
            _participantKey = participantKey;
            _characterId    = null;
            _instanceId     = null;
            _sortieId       = null;
            _accountLeaseId = null;
            _playerClass    = EPlayerClass.Warrior;
            _characterName  = null;
            _appearance     = null;
            _accountId      = null;
            _combatLoadout   = null;
            _reservedPotions = null;
            _sortie          = default;
        }

        public JoinTokenClaims(string participantKey, string characterName)
            : this(participantKey)
        {
            _characterName = characterName;
        }

        public JoinTokenClaims(string participantKey, string characterId, string instanceId, string sortieId)
            : this(participantKey, characterId, instanceId, sortieId, null)
        {
        }

        public JoinTokenClaims(string participantKey, string characterId, string instanceId, string sortieId,
            string accountLeaseId)
            : this(participantKey, characterId, instanceId, sortieId, accountLeaseId, EPlayerClass.Warrior)
        {
        }

        public JoinTokenClaims(string participantKey, string characterId, string instanceId, string sortieId,
            string accountLeaseId, EPlayerClass playerClass)
            : this(participantKey, characterId, instanceId, sortieId, accountLeaseId, playerClass, null, null)
        {
        }

        public JoinTokenClaims(string participantKey, string characterId, string instanceId, string sortieId,
            string accountLeaseId, EPlayerClass playerClass, string characterName)
            : this(participantKey, characterId, instanceId, sortieId, accountLeaseId, playerClass, characterName, null)
        {
        }

        public JoinTokenClaims(string participantKey, string characterId, string instanceId, string sortieId,
            string accountLeaseId, EPlayerClass playerClass, string characterName, CharacterAppearance appearance)
        {
            _participantKey = participantKey;
            _characterId    = characterId;
            _instanceId     = instanceId;
            _sortieId       = sortieId;
            _accountLeaseId = accountLeaseId;
            _playerClass    = playerClass;
            _characterName  = characterName;
            _appearance     = appearance;
            _accountId      = null;
            _combatLoadout   = null;
            _reservedPotions = null;
            _sortie          = default;
        }

        // 다른 필드는 그대로 두고 계정 ID만 붙인다. 생성자 인자를 더 늘리지 않으려고 복사 생성자를 쓴다
        private JoinTokenClaims(in JoinTokenClaims source, string accountId)
        {
            this       = source;
            _accountId = accountId;
        }

        private JoinTokenClaims(in JoinTokenClaims source, CharacterCombatLoadout combatLoadout, ReservationPotion[] reservedPotions)
        {
            this             = source;
            _combatLoadout   = combatLoadout;
            _reservedPotions = reservedPotions;
        }

        private JoinTokenClaims(in JoinTokenClaims source, in SortieDescriptor sortie)
        {
            this    = source;
            _sortie = sortie;
        }

        /// <summary> 출정 계약을 붙인 복사본, 던전 입장 결합 규칙과 시험만 부른다 </summary>
        public JoinTokenClaims WithSortie(in SortieDescriptor sortie)
        {
            return new JoinTokenClaims(this, sortie);
        }

        /// <summary> 출정 고정 전투 준비와 남은 물약 예약분을 붙인 복사본, 던전 입장 결합 규칙만 부른다 </summary>
        public JoinTokenClaims WithSortieLoadout(CharacterCombatLoadout combatLoadout, ReservationPotion[] reservedPotions)
        {
            return new JoinTokenClaims(this, combatLoadout, reservedPotions);
        }

        /// <summary> 계정 ID를 붙인 복사본, 마을 입장 결합 규칙만 부른다 </summary>
        public JoinTokenClaims WithAccountId(string accountId)
        {
            return new JoinTokenClaims(this, accountId);
        }
    }
}
