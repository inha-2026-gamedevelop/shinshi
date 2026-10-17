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

        /// <summary> 줌을 되돌릴 기준이 되는 카메라를 정한다 </summary>
        public void Init(Camera targetCamera)
        {
            _camera   = targetCamera;
            _baseZoom = _camera.orthographic ? _camera.orthographicSize : _camera.fieldOfView;
        }

        /// <summary> 맞은 방향으로 확 밀렸다 돌아오는 흔들림을 delayFrames 프레임 뒤에 시작한다. 더 센 흔들림이 진행 중이면 무시한다 </summary>
        public void Kick(Vector3 direction, float strength, float seconds, int delayFrames)
        {
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
            if ((_camera == null) || (_zoomEndTime < 0f))
            {
                return;
            }

            float remain = (_zoomEndTime - Time.time) / _zoomSeconds;
            float scale  = 1f;
            if (remain > 0f)
            {
                scale = 1f - (_zoomAmount * remain * remain);
            }
            else
            {
                _zoomEndTime = -1f;
            }

            if (_camera.orthographic)
            {
                _camera.orthographicSize = _baseZoom * scale;
            }
            else
            {
                _camera.fieldOfView = _baseZoom * scale;
            }
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
