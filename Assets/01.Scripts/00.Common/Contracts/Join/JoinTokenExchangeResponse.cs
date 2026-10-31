// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 접속 토큰 교환 승인 결과, 토큰이 증명한 캐릭터와 직업과 배정 인스턴스와 출정 참가자 </summary>
    [DataContract]
    public sealed class JoinTokenExchangeResponse
    {
        // 게임 서버는 instanceId와 serverRole을 자기 실행 설정과 다시 대조한다. API 응답이라도 다른 서버의 입장을 받지 않는다

        /****************************************
        *                Fields
        ****************************************/

        // classId 키가 없는 응답을 직업 0(무사)으로 읽지 않도록 값 없음을 따로 둔다. 서버는 이 값을 알 수 없는 직업으로 거절한다
        public const int UNKNOWN_CLASS_ID = -1;

        // 마을 입장이나 출정 계약을 받지 못한 응답의 mode. mode 키가 없는 응답을 일반전(0)으로 읽지 않게 한다
        public const int NO_MODE = -1;

        [DataMember(Name = "experience")] private CharacterExperienceSnapshot _experience;
        [DataMember(Name = "characterId")]      private string _characterId;
        [DataMember(Name = "instanceId")]       private string _instanceId;

        // ContractCodes.ServerRole
        [DataMember(Name = "serverRole")]       private int    _serverRole;

        // 던전 출정의 영구 참가자 ID(JoinTicket.participantId)와 출정 ID. 마을 입장이면 둘 다 빈 값이다
        [DataMember(Name = "participantId")]    private string _participantId;
        [DataMember(Name = "sortieId")]         private string _sortieId;

        [DataMember(Name = "serverTimeUnixMs")] private long   _serverTimeUnixMs;

        // 마을 입장 교환이 얻은 계정 접속 임대 ID. 마을 서버가 접속 종료 때 해제 경로(GameServerRoutes.ACCOUNT_LEASE_RELEASE_FORMAT)에
        // 넣는다. 던전 입장이면 빈 값이다(임대는 이관 경로가 옮긴다)
        [DataMember(Name = "accountLeaseId")]   private string _accountLeaseId;

        // 토큰이 발급된 캐릭터의 직업(ContractCodes.PlayerClass). 마을과 던전 입장 모두 채운다.
        // 게임 서버는 클라이언트가 아니라 이 값으로 전투 개체의 직업을 정한다
        [DataMember(Name = "classId")]          private int    _classId = UNKNOWN_CLASS_ID;

        // 토큰이 발급된 캐릭터의 표시 이름. 게임 서버가 머리 위 이름으로 복제한다. 이름이 없는 옛 캐릭터면 빈 값이다
        [DataMember(Name = "characterName")]    private string _characterName;

        // 던전 입장, 재입장의 출정 계약. 예약 때 마을이 확정한 값이고 던전 서버는 이 값으로 전투를 연다. 마을 입장이면 값 없음이다.
        // mode는 GameSessionCodes.Mode, difficulty는 GameSessionCodes.Difficulty(보스전이 아니면 NONE)
        [DataMember(Name = "mode")]             private int    _mode       = NO_MODE;
        [DataMember(Name = "mapId")]            private string _mapId;
        [DataMember(Name = "battleId")]         private string _battleId;
        [DataMember(Name = "bossId")]           private string _bossId;
        [DataMember(Name = "difficulty")]       private int    _difficulty = GameSessionCodes.Difficulty.NONE;

        // 던전 입장, 재입장 교환만 채운다. 출정 예약 때 고정한 전투 장비, 수련, 부적이며 마을 입장이나 0011 전 출정이면 null이다
        [DataMember(Name = "combatLoadout")]    private CharacterCombatLoadout _combatLoadout;

        // 토큰이 발급된 캐릭터의 계정. 마을 서버가 생활, 준비 확인, 출정 예약 요청에 넣는다. 이 필드 전의 응답이면 null이다
        [DataMember(Name = "accountId")]        private string _accountId;

        // 던전 입장, 재입장 교환만 채운다. 이 참가자가 출정에 지참한 물약의 남은 예약분(예약 수량 - 확정 사용)이며
        // 던전 서버는 이 수량 안에서만 POTION_USED를 보낸다. 마을 입장이나 이 필드를 모르는 API면 null이다
        [DataMember(Name = "potions")]          private ReservationPotion[] _potions;

        // DB 소유 캐릭터의 외형 원문. 명시적 레거시 캐릭터는 값 없음이며, 클라이언트가 보낸 외형은 쓰지 않는다.
        // 표시 resolver가 null을 직업 기본값으로 읽는다
        [DataMember(Name = "appearance")]       private string _appearanceJson;

        // 토큰이 발급된 캐릭터가 속한 길드의 표시 이름. 게임 서버가 머리 위 길드 줄로 복제한다. 길드가 없거나 이 필드를 모르는 API면 null이다.
        // 백엔드가 입장 교환 트랜잭션에서 길드 가입 행으로 채운다
        [DataMember(Name = "guildName")]        private string _guildName;

        // 프롤로그 출정(mode = GameSessionCodes.Mode.PROLOGUE)의 던전 입장, 재입장만 채운다. 캐릭터의 저장 단계(PrologueCodes.Stage),
        // 월백 구간 진입 때 남은 임시 물약 수, 부적 조사 화면을 본 적이 있는지이며 던전 서버가 이 값으로 구간을 재개한다.
        // 다른 출정, 마을 입장이면 0, 0, false다. 이 필드 전의 API 응답에는 없을 수 있고 그때도 기본값으로 읽힌다
        [DataMember(Name = "prologueStage")]           private int  _prologueStage;
        [DataMember(Name = "prologueWolbaekPotions")]  private int  _prologueWolbaekPotions;
        [DataMember(Name = "prologueTalismanChecked")] private bool _prologueTalismanChecked;

        /****************************************
        *              Properties
        ****************************************/

        public CharacterExperienceSnapshot Experience => _experience;
        public string CharacterId      => _characterId;
        public string InstanceId       => _instanceId;
        public int    ServerRole       => _serverRole;
        public string ParticipantId    => _participantId;
        public string SortieId         => _sortieId;
        public long   ServerTimeUnixMs => _serverTimeUnixMs;
        public string AccountLeaseId   => _accountLeaseId;
        public int    ClassId          => _classId;
        public string CharacterName    => _characterName;
        public int    Mode             => _mode;
        public string MapId            => _mapId;
        public string BattleId         => _battleId;
        public string BossId           => _bossId;
        public int    Difficulty       => _difficulty;
        public CharacterCombatLoadout CombatLoadout => _combatLoadout;
        public string AccountId        => _accountId;
        public ReservationPotion[] Potions => _potions;
        public string AppearanceJson   => _appearanceJson;
        public string GuildName        => _guildName;
        public int    PrologueStage           => _prologueStage;
        public int    PrologueWolbaekPotions  => _prologueWolbaekPotions;
        public bool   PrologueTalismanChecked => _prologueTalismanChecked;

        /****************************************
        *                Methods
        ****************************************/

        public JoinTokenExchangeResponse(string characterId, string instanceId, int serverRole, string participantId,
            string sortieId, long serverTimeUnixMs, int classId)
            : this(characterId, instanceId, serverRole, participantId, sortieId, serverTimeUnixMs, null, classId)
        {
        }

        public JoinTokenExchangeResponse(string characterId, string instanceId, int serverRole, string participantId,
            string sortieId, long serverTimeUnixMs, string accountLeaseId, int classId)
            : this(characterId, instanceId, serverRole, participantId, sortieId, serverTimeUnixMs, accountLeaseId, classId, null)
        {
        }

        public JoinTokenExchangeResponse(string characterId, string instanceId, int serverRole, string participantId,
            string sortieId, long serverTimeUnixMs, string accountLeaseId, int classId, string characterName)
            : this(characterId, instanceId, serverRole, participantId, sortieId, serverTimeUnixMs, accountLeaseId, classId,
                characterName, NO_MODE, null, null, null, GameSessionCodes.Difficulty.NONE, null)
        {
        }

        public JoinTokenExchangeResponse(string characterId, string instanceId, int serverRole, string participantId,
            string sortieId, long serverTimeUnixMs, string accountLeaseId, int classId, string characterName, int mode,
            string mapId, string battleId, string bossId, int difficulty, CharacterCombatLoadout combatLoadout)
            : this(characterId, instanceId, serverRole, participantId, sortieId, serverTimeUnixMs, accountLeaseId, classId,
                characterName, mode, mapId, battleId, bossId, difficulty, combatLoadout, null)
        {
        }

        public JoinTokenExchangeResponse(string characterId, string instanceId, int serverRole, string participantId,
            string sortieId, long serverTimeUnixMs, string accountLeaseId, int classId, string characterName, int mode,
            string mapId, string battleId, string bossId, int difficulty, CharacterCombatLoadout combatLoadout,
            string accountId)
            : this(characterId, instanceId, serverRole, participantId, sortieId, serverTimeUnixMs, accountLeaseId, classId,
                characterName, mode, mapId, battleId, bossId, difficulty, combatLoadout, accountId, null)
        {
        }

        public JoinTokenExchangeResponse(string characterId, string instanceId, int serverRole, string participantId,
            string sortieId, long serverTimeUnixMs, string accountLeaseId, int classId, string characterName, int mode,
            string mapId, string battleId, string bossId, int difficulty, CharacterCombatLoadout combatLoadout,
            string accountId, ReservationPotion[] potions)
            : this(characterId, instanceId, serverRole, participantId, sortieId, serverTimeUnixMs, accountLeaseId, classId,
                characterName, mode, mapId, battleId, bossId, difficulty, combatLoadout, accountId, potions, null)
        {
        }

        public JoinTokenExchangeResponse(string characterId, string instanceId, int serverRole, string participantId,
            string sortieId, long serverTimeUnixMs, string accountLeaseId, int classId, string characterName,
            string appearanceJson)
            : this(characterId, instanceId, serverRole, participantId, sortieId, serverTimeUnixMs, accountLeaseId, classId,
                characterName, NO_MODE, null, null, null, GameSessionCodes.Difficulty.NONE, null, null, null, appearanceJson)
        {
        }

        public JoinTokenExchangeResponse(string characterId, string instanceId, int serverRole, string participantId,
            string sortieId, long serverTimeUnixMs, string accountLeaseId, int classId, string characterName, int mode,
            string mapId, string battleId, string bossId, int difficulty, CharacterCombatLoadout combatLoadout,
            string accountId, ReservationPotion[] potions, string appearanceJson, CharacterExperienceSnapshot experience = null,
            string guildName = null, int prologueStage = 0, int prologueWolbaekPotions = 0, bool prologueTalismanChecked = false)
        {
            _experience       = experience;
            _characterId      = characterId;
            _instanceId       = instanceId;
            _serverRole       = serverRole;
            _participantId    = participantId;
            _sortieId         = sortieId;
            _serverTimeUnixMs = serverTimeUnixMs;
            _accountLeaseId   = accountLeaseId;
            _classId          = classId;
            _characterName    = characterName;
            _mode             = mode;
            _mapId            = mapId;
            _battleId         = battleId;
            _bossId           = bossId;
            _difficulty       = difficulty;
            _combatLoadout    = combatLoadout;
            _accountId        = accountId;
            _potions          = potions;
            _appearanceJson   = appearanceJson;
            _guildName        = guildName;
            _prologueStage           = prologueStage;
            _prologueWolbaekPotions  = prologueWolbaekPotions;
            _prologueTalismanChecked = prologueTalismanChecked;
        }

        private JoinTokenExchangeResponse() { }
    }
}
