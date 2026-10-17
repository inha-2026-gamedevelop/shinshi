// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 배치(BossGridBeamLayout)의 값을 풀어 실었다. 가로 LineCount줄이 먼저고 세로 LineCount줄이 뒤다.
    // 가로줄은 월드 -X로 뻗고 번호가 커질수록 Z가 커지며, 세로줄은 월드 +Z로 뻗고 번호가 커질수록 X가 커진다
    /// <summary> 하드 2페이즈 P02 격자 순차 빔 한 시전의 입력 </summary>
    public readonly struct BossGridBeamStart
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int     _bossId;
        private readonly int     _castId;
        private readonly Vector3 _arenaCenter;
        private readonly float   _arenaRadius;
        private readonly int     _lineCount;
        private readonly float   _lineWidth;
        private readonly float   _lineSpacing;
        private readonly double  _castStartServerTime;
        private readonly float   _firstStrikeSeconds;
        private readonly float   _strikeIntervalSeconds;
        private readonly float   _axisGapSeconds;
        private readonly int     _firstLineId;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 보스의 전투 번호(CombatId), 정의 ID(boss_mucheol 같은 문자열)가 아니다 </summary>
        public int BossId => _bossId;

        /// <summary> 서버가 시전마다 발급한 시전 번호 </summary>
        public int CastId => _castId;

        public Vector3 ArenaCenter => _arenaCenter;

        /// <summary> m </summary>
        public float ArenaRadius => _arenaRadius;

        /// <summary> 한 방향의 줄 수 </summary>
        public int LineCount => _lineCount;

        /// <summary> m, 줄 하나의 띠 폭 </summary>
        public float LineWidth => _lineWidth;

        /// <summary> m, 이웃한 줄의 중심선 사이 거리 </summary>
        public float LineSpacing => _lineSpacing;

        public double CastStartServerTime => _castStartServerTime;

        public float FirstStrikeSeconds => _firstStrikeSeconds;

        public float StrikeIntervalSeconds => _strikeIntervalSeconds;

        /// <summary> 가로 마지막 타격에서 세로 첫 타격까지(초) </summary>
        public float AxisGapSeconds => _axisGapSeconds;

        // 서버는 한 시전의 줄 번호를 발사 순서대로 이어서 발급한다. 발사 순서 k의 줄 번호는 FirstLineId + k다
        /// <summary> 발사 순서 0번 줄의 서버 발급 번호, 0이면 모른다 </summary>
        public int FirstLineId => _firstLineId;

        /****************************************
        *                Methods
        ****************************************/

        public BossGridBeamStart(int bossId, int castId, Vector3 arenaCenter, float arenaRadius, int lineCount,
            float lineWidth, float lineSpacing, double castStartServerTime, float firstStrikeSeconds,
            float strikeIntervalSeconds, float axisGapSeconds, int firstLineId = 0)
        {
            _bossId                = bossId;
            _castId                = castId;
            _arenaCenter           = arenaCenter;
            _arenaRadius           = arenaRadius;
            _lineCount             = lineCount;
            _lineWidth             = lineWidth;
            _lineSpacing           = lineSpacing;
            _castStartServerTime   = castStartServerTime;
            _firstStrikeSeconds    = firstStrikeSeconds;
            _strikeIntervalSeconds = strikeIntervalSeconds;
            _axisGapSeconds        = axisGapSeconds;
            _firstLineId           = firstLineId;
        }
    }
}
