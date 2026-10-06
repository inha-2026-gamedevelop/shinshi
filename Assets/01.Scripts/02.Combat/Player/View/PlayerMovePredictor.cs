// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 소유 클라이언트의 클릭 이동 예측과 서버 확정 위치 보정, 내비 메시 조회와 Transform 쓰기는 호출자가 한다 </summary>
    public sealed class PlayerMovePredictor
    {
        // 클라이언트는 이동을 보내는 순간 움직이고 서버는 지연 뒤에 같은 경로를 시작한다. 그래서 "지금" 위치끼리 비교하면
        // 이동 중에는 늘 지연만큼 차이가 나서 계속 뒤로 당겨진다. 대신 서버가 알려 준 "이 이동을 시작하고 몇 초 지난 위치"를
        // 클라이언트가 같은 이동을 시작하고 같은 시간이 지났을 때 보여 준 위치와 비교한다. 경로가 같으면 차이는 0에 가깝고,
        // 남는 차이(경로 재계산 간격, 공격으로 멈춘 시점, 다른 출발점)만 부드럽게 줄인다

        /****************************************
        *                Fields
        ****************************************/

        private readonly PathFollower      _path;
        private readonly PredictionHistory _history;

        private bool    _hasRecord        = false;
        private uint    _recordSequence   = 0;
        private double  _recordStartTime  = 0.0;
        private bool    _isRecordResolved = false;
        private bool    _hasDestination   = false;
        private Vector3 _destination      = Vector3.zero;
        private bool    _wasInterrupted   = false;
        private double  _lastStepTime     = 0.0;
        private Vector3 _moveDirection    = Vector3.zero;
        private double  _ackTimeout       = Constants.Player.PREDICTION_MOVE_ACK_TIMEOUT_SECONDS;

        // 예측 품질 측정값. 보정 거리가 예측 이동 거리에 비해 작을수록, 스냅이 없을수록 체감이 매끄럽다
        private float _travelledDistance = 0f;
        private float _correctedDistance = 0f;
        private int   _snapCount         = 0;

        /****************************************
        *              Properties
        ****************************************/

        public float TravelledDistance => _travelledDistance;
        public float CorrectedDistance => _correctedDistance;
        public int   SnapCount         => _snapCount;

        // MovePathRules.TryBeginPath에 넘겨 경로를 채운다
        public PathFollower Path => _path;

        public bool    IsMoving      => _path.IsMoving;
        public Vector3 MoveDirection => _moveDirection;
        public Vector3 Destination   => _destination;

        // 공격, 기술 요청으로 경로 도중에 멈췄다. 서버가 그 요청을 거절하면 이어서 간다
        public bool WasInterrupted => _wasInterrupted;

        /****************************************
        *                Methods
        ****************************************/

        public PlayerMovePredictor(int cornerCapacity, int historyCapacity, double historyInterval)
        {
            _path    = new PathFollower(cornerCapacity);
            _history = new PredictionHistory(historyCapacity, historyInterval);
        }

        /// <summary> 예측을 처음부터 시작한다, 소유권을 얻거나 잃을 때 부른다 </summary>
        public void Reset(double now)
        {
            _path.Stop();
            _history.Clear();
            _hasRecord        = false;
            _recordSequence   = 0;
            _recordStartTime  = 0.0;
            _isRecordResolved = false;
            _hasDestination   = false;
            _wasInterrupted   = false;
            _lastStepTime     = now;
            _moveDirection    = Vector3.zero;
        }

        /// <summary> 측정한 왕복 지연(초)으로 이동 응답을 기다리는 시간을 늘린다, 지연이 튀는 동안 아직 오는 중인 이동을 잃은 것으로 보지 않게 한다 </summary>
        public void SetRoundTripTime(double roundTripTime)
        {
            // 기록보다 오래 기다리면 같은 시점끼리 비교할 수 없으므로 기록 길이에서 자른다
            double scaled = roundTripTime * Constants.Player.PREDICTION_MOVE_ACK_RTT_MULTIPLIER;
            double atLeast = Math.Max(Constants.Player.PREDICTION_MOVE_ACK_TIMEOUT_SECONDS, scaled);
            _ackTimeout = Math.Min(atLeast, Constants.Player.PREDICTION_MOVE_ACK_TIMEOUT_MAX_SECONDS);
        }

        /// <summary> 진행 중인 경로와 같은 목적지인지 본다, 서버도 같은 목적지는 다시 계산하지 않고 순번을 남기지 않는다 </summary>
        public bool IsSameActiveDestination(Vector3 destination)
        {
            // 클라이언트는 서버보다 먼저 도착한다. 도착 뒤 같은 곳을 다시 누르면 서버는 아직 가는 중이라 순번 없이 버리므로,
            // 서버와 짝짓는 중인 이동도 진행 중으로 본다. 공격으로 멈춘 경로는 서버도 멈췄으므로 다시 계산한다
            bool isTracking = (_path.IsMoving) || ((_hasRecord) && (!_isRecordResolved) && (!_wasInterrupted));
            return (isTracking) && (_hasDestination)
                && ((destination - _destination).sqrMagnitude <= Constants.Player.MOVE_SAME_DESTINATION_EPSILON_SQR);
        }

        /// <summary> 경로를 채운 뒤 부른다, 이 이동 순번과 시작 시각이 서버 확정 상태와 비교하는 기준이 된다 </summary>
        public void BeginMove(uint sequence, Vector3 destination, double now)
        {
            _hasRecord        = true;
            _recordSequence   = sequence;
            _recordStartTime  = now;
            _isRecordResolved = false;
            _hasDestination   = true;
            _destination      = destination;
            _wasInterrupted   = false;
            _lastStepTime     = now;
        }

        /// <summary> 공격, 기술을 보낼 때 부른다, 서버도 그 요청을 처리하는 틱에 경로를 멈춘다 </summary>
        public void InterruptMove()
        {
            _wasInterrupted = _path.IsMoving;
            _path.Stop();
        }

        /// <summary> 멈추게 한 요청이 거절된 뒤 다시 채운 경로로 이어 갈 때 부른다, 비교 기준 시각은 원래 이동의 것을 유지한다 </summary>
        public void ClearInterrupt()
        {
            _wasInterrupted = false;
        }

        /// <summary> 기록한 이동을 더는 서버와 짝짓지 않는다, 거절되었거나 서버가 위치를 정하는 구간에 들어갔을 때 부른다 </summary>
        public void ResolveRecord()
        {
            _isRecordResolved = true;
            _wasInterrupted   = false;
            _path.Stop();
        }

        public bool IsRecord(uint sequence)
        {
            return (_hasRecord) && (_recordSequence == sequence);
        }

        /// <summary> 지난 호출 뒤 흐른 시간만큼 경로를 따라가고 결과를 기록한다 </summary>
        public Vector3 Advance(Vector3 position, float speed, double now)
        {
            double elapsed = now - _lastStepTime;
            _lastStepTime  = now;
            _moveDirection = Vector3.zero;

            if ((_path.IsMoving) && (speed > 0f) && (elapsed > 0.0))
            {
                Vector3 before = position;
                position = _path.Step(position, speed * (float)elapsed, out Vector3 direction, out bool moved);
                if (moved)
                {
                    _moveDirection     = direction;
                    _travelledDistance += Vector3.Distance(before, position);
                }
            }

            _history.Record(now, position);
            return position;
        }

        /// <summary> 서버 확정 상태로 이번 프레임 보정을 한 위치, 비교할 수 없는 상태면 그대로 돌려준다 </summary>
        public Vector3 Reconcile(in PlayerMoveAuthority authority, Vector3 position, double now, float deltaTime)
        {
            if (!authority.IsValid)
            {
                return position;
            }

            if (!TryMeasureError(in authority, position, now, out Vector3 error, out bool isPending))
            {
                // 보낸 이동이 아직 서버에 반영되지 않았으면 차이가 나는 게 정상이다
                if (isPending)
                {
                    return position;
                }

                // 짝지을 이동이 없고 한쪽만 움직이는 중이면 차이가 클 때만 맞춘다
                Vector3 gap = authority.Position - position;
                if (gap.sqrMagnitude > (Constants.Player.PREDICTION_SNAP_DISTANCE * Constants.Player.PREDICTION_SNAP_DISTANCE))
                {
                    ++_snapCount;
                    return ApplyCorrection(position, gap);
                }

                return position;
            }

            float distance = error.magnitude;
            if (distance > Constants.Player.PREDICTION_SNAP_DISTANCE)
            {
                ++_snapCount;
                return ApplyCorrection(position, error);
            }

            if (distance < Constants.Player.PREDICTION_CORRECTION_DEADZONE)
            {
                return position;
            }

            // 기록을 함께 옮기므로 다음 프레임에는 남은 차이만 다시 잰다. 그래서 보정량을 따로 쌓아 두지 않는다
            float blend = 1f - Mathf.Exp(-Constants.Player.PREDICTION_CORRECTION_RATE * deltaTime);
            return ApplyCorrection(position, error * blend);
        }

        /// <summary> 기술, 사망처럼 서버가 위치를 정하는 동안 서버 위치를 따라간 위치 </summary>
        public Vector3 Follow(in PlayerMoveAuthority authority, Vector3 position, float deltaTime)
        {
            if (!authority.IsValid)
            {
                return position;
            }

            Vector3 gap = authority.Position - position;
            if (gap.sqrMagnitude > (Constants.Player.PREDICTION_SNAP_DISTANCE * Constants.Player.PREDICTION_SNAP_DISTANCE))
            {
                return authority.Position;
            }

            float blend = 1f - Mathf.Exp(-Constants.Player.PREDICTION_FOLLOW_RATE * deltaTime);
            return position + (gap * blend);
        }

        private bool TryMeasureError(in PlayerMoveAuthority authority, Vector3 position, double now, out Vector3 error,
            out bool isPending)
        {
            error     = Vector3.zero;
            isPending = false;
            bool isSettled = (!authority.IsMoving) && (!_path.IsMoving);

            if ((_hasRecord) && (!_isRecordResolved) && (authority.MoveSequence == _recordSequence))
            {
                double matchTime = _recordStartTime + authority.MoveElapsed;

                // 서버가 이 이동을 클라이언트보다 오래 진행했다. 기술 뒤로 미뤄 시작한 이동처럼 클라이언트가 늦게 시작한 경우다
                if (matchTime >= now)
                {
                    error = authority.Position - position;
                    return true;
                }

                if (_history.TrySample(matchTime, out Vector3 shown))
                {
                    error = authority.Position - shown;
                    return true;
                }

                // 기록이 덮어쓰일 만큼 오래 서 있었으면 둘 다 멈춘 위치끼리 본다
                if (isSettled)
                {
                    error = authority.Position - position;
                    return true;
                }

                return false;
            }

            // 순번은 입력 종류와 상관없이 늘어나므로 서버가 아직 이 이동까지 오지 않았다는 뜻이다
            isPending = (_hasRecord) && (!_isRecordResolved) && (_recordSequence > authority.MoveSequence)
                && ((now - _recordStartTime) < _ackTimeout);
            if (isPending)
            {
                return false;
            }

            if (isSettled)
            {
                error = authority.Position - position;
                return true;
            }

            return false;
        }

        private Vector3 ApplyCorrection(Vector3 position, Vector3 delta)
        {
            _correctedDistance += delta.magnitude;
            _history.Shift(delta);
            return position + delta;
        }
    }
}
