// 프로젝트 내부
using Shinshi.Common;

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
            : this(participantKey, characterId, instanceId, sortieId, accountLeaseId, playerClass, null)
        {
        }

        public JoinTokenClaims(string participantKey, string characterId, string instanceId, string sortieId,
            string accountLeaseId, EPlayerClass playerClass, string characterName)
        {
            _participantKey = participantKey;
            _characterId    = characterId;
            _instanceId     = instanceId;
            _sortieId       = sortieId;
            _accountLeaseId = accountLeaseId;
            _playerClass    = playerClass;
            _characterName  = characterName;
        }
    }
}
