// System
using System;

namespace Shinshi.Data.Characters
{
    /// <summary> 저장된 캐릭터 한 행. 이름과 외형은 생성 API 이전에 만든 행이면 null이다 </summary>
    public sealed class CharacterRecord
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid CharacterId { get; }
        public Guid AccountId { get; }
        public string? Name { get; }
        public int ClassId { get; }
        public long Revision { get; }
        public long TotalExperience { get; }
        public string? AppearanceJson { get; }
        public bool DeletePending { get; }
        public long DeleteRemainingSeconds { get; }

        /****************************************
        *                Methods
        ****************************************/

        public CharacterRecord(Guid characterId, Guid accountId, string? name, int classId, long revision,
            string? appearanceJson, long totalExperience = 0, bool deletePending = false, long deleteRemainingSeconds = 0)
        {
            DeletePending          = deletePending;
            DeleteRemainingSeconds = deleteRemainingSeconds;
            TotalExperience = totalExperience;
            CharacterId    = characterId;
            AccountId      = accountId;
            Name           = name;
            ClassId        = classId;
            Revision       = revision;
            AppearanceJson = appearanceJson;
        }
    }
}
