namespace Shinshi.Common
{
    /// <summary> 로컬 플레이어의 연속 게이지 값, 이벤트 없이 매 프레임 조회해서 그린다 </summary>
    public readonly struct LocalPlayerGauges
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float EMPTY_RATIO = 0f;
        private const float FULL_RATIO  = 1f;

        /****************************************
        *                Fields
        ****************************************/

        private readonly float _health;
        private readonly float _maxHealth;
        private readonly float _energy;
        private readonly float _maxEnergy;
        private readonly float _resonance;
        private readonly float _maxResonance;
        private readonly bool  _hasResonance;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 캐릭터에 묶이지 않았을 때의 값, 모든 게이지가 0이다 </summary>
        public static LocalPlayerGauges Empty => default;

        public float Health       => _health;
        public float MaxHealth    => _maxHealth;
        public float Energy       => _energy;
        public float MaxEnergy    => _maxEnergy;
        public float Resonance    => _resonance;
        public float MaxResonance => _maxResonance;

        // 공명은 신령사만 쓴다. false면 공명 게이지를 숨긴다
        public bool HasResonance => _hasResonance;

        public float HealthRatio    => ToRatio(_health, _maxHealth);
        public float EnergyRatio    => ToRatio(_energy, _maxEnergy);
        public float ResonanceRatio => ToRatio(_resonance, _maxResonance);

        /****************************************
        *                Methods
        ****************************************/

        public LocalPlayerGauges(float health, float maxHealth, float energy, float maxEnergy,
            float resonance, float maxResonance, bool hasResonance)
        {
            _health       = health;
            _maxHealth    = maxHealth;
            _energy       = energy;
            _maxEnergy    = maxEnergy;
            _resonance    = resonance;
            _maxResonance = maxResonance;
            _hasResonance = hasResonance;
        }

        // 최대값이 0인 빈 게이지에서 0으로 나누지 않는다
        private static float ToRatio(float current, float max)
        {
            if ((max <= 0f) || (current <= 0f))
            {
                return EMPTY_RATIO;
            }

            return (current >= max) ? FULL_RATIO : (current / max);
        }
    }
}
