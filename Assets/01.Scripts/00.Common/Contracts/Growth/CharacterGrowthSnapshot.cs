// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 캐릭터 성장 상태 전체. 마을 서버가 확인창과 다음 요청의 expectedRevision에 쓴다 </summary>
    [DataContract]
    public sealed class CharacterGrowthSnapshot
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "characterId")]      private string                 _characterId;
        [DataMember(Name = "classId")]          private int                    _classId;
        // 성장 쓰기가 성공할 때마다 1 오른다. 출정 예약의 characterRevision도 이 값이다
        [DataMember(Name = "loadoutRevision")]  private long                   _loadoutRevision;
        [DataMember(Name = "essence")]          private long                   _essence;
        [DataMember(Name = "shards")]           private long                   _shards;
        // ETrainingStat 순서의 다섯 투자 단계
        [DataMember(Name = "training")]         private int[]                  _training;
        [DataMember(Name = "equipment")]        private EquipmentView[]        _equipment;
        [DataMember(Name = "enhancementPity")]  private EnhancementPityView[]  _enhancementPity;
        [DataMember(Name = "talismans")]        private TalismanProgressView[] _talismans;
        [DataMember(Name = "loadout")]          private TalismanLoadoutEntry[] _loadout;
        [DataMember(Name = "accountItems")]     private GrowthItemView[]       _accountItems;
        // 착용하지 않은 장비 수
        [DataMember(Name = "storageUsed")]      private int                    _storageUsed;
        [DataMember(Name = "storageCapacity")]  private int                    _storageCapacity;
        // API가 규칙 판정에 쓴 성장 콘텐츠 버전
        [DataMember(Name = "contentVersion")]   private string                 _contentVersion;
        [DataMember(Name = "serverTimeUnixMs")] private long                   _serverTimeUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string                 CharacterId      => _characterId;
        public int                    ClassId          => _classId;
        public long                   LoadoutRevision  => _loadoutRevision;
        public long                   Essence          => _essence;
        public long                   Shards           => _shards;
        public int[]                  Training         => _training;
        public EquipmentView[]        Equipment        => _equipment;
        public EnhancementPityView[]  EnhancementPity  => _enhancementPity;
        public TalismanProgressView[] Talismans        => _talismans;
        public TalismanLoadoutEntry[] Loadout          => _loadout;
        public GrowthItemView[]       AccountItems     => _accountItems;
        public int                    StorageUsed      => _storageUsed;
        public int                    StorageCapacity  => _storageCapacity;
        public string                 ContentVersion   => _contentVersion;
        public long                   ServerTimeUnixMs => _serverTimeUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public CharacterGrowthSnapshot(string characterId, int classId, long loadoutRevision, long essence,
            long shards, int[] training, EquipmentView[] equipment, EnhancementPityView[] enhancementPity,
            TalismanProgressView[] talismans, TalismanLoadoutEntry[] loadout, GrowthItemView[] accountItems,
            int storageUsed, int storageCapacity, string contentVersion, long serverTimeUnixMs)
        {
            _characterId      = characterId;
            _classId          = classId;
            _loadoutRevision  = loadoutRevision;
            _essence          = essence;
            _shards           = shards;
            _training         = training;
            _equipment        = equipment;
            _enhancementPity  = enhancementPity;
            _talismans        = talismans;
            _loadout          = loadout;
            _accountItems     = accountItems;
            _storageUsed      = storageUsed;
            _storageCapacity  = storageCapacity;
            _contentVersion   = contentVersion;
            _serverTimeUnixMs = serverTimeUnixMs;
        }

        private CharacterGrowthSnapshot() { }
    }
}
