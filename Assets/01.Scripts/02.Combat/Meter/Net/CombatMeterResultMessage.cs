// System
using System;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    // 접속 중인 참가자 전원이 받는다. 출정 종료 확정 때 한 번. SessionEpoch가 같으면 다시 표시하지 않는다(CombatMeterClient).
    // 파티원 행은 피해 내림차순이 아니라 서버 명단 순서로 온다(설계 7장 결정 2, 순위 인상을 줄이기 위함)
    /// <summary> 결과 화면용 파티 딜미터 </summary>
    public readonly struct CombatMeterResultMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int                 _sessionEpoch;
        private readonly ESessionEndReason    _reason;
        private readonly float                _encounterSeconds;
        private readonly CombatMeterResultEntry[] _entries;
        private readonly CombatMeterPhaseStat[]   _phaseTotals;

        /****************************************
        *              Properties
        ****************************************/

        public int              SessionEpoch     => _sessionEpoch;
        public ESessionEndReason Reason          => _reason;
        public float             EncounterSeconds => _encounterSeconds;

        public CombatMeterResultEntry[] Entries     => _entries ?? Array.Empty<CombatMeterResultEntry>();
        public CombatMeterPhaseStat[]   PhaseTotals => _phaseTotals ?? Array.Empty<CombatMeterPhaseStat>();

        /****************************************
        *                Methods
        ****************************************/

        public CombatMeterResultMessage(int sessionEpoch, ESessionEndReason reason, float encounterSeconds,
            CombatMeterResultEntry[] entries, CombatMeterPhaseStat[] phaseTotals)
        {
            _sessionEpoch     = sessionEpoch;
            _reason           = reason;
            _encounterSeconds = encounterSeconds;
            _entries          = entries;
            _phaseTotals      = phaseTotals;
        }
    }
}
