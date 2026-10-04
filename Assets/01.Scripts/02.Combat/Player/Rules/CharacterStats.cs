namespace Shinshi.Combat
{
    /// <summary> 육체, 장비, 수련을 합친 출정 최종 능력치, 출정 중에는 바뀌지 않는다 </summary>
    public readonly struct CharacterStats
    {
        // 임시 부적, 버프의 증감은 이 값 위에 따로 얹는다. 이 값 자체를 다시 수련의 기초로 쓰지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private readonly float _attack;
        private readonly float _maxHealth;
        private readonly float _defense;
        private readonly float _critChance;
        private readonly float _maxEnergy;
        private readonly float _moveSpeed;

        /****************************************
        *              Properties
        ****************************************/

        // 소수 유지, 기술 계수와 곱하는 A
        public float Attack     => _attack;

        // 버림 정수, 물약 회복과 부활 체력의 기준
        public float MaxHealth  => _maxHealth;
        public float Defense    => _defense;

        // 0에서 1 사이 비율, 상한을 이미 적용했다
        public float CritChance => _critChance;

        // 버림 정수
        public float MaxEnergy  => _maxEnergy;

        // m/s
        public float MoveSpeed  => _moveSpeed;

        /****************************************
        *                Methods
        ****************************************/

        public CharacterStats(float attack, float maxHealth, float defense, float critChance, float maxEnergy, float moveSpeed)
        {
            _attack     = attack;
            _maxHealth  = maxHealth;
            _defense    = defense;
            _critChance = critChance;
            _maxEnergy  = maxEnergy;
            _moveSpeed  = moveSpeed;
        }
    }
}
