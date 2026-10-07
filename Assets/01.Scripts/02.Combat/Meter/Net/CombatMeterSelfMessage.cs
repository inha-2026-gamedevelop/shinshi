// System
using System;

// 서드파티
using Mirror;

namespace Shinshi.Combat
{
    // 소유자만 받는다. 서버가 갱신 주기마다 보스전 전체 누적과 현재 단계 값을 함께 보낸다
    /// <summary> 본인 딜미터의 주기 갱신 </summary>
    public readonly struct CombatMeterSelfMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly byte  _phase;
        private readonly float _phaseElapsedSeconds;
        private readonly float _encounterElapsedSeconds;
        private readonly float _activeSeconds;
        private readonly float _encounterDamage;
        private readonly float _phaseDamage;
        private readonly float _damageTaken;
        private readonly ushort _guardedHits;
        private readonly CombatMeterSlotStat[] _slots;

        /****************************************
        *              Properties
        ****************************************/

        public byte   Phase                   => _phase;
        public float  PhaseElapsedSeconds      => _phaseElapsedSeconds;
        public float  EncounterElapsedSeconds  => _encounterElapsedSeconds;
        public float  ActiveSeconds            => _activeSeconds;
        public float  EncounterDamage          => _encounterDamage;
        public float  PhaseDamage              => _phaseDamage;
        public float  DamageTaken              => _damageTaken;
        public ushort GuardedHits              => _guardedHits;

        // 길이는 Constants.CombatMeter.SLOT_COUNT, 인덱스는 (int)ECombatMeterSlot
        public CombatMeterSlotStat[] Slots => _slots ?? Array.Empty<CombatMeterSlotStat>();

        /****************************************
        *                Methods
        ****************************************/

        public CombatMeterSelfMessage(byte phase, float phaseElapsedSeconds, float encounterElapsedSeconds,
            float activeSeconds, float encounterDamage, float phaseDamage, float damageTaken, ushort guardedHits,
            CombatMeterSlotStat[] slots)
        {
            _phase                   = phase;
            _phaseElapsedSeconds     = phaseElapsedSeconds;
            _encounterElapsedSeconds = encounterElapsedSeconds;
            _activeSeconds           = activeSeconds;
            _encounterDamage         = encounterDamage;
            _phaseDamage             = phaseDamage;
            _damageTaken             = damageTaken;
            _guardedHits             = guardedHits;
            _slots                   = slots;
        }
    }
}
