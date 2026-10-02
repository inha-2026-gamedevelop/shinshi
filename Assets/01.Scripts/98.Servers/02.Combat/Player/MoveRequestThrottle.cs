// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.Combat
{
    /// <summary> 참가자 한 명의 이동 요청 제한, 목적지 거리 상한과 같은 목적지 무시와 경로 재계산 최소 간격을 본다 </summary>
    public sealed class MoveRequestThrottle
    {
        // 경로 계산(NavMesh.CalculatePath)은 서버 틱에서 가장 비싼 일이라 클릭 폭주로 틱 예산을 다 쓰지 못하게 막는다.
        // 간격 안에 온 요청은 거절하지 않고 마지막 목적지만 남겨 간격이 지나면 계산한다

        /****************************************
        *                Fields
        ****************************************/

        private readonly float  _maxDistanceSqr;
        private readonly double _minInterval;
        private readonly float  _sameDestinationEpsilonSqr;

        private Vector3 _activeDestination = Vector3.zero;
        private bool    _hasActive         = false;
        private double  _nextRepathTime    = 0.0;

        /****************************************
        *              Properties
        ****************************************/

        public bool   HasActiveDestination => _hasActive;
        public double NextRepathTime       => _nextRepathTime;

        /****************************************
        *                Methods
        ****************************************/

        public MoveRequestThrottle(float maxDistance, double minInterval, float sameDestinationEpsilonSqr)
        {
            if ((!(maxDistance > 0f)) || (minInterval < 0.0) || (sameDestinationEpsilonSqr < 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(maxDistance), "거리 상한은 0보다 크고 간격, 오차는 0 이상이다");
            }

            _maxDistanceSqr            = maxDistance * maxDistance;
            _minInterval               = minInterval;
            _sameDestinationEpsilonSqr = sameDestinationEpsilonSqr;
        }

        /// <summary> Command 처리기에서 부른다, 거리 상한과 진행 중 경로와 같은 목적지인지 본다 </summary>
        public EMoveAdmission CheckDestination(Vector3 position, Vector3 destination, bool isMoving)
        {
            if ((destination - position).sqrMagnitude > _maxDistanceSqr)
            {
                return EMoveAdmission.TooFar;
            }

            if ((isMoving) && (_hasActive)
                && ((destination - _activeDestination).sqrMagnitude <= _sameDestinationEpsilonSqr))
            {
                return EMoveAdmission.SameDestination;
            }

            return EMoveAdmission.Accepted;
        }

        public bool IsRepathReady(double serverTime)
        {
            return serverTime >= _nextRepathTime;
        }

        /// <summary> 경로 계산을 시도한 틱에 부른다, 실패한 시도도 간격을 쓴다 </summary>
        public void MarkAttempt(double serverTime, bool hasPath, Vector3 destination)
        {
            _nextRepathTime    = serverTime + _minInterval;
            _hasActive         = hasPath;
            _activeDestination = destination;
        }

        public void ClearActive()
        {
            _hasActive = false;
        }
    }
}
