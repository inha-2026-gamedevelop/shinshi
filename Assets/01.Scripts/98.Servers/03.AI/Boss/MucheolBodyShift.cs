// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;

namespace Shinshi.AI
{
    // 무철 타격 클립의 몸 이동을 서버 위치로 옮긴다. 패턴이 하나씩 가지며(시전마다 새로 만든다) 패턴이 부르는 시각까지만 진행한다.
    //
    // 경로 방향은 시전 시작에 한 번 정해 시전 끝까지 고정한다. 판정 중심이 타 확정 시각의 보스 위치라 예고를 그 위치에 그려야 하는데,
    // 조준이 도는 동안 매 순간의 정면으로 경로를 돌리면 타격 위치를 시전 시작에 미리 알 수 없다. 조준 패턴은 시전 시작 순간의
    // 대상 방향(조준이 향해 갈 방향)을, 조준이 없는 패턴은 보스 정면을 쓴다. 대상이 시전 중에 움직이면 몸이 조금 비스듬히 나간다.
    //
    // 이동은 매번 시전 시작 위치 + 경로 값으로 목표를 잡고 지금 위치에서 그 목표까지 이동 창구로 옮긴다. 벽이나 전장 경계에 막히면
    // 그 자리에서 멈추고, 경로가 다시 막히지 않는 쪽으로 돌아오면 그때부터 경로를 따라간다(경로 전체를 줄이지 않는다).
    // 시전이 끊기면(그로기, 직접 제어, 사망) 패턴이 더는 부르지 않아 그 자리에 남고 시전 시작 위치로 되돌리지 않는다.
    // 플레이어 몸통과 겹치면 플레이어 쪽을 밀어내는 서버 틱 끝 규칙이 맡고 보스는 막히지 않는다(돌진과 같다)
    /// <summary> 무철 패턴 한 시전의 몸 이동, 시전 시작 위치에서 경로를 따라 보스를 옮기고 타 확정 위치를 미리 계산한다 </summary>
    public sealed class MucheolBodyShift
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float MIN_DIRECTION_SQR = 0.000001f; // m^2, 이보다 가까우면 방향을 정할 수 없다
        private const float MIN_MOVE          = 0.0005f;   // m, 이보다 짧은 이동은 창구를 부르지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private readonly IBossMovement        _movement;
        private readonly MucheolBodyShiftSpec _spec;
        private readonly float[]              _strikeDelays;
        private readonly double[]             _keyTimes;

        private ICombatTarget _self;
        private Vector3       _origin;
        private Vector3       _forward;
        private Vector3       _right;
        private Vector2       _base;
        private double        _castStart;
        private double        _executeStart;
        private double        _advancedTo;
        private bool          _isBegun;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 시전 시작 위치, 경로의 0점이다 </summary>
        public Vector3 Origin => _origin;

        /// <summary> 경로의 앞 방향, 시전 시작에 정해 바뀌지 않는다 </summary>
        public Vector3 Forward => _forward;

        /// <summary> 마지막 키의 시각, 이후에는 경로 끝에 머문다 </summary>
        public double EndTime => _keyTimes[_keyTimes.Length - 1];

        /****************************************
        *                Methods
        ****************************************/

        // strikeDelays는 타마다 실행 시작부터 확정까지의 초다. 단발 패턴은 { 0 }이고 콤보와 순차 타격은 타 데이터의 지연이다
        public MucheolBodyShift(IBossMovement movement, MucheolBodyShiftSpec spec, float[] strikeDelays)
        {
            if (movement == null)
            {
                throw new ArgumentNullException(nameof(movement));
            }

            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            if ((strikeDelays == null) || (spec.MaxStrikeIndex >= strikeDelays.Length))
            {
                throw new ArgumentException("몸 이동 키가 기준으로 삼는 타가 타 목록에 없다", nameof(strikeDelays));
            }

            _movement     = movement;
            _spec         = spec;
            _strikeDelays = strikeDelays;
            _keyTimes     = new double[spec.KeyCount];
        }

        /// <summary> 시전 시작, castStart는 예고 시작이고 executeStart는 예고 끝(실행 시작) 시각이다 </summary>
        public void Begin(ICombatTarget self, Vector3 forward, double castStart, double executeStart)
        {
            _self         = self ?? throw new ArgumentNullException(nameof(self));
            _origin       = self.Position;
            _forward      = Flatten(forward, Vector3.forward);
            _right        = new Vector3(_forward.z, 0f, -_forward.x);
            _castStart    = castStart;
            _executeStart = executeStart;
            _advancedTo   = castStart;
            _isBegun      = true;

            for (int i = 0; i < _keyTimes.Length; ++i)
            {
                MucheolBodyShiftKey key = _spec.GetKey(i);
                double anchor = (key.StrikeIndex == MucheolBodyShiftKey.CAST_START) ? _castStart : GetStrikeTime(key.StrikeIndex);
                _keyTimes[i] = anchor + key.OffsetSeconds;
            }

            _base = _spec.Sample(_spec.GetKey(0).ClipSeconds);
        }

