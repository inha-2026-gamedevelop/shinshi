// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 백엔드에 완전한 계산 콘텐츠가 있을 때 제공할 전투 능력치 </summary>
    [DataContract]
    public sealed class AdminCharacterCombatStatsView
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "attack")]  private AdminCharacterCombatStatView _attack;
        [DataMember(Name = "health")]  private AdminCharacterCombatStatView _health;
        [DataMember(Name = "defense")] private AdminCharacterCombatStatView _defense;
        [DataMember(Name = "crit")]    private AdminCharacterCombatStatView _crit;
        [DataMember(Name = "energy")]  private AdminCharacterCombatStatView _energy;

        /****************************************
        *              Properties
        ****************************************/

        public AdminCharacterCombatStatView Attack  => _attack;
        public AdminCharacterCombatStatView Health  => _health;
        public AdminCharacterCombatStatView Defense => _defense;
        public AdminCharacterCombatStatView Crit    => _crit;
        public AdminCharacterCombatStatView Energy  => _energy;

        /****************************************
        *                Methods
        ****************************************/

        public AdminCharacterCombatStatsView(AdminCharacterCombatStatView attack,
            AdminCharacterCombatStatView health, AdminCharacterCombatStatView defense,
            AdminCharacterCombatStatView crit, AdminCharacterCombatStatView energy)
        {
            _attack  = attack;
            _health  = health;
            _defense = defense;
            _crit    = crit;
            _energy  = energy;
        }

        private AdminCharacterCombatStatsView() { }
    }
}
