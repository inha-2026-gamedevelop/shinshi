namespace Shinshi.Network
{
    /// <summary> 참가자 전투 개체 하나의 체크포인트 값 </summary>
    public readonly struct CheckpointVitals
    {
        /****************************************
        *              Properties
        ****************************************/

        public float  Health     { get; }
        public float  MaxHealth  { get; }

        // 기력(기술 자원)과 공명(승천 자원)
        public float  Energy     { get; }
        public float  Resonance  { get; }

        // 사망 시각(서버 시각), 살아 있으면 의미 없다
        public double DeathTime  { get; }

        // 연결이 끊겨 소유권만 뗀 채 남은 개체다
        public bool   IsDetached { get; }

        /****************************************
        *                Methods
        ****************************************/

        public CheckpointVitals(float health, float maxHealth, float energy, float resonance, double deathTime, bool isDetached)
        {
            Health     = health;
            MaxHealth  = maxHealth;
            Energy     = energy;
            Resonance  = resonance;
            DeathTime  = deathTime;
            IsDetached = isDetached;
        }
    }
}
