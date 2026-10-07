// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 전투 능력치 하나의 기본값, 보너스와 최종값 </summary>
    [DataContract]
    public sealed class AdminCharacterCombatStatView
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "base")]  private double _base;
        [DataMember(Name = "bonus")] private double _bonus;
        [DataMember(Name = "final")] private double _final;

        /****************************************
        *              Properties
        ****************************************/

        public double Base  => _base;
        public double Bonus => _bonus;
        public double Final => _final;

        /****************************************
        *                Methods
        ****************************************/

        public AdminCharacterCombatStatView(double @base, double bonus, double final)
        {
            _base  = @base;
            _bonus = bonus;
            _final = final;
        }

        private AdminCharacterCombatStatView() { }
    }
}