        /// <summary> 타 확정 시각, 실행 시작에 그 타의 지연을 더한다 </summary>
        public double GetStrikeTime(int strikeIndex)
        {
            return _executeStart + _strikeDelays[strikeIndex];
        }

        /// <summary> 이 시각에 재생 중인 클립 초, 같은 시각의 키가 둘이면 뒤 키다 </summary>
        public float GetClipSeconds(double serverTime)
        {
            int last = _keyTimes.Length - 1;
            if (serverTime < _keyTimes[0])
            {
                return _spec.GetKey(0).ClipSeconds;
            }

            for (int i = last; i >= 0; --i)
            {
                if (serverTime < _keyTimes[i])
                {
                    continue;
                }

                if (i == last)
                {
                    return _spec.GetKey(last).ClipSeconds;
                }

                float  fromClip = _spec.GetKey(i).ClipSeconds;
                float  toClip   = _spec.GetKey(i + 1).ClipSeconds;
                double span     = _keyTimes[i + 1] - _keyTimes[i];
                float  t        = (span > 0.0) ? (float)((serverTime - _keyTimes[i]) / span) : 1f;
                return Mathf.Lerp(fromClip, toClip, t);
            }

            return _spec.GetKey(0).ClipSeconds;
        }

        /// <summary> 벽을 생각하지 않은 경로 위치 </summary>
        public Vector3 GetPathPosition(double serverTime)
        {
            Vector2 shift = _spec.Sample(GetClipSeconds(serverTime)) - _base;
            return _origin + (_right * shift.x) + (_forward * shift.y);
        }

        // 시전 시작 위치에서 경로 위치까지 직선으로 막힘을 잰다. 실제 이동은 매 틱의 짧은 구간을 잇지만
        // 무철 경로는 시전 시작 기준 2m 안팎이라 벽에 닿지 않으면 두 값이 같고, 닿으면 예고와 판정이 벽 앞에서 조금 어긋날 수 있다
        /// <summary> 타 확정 순간의 예상 보스 위치, 예고를 그릴 자리다 </summary>
        public Vector3 PredictStrikePosition(int strikeIndex)
        {
            Vector3 target = GetPathPosition(GetStrikeTime(strikeIndex));
            Vector3 delta  = target - _origin;
            delta.y = 0f;

            float distance = delta.magnitude;
            if (distance <= MIN_MOVE)
            {
                return _origin;
            }

            Vector3 direction = delta / distance;
            float   clear     = _movement.GetClearDistance(_origin, direction, distance, _self.BodyRadius);
            return _origin + (direction * clear);
        }

        // 지난 시각은 무시한다. 패턴은 아직 처리하지 않은 타의 확정 시각을 넘지 않게 불러 타가 그 순간의 위치에서 판정되게 한다
        /// <summary> serverTime의 경로 위치로 보스를 옮긴다 </summary>
        public void Advance(double serverTime)
        {
            if ((!_isBegun) || (serverTime <= _advancedTo))
            {
                return;
            }

            _advancedTo = serverTime;

            Vector3 from   = _self.Position;
            Vector3 target = GetPathPosition(serverTime);
            target.y = from.y;

            if ((target - from).sqrMagnitude <= (MIN_MOVE * MIN_MOVE))
            {
                return;
            }

            _movement.Move(from, target, _self.BodyRadius);
        }

        /// <summary> 경로 끝까지 옮긴다, 회복이 꼬리 구간보다 짧아 시전이 먼저 끝나도 클라이언트 모션의 끝 자세와 맞춘다 </summary>
        public void Complete()
        {
            Advance(EndTime);
        }

        // 조준 패턴은 시전 시작 순간의 대상 방향, 대상이 없거나 조준이 없는 패턴은 보스 정면이다
        /// <summary> 경로의 앞 방향을 고른다 </summary>
        public static Vector3 ResolveForward(in BossPatternContext context, bool towardTarget)
        {
            Vector3 forward = Flatten(context.Self.Forward, Vector3.forward);
            if ((towardTarget) && (MucheolAimTracker.TryReadTarget(context, context.Self.Position, out Vector3 toTarget)))
            {
                forward = Flatten(toTarget, forward);
            }

            return forward;
        }

        private static Vector3 Flatten(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            return (direction.sqrMagnitude < MIN_DIRECTION_SQR) ? fallback : direction.normalized;
        }
    }
}
