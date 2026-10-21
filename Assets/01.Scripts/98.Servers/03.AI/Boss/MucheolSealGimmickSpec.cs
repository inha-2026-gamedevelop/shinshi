// System
using System;

namespace Shinshi.AI
{
    // 발동 체력 비율, 인원별 타수, 방향 구역 각도는 밸런스 값이라 실행 코드에 두지 않고 호출하는 쪽(데이터)이 정한다
    /// <summary> 무철 방향 봉인 기믹의 설정, 발동 체력 비율과 방향마다 필요한 타수와 방향 구역 각도 </summary>
    public sealed class MucheolSealGimmickSpec
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int   MIN_HITS          = 1;
        private const int   MIN_STARTING      = 1;
        private const float MAX_ANGLE         = 180f;

        /****************************************
        *                Fields
        ****************************************/

        private readonly float _triggerHealthRate;
        private readonly int   _soloHits;
        private readonly int   _groupHits;
        private readonly float _frontHalfAngle;
        private readonly float _sideLimitAngle;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 체력 비율이 이 값 이하(경계 포함)이면서 0이 아닐 때 발동하고 그 순간 남은 체력에서 고정된다 </summary>
        public float TriggerHealthRate => _triggerHealthRate;

        /// <summary> 출정 시작 인원이 1명일 때 방향마다 필요한 타수 </summary>
        public int SoloHits => _soloHits;

        /// <summary> 출정 시작 인원이 2명 이상일 때 방향마다 필요한 타수 </summary>
        public int GroupHits => _groupHits;

        /// <summary> 도, 정면 구역 반각, 이 각도까지는 정면이다(경계 포함) </summary>
        public float FrontHalfAngle => _frontHalfAngle;

        /// <summary> 도, 측면 구역의 바깥 한계, 정면 반각 초과부터 이 각도까지가 측면이고 경계는 측면에 속한다 </summary>
        public float SideLimitAngle => _sideLimitAngle;

        /****************************************
        *                Methods
        ****************************************/

        public MucheolSealGimmickSpec(float triggerHealthRate, int soloHits, int groupHits, float frontHalfAngle, float sideLimitAngle)
        {
            if ((float.IsNaN(triggerHealthRate)) || (triggerHealthRate <= 0f) || (triggerHealthRate >= 1f))
            {
                throw new ArgumentOutOfRangeException(nameof(triggerHealthRate), "발동 체력 비율은 0 초과 1 미만이다");
            }

            if (soloHits < MIN_HITS)
            {
                throw new ArgumentOutOfRangeException(nameof(soloHits), "방향당 타수는 1 이상이다");
            }

            if (groupHits < MIN_HITS)
            {
                throw new ArgumentOutOfRangeException(nameof(groupHits), "방향당 타수는 1 이상이다");
            }

            if ((float.IsNaN(frontHalfAngle)) || (frontHalfAngle <= 0f) || (frontHalfAngle >= MAX_ANGLE))
            {
                throw new ArgumentOutOfRangeException(nameof(frontHalfAngle), "정면 반각은 0 초과 180 미만이다");
            }

            if ((float.IsNaN(sideLimitAngle)) || (sideLimitAngle <= frontHalfAngle) || (sideLimitAngle >= MAX_ANGLE))
            {
                throw new ArgumentOutOfRangeException(nameof(sideLimitAngle), "측면 한계는 정면 반각 초과 180 미만이다");
            }

            _triggerHealthRate = triggerHealthRate;
            _soloHits          = soloHits;
            _groupHits         = groupHits;
            _frontHalfAngle    = frontHalfAngle;
            _sideLimitAngle    = sideLimitAngle;
        }

        /// <summary> 출정 시작 인원에 따른 방향당 필요 타수, 1명이면 솔로 값이고 2명 이상이면 파티 값이다 </summary>
        public int GetHitsPerDirection(int startingCount)
        {
            if (startingCount < MIN_STARTING)
            {
                throw new ArgumentOutOfRangeException(nameof(startingCount), "시작 인원은 1 이상이다");
            }

            return (startingCount == MIN_STARTING) ? _soloHits : _groupHits;
        }
    }
}
