// System
using System;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 출정 계약(종류, 맵, 전투, 보스, 난이도), 던전 호스트가 첫 입장 토큰에서 고정하고 이후 토큰과 대조한다 </summary>
    public readonly struct SortieDescriptor
    {
        // 예약 때 마을이 확정해 입장 교환 응답에 실린 값이다. 클라이언트 값이 아니므로 던전은 이 값으로 전투를 연다.
        // default는 계약 없음(개발 토큰, mode를 모르는 API)이다. mode 0(일반전)과 섞이지 않게 계약 여부를 따로 둔다

        /****************************************
        *                Fields
        ****************************************/

        private readonly bool   _hasContract;
        private readonly int    _mode;
        private readonly string _mapId;
        private readonly string _battleId;
        private readonly string _bossId;
        private readonly int    _difficulty;

        /****************************************
        *              Properties
        ****************************************/

        public bool   HasContract => _hasContract;

        // GameSessionCodes.Mode, 계약이 없으면 JoinTokenExchangeResponse.NO_MODE
        public int    Mode        => _hasContract ? _mode : JoinTokenExchangeResponse.NO_MODE;
        public string MapId       => _mapId;
        public string BattleId    => _battleId;

        // 보스전만 값이 있다
        public string BossId      => _bossId;

        // GameSessionCodes.Difficulty, 보스전이 아니면 NONE
        public int    Difficulty  => _hasContract ? _difficulty : GameSessionCodes.Difficulty.NONE;

        public bool   IsNormal    => (_hasContract) && (_mode == GameSessionCodes.Mode.NORMAL);
        public bool   IsBoss      => (_hasContract) && (_mode == GameSessionCodes.Mode.BOSS);

        // 보스전의 난이도, 보스전이 아니면 쉬움으로 본다
        public EBossDifficulty BossDifficulty => (IsBoss) && (_difficulty == GameSessionCodes.Difficulty.HARD)
            ? EBossDifficulty.Hard
            : EBossDifficulty.Easy;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 계약 값으로 만든다, 형식 검사는 JoinTokenClaimsRules가 먼저 한다 </summary>
        public SortieDescriptor(int mode, string mapId, string battleId, string bossId, int difficulty)
        {
            _hasContract = true;
            _mode        = mode;
            _mapId       = mapId;
            _battleId    = battleId;
            _bossId      = bossId;
            _difficulty  = difficulty;
        }

        /// <summary> 같은 출정의 계약인지 본다, 계약 없음끼리도 같다 </summary>
        public bool Matches(in SortieDescriptor other)
        {
            if (_hasContract != other._hasContract)
            {
                return false;
            }

            if (!_hasContract)
            {
                return true;
            }

            return (_mode == other._mode) && (_difficulty == other._difficulty)
                && (string.Equals(_mapId, other._mapId, StringComparison.Ordinal))
                && (string.Equals(_battleId, other._battleId, StringComparison.Ordinal))
                && (string.Equals(_bossId, other._bossId, StringComparison.Ordinal));
        }
    }
}
