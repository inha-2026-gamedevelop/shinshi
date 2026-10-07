// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 수련 투자 단계와 DB CHECK에 맞는 항목 및 합계 상한 </summary>
    [DataContract]
    public sealed class AdminCharacterTrainingView
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "attack")]   private int _attack;
        [DataMember(Name = "health")]   private int _health;
        [DataMember(Name = "defense")]  private int _defense;
        [DataMember(Name = "crit")]     private int _crit;
        [DataMember(Name = "energy")]   private int _energy;
        [DataMember(Name = "total")]    private int _total;
        [DataMember(Name = "itemCap")]  private int _itemCap;
        [DataMember(Name = "totalCap")] private int _totalCap;

        /****************************************
        *              Properties
        ****************************************/

        public int Attack   => _attack;
        public int Health   => _health;
        public int Defense  => _defense;
        public int Crit     => _crit;
        public int Energy   => _energy;
        public int Total    => _total;
        public int ItemCap  => _itemCap;
        public int TotalCap => _totalCap;

        /****************************************
        *                Methods
        ****************************************/

        public AdminCharacterTrainingView(int attack, int health, int defense, int crit, int energy, int total,
            int itemCap, int totalCap)
        {
            _attack   = attack;
            _health   = health;
            _defense  = defense;
            _crit     = crit;
            _energy   = energy;
            _total    = total;
            _itemCap  = itemCap;
            _totalCap = totalCap;
        }

        private AdminCharacterTrainingView() { }
    }
}
