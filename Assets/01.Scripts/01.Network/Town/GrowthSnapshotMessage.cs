// System
using System;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 백엔드 성장 스냅샷 DTO를 그대로 Mirror로 보내지 않고 화면에 필요한 값만 옮긴다. DTO는 HTTP 계약이라 필드가 늘면
    // 클라이언트 빌드와 맞지 않게 된다. 작업 확정 뒤나 조회 요청에만 보내는 드문 메시지라 정의 ID 문자열을 그대로 싣는다
    /// <summary> 내 캐릭터의 현재 성장 상태, 소유자에게만 온다 </summary>
    public readonly struct GrowthSnapshotMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly long _totalExperience;
        private readonly int                  _classId;
        private readonly long                 _loadoutRevision;
        private readonly long                 _essence;
        private readonly long                 _shards;
        private readonly int[]                _training;
        private readonly TownEquipmentEntry[] _equipment;
        private readonly TownPityEntry[]      _pity;
        private readonly TownTalismanEntry[]  _talismans;
        private readonly TownLoadoutEntry[]   _loadout;
        private readonly TownItemEntry[]      _accountItems;
        private readonly int                  _storageUsed;
        private readonly int                  _storageCapacity;
        private readonly string               _contentVersion;
        private readonly int[]                _optionPity;

        /****************************************
        *              Properties
        ****************************************/

        public long TotalExperience => _totalExperience;
        public int                  ClassId         => _classId;
        public long                 LoadoutRevision => _loadoutRevision;
        public long                 Essence         => _essence;
        public long                 Shards          => _shards;

        // ETrainingStat 순서의 투자 단계
        public int[]                Training        => _training ?? Array.Empty<int>();
        public TownEquipmentEntry[] Equipment       => _equipment ?? Array.Empty<TownEquipmentEntry>();
        public TownPityEntry[]      Pity            => _pity ?? Array.Empty<TownPityEntry>();
        public TownTalismanEntry[]  Talismans       => _talismans ?? Array.Empty<TownTalismanEntry>();
        public TownLoadoutEntry[]   Loadout         => _loadout ?? Array.Empty<TownLoadoutEntry>();
        public TownItemEntry[]      AccountItems    => _accountItems ?? Array.Empty<TownItemEntry>();
        public int                  StorageUsed     => _storageUsed;
        public int                  StorageCapacity => _storageCapacity;
        public string               ContentVersion  => _contentVersion;

        // EEquipmentSlot 순서의 옵션 재설정 누계, 상급 줄 없이 연속으로 재설정한 횟수. 값이 없으면 빈 배열이다
        public int[]                OptionPity      => _optionPity ?? Array.Empty<int>();

        /****************************************
        *                Methods
        ****************************************/

        public GrowthSnapshotMessage(int classId, long loadoutRevision, long essence, long shards, int[] training,
            TownEquipmentEntry[] equipment, TownPityEntry[] pity, TownTalismanEntry[] talismans, TownLoadoutEntry[] loadout,
            TownItemEntry[] accountItems, int storageUsed, int storageCapacity, string contentVersion, long totalExperience = 0,
            int[] optionPity = null)
        {
            _optionPity      = optionPity;
            _totalExperience = totalExperience;
            _classId         = classId;
            _loadoutRevision = loadoutRevision;
            _essence         = essence;
            _shards          = shards;
            _training        = training;
            _equipment       = equipment;
            _pity            = pity;
            _talismans       = talismans;
            _loadout         = loadout;
            _accountItems    = accountItems;
            _storageUsed     = storageUsed;
            _storageCapacity = storageCapacity;
            _contentVersion  = contentVersion;
        }
    }
}
