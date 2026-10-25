// Unity
using UnityEngine;

namespace Shinshi.Dev
{
    /// <summary> 타격감용 카메라 흔들림과 확대를 맡는다. 카메라 위치를 정하는 쪽이 매 프레임 오프셋을 읽어 더한다 </summary>
    public sealed class HitFeedbackCamera : MonoBehaviour
    {
        // 카메라 위치와 줌은 이 클라이언트 화면에서만 쓰는 표현이다. 남의 공격으로 흔들리지 않게
        // 호출하는 쪽이 자기 캐릭터의 타격에만 Kick과 ZoomPunch를 부른다

        /****************************************
        *                Fields
        ****************************************/

        private const float SHAKE_FREQUENCY = 38f; // rad/s
        private const float MIN_ZOOM_SECONDS = 0.01f;
        private const float HOLD_STAGE_RATIO = 0.7f; // 두 단계 각각에 쓰는 감쇠 시간 비율
        private const float HOLD_END_LEVEL   = 0.002f; // 이만큼 아래로 풀리면 줌 상태를 끝낸다

        private Camera  _camera          = null;
        private Vector3 _direction       = Vector3.zero;
        private float   _strength        = 0f;
        private float   _seconds         = 1f;
        private float   _endTime         = 0f;  // Time.time 기준
        private float   _pendingStrength = 0f;  // 렉프레임이 끝나면 시작할 흔들림, 0이면 없다
        private float   _pendingSeconds  = 1f;
        private int     _delayFrames     = 0;
        private float   _baseZoom        = 0f;  // 확대하기 전 정사영 크기 또는 시야각
        private float   _zoomAmount      = 0f;
        private float   _zoomSeconds     = 1f;
        private float   _zoomEndTime     = -1f; // Time.time 기준
        private float   _holdAmount      = 0f;  // 기술 시전 동안 당겨 두는 확대 비율
        private float   _holdStartTime   = -1f; // Time.time 기준, 음수면 없다
        private float   _holdSeconds     = 0f;  // 당긴 채로 두는 시간
        private float   _holdInSmooth    = 0f;  // 당길 때의 감쇠 시간, 클수록 느긋하게 따라간다
        private float   _holdOutSmooth   = 0f;  // 풀 때의 감쇠 시간
        private float   _holdLevel       = 0f;  // 지금 당겨진 정도(0에서 1)
        private float   _holdVelocity    = 0f;
        private float   _holdMid         = 0f;  // 목표를 먼저 한 번 거른 중간값, 시작을 부드럽게 만든다
        private float   _holdMidVelocity = 0f;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            if (_pendingStrength <= 0f)
            {
                return;
            }

