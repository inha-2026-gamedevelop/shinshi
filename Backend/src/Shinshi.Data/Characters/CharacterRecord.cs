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
        public string? AppearanceJson { get; }

        /****************************************
        *                Methods
        ****************************************/

        public CharacterRecord(Guid characterId, Guid accountId, string? name, int classId, long revision,
            string? appearanceJson)
        {
            CharacterId    = characterId;
            AccountId      = accountId;
            Name           = name;
            ClassId        = classId;
            Revision       = revision;
            AppearanceJson = appearanceJson;
        }
    }
}
