// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 캐릭터 프로필을 한 읽기 스냅샷으로 모은 민감 조회 응답 </summary>
    [DataContract]
    public sealed class AdminCharacterDetailResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "characterId")]         private string                               _characterId;
        [DataMember(Name = "accountId")]           private string                               _accountId;
        [DataMember(Name = "name")]                private string                               _name;
        [DataMember(Name = "classId")]             private int                                  _classId;
        [DataMember(Name = "classDisplayName")]    private string                               _classDisplayName;
        [DataMember(Name = "appearanceJson")]      private string                               _appearanceJson;
        [DataMember(Name = "createdAtUnixMs")]     private long?                                _createdAtUnixMs;
        [DataMember(Name = "deletedAtUnixMs")]     private long?                                _deletedAtUnixMs;
        [DataMember(Name = "essence")]             private long                                 _essence;
        [DataMember(Name = "shards")]              private long                                 _shards;
        [DataMember(Name = "revision")]            private long                                 _revision;
        [DataMember(Name = "loadoutRevision")]     private long                                 _loadoutRevision;
        [DataMember(Name = "training")]            private AdminCharacterTrainingView           _training;
        [DataMember(Name = "combatStats")]         private AdminCharacterCombatStatsView        _combatStats;
        [DataMember(Name = "equipment")]           private AdminCharacterEquipmentView[]        _equipment;
        [DataMember(Name = "highestEnhanceLevel")] private int                                  _highestEnhanceLevel;
        [DataMember(Name = "talismanProgress")]    private AdminCharacterTalismanProgressView[] _talismanProgress;
        [DataMember(Name = "talismanLoadout")]     private AdminCharacterTalismanLoadoutView[]  _talismanLoadout;
        [DataMember(Name = "accountProgress")]     private AdminCharacterAccountProgressView    _accountProgress;
        [DataMember(Name = "life")]                private AdminCharacterLifeView               _life;
        [DataMember(Name = "wallet")]              private AdminCharacterWalletView             _wallet;
        [DataMember(Name = "items")]               private AdminAccountItemView[]               _items;
        [DataMember(Name = "lease")]               private AdminAccountLeaseView                _lease;
        [DataMember(Name = "online")]              private bool                                 _online;
        [DataMember(Name = "recentSessions")]      private AdminAccountSessionView[]            _recentSessions;
        [DataMember(Name = "otherCharacters")]     private AdminCharacterSummaryView[]          _otherCharacters;

        /****************************************
        *              Properties
        ****************************************/

        public string                               CharacterId         => _characterId;
        public string                               AccountId           => _accountId;
        public string                               Name                => _name;
        public int                                  ClassId             => _classId;
        public string                               ClassDisplayName    => _classDisplayName;
        public string                               AppearanceJson      => _appearanceJson;
        public long?                                CreatedAtUnixMs     => _createdAtUnixMs;
        public long?                                DeletedAtUnixMs     => _deletedAtUnixMs;
        public long                                 Essence             => _essence;
        public long                                 Shards              => _shards;
        public long                                 Revision            => _revision;
        public long                                 LoadoutRevision     => _loadoutRevision;
        public AdminCharacterTrainingView           Training            => _training;
        public AdminCharacterCombatStatsView        CombatStats         => _combatStats;
        public AdminCharacterEquipmentView[]        Equipment           => _equipment;
        public int                                  HighestEnhanceLevel => _highestEnhanceLevel;
        public AdminCharacterTalismanProgressView[] TalismanProgress    => _talismanProgress;
        public AdminCharacterTalismanLoadoutView[]  TalismanLoadout     => _talismanLoadout;
        public AdminCharacterAccountProgressView    AccountProgress     => _accountProgress;
        public AdminCharacterLifeView               Life                => _life;
        public AdminCharacterWalletView             Wallet              => _wallet;
        public AdminAccountItemView[]               Items               => _items;
        public AdminAccountLeaseView                Lease               => _lease;
        public bool                                 Online              => _online;
        public AdminAccountSessionView[]            RecentSessions      => _recentSessions;
        public AdminCharacterSummaryView[]          OtherCharacters     => _otherCharacters;

        /****************************************
        *                Methods
        ****************************************/

        public AdminCharacterDetailResponse(string characterId, string accountId, string name, int classId,
            string classDisplayName, string appearanceJson, long? createdAtUnixMs, long? deletedAtUnixMs,
            long essence, long shards, long revision, long loadoutRevision, AdminCharacterTrainingView training,
            AdminCharacterCombatStatsView combatStats, AdminCharacterEquipmentView[] equipment,
            int highestEnhanceLevel, AdminCharacterTalismanProgressView[] talismanProgress,
            AdminCharacterTalismanLoadoutView[] talismanLoadout, AdminCharacterAccountProgressView accountProgress,
            AdminCharacterLifeView life, AdminCharacterWalletView wallet, AdminAccountItemView[] items,
            AdminAccountLeaseView lease, bool online, AdminAccountSessionView[] recentSessions,
            AdminCharacterSummaryView[] otherCharacters)
        {
            _characterId         = characterId;
            _accountId           = accountId;
            _name                = name;
            _classId             = classId;
            _classDisplayName    = classDisplayName;
            _appearanceJson      = appearanceJson;
            _createdAtUnixMs     = createdAtUnixMs;
            _deletedAtUnixMs     = deletedAtUnixMs;
            _essence             = essence;
            _shards              = shards;
            _revision            = revision;
            _loadoutRevision     = loadoutRevision;
            _training            = training;
            _combatStats         = combatStats;
            _equipment           = equipment;
            _highestEnhanceLevel = highestEnhanceLevel;
            _talismanProgress    = talismanProgress;
            _talismanLoadout     = talismanLoadout;
            _accountProgress     = accountProgress;
            _life                = life;
            _wallet              = wallet;
            _items               = items;
            _lease               = lease;
            _online              = online;
            _recentSessions      = recentSessions;
            _otherCharacters     = otherCharacters;
        }

        private AdminCharacterDetailResponse() { }
    }
}