            --_delayFrames;
            if (_delayFrames <= 0)
            {
                StartKick();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 이후 Kick의 세기에 곱하는 배율. 특정 타격만 다르게 흔들 때 호출한 쪽이 잠깐 바꿨다가 1로 되돌린다 </summary>
        public float ShakeScale { get; set; } = 1f;

        /// <summary> 줌을 되돌릴 기준이 되는 카메라를 정한다 </summary>
        public void Init(Camera targetCamera)
        {
            _camera   = targetCamera;
            _baseZoom = _camera.orthographic ? _camera.orthographicSize : _camera.fieldOfView;
        }

        /// <summary> 줌을 되돌릴 기준(정사영 크기 또는 시야각)을 바꾸고 카메라에도 바로 반영한다. 줌이 진행 중이면 다음 프레임부터 새 기준으로 돌아간다 </summary>
        public void SetBaseZoom(float value)
        {
            _baseZoom = value;
            if (_camera == null)
            {
                return;
            }

            if (_camera.orthographic)
            {
                _camera.orthographicSize = value;
            }
            else
            {
                _camera.fieldOfView = value;
            }
        }

        /// <summary> 맞은 방향으로 확 밀렸다 돌아오는 흔들림을 delayFrames 프레임 뒤에 시작한다. 더 센 흔들림이 진행 중이면 무시한다 </summary>
        public void Kick(Vector3 direction, float strength, float seconds, int delayFrames)
        {
            strength *= ShakeScale;
            if ((strength <= 0f) || (strength < ReadStrength()))
            {
                return;
            }

            _direction       = direction;
            _pendingStrength = strength;
            _pendingSeconds  = seconds;
            _delayFrames     = delayFrames;

            if (delayFrames <= 0)
            {
                StartKick();
            }
        }

        /// <summary> 화면이 amount 비율만큼 확 당겨졌다가 seconds 동안 부드럽게 돌아온다 </summary>
        public void ZoomPunch(float amount, float seconds)
        {
            if (amount <= 0f)
            {
                return;
            }

            _zoomAmount  = amount;
            _zoomSeconds = seconds;
            _zoomEndTime = Time.time + seconds;
        }

        /// <summary> 화면이 amount 비율만큼 당겨져 holdSeconds 동안 유지된 뒤 풀린다. 당기고 푸는 움직임은 목표를 향해 감쇠하며 따라가고, 감쇠 시간이 클수록 부드럽고 느리다 </summary>
        public void ZoomHold(float amount, float holdSeconds, float inSmoothSeconds, float outSmoothSeconds)
        {
            if (amount <= 0f)
            {
                return;
            }

            _holdAmount    = amount;
            _holdSeconds   = Mathf.Max(holdSeconds, 0f);
            _holdInSmooth  = Mathf.Max(inSmoothSeconds, MIN_ZOOM_SECONDS);
            _holdOutSmooth = Mathf.Max(outSmoothSeconds, MIN_ZOOM_SECONDS);
            _holdStartTime = Time.time;
            _holdLevel     = 0f;
            _holdVelocity  = 0f;
            _holdMid       = 0f;
            _holdMidVelocity = 0f;
        }

        /// <summary> 지금 흔들림이 카메라 위치에 더할 오프셋 </summary>
        public Vector3 ReadShakeOffset()
        {
            float remain = (_endTime - Time.time) / _seconds;
            if (remain <= 0f)
            {
                return Vector3.zero;
            }

            float wave = Mathf.Cos((1f - remain) * _seconds * SHAKE_FREQUENCY);
            return _direction * (_strength * remain * remain * wave);
        }

        /// <summary> 확대 상태를 카메라에 반영한다. 매 프레임 LateUpdate에서 부른다 </summary>
        public void ApplyZoom()
        {
            if ((_camera == null) || ((_zoomEndTime < 0f) && (_holdStartTime < 0f)))
            {
                return;
            }

            float scale = ReadPunchScale() * ReadHoldScale();
            if (_camera.orthographic)
            {
                _camera.orthographicSize = _baseZoom * scale;
            }
            else
            {
                _camera.fieldOfView = _baseZoom * scale;
            }
        }

        private float ReadPunchScale()
        {
            if (_zoomEndTime < 0f)
            {
                return 1f;
            }

            float remain = (_zoomEndTime - Time.time) / _zoomSeconds;
            if (remain > 0f)
            {
                return 1f - (_zoomAmount * remain * remain);
            }

            _zoomEndTime = -1f;
            return 1f;
        }

        // 유지 시간 안에는 목표 1을, 그 뒤에는 목표 0을 향해 감쇠하며 따라간다. 고정 곡선이 아니라서 시작과 끝이 기계적이지 않다
        private float ReadHoldScale()
        {
            if (_holdStartTime < 0f)
            {
                return 1f;
            }

            bool  isHolding = (Time.time - _holdStartTime) < _holdSeconds;
            float target    = isHolding ? 1f : 0f;
            float smooth    = isHolding ? _holdInSmooth : _holdOutSmooth;
            // 두 단계로 걸러 처음 움직임이 급하지 않고 도착도 부드럽게 한다
            _holdMid   = Mathf.SmoothDamp(_holdMid, target, ref _holdMidVelocity, smooth * HOLD_STAGE_RATIO);
            _holdLevel = Mathf.SmoothDamp(_holdLevel, _holdMid, ref _holdVelocity, smooth * HOLD_STAGE_RATIO);

            if ((!isHolding) && (_holdLevel < HOLD_END_LEVEL))
            {
                _holdStartTime = -1f;
                _holdLevel     = 0f;
                _holdVelocity  = 0f;
                _holdMid       = 0f;
                _holdMidVelocity = 0f;
                return 1f;
            }

            return 1f - (_holdAmount * _holdLevel);
        }

        private void StartKick()
        {
            _strength        = _pendingStrength;
            _seconds         = _pendingSeconds;
            _endTime         = Time.time + _seconds;
            _pendingStrength = 0f;
        }

        // 아직 시작 전인 흔들림도 비교 대상에 넣어, 약한 타격이 센 타격의 대기를 덮어쓰지 않게 한다
        private float ReadStrength()
        {
            float remain = (_endTime - Time.time) / _seconds;
            float active = (remain > 0f) ? (_strength * remain) : 0f;
            return Mathf.Max(active, _pendingStrength);
        }
    }
}
