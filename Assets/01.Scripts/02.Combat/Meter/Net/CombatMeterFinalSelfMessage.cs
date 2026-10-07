// System
using System;

// 서드파티
using Mirror;

namespace Shinshi.Combat
{
    // 소유자만 받는다. 출정 종료 확정 때 한 번, 동결된 본인 상세를 보낸다. SessionEpoch가 같으면 다시 표시하지 않는다(CombatMeterClient)
    /// <summary> 본인 딜미터의 종료 확정 상세 </summary>
    public readonly struct CombatMeterFinalSelfMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int    _sessionEpoch;
        private readonly float  _encounterSeconds;
        private readonly float  _activeSeconds;
        private readonly float  _encounterDamage;
        private readonly float  _damageTaken;
        private readonly ushort _guardedHits;
        private readonly byte   _deaths;
        private readonly byte   _revives;
        private readonly byte   _potionsUsed;
        private readonly CombatMeterSlotStat[]      _slots;
        private readonly CombatMeterPhaseStat[]     _phases;
        private readonly CombatMeterSourceAmount[]  _topDamageTaken;
        private readonly CombatMeterDeathEntry[]    _lastDeathRecap;

        /****************************************
        *              Properties
        ****************************************/

        public int    SessionEpoch     => _sessionEpoch;
        public float  EncounterSeconds => _encounterSeconds;
        public float  ActiveSeconds    => _activeSeconds;
        public float  EncounterDamage  => _encounterDamage;
        public float  DamageTaken      => _damageTaken;
        public ushort GuardedHits      => _guardedHits;
        public byte   Deaths           => _deaths;
        public byte   Revives          => _revives;
        public byte   PotionsUsed      => _potionsUsed;

        public CombatMeterSlotStat[]     Slots          => _slots ?? Array.Empty<CombatMeterSlotStat>();
        public CombatMeterPhaseStat[]    Phases         => _phases ?? Array.Empty<CombatMeterPhaseStat>();
        public CombatMeterSourceAmount[] TopDamageTaken => _topDamageTaken ?? Array.Empty<CombatMeterSourceAmount>();
        public CombatMeterDeathEntry[]   LastDeathRecap => _lastDeathRecap ?? Array.Empty<CombatMeterDeathEntry>();

        /****************************************
        *                Methods
        ****************************************/

        public CombatMeterFinalSelfMessage(int sessionEpoch, float encounterSeconds, float activeSeconds,
            float encounterDamage, float damageTaken, ushort guardedHits, byte deaths, byte revives, byte potionsUsed,
            CombatMeterSlotStat[] slots, CombatMeterPhaseStat[] phases, CombatMeterSourceAmount[] topDamageTaken,
            CombatMeterDeathEntry[] lastDeathRecap)
        {
            _sessionEpoch     = sessionEpoch;
            _encounterSeconds = encounterSeconds;
            _activeSeconds    = activeSeconds;
            _encounterDamage  = encounterDamage;
            _damageTaken      = damageTaken;
            _guardedHits      = guardedHits;
            _deaths           = deaths;
            _revives          = revives;
            _potionsUsed      = potionsUsed;
            _slots            = slots;
            _phases           = phases;
            _topDamageTaken   = topDamageTaken;
            _lastDeathRecap   = lastDeathRecap;
        }
    }
}
