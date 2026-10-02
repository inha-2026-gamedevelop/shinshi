// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 재접속으로 기력, 공명과 기술 쿨다운을 초기화하지 않기 위한 값 스냅샷 </summary>
    public readonly struct SkillCombatSnapshot
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly float  _energy;
        private readonly float  _resonance;
        private readonly double _q;
        private readonly double _w;
        private readonly double _e;
        private readonly double _r;
        private readonly double _ascension;

        /****************************************
        *              Properties
        ****************************************/

        public float  Energy           => _energy;
        public float  Resonance        => _resonance;
        public double AscensionReadyAt => _ascension;

        /****************************************
        *                Methods
        ****************************************/

        public SkillCombatSnapshot(float energy, float resonance, double q, double w, double e, double r, double ascension)
        {
            _energy    = energy;
            _resonance = resonance;
            _q         = q;
            _w         = w;
            _e         = e;
            _r         = r;
            _ascension = ascension;
        }

        public double GetReadyAt(int slot)
        {
            switch (slot)
            {
                case Constants.Combat.SKILL_SLOT_Q:
                    return _q;
                case Constants.Combat.SKILL_SLOT_W:
                    return _w;
                case Constants.Combat.SKILL_SLOT_E:
                    return _e;
                case Constants.Combat.SKILL_SLOT_R:
                    return _r;
                default:
                    return double.PositiveInfinity;
            }
        }
    }
}
