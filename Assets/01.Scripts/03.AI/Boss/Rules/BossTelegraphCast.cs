// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 알림을 받아 표현 한 건의 상태를 정하는 순수 모델이다. 서버가 보낸 값만 받아들이고 스스로 방향이나 길이를 추정하지 않는다
    // 늦은 갱신, 중복, 순서가 바뀐 알림은 여기서 거절하고 끝난 표현은 다시 살아나지 않는다. 엔진 오브젝트를 만들지 않아 EditMode에서 그대로 시험한다
    /// <summary> 예고 표현 한 건(시전 번호 하나)의 상태와 전이 규칙 </summary>
    public sealed class BossTelegraphCast
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float NOT_STOPPED = -1f; // 돌진 정지 거리가 아직 없다

        /****************************************
        *                Fields
        ****************************************/

        private readonly BossTelegraphStart _start;

        private Vector3             _direction;
        private float               _length;
        private bool                _isLocked;
        private bool                _hasExecuted;
        private EBossTelegraphPhase _phase;
        private double              _lastAimServerTime;
        private double              _executeServerTime;
        private float               _dashDistance;
        private float               _dashSeconds;
        private float               _dashSpeed;
        private float               _dashStoppedDistance = NOT_STOPPED;
        private bool                _dashStoppedByWall;

        /****************************************
        *              Properties
        ****************************************/

        public BossTelegraphStart Start => _start;

        public int                 BossId    => _start.BossId;
        public int                 CastId    => _start.CastId;
        public string              PatternId => _start.PatternId;
        public int                 GroupId   => _start.GroupId;
        public EBossTelegraphShape Shape     => _start.Shape;
        public Vector3             Origin    => _start.Origin;
        public float               Width     => _start.Width;
        public float               ArcAngle  => _start.ArcAngle;

        /// <summary> 지금의 조준 방향, 수평 단위 벡터 </summary>
        public Vector3 Direction => _direction;

        /// <summary> 지금 보여 주는 길이(원형과 부채꼴은 반경) </summary>
        public float Length => _length;

        public bool                IsDirectionLocked => _isLocked;
        public EBossTelegraphPhase Phase             => _phase;

        /// <summary> 끝나서 더는 바뀌지 않는다 </summary>
        public bool IsEnded => (_phase == EBossTelegraphPhase.Finished) || (_phase == EBossTelegraphPhase.Cancelled);

        /// <summary> 서버가 발동을 알렸다, 정상 종료 뒤에도 참이다 </summary>
        public bool HasExecuted => _hasExecuted;

        public double ExecuteServerTime => _executeServerTime;
        public float  DashDistance      => _dashDistance;
        public float  DashSeconds       => _dashSeconds;
        public float  DashSpeed         => _dashSpeed;

        /// <summary> 돌진이 멈췄다, 정해진 거리에 닿았거나 벽에서 멈춘 것을 서버가 알렸다 </summary>
        public bool IsDashStopped => _dashStoppedDistance >= 0f;

        public bool DashStoppedByWall => _dashStoppedByWall;

        /// <summary> 예고가 끝나는 서버 시각 </summary>
        public double TelegraphEndServerTime => _start.StartServerTime + _start.TelegraphSeconds;

        /****************************************
        *                Methods
        ****************************************/

        public BossTelegraphCast(in BossTelegraphStart start)
        {
            _start             = start;
            _direction         = BossTelegraphRules.Flatten(start.Direction, Vector3.forward);
            _length            = Mathf.Max(0f, start.Length);
            _phase             = EBossTelegraphPhase.Telegraph;
            _lastAimServerTime = start.StartServerTime;
        }

        /// <summary> 스냅샷으로 복원한다, 이미 끝난 상태의 스냅샷으로도 만들 수 있지만 그 표현은 그려지지 않는다 </summary>
        public static BossTelegraphCast FromSnapshot(in BossTelegraphSnapshot snapshot)
        {
            BossTelegraphCast cast = new BossTelegraphCast(snapshot.Start);
            cast._direction          = BossTelegraphRules.Flatten(snapshot.Direction, cast._direction);
            cast._length             = Mathf.Max(0f, snapshot.Length);
            cast._isLocked           = snapshot.IsDirectionLocked;
            cast._phase              = snapshot.Phase;
            cast._hasExecuted        = (snapshot.Phase == EBossTelegraphPhase.Execute);
            cast._executeServerTime  = snapshot.ExecuteServerTime;
            cast._dashDistance       = Mathf.Max(0f, snapshot.DashDistance);
            cast._dashSeconds        = Mathf.Max(0f, snapshot.DashSeconds);
            cast._dashSpeed          = Mathf.Max(0f, snapshot.DashSpeed);
            cast._dashStoppedDistance = (snapshot.DashStoppedDistance >= 0f) ? snapshot.DashStoppedDistance : NOT_STOPPED;
            cast._dashStoppedByWall  = snapshot.DashStoppedByWall;
            return cast;
        }

        public BossTelegraphSnapshot ToSnapshot()
        {
            return new BossTelegraphSnapshot
            {
                Start               = _start,
                Direction           = _direction,
                Length              = _length,
                IsDirectionLocked   = _isLocked,
                Phase               = _phase,
                ExecuteServerTime   = _executeServerTime,
                DashDistance        = _dashDistance,
                DashSeconds         = _dashSeconds,
                DashSpeed           = _dashSpeed,
                DashStoppedDistance   = _dashStoppedDistance,
                DashStoppedByWall     = _dashStoppedByWall,
                DashStoppedServerTime = double.NaN
            };
        }

        /// <summary> 추적 중 방향과 길이 갱신, 고정 뒤나 발동 뒤나 이전 시각의 갱신은 거절한다 </summary>
        public bool TryUpdateAim(Vector3 direction, float length, double serverTime)
        {
            if ((_phase != EBossTelegraphPhase.Telegraph) || _isLocked || (serverTime < _lastAimServerTime))
            {
                return false;
            }

            _lastAimServerTime = serverTime;
            _direction         = BossTelegraphRules.Flatten(direction, _direction);
            _length            = Mathf.Max(0f, length);
            return true;
        }

        /// <summary> 방향 고정, 서버가 확정한 마지막 값이라 갱신 시각과 상관없이 받아들인다 </summary>
        public bool TryLockDirection(Vector3 direction, float length)
        {
            if ((_phase != EBossTelegraphPhase.Telegraph) || _isLocked)
            {
                return false;
            }

            _direction = BossTelegraphRules.Flatten(direction, _direction);
            _length    = Mathf.Max(0f, length);
            _isLocked  = true;
            return true;
        }

        /// <summary> 부채꼴, 원형, 직사각형의 타격 확정, 돌진은 TryBeginDash를 쓴다 </summary>
        public bool TryStrike(double serverTime)
        {
            if ((_phase != EBossTelegraphPhase.Telegraph) || (Shape == EBossTelegraphShape.Dash))
            {
                return false;
            }

            _isLocked          = true;
            _hasExecuted       = true;
            _phase             = EBossTelegraphPhase.Execute;
            _executeServerTime = serverTime;
            return true;
        }

        /// <summary> 돌진 시작, distance는 벽 앞 거리라 이후 길이로 쓴다. speed는 서버가 정한 일정한 속도다 </summary>
        public bool TryBeginDash(float distance, float seconds, float speed, double serverTime)
        {
            if ((_phase != EBossTelegraphPhase.Telegraph) || (Shape != EBossTelegraphShape.Dash))
            {
                return false;
            }

            _isLocked          = true;
            _hasExecuted       = true;
            _phase             = EBossTelegraphPhase.Execute;
            _executeServerTime = serverTime;
            _dashDistance      = Mathf.Max(0f, distance);
            _dashSeconds       = Mathf.Max(0f, seconds);
            _dashSpeed         = Mathf.Max(0f, speed);
            _length            = _dashDistance;
            return true;
        }

        /// <summary> 돌진 정지, 서버가 알린 정지 위치를 시작점에서 잰 거리로 바꿔 둔다 </summary>
        public bool TryStopDash(Vector3 position, bool stoppedByWall)
        {
            if ((_phase != EBossTelegraphPhase.Execute) || (Shape != EBossTelegraphShape.Dash) || IsDashStopped)
            {
                return false;
            }

            Vector3 offset = position - _start.Origin;
            offset.y = 0f;

            float along = (offset.x * _direction.x) + (offset.z * _direction.z);
            _dashStoppedDistance = Mathf.Clamp(along, 0f, _dashDistance);
            _dashStoppedByWall   = stoppedByWall;
            return true;
        }

        /// <summary> 정상 종료 </summary>
        public bool TryFinish()
        {
            if (IsEnded)
            {
                return false;
            }

            _phase = EBossTelegraphPhase.Finished;
            return true;
        }

        /// <summary> 중단, 이미 타격이 확정된 직사각형과 부채꼴과 원형은 서버도 취소하지 않으므로 받아들이지 않는다 </summary>
        public bool TryCancel()
        {
            if (IsEnded)
            {
                return false;
            }

            if ((_phase == EBossTelegraphPhase.Execute) && (Shape != EBossTelegraphShape.Dash))
            {
                return false;
            }

            _phase = EBossTelegraphPhase.Cancelled;
            return true;
        }

        /// <summary> 서버 확정이 제때 오지 않아 지운다, 이후 늦게 온 알림도 받아들이지 않는다 </summary>
        public bool TryExpire()
        {
            if (_phase != EBossTelegraphPhase.Telegraph)
            {
                return false;
            }

            _phase = EBossTelegraphPhase.Cancelled;
            return true;
        }

        public float GetTelegraphProgress(double serverNow)
        {
            return BossTelegraphRules.GetTelegraphProgress(_start.StartServerTime, _start.TelegraphSeconds, serverNow);
        }

        /// <summary> 발동 뒤 흐른 시간, 발동 전이면 0. 정상 종료를 알린 뒤에도 사라지는 표현을 이어 그릴 수 있게 센다 </summary>
        public float GetExecuteAge(double serverNow)
        {
            if (!_hasExecuted)
            {
                return 0f;
            }

            return Mathf.Max(0f, (float)(serverNow - _executeServerTime));
        }

        /// <summary> 돌진이 시작점에서 간 거리, 서버가 정지를 알렸으면 그 거리이고 아니면 일정한 속도로 센다 </summary>
        public float GetDashTraveled(double serverNow)
        {
            if ((Shape != EBossTelegraphShape.Dash) || (!_hasExecuted))
            {
                return 0f;
            }

            if (IsDashStopped)
            {
                return _dashStoppedDistance;
            }

            return BossTelegraphRules.GetDashTraveled(_executeServerTime, _dashSpeed, _dashDistance, serverNow);
        }

        /// <summary> 돌진 머리가 마지막 거리에 닿는 데 걸리는 발동 뒤 시간, 서버가 보낸 속도와 정지 거리로 센다 </summary>
        public float GetDashEndAge()
        {
            if ((Shape != EBossTelegraphShape.Dash) || (_dashSpeed <= 0f))
            {
                return 0f;
            }

            float finalDistance = IsDashStopped ? _dashStoppedDistance : _dashDistance;
            return finalDistance / _dashSpeed;
        }
    }
}
