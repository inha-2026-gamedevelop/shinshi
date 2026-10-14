// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 배치(BossCircleBurstLayout)가 계산한 원의 중심과 반경을 그대로 싣는다. 0번은 보스 중심 원이다.
    // 폭발 예정 시각은 CastStartServerTime + StrikeSeconds이고 모든 원이 같은 시각에 터진다
    /// <summary> 하드 2페이즈 P06 동시 원형 폭발 한 시전의 입력 </summary>
    public readonly struct BossCircleBurstStart
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int       _bossId;
        private readonly int       _castId;
        private readonly Vector3[] _centers;
        private readonly float[]   _radii;
        private readonly double    _castStartServerTime;
        private readonly float     _strikeSeconds;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 보스의 전투 번호(CombatId), 정의 ID(boss_mucheol 같은 문자열)가 아니다 </summary>
        public int BossId => _bossId;

        /// <summary> 서버가 시전마다 발급한 시전 번호 </summary>
        public int CastId => _castId;

        /// <summary> 원별 중심(월드) </summary>
        public IReadOnlyList<Vector3> Centers => _centers;

        /// <summary> 원별 반경(m) </summary>
        public IReadOnlyList<float> Radii => _radii;

        public double CastStartServerTime => _castStartServerTime;

        public float StrikeSeconds => _strikeSeconds;

        /// <summary> 원 수 </summary>
        public int CircleCount => (_centers != null) ? _centers.Length : 0;

        /****************************************
        *                Methods
        ****************************************/

        public BossCircleBurstStart(int bossId, int castId, Vector3[] centers, float[] radii, double castStartServerTime,
            float strikeSeconds)
        {
            _bossId              = bossId;
            _castId              = castId;
            _centers             = centers;
            _radii               = radii;
            _castStartServerTime = castStartServerTime;
            _strikeSeconds       = strikeSeconds;
        }
    }
}
