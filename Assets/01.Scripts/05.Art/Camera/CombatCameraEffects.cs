// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 전투 연출용 카메라 흔들림과 줌 상태를 계산하는 순수 계산기, 카메라는 읽어 간 값만 적용한다 </summary>
    public sealed class CombatCameraEffects
    {
        // 시간은 호출하는 쪽이 넘기는 초 단위 값이다. 엔진 시계를 직접 읽지 않아 EditMode에서 시험한다

        /****************************************
        *                Fields
        ****************************************/

        private const float  SHAKE_FREQUENCY  = 38f;    // rad/s
        private const float  MIN_ZOOM_SECONDS = 0.01f;
        private const float  HOLD_STAGE_RATIO = 0.7f;   // 두 단계 각각에 쓰는 감쇠 시간 비율
        private const float  HOLD_END_LEVEL   = 0.002f; // 이만큼 아래로 풀리면 줌 상태를 끝낸다
        private const double NO_TIME          = -1.0;

        private Vector3 _shakeDirection = Vector3.zero;
        private float   _shakeStrength  = 0f;
        private float   _shakeSeconds   = 1f;
        private double  _shakeEndTime   = NO_TIME;

        private float  _zoomAmount     = 0f;
        private double _zoomStartTime  = NO_TIME;
        private float  _zoomHoldSeconds = 0f;
        private float  _zoomInSmooth   = 0f;
        private float  _zoomOutSmooth  = 0f;
        private float  _zoomMid        = 0f;
        private float  _zoomMidVelocity = 0f;
        private float  _zoomLevel      = 0f;
        private float  _zoomVelocity   = 0f;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 시야각에 곱할 값, 줌이 없으면 1이다 </summary>
        public float ZoomScale { get; private set; } = 1f;

        /// <summary> 카메라 기준 좌표계로 더할 흔들림 오프셋 </summary>
        public Vector3 ShakeOffset { get; private set; } = Vector3.zero;

        public bool IsActive => (_shakeEndTime > NO_TIME) || (_zoomStartTime > NO_TIME);

        /// <summary> 줌이 진행 중이면 참, 이미 당겨진 줌을 다른 줌이 덮어쓰지 않게 확인할 때 쓴다 </summary>
        public bool IsZooming => _zoomStartTime > NO_TIME;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 방향으로 확 밀렸다 돌아오는 흔들림을 시작한다. 지금 진행 중인 흔들림보다 약하면 무시한다 </summary>
        public void Kick(Vector3 cameraSpaceDirection, float strength, float seconds, double now)
        {
            if ((strength <= 0f) || (seconds <= 0f) || (strength < ReadShakeStrength(now)))
            {
                return;
            }

            _shakeDirection = cameraSpaceDirection;
            _shakeStrength  = strength;
            _shakeSeconds   = seconds;
            _shakeEndTime   = now + seconds;
        }

        /// <summary> 화면이 amount 비율만큼 당겨져 holdSeconds 동안 유지된 뒤 풀린다. 당기고 푸는 움직임은 목표를 향해 감쇠하며 따라간다 </summary>
        public void ZoomHold(float amount, float holdSeconds, float inSmoothSeconds, float outSmoothSeconds, double now)
        {
            if (amount <= 0f)
            {
                return;
            }

            _zoomAmount      = amount;
            _zoomHoldSeconds = Mathf.Max(holdSeconds, 0f);
            _zoomInSmooth    = Mathf.Max(inSmoothSeconds, MIN_ZOOM_SECONDS);
            _zoomOutSmooth   = Mathf.Max(outSmoothSeconds, MIN_ZOOM_SECONDS);
            _zoomStartTime   = now;
            _zoomMid         = 0f;
            _zoomMidVelocity = 0f;
            _zoomLevel       = 0f;
            _zoomVelocity    = 0f;
        }

        /// <summary> 진행 중인 효과를 모두 끝낸다 </summary>
        public void Reset()
        {
            _shakeEndTime  = NO_TIME;
            _zoomStartTime = NO_TIME;
            ZoomScale      = 1f;
            ShakeOffset    = Vector3.zero;
        }

        /// <summary> 한 프레임 진행한다. 결과는 ZoomScale과 ShakeOffset에서 읽는다 </summary>
        public void Tick(double now, float deltaTime)
        {
            ShakeOffset = ReadShakeOffset(now);
            ZoomScale   = ReadZoomScale(now, deltaTime);
        }

        private float ReadShakeStrength(double now)
        {
            float remain = (float)((_shakeEndTime - now) / _shakeSeconds);
            return (remain > 0f) ? (_shakeStrength * remain * remain) : 0f;
        }

        private Vector3 ReadShakeOffset(double now)
        {
            if (_shakeEndTime <= NO_TIME)
            {
                return Vector3.zero;
            }

            float remain = (float)((_shakeEndTime - now) / _shakeSeconds);
            if (remain <= 0f)
            {
                _shakeEndTime = NO_TIME;
                return Vector3.zero;
            }

            float wave = Mathf.Cos((1f - remain) * _shakeSeconds * SHAKE_FREQUENCY);
            return _shakeDirection * (_shakeStrength * remain * remain * wave);
        }

        // 유지 시간 안에는 목표 1을, 그 뒤에는 목표 0을 향해 두 단계로 걸러 따라간다. 처음 움직임이 급하지 않고 도착도 부드럽다
        private float ReadZoomScale(double now, float deltaTime)
        {
            if (_zoomStartTime <= NO_TIME)
            {
                return 1f;
            }

            bool  isHolding = (now - _zoomStartTime) < _zoomHoldSeconds;
            float target    = isHolding ? 1f : 0f;
            float smooth    = (isHolding ? _zoomInSmooth : _zoomOutSmooth) * HOLD_STAGE_RATIO;

            _zoomMid   = Mathf.SmoothDamp(_zoomMid, target, ref _zoomMidVelocity, smooth, Mathf.Infinity, deltaTime);
            _zoomLevel = Mathf.SmoothDamp(_zoomLevel, _zoomMid, ref _zoomVelocity, smooth, Mathf.Infinity, deltaTime);

            if ((!isHolding) && (_zoomLevel < HOLD_END_LEVEL))
            {
                _zoomStartTime = NO_TIME;
                _zoomLevel     = 0f;
                return 1f;
            }

            return 1f - (_zoomAmount * _zoomLevel);
        }
    }
}
