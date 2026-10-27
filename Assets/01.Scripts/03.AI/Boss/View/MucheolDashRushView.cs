// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 무철 돌진(P06) 달리는 구간의 지면 분출 표현이다. 애니메이션 없이 서버가 알린 진행 거리만으로 분출 선두를 움직인다.
    // 입자는 선두가 움직인 거리당 나오므로 서버 돌진 시간이나 거리가 달라져도 밀도가 같고, 벽에서 일찍 멈추면 그 뒤로 터지지 않는다.
    // 끝 타격과 예고는 원본 재생기가 그리므로 여기서는 그리지 않는다
    /// <summary> 돌진 중 지면 분출 한 벌, 서버 진행 거리로 구동하고 멈춘 뒤 남은 꼬리까지 스스로 거둔다 </summary>
    [DisallowMultipleComponent]
    public sealed class MucheolDashRushView : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float PATH_LENGTH        = 18f;   // m, 균열 평면이 덮는 경로 길이
        private const float CRACK_LEAD         = 1f;    // m, 균열이 분출 선두보다 앞서 드러나는 거리
        private const float CRACK_HOLD_SECONDS = 0.6f;  // 멈춘 뒤 균열이 그대로 있는 시간
        private const float CRACK_FADE_SECONDS = 1.8f;
        private const float TAIL_SECONDS       = 2.5f;  // 멈춘 뒤 남은 입자가 모두 사라지는 시간, 암석 수명 2.3초보다 길다
        private const float OPAQUE             = 1f;

        private static readonly int REVEAL_ID = Shader.PropertyToID("_Reveal");
        private static readonly int FADE_ID   = Shader.PropertyToID("_Fade");

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private Transform        _front;
        [SerializeField] private MeshRenderer     _crack;
        [SerializeField] private ParticleSystem[] _systems;

        private MaterialPropertyBlock _block;
        private bool                  _isFinished;
        private float                 _finishedSeconds;
        private float                 _reveal;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 시작한 뒤 아직 거두지 않았다, 풀에서 비어 있는지 가르는 값이다 </summary>
        public bool IsPlaying { get; private set; }

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            if ((!IsPlaying) || (!_isFinished))
            {
                return;
            }

            _finishedSeconds += Time.deltaTime;
            if (_finishedSeconds >= TAIL_SECONDS)
            {
                Stop();
                return;
            }

            float fade = OPAQUE - Mathf.Clamp01((_finishedSeconds - CRACK_HOLD_SECONDS) / CRACK_FADE_SECONDS);
            ApplyCrack(_reveal, fade);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 시작점과 방향에 놓고 처음부터 재생한다, 폭 배율은 하드 돌진처럼 넓은 경로에 쓴다 </summary>
        public void Begin(Vector3 position, Quaternion rotation, float widthScale)
        {
            transform.SetPositionAndRotation(position, rotation);
            transform.localScale = new Vector3(widthScale, OPAQUE, OPAQUE);
            _front.localPosition = Vector3.zero;

            // 비활성 상태로 만들어 두므로 입자는 켠 뒤에 비우고 다시 시작해야 이전 판의 입자가 남지 않는다
            gameObject.SetActive(true);
            IsPlaying        = true;
            _isFinished      = false;
            _finishedSeconds = 0f;
            _reveal          = 0f;
            ApplyCrack(0f, OPAQUE);

            for (int i = 0; i < _systems.Length; ++i)
            {
                _systems[i].Clear(false);
                _systems[i].Play(false);
            }
        }

        /// <summary> 시작점에서 분출 선두까지 간 거리, 매 프레임 서버 시각으로 계산한 값을 넣는다 </summary>
        public void SetDistance(float meters)
        {
            if ((!IsPlaying) || _isFinished)
            {
                return;
            }

            float distance = Mathf.Clamp(meters, 0f, PATH_LENGTH);
            _front.localPosition = new Vector3(0f, 0f, distance);
            _reveal = Mathf.Clamp01((distance + CRACK_LEAD) / PATH_LENGTH);
            ApplyCrack(_reveal, OPAQUE);
        }

        // 선두를 세우고 새 입자만 끊는다. 이미 나온 암석과 불씨는 수명대로 두어 정지 순간에 뚝 끊기지 않게 한다
        /// <summary> 서버가 멈추거나 끝 지점에 닿았다, 남은 꼬리를 재생하고 스스로 거둔다 </summary>
        public void Finish()
        {
            if ((!IsPlaying) || _isFinished)
            {
                return;
            }

            _isFinished = true;
            for (int i = 0; i < _systems.Length; ++i)
            {
                _systems[i].Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        /// <summary> 남은 입자까지 즉시 지우고 거둔다, 보스 사망이나 비활성화에 쓴다 </summary>
        public void Stop()
        {
            for (int i = 0; i < _systems.Length; ++i)
            {
                _systems[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            IsPlaying   = false;
            _isFinished = false;
            gameObject.SetActive(false);
        }

        private void ApplyCrack(float reveal, float fade)
        {
            if (_crack == null)
            {
                return;
            }

            if (_block == null)
            {
                _block = new MaterialPropertyBlock();
            }

            _crack.GetPropertyBlock(_block);
            _block.SetFloat(REVEAL_ID, reveal);
            _block.SetFloat(FADE_ID, fade);
            _crack.SetPropertyBlock(_block);
        }
    }
}
