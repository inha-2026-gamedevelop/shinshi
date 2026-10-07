// System
using System;

// 서드파티
using Mirror;

namespace Shinshi.Combat
{
    // 접속 중인 참가자 전원이 받는다. 접힘, 펼침 여부와 무관하게 항상 보낸다(설계 6장, 펼치는 즉시 표시하기 위함).
    // DPS, 점유율은 클라이언트가 이 합계로 계산해 표시만 한다
    /// <summary> 파티 딜미터의 주기 갱신 </summary>
    public readonly struct CombatMeterPartyMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly byte  _phase;
        private readonly float _phaseElapsedSeconds;
        private readonly float _encounterElapsedSeconds;
        private readonly CombatMeterPartyEntry[] _entries;

        /****************************************
        *              Properties
        ****************************************/

        public byte  Phase                  => _phase;
        public float PhaseElapsedSeconds     => _phaseElapsedSeconds;
        public float EncounterElapsedSeconds => _encounterElapsedSeconds;

        // 서버 명단 순서, 최대 Constants.CombatMeter.MAX_PARTICIPANTS
        public CombatMeterPartyEntry[] Entries => _entries ?? Array.Empty<CombatMeterPartyEntry>();

        /****************************************
        *                Methods
        ****************************************/

        public CombatMeterPartyMessage(byte phase, float phaseElapsedSeconds, float encounterElapsedSeconds,
            CombatMeterPartyEntry[] entries)
        {
            _phase                   = phase;
            _phaseElapsedSeconds     = phaseElapsedSeconds;
            _encounterElapsedSeconds = encounterElapsedSeconds;
            _entries                 = entries;
        }
    }
}
