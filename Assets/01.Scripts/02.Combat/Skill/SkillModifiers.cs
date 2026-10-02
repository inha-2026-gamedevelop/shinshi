namespace Shinshi.Combat
{
    /// <summary> 한 시전에서 고정하는 수정값, 추가 효과는 이 계약을 확장해 넣는다 </summary>
    public readonly struct SkillModifiers
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly float _energyScale;
        private readonly float _cooldownScale;
        private readonly float _damageScale;
        private readonly float _resonanceBonus;

        /****************************************
        *              Properties
        ****************************************/

        public float EnergyScale    => _energyScale;
        public float CooldownScale  => _cooldownScale;
        public float DamageScale    => _damageScale;
        public float ResonanceBonus => _resonanceBonus;

        public static SkillModifiers Identity => new SkillModifiers(1f, 1f, 1f, 0f);

        /****************************************
        *                Methods
        ****************************************/

        public SkillModifiers(float energyScale, float cooldownScale, float damageScale, float resonanceBonus)
        {
            _energyScale    = energyScale;
            _cooldownScale  = cooldownScale;
            _damageScale    = damageScale;
            _resonanceBonus = resonanceBonus;
        }
    }
}
