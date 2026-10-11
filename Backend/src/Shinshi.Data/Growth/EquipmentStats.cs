namespace Shinshi.Data.Growth
{
    /// <summary> 장비 능력치 한 벌 </summary>
    public readonly struct EquipmentStats
    {
        /****************************************
        *              Properties
        ****************************************/

        public decimal Attack     { get; }
        public decimal Health     { get; }
        public decimal Defense    { get; }
        public decimal CritChance { get; }
        public decimal LifeSteal  { get; }

        /****************************************
        *                Methods
        ****************************************/

        public EquipmentStats(decimal attack, decimal health, decimal defense, decimal critChance, decimal lifeSteal = 0m)
        {
            Attack     = attack;
            Health     = health;
            Defense    = defense;
            CritChance = critChance;
            LifeSteal  = lifeSteal;
        }

        /// <summary> 공격, 생명, 방어가 모두 other 이상인지 본다. 고정 치명과 흡수는 비교하지 않는다 </summary>
        public bool CoversCoreStats(EquipmentStats other)
        {
            return (Attack >= other.Attack) && (Health >= other.Health) && (Defense >= other.Defense);
        }
    }
}
